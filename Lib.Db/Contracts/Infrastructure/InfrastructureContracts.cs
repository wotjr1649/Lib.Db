// ============================================================================
// 파일명: Lib.Db/Contracts/Infrastructure/InfrastructureContracts.cs
// 역할  : DB 연결, 스키마 메타데이터 조회를 위한 인프라 계약 및 DTO 정의
// 설명  :
//   - SqlConnection 생성 및 Ad-hoc 연결 관리
//   - sys.* 뷰 기반 스키마 메타데이터 조회 계약
//   - 대량 스키마 로딩(Bulk)용 강타입 데이터 컨테이너 정의
// ============================================================================

#nullable enable

namespace Lib.Db.Contracts.Infrastructure;

#region 스키마 메타데이터 모델 정의

/// <summary>
/// 저장 프로시저(SP)의 메타데이터 불변 객체입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>불변성(Immutability)</b>: 스키마 캐시에 장기간 보관되므로, 생성 후 변경되지 않음을 보장합니다.<br/>
/// - <b>참조 타입(record class)</b>: 파라미터 리스트 등 힙 할당이 필수적인 필드를 포함하므로, 구조체보다는 클래스가 적합합니다.
/// </para>
/// <para>
/// DB 시스템 뷰(sys.objects, sys.parameters 등)에서 조회한 결과를
/// 애플리케이션 계층으로 전달하기 위한 DTO입니다.
/// </para>
/// </summary>
/// <param name="Version">
/// 객체 수정일(<c>modify_date</c>) 기반 버전 값입니다.
/// <para>일반적으로 <see cref="DateTime.Ticks"/> 형태로 사용됩니다.</para>
/// </param>
/// <param name="Parameters">저장 프로시저 파라미터 상세 목록</param>
public sealed record SpMetadata(
    long Version,
    List<SpParameterInfo> Parameters
);

/// <summary>
/// 저장 프로시저 파라미터의 상세 메타데이터입니다.
/// <para>
/// <b>[메모리 최적화]</b><br/>
/// <c>readonly record struct</c>를 사용하여
/// 힙 할당을 피하고 스택 기반 전달이 가능하도록 설계되었습니다.
/// </para>
/// </summary>
/// <param name="Name">파라미터 이름(@UserId 등)</param>
/// <param name="TypeName">SQL 타입 이름(NVARCHAR, INT 등)</param>
/// <param name="MaxLength">최대 길이(문자열/바이너리 계열)</param>
/// <param name="Precision">정밀도(Decimal 계열)</param>
/// <param name="Scale">스케일(Decimal 계열)</param>
/// <param name="IsOutput">OUTPUT 또는 INPUTOUTPUT 여부</param>
/// <param name="IsNullable">NULL 허용 여부</param>
/// <param name="HasDefault">DEFAULT 값 존재 여부</param>
/// <param name="UdtName">사용자 정의 타입 이름(UDT/TVP, 해당 시)</param>
public readonly record struct SpParameterInfo(
    string Name,
    string TypeName,
    int MaxLength,
    int Precision,
    int Scale,
    bool IsOutput,
    bool IsNullable,
    bool HasDefault,
    string? UdtName
);

/// <summary>
/// 테이블 반환 매개변수(TVP)의 메타데이터 불변 객체입니다.
/// </summary>
/// <param name="Version">
/// TVP 타입 수정일(<c>modify_date</c>) 기반 버전 값
/// </param>
/// <param name="Columns">TVP 컬럼 메타데이터 목록</param>
public sealed record TvpMetadata(
    long Version,
    List<TvpColumnInfo> Columns
);

/// <summary>
/// TVP 컬럼의 상세 메타데이터입니다.
/// <para>
/// <b>[메모리 최적화]</b><br/>
/// 대량 컬럼 비교 및 검증 시 GC 부담을 줄이기 위해
/// <c>record struct</c>를 사용합니다.
/// </para>
/// </summary>
/// <param name="Name">컬럼 이름</param>
/// <param name="TypeName">SQL 타입 이름</param>
/// <param name="Ordinal">컬럼 순서(0-based)</param>
/// <param name="MaxLength">최대 길이</param>
/// <param name="Precision">정밀도</param>
/// <param name="Scale">스케일</param>
/// <param name="IsIdentity">IDENTITY 컬럼 여부</param>
/// <param name="IsComputed">계산 컬럼 여부</param>
/// <param name="IsNullable">NULL 허용 여부</param>
public readonly record struct TvpColumnInfo(
    string Name,
    string TypeName,
    int Ordinal,
    int MaxLength,
    int Precision,
    int Scale,
    bool IsIdentity,
    bool IsComputed,
    bool IsNullable
);

/// <summary>
/// 특정 스키마(dbo 등)에 포함된 모든 객체(SP/TVP)의
/// 메타데이터를 한 번에 담는 대량 데이터 컨테이너입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>조회 성능</b>: O(1) 조회를 위해 DataTable 대신 강타입 Dictionary/List 구조를 사용합니다.<br/>
/// - <b>초기화 최적화</b>: 애플리케이션 시작 시 대량의 스키마 정보를 한 번에 로드(Warming-up)하여 런타임 지연을 방지합니다.
/// </para>
/// </summary>
public sealed class SchemaBulkData
{
    /// <summary>
    /// 저장 프로시저 버전 정보
    /// <para>Key: SP 이름, Value: 버전</para>
    /// </summary>
    public Dictionary<string, long> SpVersions { get; init; } = [];

    /// <summary>
    /// TVP 타입 버전 정보
    /// <para>Key: TVP 이름, Value: 버전</para>
    /// </summary>
    public Dictionary<string, long> TvpVersions { get; init; } = [];

    /// <summary>
    /// 저장 프로시저 파라미터 정보
    /// <para>Key: SP 이름, Value: 파라미터 목록</para>
    /// </summary>
    public Dictionary<string, List<SpParameterInfo>> SpParameters { get; init; } = [];

    /// <summary>
    /// TVP 컬럼 정보
    /// <para>Key: TVP 이름, Value: 컬럼 목록</para>
    /// </summary>
    public Dictionary<string, List<TvpColumnInfo>> TvpColumns { get; init; } = [];
}

#endregion

#region 인프라 서비스 인터페이스 정의

/// <summary>
/// DB 연결 문자열 관리 및 <see cref="SqlConnection"/> 생성을 담당하는 팩터리 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>연결 관리 추상화</b>: 단순 <c>new SqlConnection()</c>을 넘어, 인스턴스 해시 기반 조회 및 Ad-hoc 연결 관리 기능을 캡슐화합니다.<br/>
/// - <b>테스트 유연성</b>: 실제 DB 연결 대신 In-memory Mock 연결 등을 주입하여 테스트 범위를 확장할 수 있습니다.
/// </para>
/// <para>
/// 다중 DB 인스턴스, Ad-hoc 연결, 테스트 환경 등을
/// 일관된 방식으로 관리하기 위해 사용됩니다.
/// </para>
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// 인스턴스 해시 또는 Raw ConnectionString을 사용하여
    /// 새로운 DB 연결을 비동기로 생성합니다.
    /// </summary>
    /// <param name="instanceHash">DB 인스턴스 해시 또는 Raw 연결 문자열</param>
    /// <param name="ct">취소 토큰</param>
    Task<Microsoft.Data.SqlClient.SqlConnection> CreateConnectionAsync(
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// <see cref="DbInstanceId"/>를 사용하는 타입 안전(Type-Safe) 오버로드입니다.
    /// </summary>
    /// <param name="instanceId">DB 인스턴스 식별자 값 객체</param>
    /// <param name="ct">취소 토큰</param>
    async Task<Microsoft.Data.SqlClient.SqlConnection> CreateConnectionAsync(
        DbInstanceId instanceId,
        CancellationToken ct)
        => await CreateConnectionAsync(instanceId.Value, ct).ConfigureAwait(false);

    /// <summary>
    /// Ad-hoc(임시) 연결 문자열을 런타임에 동적으로 등록합니다.
    /// <para>
    /// <b>[사용 시나리오]</b><br/>
    /// <c>DbSession.UseConnectionString()</c> 호출 시
    /// 임시 인스턴스 이름으로 연결 문자열을 등록합니다.
    /// </para>
    /// </summary>
    /// <param name="instanceName">임시 인스턴스 이름 (예: "__adhoc_xxx")</param>
    /// <param name="connectionString">실제 연결 문자열</param>
    void RegisterAdHocInstance(string instanceName, string connectionString);

    /// <summary>
    /// 런타임에 등록된 Ad-hoc 연결 문자열을 제거합니다.
    /// <para>
    /// <b>[호출 시점]</b> DbSession Dispose 시
    /// 임시 인스턴스를 정리하는 용도로 사용됩니다.
    /// </para>
    /// </summary>
    /// <param name="instanceName">제거할 임시 인스턴스 이름</param>
    void UnregisterAdHocInstance(string instanceName);
}

/// <summary>
/// SQL Server 시스템 뷰(sys.*)에 직접 접근하여
/// 스키마 메타데이터를 조회하는 저장소(Repository) 계약입니다.
/// <para>
/// DbSchema / SchemaService 계층의 최하단에서
/// "실제 DB 조회"만 담당하는 역할을 합니다.
/// </para>
/// </summary>
internal interface ISchemaRepository
{
    /// <summary>
    /// 단일 DB 객체(SP 또는 TVP)의 버전을 조회합니다.
    /// </summary>
    Task<long> GetObjectVersionAsync(
        string name,
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// 저장 프로시저(SP)의 메타데이터(버전 + 파라미터 목록)를 조회합니다.
    /// </summary>
    Task<SpMetadata> GetSpMetadataAsync(
        string name,
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// TVP 타입의 메타데이터(버전 + 컬럼 목록)를 조회합니다.
    /// </summary>
    Task<TvpMetadata> GetTvpMetadataAsync(
        string name,
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// TVP 타입의 버전 정보만 조회합니다.
    /// </summary>
    Task<long> GetTvpVersionAsync(
        string name,
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// 특정 스키마(dbo 등)에 포함된 모든 객체(SP/TVP)의
    /// 메타데이터를 한 번에 대량(Bulk)으로 조회합니다.
    /// <para>
    /// <b>[선택적 필터링]</b><br/>
    /// <paramref name="includePatterns"/>이 비어있으면 모든 객체를 조회합니다.<br/>
    /// 패턴이 지정되면 매칭되는 객체만 조회합니다.<br/>
    /// <paramref name="excludePatterns"/>로 제외할 패턴을 지정할 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="schemaName">스키마 이름 (예: "dbo")</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="includePatterns">포함할 객체 이름 패턴 목록 (* 문법)</param>
    /// <param name="excludePatterns">제외할 객체 이름 패턴 목록 (* 문법)</param>
    /// <param name="ct">취소 토큰</param>
    Task<SchemaBulkData> GetAllSchemaMetadataAsync(
        string schemaName,
        string instanceHash,
        List<string> includePatterns,
        List<string> excludePatterns,
        CancellationToken ct);
}

#endregion
