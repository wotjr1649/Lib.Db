// ============================================================================
// 파일명: Lib.Db/Contracts/Execution/DbExecutionContracts.cs
// 설명  : 데이터베이스 실행기 핵심 계약 + 실행 모델 통합 정의
// 대상  : .NET 10 / C# 14
// 역할  :
//   - 표준 쿼리/명령 실행의 단일 진입점(IDbExecutor)
//   - 대량(Bulk) 처리 및 파이프라인 기반 처리
//   - 장애 발생 시 자동 복구(Resumable) 스트림 제공
//   - 실행 파이프라인 공용 모델(SchemaResolutionMode, DbExecutionOptions, DbRequest)
// ============================================================================

#nullable enable

using Lib;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Lib.Db.Contracts.Execution;

#region 핵심 실행 로직

/// <summary>
/// 데이터베이스 실행기의 최상위 진입점입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>추상화 & 캡슐화</b>: 복잡한 실행 세부 사항(전략, 로깅, 재시도, 커넥션 관리)을 숨기고, 일관된 호출 방식을 제공합니다.<br/>
/// - <b>유연성</b>: <see cref="DbExecutionOptions"/>를 통해 실행별 동작을 미세 조정할 수 있습니다.<br/>
/// - <b>확장성</b>: 표준 쿼리뿐만 아니라 Bulk, Pipeline, Resumable 등 다양한 워크로드를 단일 인터페이스로 지원합니다.
/// </para>
/// <para>
/// 내부적으로 실행 전략(Resilient / Transactional),
/// 스키마 서비스, 매퍼, 인터셉터 체인을 조합하여
/// 실제 DB 작업을 수행합니다.
/// </para>
/// </summary>
public interface IDbExecutor
{
    #region 표준 쿼리 메서드

    /// <summary>
    /// 비동기 스트림 형태로 결과를 조회합니다.
    /// <para>
    /// 대량 결과를 메모리에 모두 적재하지 않고,
    /// <see cref="IAsyncEnumerable{T}"/> 형태로 순차 소비할 수 있습니다.
    /// </para>
    /// </summary>
    IAsyncEnumerable<TResult> QueryAsync<TParams, TResult>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct);

    /// <summary>
    /// 단일 결과 행을 조회합니다.
    /// <para>
    /// 결과가 없으면 <c>null</c>을 반환합니다.
    /// </para>
    /// </summary>
    Task<TResult?> QuerySingleAsync<TParams, TResult>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct);

    /// <summary>
    /// 단일 스칼라 값을 조회합니다. (1행 1열)
    /// <para>
    /// 예: COUNT(*), SUM(), IDENTITY 값 등
    /// </para>
    /// </summary>
    Task<TScalar?> ExecuteScalarAsync<TParams, TScalar>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct);

    /// <summary>
    /// 데이터 변경 명령(INSERT/UPDATE/DELETE)을 실행하고,
    /// 영향 받은 행 수만 반환합니다.
    /// </summary>
    Task<int> ExecuteNonQueryAsync<TParams>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct);

    /// <summary>
    /// 다중 결과 셋(ResultSet)을 반환하는 쿼리를 실행합니다.
    /// <para>
    /// 여러 SELECT 결과를 순차적으로 읽어야 하는
    /// 저장 프로시저 호출 시 사용됩니다.
    /// </para>
    /// </summary>
    Task<IMultipleResultReader> QueryMultipleAsync<TParams>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct);

    #endregion

    #region 대량 처리 메서드

    /// <summary>
    /// <see cref="Microsoft.Data.SqlClient.SqlBulkCopy"/>를 사용하여
    /// 지정된 테이블에 데이터를 고속 삽입합니다.
    /// <para>
    /// 대량 적재(ETL, 인터페이스 수신 등)에 최적화된 경로입니다.
    /// </para>
    /// </summary>
    Task BulkInsertAsync<T>(
        string destinationTableName,
        IEnumerable<T> data,
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// 임시 테이블 + MERGE 패턴을 사용하여
    /// 대량 업데이트를 수행합니다.
    /// </summary>
    Task BulkUpdateAsync<T>(
        string targetTableName,
        IEnumerable<T> data,
        string[] keyColumns,
        string[] updateColumns,
        string instanceHash,
        CancellationToken ct);

    /// <summary>
    /// 임시 테이블을 사용하여
    /// 대량 삭제를 수행합니다.
    /// </summary>
    Task BulkDeleteAsync<T>(
        string targetTableName,
        IEnumerable<T> data,
        string[] keyColumns,
        string instanceHash,
        CancellationToken ct);

    #endregion

    #region 파이프라인 및 복구형 처리 메서드

    /// <summary>
    /// [복구형 스트림]
    /// 네트워크 단절 또는 일시 오류 발생 시,
    /// 마지막 커서(Cursor) 위치부터 쿼리를 자동으로 재개하는
    /// 복원형(Resumable) 비동기 스트림을 생성합니다.
    /// <para>
    /// <b>TOP 기반 배치 페이징 완전 지원</b>
    /// <list type="bullet">
    /// <item>각 배치에서 1건 이상 조회되면 마지막 커서로 다음 배치 요청</item>
    /// <item>특정 배치에서 0건 조회 시 전체 스트림 종료</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <typeparam name="TCursor">커서 타입(ID, Timestamp 등)</typeparam>
    /// <typeparam name="TResult">결과 타입</typeparam>
    /// <param name="queryBuilder">커서 기반 쿼리 생성 함수</param>
    /// <param name="cursorSelector">결과에서 다음 커서를 추출하는 함수</param>
    /// <param name="instanceHash">DB 인스턴스 식별자</param>
    /// <param name="initialCursor">초기 커서 값</param>
    /// <param name="ct">취소 토큰</param>
    IAsyncEnumerable<TResult> QueryResumableAsync<TCursor, TResult>(
        Func<TCursor, string> queryBuilder,
        Func<TResult, TCursor> cursorSelector,
        string instanceHash,
        TCursor initialCursor = default!,
        CancellationToken ct = default);

    /// <summary>
    /// [파이프라인]
    /// <see cref="ChannelReader{T}"/>를 통해 실시간으로 유입되는 데이터를
    /// 내부 버퍼에 모아 Batch 단위로 Bulk Insert를 수행합니다.
    /// </summary>
    Task BulkInsertPipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string instanceHash,
        int batchSize = 5000,
        CancellationToken ct = default);

    /// <summary>
    /// [파이프라인]
    /// Channel 기반 입력 데이터를 Batch 단위로 묶어
    /// Bulk Update를 수행합니다.
    /// </summary>
    Task BulkUpdatePipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string[] keyColumns,
        string[] updateColumns,
        string instanceHash,
        int batchSize = 5000,
        CancellationToken ct = default);

    /// <summary>
    /// [파이프라인]
    /// Channel 기반 입력 데이터를 Batch 단위로 묶어
    /// Bulk Delete를 수행합니다.
    /// </summary>
    Task BulkDeletePipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string[] keyColumns,
        string instanceHash,
        int batchSize = 5000,
        CancellationToken ct = default);

    #endregion
}

#endregion

#region 다중 결과 셋 리더 정의

/// <summary>
/// 다중 결과 셋(ResultSet)을 순차적으로 읽기 위한 계약입니다.
/// <para>
/// 저장 프로시저에서 여러 SELECT 결과를 반환하는 경우 사용됩니다.
/// </para>
/// </summary>
public interface IMultipleResultReader : IAsyncDisposable
{
    /// <summary>
    /// 현재 ResultSet 전체를 리스트로 읽습니다.
    /// </summary>
    Task<List<T>> ReadAsync<T>(CancellationToken ct = default);

    /// <summary>
    /// 현재 ResultSet에서 단일 레코드만 읽습니다.
    /// <para>
    /// 결과가 없으면 <c>default</c>를 반환합니다.
    /// </para>
    /// </summary>
    Task<T?> ReadSingleAsync<T>(CancellationToken ct = default);
}

#endregion

#region 스키마 해석 전략 정의

/// <summary>
/// 쿼리 실행 시 스키마 정보를 어떤 경로에서 조회할지 결정하는 전략 열거형입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>성능/안정성 균형</b>: 모든 쿼리가 매번 스키마 서비스를 호출하면 병목이 될 수 있고, 반대로 스냅샷만 의존하면 최신 변경사항을 놓칠 수 있습니다.<br/>
/// - <b>유연한 제어</b>: 중요도나 호출 빈도에 따라 전략을 선택할 수 있도록 옵션을 제공합니다.
/// </para>
/// <para>
/// 실행 전략(Resilient / Transactional) 및 실행 옵션에 따라
/// 스키마 조회 비용과 안정성 간의 트레이드오프를 조절할 수 있습니다.
/// </para>
/// </summary>
public enum SchemaResolutionMode
{
    /// <summary>
    /// 스키마를 전혀 조회하지 않습니다.
    /// <para>
    /// Text 기반 쿼리 또는 스키마 검증이 불필요한 경우에 사용됩니다.
    /// </para>
    /// </summary>
    None = 0,

    /// <summary>
    /// L2/HybridCache 기반 스키마 서비스만 사용합니다.
    /// <para>
    /// <see cref="Schema.ISchemaService"/>를 통해 조회하며,
    /// 로컬 스냅샷(L1)은 사용하지 않습니다.
    /// </para>
    /// </summary>
    ServiceOnly,

    /// <summary>
    /// L1 스냅샷에서만 스키마를 조회합니다.
    /// <para>
    /// 스냅샷에 없거나 Negative Cache인 경우
    /// 즉시 예외가 발생합니다. (Fail-Fast)
    /// </para>
    /// </summary>
    SnapshotOnly,

    /// <summary>
    /// L1 스냅샷을 우선 조회하고,
    /// Miss 또는 Negative Cache인 경우 스키마 서비스로 폴백합니다.
    /// <para>
    /// 성능과 안정성의 균형을 위한 기본 권장 모드입니다.
    /// </para>
    /// </summary>
    SnapshotThenServiceFallback
}

#endregion

#region 실행 옵션 모델 정의

/// <summary>
/// 개별 DB 명령 실행 시 적용되는 내부 실행 옵션입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>불변성(Immutability) & 성능</b>: <c>readonly record struct</c>로 정의하여 값 복사 비용을 줄이고 스레드 안전성을 보장합니다.<br/>
/// - <b>세밀한 제어</b>: 전역 설정 대신, 특정 쿼리에 대해서만 타임아웃이나 스키마 모드를 변경해야 할 때 사용합니다.
/// </para>
/// <para>
/// 스키마 해석 모드 오버라이드, 명령 타임아웃 등
/// 실행기 내부 동작을 세밀하게 제어하는 용도로 사용됩니다.
/// </para>
/// <para>
/// <b>주의:</b>
/// 이 타입은 외부 사용자에게 직접 노출되지 않으며,
/// Fluent API 빌더와 실행기 구현 간의 내부 계약 용도로만 사용됩니다.
/// </para>
/// </summary>
public readonly record struct DbExecutionOptions(
    SchemaResolutionMode? SchemaModeOverride,
    int? CommandTimeout = null // 명령 단위 타임아웃(초) 오버라이드
)
{
    /// <summary>
    /// 기본 실행 옵션입니다.
    /// <para>
    /// 스키마 모드 및 타임아웃을 오버라이드하지 않습니다.
    /// </para>
    /// </summary>
    public static DbExecutionOptions Default => default;

    /// <summary>
    /// 지정한 스키마 해석 모드로 강제 오버라이드하는 옵션을 생성합니다.
    /// </summary>
    /// <param name="mode">강제 적용할 스키마 해석 모드</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DbExecutionOptions Override(SchemaResolutionMode mode)
        => new(mode);

    /// <summary>
    /// 명령 단위 타임아웃을 초 단위로 오버라이드하는 옵션을 생성합니다.
    /// </summary>
    /// <param name="seconds">타임아웃(초)</param>
    public static DbExecutionOptions WithTimeout(int seconds)
        => new(null, seconds);
}

#endregion

#region 실행 요청 모델 정의

/// <summary>
/// DB 명령 실행을 위한 불변(Immutable) 요청 모델입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>컨텍스트 전달</b>: 실행 파이프라인(전략 -> 인터셉터 -> 실행기 -> ADO.NET) 전반에 걸쳐 동일한 요청 정보를 효율적으로 전달합니다.<br/>
/// - <b>불변성</b>: 실행 도중 요청 내용이 변경되지 않음을 보장하여 디버깅과 동시성 제어를 용이하게 합니다.
/// </para>
/// <para>
/// 실행 전략, 인터셉터, 실행기 전반에 걸쳐
/// 동일한 요청 컨텍스트를 전달하기 위한 표준 형태입니다.
/// </para>
/// </summary>
/// <typeparam name="TParams">명령 파라미터 타입</typeparam>
/// <param name="InstanceHash">대상 DB 인스턴스 식별자</param>
/// <param name="CommandText">실행할 SQL 또는 저장 프로시저 이름</param>
/// <param name="CommandType">명령 타입(Text / StoredProcedure)</param>
/// <param name="Parameters">명령 파라미터 객체</param>
/// <param name="CancellationToken">요청 취소 토큰</param>
/// <param name="IsTransactional">트랜잭션 실행 여부</param>
public readonly record struct DbRequest<TParams>(
    string InstanceHash,
    string CommandText,
    CommandType CommandType,
    TParams Parameters,
    CancellationToken CancellationToken,
    bool IsTransactional
);

#endregion
