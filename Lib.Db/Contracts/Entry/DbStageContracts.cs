// ============================================================================
// 파일명: Lib.Db/Contracts/Access/IStages.cs
// 설명  : Fluent 접근 API의 단계(Stage) 인터페이스 정의
// 대상  : .NET 10 / C# 14
// 역할  :
//   - 1단계: 실행할 명령(Procedure/Text) 또는 Bulk/Pipeline/Resumable 작업 선택
//   - 2단계: 파라미터/타임아웃 등 실행 옵션 지정
//   - 3단계: 최종 실행 및 결과 조회(Query/Scalar/NonQuery/Multiple)
// ============================================================================

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.Contracts.Entry;

#region 명령 선택 단계
// 1단계: SP / SQL / Bulk / Pipeline / Resumable

/// <summary>
/// 1단계: 실행할 명령(SP/Text) 또는 Bulk/Pipeline/Resumable 작업을 지정하는 단계입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>가독성(Fluent API)</b>: 자연어와 유사한 흐름으로 코드를 작성할 수 있도록 합니다.<br/>
/// - <b>타입 안전성</b>: 단계별로 인터페이스를 분리하여, 올바른 순서(명령 -> 파라미터 -> 실행)로만 호출할 수 있도록 강제합니다.
/// </para>
/// <para>
/// Fluent API의 시작점이며, 여기서 명령 종류를 확정한 후 파라미터 단계로 진행합니다.
/// </para>
/// </summary>
public interface IProcedureStage
{
    #region 명령 정의 (SP, Raw SQL, 보간 SQL)

    /// <summary>
    /// 저장 프로시저(SP) 이름을 지정합니다.
    /// <para>예: <c>"dbo.usp_GetUser"</c></para>
    /// </summary>
    /// <param name="spName">저장 프로시저의 정규 이름</param>
    /// <returns>파라미터를 설정하는 2단계 인터페이스</returns>
    IParameterStage Procedure(string spName);

    /// <summary>
    /// Raw SQL 텍스트를 지정합니다.
    /// <para>예: <c>"SELECT * FROM ..."</c></para>
    /// </summary>
    /// <param name="sqlText">실행할 SQL 텍스트</param>
    /// <returns>파라미터를 설정하는 2단계 인터페이스</returns>
    IParameterStage Sql(string sqlText);

    /// <summary>
    /// 문자열 보간(<c>$</c>)을 사용하여 SQL과 파라미터를 동시에 지정합니다.
    /// <para>
    /// SQL Injection 방지를 위해 보간 인수는 자동으로 파라미터화 처리됩니다.
    /// </para>
    /// </summary>
    /// <param name="sql">보간 문자열로 표현된 SQL</param>
    /// <returns>확정 파라미터 타입이 Dictionary인 3단계 실행 인터페이스</returns>
    IExecutionStage<Dictionary<string, object?>> Sql(FormattableString sql);

    /// <summary>
    /// [최적화] <c>params ReadOnlySpan</c>을 사용하여 배열 할당 없이 파라미터화된 SQL을 생성합니다.
    /// <para>대량의 파라미터 바인딩 시 GC 부하를 줄이는 데 유리합니다.</para>
    /// </summary>
    /// <param name="sqlFormat">복합 포맷 문자열</param>
    /// <param name="args">포맷 인수(배열 할당 없이 전달)</param>
    /// <returns>확정 파라미터 타입이 Dictionary인 3단계 실행 인터페이스</returns>
    IExecutionStage<Dictionary<string, object?>> Sql(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string sqlFormat,
        params ReadOnlySpan<object?> args);

    #endregion

    #region 대량 처리 (Bulk Insert/Update/Delete)

    /// <summary>
    /// 대량 데이터를 고속으로 삽입합니다. (Bulk Insert)
    /// </summary>
    /// <typeparam name="T">행(레코드) 타입</typeparam>
    /// <param name="destinationTableName">대상 테이블명</param>
    /// <param name="data">삽입할 데이터</param>
    /// <param name="ct">취소 토큰</param>
    Task BulkInsertAsync<T>(
        string destinationTableName,
        IEnumerable<T> data,
        CancellationToken ct = default);

    /// <summary>
    /// 대량 데이터를 고속으로 업데이트합니다. (Bulk Update)
    /// </summary>
    /// <typeparam name="T">행(레코드) 타입</typeparam>
    /// <param name="targetTableName">대상 테이블명</param>
    /// <param name="data">업데이트할 데이터</param>
    /// <param name="keyColumns">키 컬럼 목록</param>
    /// <param name="updateColumns">업데이트 컬럼 목록</param>
    /// <param name="ct">취소 토큰</param>
    Task BulkUpdateAsync<T>(
        string targetTableName,
        IEnumerable<T> data,
        string[] keyColumns,
        string[] updateColumns,
        CancellationToken ct = default);

    /// <summary>
    /// 대량 데이터를 고속으로 삭제합니다. (Bulk Delete)
    /// </summary>
    /// <typeparam name="T">행(레코드) 타입</typeparam>
    /// <param name="targetTableName">대상 테이블명</param>
    /// <param name="data">삭제 기준 데이터</param>
    /// <param name="keyColumns">키 컬럼 목록</param>
    /// <param name="ct">취소 토큰</param>
    Task BulkDeleteAsync<T>(
        string targetTableName,
        IEnumerable<T> data,
        string[] keyColumns,
        CancellationToken ct = default);

    #endregion

    #region 파이프라인 처리 (Channel 기반 스트리밍)

    /// <summary>
    /// [파이프라인] Channel을 통해 유입되는 데이터를 버퍼링하여 배치 단위로 Bulk Insert 합니다.
    /// <para>메모리 사용량을 일정하게 유지하면서 실시간 데이터를 처리할 때 유용합니다.</para>
    /// </summary>
    /// <typeparam name="T">행(레코드) 타입</typeparam>
    /// <param name="tableName">대상 테이블명</param>
    /// <param name="reader">입력 채널 리더</param>
    /// <param name="batchSize">배치 크기(기본 5000)</param>
    /// <param name="ct">취소 토큰</param>
    Task BulkInsertPipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        int batchSize = 5000,
        CancellationToken ct = default);

    /// <summary>
    /// [파이프라인] Channel을 통해 유입되는 데이터를 버퍼링하여 배치 단위로 Bulk Update 합니다.
    /// </summary>
    /// <typeparam name="T">행(레코드) 타입</typeparam>
    /// <param name="tableName">대상 테이블명</param>
    /// <param name="reader">입력 채널 리더</param>
    /// <param name="keyColumns">키 컬럼 목록</param>
    /// <param name="updateColumns">업데이트 컬럼 목록</param>
    /// <param name="batchSize">배치 크기(기본 5000)</param>
    /// <param name="ct">취소 토큰</param>
    Task BulkUpdatePipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string[] keyColumns,
        string[] updateColumns,
        int batchSize = 5000,
        CancellationToken ct = default);

    /// <summary>
    /// [파이프라인] Channel을 통해 유입되는 데이터를 버퍼링하여 배치 단위로 Bulk Delete 합니다.
    /// </summary>
    /// <typeparam name="T">행(레코드) 타입</typeparam>
    /// <param name="tableName">대상 테이블명</param>
    /// <param name="reader">입력 채널 리더</param>
    /// <param name="keyColumns">키 컬럼 목록</param>
    /// <param name="batchSize">배치 크기(기본 5000)</param>
    /// <param name="ct">취소 토큰</param>
    Task BulkDeletePipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string[] keyColumns,
        int batchSize = 5000,
        CancellationToken ct = default);

    #endregion

    #region 복구형 조회 (Resumable Query)

    /// <summary>
    /// [복구형 스트림]
    /// 네트워크 단절 등 일시 장애 발생 시 마지막 커서 위치부터 쿼리를 자동 재개하는 복원형 스트림을 생성합니다.
    /// </summary>
    /// <typeparam name="TCursor">커서 타입(예: <c>long</c>, <c>DateTime</c>)</typeparam>
    /// <typeparam name="TResult">결과 레코드 타입</typeparam>
    /// <param name="queryBuilder">현재 커서 값을 기반으로 실행할 SQL을 생성하는 함수</param>
    /// <param name="cursorSelector">결과 레코드에서 다음 커서 값을 추출하는 함수</param>
    /// <param name="initialCursor">초기 커서 값(기본값 사용 시 반드시 호출자가 의도 확인)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>복원형 비동기 결과 스트림</returns>
    IAsyncEnumerable<TResult> QueryResumableAsync<TCursor, TResult>(
        Func<TCursor, string> queryBuilder,
        Func<TResult, TCursor> cursorSelector,
        TCursor initialCursor = default!,
        CancellationToken ct = default);

    #endregion
}

#endregion

#region 파라미터 설정 단계
// 2단계: 파라미터/타임아웃 확정

/// <summary>
/// 2단계: 파라미터 설정 단계입니다.
/// <para>명령(SP/SQL)을 선택한 후, 실행에 필요한 파라미터와 옵션을 확정합니다.</para>
/// </summary>
public interface IParameterStage : IExecutionStage<object>
{
    #region 파라미터 설정 (DTO, 익명 타입, Dictionary)

    /// <summary>
    /// 실행에 필요한 파라미터 객체(DTO, Anonymous Type 등)를 설정합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 타입</typeparam>
    /// <param name="parameters">파라미터 객체</param>
    /// <returns>확정 파라미터 타입을 갖는 3단계 실행 인터페이스</returns>
    IExecutionStage<TParams> With<TParams>(TParams parameters);

    #endregion

    #region 실행 옵션 (타임아웃 등)

    /// <summary>
    /// 명령 실행 타임아웃을 초 단위로 설정합니다.
    /// <para>내부적으로 <see cref="DbExecutionOptions.CommandTimeout"/> 오버라이드로 반영될 수 있습니다.</para>
    /// </summary>
    /// <param name="timeoutSeconds">타임아웃(초)</param>
    /// <returns>동일 파라미터 단계(체이닝 지원)</returns>
    IParameterStage WithTimeout(int timeoutSeconds);

    #endregion
}

#endregion

#region 실행 및 조회 단계
// 3단계: Query / Scalar / NonQuery / Multiple

/// <summary>
/// 3단계: 최종 실행 및 결과 조회 단계입니다.
/// <para>파라미터 타입이 확정된 상태에서 실제 DB 작업을 수행합니다.</para>
/// </summary>
/// <typeparam name="TParams">확정된 파라미터 타입</typeparam>
public interface IExecutionStage<in TParams>
{
    #region 조회 (스트림, 단건)

    /// <summary>
    /// 결과를 비동기 스트림(<see cref="IAsyncEnumerable{T}"/>)으로 조회합니다.
    /// </summary>
    /// <typeparam name="TResult">결과 타입</typeparam>
    /// <param name="ct">취소 토큰</param>
    IAsyncEnumerable<TResult> QueryAsync<TResult>(CancellationToken ct = default);

    /// <summary>
    /// 단일 결과를 조회합니다.
    /// <para>결과가 없으면 <c>null</c>을 반환합니다.</para>
    /// </summary>
    /// <typeparam name="TResult">결과 타입</typeparam>
    /// <param name="ct">취소 토큰</param>
    Task<TResult?> QuerySingleAsync<TResult>(CancellationToken ct = default);

    #endregion

    #region 스칼라 (1행 1열)

    /// <summary>
    /// 단일 스칼라 값(1행 1열)을 조회합니다.
    /// </summary>
    /// <typeparam name="TScalar">스칼라 타입</typeparam>
    /// <param name="ct">취소 토큰</param>
    Task<TScalar?> ExecuteScalarAsync<TScalar>(CancellationToken ct = default);

    #endregion

    #region 다중 결과 (GridReader)

    /// <summary>
    /// 다중 결과 셋(GridReader)을 조회합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task<IMultipleResultReader> QueryMultipleAsync(CancellationToken ct = default);

    #endregion

    #region 명령 실행 (NonQuery)

    /// <summary>
    /// 결과 조회 없이 명령을 실행하고 영향 받은 행 수를 반환합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task<int> ExecuteAsync(CancellationToken ct = default);

    #endregion
}

#endregion
