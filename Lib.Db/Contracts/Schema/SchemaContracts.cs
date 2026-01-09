// ============================================================================
// 파일명: Lib.Db/Contracts/Schema/SchemaContracts.cs
// 역할  : 스키마 서비스, TVP 검증, Flush Hook, 예외 정의 등 공용 계약 통합 정의
// 환경  : .NET 10 / C# 14
// 설명  : DbSchema.cs의 하이브리드 스냅샷 + HybridCache 설계에 맞춰
//         실제로 사용하는 계약만 외부에 노출하도록 정리한 계약 모음입니다.
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Models;

namespace Lib.Db.Contracts.Schema;

#region 프리워밍 결과 구조체

/// <summary>
/// 스키마 프리워밍(Preload) 결과를 나타내는 구조체입니다.
/// <para>
/// <b>[설계 의도]</b>
/// - <b>검증 강화</b>: 요청한 스키마 중 실제 로드된 것과 누락된 것을 구분하여 호출자에게 경고/예외 처리의 기회를 제공합니다.
/// </para>
/// </summary>
/// <param name="LoadedItemsCount">로드된 총 객체(SP + TVP) 수</param>
/// <param name="MissingSchemas">요청했으나 DB에서 발견되지 않은 스키마 목록</param>
public readonly record struct PreloadResult(
    int LoadedItemsCount,
    List<string> MissingSchemas
);

#endregion

#region 스키마 서비스 계약

/// <summary>
/// 저장 프로시저(SP) 및 테이블 반환 매개변수(TVP)의 메타데이터 조회와
/// 캐시/스냅샷 수명 주기를 관리하는 핵심 서비스 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>하이브리드 캐싱</b>: L1(로컬 메모리)과 L2(Redis 등 분산)를 결합하여 조회 성능을 극대화하고 DB 부하를 최소화합니다.<br/>
/// - <b>안전한 갱신</b>: 스키마 변경 시 <c>FluxhSchema</c>를 통해 전체 인스턴스의 캐시를 동기화하여 일관성을 유지합니다.
/// </para>
/// <para>
/// - 캐시 우선 조회(L1/L2), 미존재/만료 시 DB 조회<br/>
/// - 특정 객체 무효화/전체 Flush 지원<br/>
/// - 스키마 변경으로 인한 실행 오류를 Self-Healing 패턴으로 복구할 수 있도록 보조 기능 제공
/// </para>
/// </summary>
public interface ISchemaService
{
    #region 사전 로딩

    /// <summary>
    /// 지정된 스키마들(예: <c>["dbo", "sales"]</c>)에 속한 객체(SP, TVP)의 메타데이터를 미리 로딩합니다.
    /// <para>
    /// Cold Start 방지 및 애플리케이션 시작 시 워밍업 목적에 사용합니다.
    /// </para>
    /// </summary>
    /// <param name="schemaNames">데이터베이스 스키마 이름 목록</param>
    /// <param name="instanceHash">대상 DB 인스턴스 해시(연결 문자열 식별자)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>로딩 결과(성공 수, 누락된 스키마 등)</returns>
    Task<PreloadResult> PreloadSchemaAsync(IEnumerable<string> schemaNames, string instanceHash, CancellationToken ct);

    #endregion

    #region 스키마 조회

    /// <summary>
    /// 저장 프로시저(SP)의 스키마 정보를 조회합니다. (캐시 우선, 미존재 시 DB 조회)
    /// </summary>
    /// <param name="spName">SP 전체 이름 (예: <c>"dbo.usp_GetUser"</c>)</param>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>SP 메타데이터(파라미터 정보 포함)</returns>
    Task<SpSchema> GetSpSchemaAsync(string spName, string instanceHash, CancellationToken ct);

    /// <summary>
    /// 테이블 반환 매개변수(TVP)의 스키마 정보를 조회합니다. (캐시 우선, 미존재 시 DB 조회)
    /// </summary>
    /// <param name="tvpName">TVP 타입 이름 (예: <c>"dbo.Tvp_UserList"</c>)</param>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>TVP 메타데이터(컬럼 정보 포함)</returns>
    Task<TvpSchema> GetTvpSchemaAsync(string tvpName, string instanceHash, CancellationToken ct);

    #endregion

    #region 캐시 무효화

    /// <summary>
    /// 지정된 인스턴스의 모든 스키마 캐시(L1 스냅샷 + L2 분산 캐시)를 완전히 제거(Flush)합니다.
    /// <para>
    /// 배포/스키마 변경 직후 강제 동기화가 필요할 때 사용합니다.
    /// </para>
    /// </summary>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    Task FlushSchemaAsync(string instanceHash, CancellationToken ct);

    /// <summary>
    /// 특정 SP의 스키마만 선택적으로 무효화합니다.
    /// <para>
    /// 파라미터 불일치 오류 발생 시 자동 복구(Self-Healing)를 유도할 때 사용합니다.
    /// </para>
    /// </summary>
    /// <param name="spName">대상 SP 이름</param>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    void InvalidateSpSchema(string spName, string instanceHash);

    #endregion

    #region 자가 복구 (Self-Healing)

    /// <summary>
    /// SqlException이 "스키마 버전 불일치(파라미터 변경)"에 해당하는지 판별합니다.
    /// <para>
    /// <b>[Self-Healing 메커니즘]</b><br/>
    /// 분산 환경에서 SP 파라미터가 변경되었으나 캐시가 아직 무효화되지 않은 경우,
    /// 아래 오류 코드는 스키마 불일치 가능성이 높으므로 캐시 무효화 후 재조회/재시도를 유도합니다.
    /// </para>
    /// <para>
    /// <b>판정 기준(SQL Server 오류 코드)</b>
    /// <list type="bullet">
    /// <item><c>201</c> : 프로시저에 매개변수가 필요하지만 제공되지 않음</item>
    /// <item><c>8144</c>: 프로시저에 너무 많은 인수가 지정됨</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="ex">발생한 SqlException</param>
    /// <returns>스키마 버전 불일치(파라미터 불일치)로 판단되면 <c>true</c></returns>
    static bool IsSchemaVersionMismatch(SqlException ex)
    {
        // 201  : 프로시저에 매개변수가 필요하지만 제공되지 않음
        // 8144 : 프로시저에 너무 많은 인수가 지정됨
        return ex.Number is 201 or 8144;
    }

    #endregion

    #region 편의성 오버로드

    // ---------------------------------------------------------------------
    // [C# Default Interface Methods]
    // - 구현체는 string 기반 메서드만 구현해도 됩니다.
    // - 호출부에서는 VO(SpName/TvpName/DbInstanceId)로 안전하고 명확하게 사용할 수 있습니다.
    // ---------------------------------------------------------------------

    /// <summary>
    /// <see cref="SpName"/> 및 <see cref="DbInstanceId"/>를 사용하는 SP 스키마 조회 오버로드입니다.
    /// </summary>
    /// <param name="spName">SP 이름 값 객체</param>
    /// <param name="instance">DB 인스턴스 식별 값 객체</param>
    /// <param name="ct">취소 토큰</param>
    async Task<SpSchema> GetSpSchemaAsync(SpName spName, DbInstanceId instance, CancellationToken ct)
        => await GetSpSchemaAsync(spName.FullName, instance.Value, ct).ConfigureAwait(false);

    /// <summary>
    /// <see cref="TvpName"/> 및 <see cref="DbInstanceId"/>를 사용하는 TVP 스키마 조회 오버로드입니다.
    /// </summary>
    /// <param name="tvpName">TVP 이름 값 객체</param>
    /// <param name="instance">DB 인스턴스 식별 값 객체</param>
    /// <param name="ct">취소 토큰</param>
    async Task<TvpSchema> GetTvpSchemaAsync(TvpName tvpName, DbInstanceId instance, CancellationToken ct)
        => await GetTvpSchemaAsync(tvpName.FullName, instance.Value, ct).ConfigureAwait(false);

    /// <summary>
    /// <see cref="DbInstanceId"/>를 사용하는 스키마 워밍업(Preload) 오버로드입니다.
    /// </summary>
    /// <param name="schemaName">스키마 이름(예: dbo)</param>
    /// <param name="instance">DB 인스턴스 식별 값 객체</param>
    /// <param name="ct">취소 토큰</param>
    async Task<PreloadResult> PreloadSchemaAsync(string schemaName, DbInstanceId instance, CancellationToken ct)
        => await PreloadSchemaAsync([schemaName], instance.Value, ct).ConfigureAwait(false);

    /// <summary>
    /// <see cref="DbInstanceId"/>를 사용하는 전체 Flush 오버로드입니다.
    /// </summary>
    /// <param name="instance">DB 인스턴스 식별 값 객체</param>
    /// <param name="ct">취소 토큰</param>
    async Task FlushSchemaAsync(DbInstanceId instance, CancellationToken ct)
        => await FlushSchemaAsync(instance.Value, ct).ConfigureAwait(false);

    #endregion
}

#endregion

#region TVP 검증 및 훅 계약

/// <summary>
/// 애플리케이션 DTO 모델과 실제 데이터베이스 TVP 타입 간의
/// 구조적 일치 여부를 검증하는 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>조기 발견</b>: 잘못된 컬럼 순서나 타입으로 인한 런타임 오류를 실행 전에 감지합니다.<br/>
/// - <b>엄격한 검증</b>: 개발/테스트 환경에서는 검증을 켜고, 운영 환경에서는 성능을 위해 선택적으로 끌 수 있도록 설계되었습니다.
/// </para>
/// <para>
/// 검증 항목 예:
/// 컬럼 수/순서(Ordinal), 타입(SqlDbType), 길이(MaxLength),
/// 정밀도/스케일(Precision/Scale), NULL 허용 여부 등
/// </para>
/// </summary>
public interface ITvpSchemaValidator
{
    /// <summary>
    /// 지정된 TVP 타입에 대해 DTO 모델(행 타입)의 유효성을 검사합니다.
    /// </summary>
    /// <typeparam name="T">TVP 행(Row) 모델 타입</typeparam>
    /// <param name="tvpTypeName">DB에 정의된 TVP 타입 이름</param>
    /// <param name="accessors">소스 제너레이터 또는 캐시에서 제공되는 고속 접근자</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    Task ValidateAsync<T>(
        string tvpTypeName,
        TvpAccessors<T> accessors,
        string instanceHash,
        CancellationToken ct);
}

/// <summary>
/// Source Generator가 생성하는 정적 TVP 검증기 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>AOT 최적화</b>: 런타임 리플렉션 없이 컴파일 타임에 생성된 검증 로직을 사용하여 속도와 안전성을 모두 확보합니다.
/// </para>
/// <para>
/// 런타임 리플렉션 없이, 컴파일 타임에 확정된 규칙으로 구조를 검증합니다.
/// AOT 환경에서 특히 유리합니다.
/// </para>
/// </summary>
/// <typeparam name="T">TVP 행(Row) 모델 타입</typeparam>
public interface ITvpStaticValidator<T>
{
    /// <summary>
    /// 컴파일 타임에 확정된 규칙으로 TVP 스키마를 즉시 검증합니다.
    /// </summary>
    /// <param name="schema">DB에서 조회한 TVP 스키마</param>
    void ValidateStatic(TvpSchema schema);
}

/// <summary>
/// 스키마 Flush(무효화) 시 함께 초기화해야 하는 외부 캐시/엔진에 대한 콜백을 정의합니다.
/// <para>
/// <b>[AOT 호환성]</b><br/>
/// NativeAOT 환경에서 DI Enumerable 서비스(<c>IEnumerable&lt;SchemaFlushHook&gt;</c>)로
/// 안전하게 등록할 수 있도록 <c>sealed record</c>(참조형)로 정의합니다.
/// </para>
/// <para>
/// <b>주의:</b> ValueType(struct)은 AOT 환경에서 Enumerable 서비스 구성 시
/// 필요한 네이티브 코드 경로가 없어 런타임 오류를 유발할 수 있으므로 피합니다.
/// </para>
/// </summary>
/// <param name="Name">훅 식별자(로깅/디버깅용)</param>
/// <param name="Callback">스키마 무효화 시 실행할 콜백</param>
/// <remarks>
/// <b>성능 영향:</b> struct → class 전환으로 힙 할당이 발생하지만,
/// 훅은 일반적으로 초기화 시점에만 생성되므로 런타임 영향은 매우 제한적입니다.
/// </remarks>
public sealed record SchemaFlushHook(string Name, Action Callback);

#endregion

#region 스키마 예외

/// <summary>
/// TVP 스키마 검증 실패 시 발생하는 예외입니다.
/// <para>
/// 운영 환경에서 즉시 이해할 수 있도록 예외 메시지에 한글 정보를 포함합니다.
/// </para>
/// </summary>
/// <param name="tvpName">문제가 발생한 TVP 이름</param>
/// <param name="reason">실패 사유 코드(예: <c>스키마_컬럼수_불일치</c>)</param>
/// <param name="message">상세 에러 메시지</param>
/// <param name="columnName">관련 컬럼명(선택)</param>
/// <param name="ordinal">관련 컬럼 순서(선택)</param>
/// <param name="inner">내부 예외(선택)</param>
public sealed class TvpSchemaValidationException(
    string tvpName,
    string reason,
    string message,
    string? columnName = null,
    int? ordinal = null,
    Exception? inner = null)
    : InvalidOperationException(
        $"[TVP 검증 실패] 사유: {reason} / 상세: {message} (대상 TVP: {tvpName})",
        inner)
{
    /// <summary>
    /// 대상 TVP 이름입니다.
    /// </summary>
    public string TvpName { get; } = tvpName;

    /// <summary>
    /// 실패 사유 코드입니다. (예: <c>스키마_컬럼수_불일치</c>)
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    /// 문제가 발생한 컬럼 이름입니다. (없을 수 있음)
    /// </summary>
    public string? ColumnName { get; } = columnName;

    /// <summary>
    /// 문제가 발생한 컬럼 인덱스(Ordinal)입니다. (없을 수 있음)
    /// </summary>
    public int? Ordinal { get; } = ordinal;
}

#endregion

#region 분산 캐시 무효화 계약

/// <summary>
/// Epoch 기반 분산 스키마 캐시 무효화 조정자입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>이벤트적 일관성(Eventual Consistency)</b>: 모든 인스턴스가 절대 동시에 갱신되는 것은 불가능하므로, Epoch(논리적 시간)를 통해 순차적으로 최신 상태를 따라가도록 유도합니다.<br/>
/// - <b>리더 없는 조정</b>: 리더 선출 없이 Redis/DB의 원자적 카운터를 활용하여 간단하게 구현할 수 있는 Epoch 방식을 채택했습니다.
/// </para>
/// <para>
/// v9 FINAL: EpochStore를 활용하여 멀티 프로세스 환경에서
/// 스키마 Flush를 동기화합니다.
/// </para>
/// </summary>
public interface ISchemaFlushCoordinator
{
    /// <summary>
    /// 인스턴스의 스키마 캐시를 무효화하고 Epoch를 증가시킵니다.
    /// </summary>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    Task FlushAsync(string instanceHash, CancellationToken ct = default);
    
    /// <summary>
    /// 현재 Epoch 값을 가져옵니다.
    /// </summary>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    /// <returns>현재 Epoch 값</returns>
    long GetCurrentEpoch(string instanceHash);
    
    /// <summary>
    /// Epoch가 변경되었는지 확인하고, 변경되었다면 로컬 캐시를 무효화합니다.
    /// </summary>
    /// <param name="instanceHash">대상 DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>동기화가 필요했으면 true, 변경 없으면 false</returns>
    Task<bool> CheckAndSyncEpochAsync(string instanceHash, CancellationToken ct = default);
}

#endregion
