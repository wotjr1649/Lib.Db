// ============================================================================
// 파일명: Lib.Db/Contracts/Entry/DbStageContracts.cs
// 설명  : Fluent 접근 API의 단계(Stage) 인터페이스 정의
// 대상  : .NET 10 / C# 14
// 역할  :
//   - 1단계: 실행할 명령(Procedure/Text) 선택
//   - 2단계: 파라미터/타임아웃 등 실행 옵션 지정
//   - 3단계: 최종 실행 및 결과 조회(Query/Scalar/NonQuery/Multiple)
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.Contracts.Entry;

#region 명령 선택 단계
// 1단계: SP / SQL

/// <summary>
/// 1단계: 실행할 명령(SP/Text)을 지정하는 단계입니다.
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
    /// <para>
    /// 이 오버로드는 SQL 텍스트를 그대로 실행기로 전달합니다. 사용자 입력을 문자열 결합으로 포함하지 말고,
    /// 값 바인딩은 <see cref="Sql(FormattableString)"/>, <see cref="SqlInterpolated(FormattableString)"/> 또는 <see cref="IParameterStage.With{TParams}(TParams)"/>를 사용하세요.
    /// </para>
    /// <para>예: <c>"SELECT * FROM Users WHERE Id = @Id"</c></para>
    /// </summary>
    /// <param name="sqlText">실행할 SQL 텍스트</param>
    /// <returns>파라미터를 설정하는 2단계 인터페이스</returns>
    IParameterStage Sql(string sqlText);

    /// <summary>
    /// 문자열 보간(<c>$</c>)을 사용하여 SQL과 파라미터를 동시에 지정합니다.
    /// <para>
    /// 보간 값 인수는 자동으로 파라미터화되어 값 기반 SQL injection 위험을 줄입니다.
    /// </para>
    /// </summary>
    /// <param name="sql">보간 문자열로 표현된 SQL</param>
    /// <returns>보간 인수로 생성된 파라미터를 유지하는 2단계 인터페이스. 추가 <c>With(...)</c> 호출은 충돌 없는 명명 파라미터를 병합합니다.</returns>
    IParameterStage Sql(FormattableString sql);

    /// <summary>
    /// 문자열 보간(<c>$</c>)을 사용하여 SQL과 파라미터를 동시에 지정합니다.
    /// <para>
    /// <see cref="Sql(FormattableString)"/>와 동일한 동작을 하는 명시적 이름의 파라미터화 API입니다.
    /// Raw SQL 오버로드와 혼동을 줄이려면 이 메서드를 우선 사용하세요.
    /// </para>
    /// </summary>
    /// <param name="sql">보간 문자열로 표현된 SQL</param>
    /// <returns>보간 인수로 생성된 파라미터를 유지하는 2단계 인터페이스. 추가 <c>With(...)</c> 호출은 충돌 없는 명명 파라미터를 병합합니다.</returns>
    IParameterStage SqlInterpolated(FormattableString sql)
        => Sql(sql);

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
/// <para>파라미터 타입이 확정된 상태에서 실제 DB 작업을 수행하며,
/// 모든 반환값은 <see cref="DbResult{T}"/>로 래핑되어 성공/실패를 명시적으로 구분합니다.</para>
/// </summary>
/// <typeparam name="TParams">확정된 파라미터 타입</typeparam>
public interface IExecutionStage<in TParams>
{
    #region 조회 (스트림, 단건)

    /// <summary>
    /// 결과를 비동기 스트림(<see cref="IAsyncEnumerable{T}"/>)으로 조회합니다.
    /// <para>스트림 생성 시점에서 발생하는 오류는 <see cref="DbResult{T}"/>로 래핑됩니다.</para>
    /// </summary>
    /// <typeparam name="TResult">결과 타입</typeparam>
    /// <param name="ct">취소 토큰</param>
    /// <returns>스트림을 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default);

    /// <summary>
    /// 단일 결과를 조회합니다.
    /// <para>결과가 없으면 <c>null</c>을 값으로 갖는 성공 결과를 반환합니다.</para>
    /// </summary>
    /// <typeparam name="TResult">결과 타입</typeparam>
    /// <param name="ct">취소 토큰</param>
    /// <returns>단일 결과를 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default);

    #endregion

    #region 스칼라 (1행 1열)

    /// <summary>
    /// 단일 스칼라 값(1행 1열)을 조회합니다.
    /// </summary>
    /// <typeparam name="TScalar">스칼라 타입</typeparam>
    /// <param name="ct">취소 토큰</param>
    /// <returns>스칼라 값을 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default);

    #endregion

    #region 다중 결과 (GridReader)

    /// <summary>
    /// 다중 결과 셋(GridReader)을 조회합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>다중 결과 리더를 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default);

    #endregion

    #region 명령 실행 (NonQuery)

    /// <summary>
    /// 결과 조회 없이 명령을 실행하고 영향 받은 행 수를 반환합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>영향 받은 행 수를 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default);

    #endregion
}

#endregion
