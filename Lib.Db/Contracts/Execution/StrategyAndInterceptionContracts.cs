// ============================================================================
// 파일명: Lib.Db/Contracts/Execution/StrategyAndInterceptionContracts.cs
// 설명  : 실행 전략 / 실행기 팩터리 / 인터셉터 / 인프라(회복 탄력성) 공용 계약 통합
// 대상  : .NET 10 / C# 14
// 역할  :
//   - 실행 전략(IDbExecutionStrategy): 연결/재시도/트랜잭션 정책 추상화
//   - 실행기 팩터리(IDbExecutorFactory): 전략별 실행기 조립 책임 분리
//   - 인터셉터(IDbCommandInterceptor): 실행 수명 주기 개입 및 진단/Mocking
//   - 인프라(IChaosInjector/IMemoryPressureMonitor/IResumableStateStore): 회복 탄력성 보조 계약
// ============================================================================

#nullable enable

using Lib;

namespace Lib.Db.Contracts.Execution;

#region 실행 전략 계약

/// <summary>
/// DB 연결 생성, 재시도(Resilience) 정책, 트랜잭션 관리를 담당하는 실행 전략 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>책임 분리</b>: 실행 로직(Executor)으로부터 "어떻게 연결하고 재시도할 것인가"에 대한 정책을 분리합니다.<br/>
/// - <b>전략 패턴</b>: 일반 실행, 트랜잭션 실행, 로깅 전용 실행 등 다양한 전략을 런타임에 교체할 수 있습니다.
/// </para>
/// <para>
/// 실제 DB 호출 로직은 실행기(Executor)에 존재하며,
/// 이 전략은 <b>어떻게 실행할 것인가</b>에 대한 정책만을 담당합니다.
/// </para>
/// </summary>
public interface IDbExecutionStrategy
{
    /// <summary>
    /// 현재 전략이 트랜잭션을 사용하는지 여부입니다.
    /// </summary>
    bool IsTransactional { get; }

    /// <summary>
    /// 현재 사용 중인 트랜잭션 객체입니다.
    /// <para>트랜잭션 전략이 아닐 경우 <c>null</c>일 수 있습니다.</para>
    /// </summary>
    SqlTransaction? CurrentTransaction { get; }

    /// <summary>
    /// 이 전략에서 기본으로 사용하는 스키마 해석 모드입니다.
    /// <para>
    /// 예: FullValidation, SnapshotOnly 등
    /// </para>
    /// </summary>
    SchemaResolutionMode DefaultSchemaMode { get; }

    /// <summary>
    /// 단일 실행 결과를 반환하는 일반 실행 메서드입니다.
    /// </summary>
    /// <typeparam name="TResult">실행 결과 타입</typeparam>
    /// <typeparam name="TParams">요청 파라미터 타입</typeparam>
    /// <param name="request">DB 실행 요청 정보</param>
    /// <param name="operation">실제 DB 작업 델리게이트</param>
    /// <param name="ct">취소 토큰</param>
    Task<TResult> ExecuteAsync<TResult, TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken ct);

    /// <summary>
    /// 스트리밍 결과를 반환하는 실행 메서드입니다.
    /// <para>
    /// 대량 조회 시 <see cref="DbDataReader"/>를 그대로 노출하여
    /// 메모리 사용량을 최소화할 수 있습니다.
    /// </para>
    /// </summary>
    /// <typeparam name="TParams">요청 파라미터 타입</typeparam>
    /// <param name="request">DB 실행 요청 정보</param>
    /// <param name="operation">실제 DB 작업 델리게이트</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>데이터 리더 또는 null</returns>
    Task<DbDataReader?> ExecuteStreamAsync<TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<SqlDataReader>> operation,
        CancellationToken ct);

    /// <summary>
    /// 현재 전략의 트랜잭션을 지정된 <see cref="SqlCommand"/>에 연결합니다.
    /// <para>
    /// 트랜잭션 전략이 아닐 경우 구현체가 무시할 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="cmd">트랜잭션에 참여시킬 DB 명령</param>
    void EnlistTransaction(SqlCommand cmd);
}

#endregion

#region 실행기 팩터리 계약

/// <summary>
/// DB 실행기(<see cref="IDbExecutor"/>)를 생성하는 팩터리 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>조립 책임</b>: 전략, 매퍼, 스키마 서비스 등 복잡한 의존성을 갖는 실행기를 조립하는 책임을 맡습니다.<br/>
/// - <b>상태 관리</b>: 트랜잭션 여부에 따라 상태를 갖는(Stateful) 실행기와 상태가 없는(Stateless) 실행기를 구분하여 생성합니다.
/// </para>
/// <para>
/// 실행 전략(Resilient / Transactional)에 따라
/// 서로 다른 실행기를 생성하는 책임을 가집니다.
/// </para>
/// </summary>
public interface IDbExecutorFactory
{
    /// <summary>
    /// 재시도/회복 탄력성(Resilience)을 적용한 일반 실행기를 생성합니다.
    /// </summary>
    IDbExecutor CreateResilient();

    /// <summary>
    /// 외부에서 제공된 트랜잭션을 사용하는
    /// Transactional 실행기를 생성합니다.
    /// </summary>
    /// <param name="conn">기존 SqlConnection</param>
    /// <param name="tx">공유할 SqlTransaction</param>
    IDbExecutor CreateTransactional(SqlConnection conn, SqlTransaction tx);
}


#endregion

#region DB 명령 인터셉터 계약

/// <summary>
/// DB 명령 실행 수명 주기에 개입하여
/// 로깅, 성능 측정, 실행 제어(Mock/억제)를 수행하는 인터셉터 계약입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>관점 지향 프로그래밍(AOP)</b>: 비즈니스 로직과 무관한 횡단 관심사(로깅, 메트릭, 보안 검사)를 모듈화합니다.<br/>
/// - <b>테스트 용이성</b>: 실제 DB 없이 인터셉터를 통해 결과를 Mocking 할 수 있는 후크(Hook)를 제공합니다.
/// </para>
/// <para>
/// 실행 흐름:
/// <list type="number">
/// <item>ReaderExecutingAsync (실행 직전)</item>
/// <item>ReaderExecutedAsync (성공 후)</item>
/// <item>CommandFailedAsync (실패 시)</item>
/// </list>
/// </para>
/// </summary>
public interface IDbCommandInterceptor
{
    #region [실행 전] 명령 실행 직전(Before)

    /// <summary>
    /// DB 명령이 실제로 실행되기 <b>직전</b>에 호출됩니다.
    /// <para>
    /// 이 단계에서 <paramref name="context"/>의
    /// <see cref="DbCommandInterceptionContext.SetResult"/>를 호출하면,
    /// 실제 DB 쿼리 실행을 <b>억제(Suppress)</b>하고
    /// 지정된 가짜(Mock) 결과를 즉시 반환할 수 있습니다.
    /// </para>
    /// <para>
    /// 주 사용 시나리오:
    /// <list type="bullet">
    /// <item>단위 테스트/통합 테스트용 Mock 결과 주입</item>
    /// <item>특정 조건에서 DB 접근 차단</item>
    /// <item>사전 캐시 히트 시 DB 실행 생략</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="command">실행 예정인 DB 명령 객체</param>
    /// <param name="context">실행 제어 및 상태 전달용 컨텍스트</param>
    ValueTask ReaderExecutingAsync(
        DbCommand command,
        DbCommandInterceptionContext context);

    #endregion

    #region [실행 후 - 성공] 명령 실행 완료(After Success)

    /// <summary>
    /// DB 명령이 <b>성공적으로 실행된 후</b> 호출됩니다.
    /// <para>
    /// 결과가 호출자에게 반환된 이후 시점이므로,
    /// 로깅, 성능 메트릭 기록, 결과 관찰 용도로 적합합니다.
    /// </para>
    /// </summary>
    /// <param name="command">실행이 완료된 DB 명령 객체</param>
    /// <param name="eventData">실행 결과 및 소요 시간 정보</param>
    ValueTask ReaderExecutedAsync(
        DbCommand command,
        DbCommandExecutedEventData eventData);

    #endregion

    #region [실행 후 - 실패] 명령 실행 실패(After Failure)

    /// <summary>
    /// DB 명령 실행 중 <b>예외가 발생했을 때</b> 호출됩니다.
    /// <para>
    /// 예외 분석, 오류 로깅, 재시도 정책 연계 등의 용도로 사용됩니다.
    /// </para>
    /// </summary>
    /// <param name="command">실행에 실패한 DB 명령 객체</param>
    /// <param name="eventData">예외 정보 및 실행 중단 시점까지의 소요 시간</param>
    ValueTask CommandFailedAsync(
        DbCommand command,
        DbCommandFailedEventData eventData);

    #endregion
}

#endregion

#region 실행 전 컨텍스트

/// <summary>
/// 인터셉터의 실행 전(Before) 단계에서
/// DB 실행 흐름을 제어하기 위한 컨텍스트 객체입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>제어권 역전</b>: 인터셉터가 실행 자체를 취소하거나 대체 결과를 제공할 수 있도록 제어권을 부여합니다.<br/>
/// - <b>상태 공유</b>: 비동기 흐름 내에서 상태를 유지하고 전달하기 위해 참조 타입(class)을 사용합니다.
/// </para>
/// <para>
/// 실제 DB 실행을 억제(Suppress)하거나,
/// 가짜(Mock) 결과를 주입할 수 있는 상태를 관리합니다.
/// </para>
/// <para>
/// 비동기 메서드 간 상태 전달이 필요하므로
/// 값 타입이 아닌 <c>class</c>로 설계되었습니다.
/// </para>
/// </summary>
/// <param name="instanceHash">현재 실행 흐름을 식별하는 고유 인스턴스 해시</param>
/// <param name="cancellationToken">요청 취소 토큰</param>
public sealed class DbCommandInterceptionContext(
    string instanceHash,
    CancellationToken cancellationToken)
{
    /// <summary>
    /// 현재 DB 실행 흐름의 고유 식별자 해시입니다.
    /// <para>멀티 DB/멀티 세션 환경에서 실행 컨텍스트를 구분하는 데 사용됩니다.</para>
    /// </summary>
    public string InstanceHash { get; } = instanceHash;

    /// <summary>
    /// 요청 취소를 전파하기 위한 취소 토큰입니다.
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>
    /// 실제 DB 명령 실행을 억제할지 여부입니다.
    /// <para>
    /// 기본값은 <c>false</c>이며,
    /// <see cref="SetResult"/> 호출 시 자동으로 <c>true</c>로 설정됩니다.
    /// </para>
    /// </summary>
    public bool SuppressExecution { get; private set; }

    /// <summary>
    /// 실행 억제 시 반환할 Mock 결과 객체입니다.
    /// <para>
    /// 예: DataReader, Scalar 값, 사용자 정의 결과 등
    /// </para>
    /// </summary>
    public object? MockResult { get; private set; }

    /// <summary>
    /// 실제 DB 실행을 건너뛰고,
    /// 지정된 가짜(Mock) 결과를 반환하도록 설정합니다.
    /// </summary>
    /// <param name="result">반환할 가짜 결과 객체</param>
    public void SetResult(object? result)
    {
        SuppressExecution = true;
        MockResult = result;
    }

    /// <summary>
    /// 기존에 설정된 실행 억제/Mock 결과를 초기화하고,
    /// 다시 실제 DB 실행 모드로 되돌립니다.
    /// </summary>
    public void Reset()
    {
        SuppressExecution = false;
        MockResult = null;
    }
}


#endregion

#region 실행 결과 이벤트 데이터

/// <summary>
/// DB 명령 실행이 성공적으로 완료된 후 전달되는 진단 데이터입니다.
/// <para>
/// <b>[성능 최적화]</b><br/>
/// 불변성을 보장하고 GC 할당을 최소화하기 위해
/// <c>readonly record struct</c>로 정의되었습니다.
/// </para>
/// </summary>
/// <param name="DurationUs">실행 소요 시간(마이크로초)</param>
/// <param name="Result">실행 결과(ExecuteScalar 결과 등, 없으면 null)</param>
public readonly record struct DbCommandExecutedEventData(
    long DurationUs,
    object? Result
);

/// <summary>
/// DB 명령 실행이 실패했을 때 전달되는 진단 데이터입니다.
/// </summary>
/// <param name="DurationUs">실행 중단 시점까지의 소요 시간(마이크로초)</param>
/// <param name="Exception">발생한 예외 객체</param>
public readonly record struct DbCommandFailedEventData(
    long DurationUs,
    Exception Exception
);

#endregion

#region 인프라 및 회복 탄력성 계약

/// <summary>
/// 카오스 엔지니어링(Chaos Engineering) 주입기 계약입니다.
/// <para>
/// 개발/테스트 환경에서 인위적인 지연, 예외, 타임아웃 등을 주입하여
/// 시스템의 회복 탄력성(Resilience)과 오류 처리 경로를 검증하는 데 사용됩니다.
/// </para>
/// <para>
/// 운영 환경에서는 비활성화되거나 매우 제한적으로만 사용되는 것이 일반적입니다.
/// </para>
/// </summary>
public interface IChaosInjector
{
    #region [카오스 주입] 지연/오류 인위적 삽입

    /// <summary>
    /// 설정된 정책에 따라 카오스(지연 또는 오류)를 주입합니다.
    /// <para>
    /// 구현체는 확률 기반(Random), 시나리오 기반, 환경 변수 기반 등
    /// 다양한 방식으로 동작할 수 있습니다.
    /// </para>
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task InjectAsync(CancellationToken ct);

    #endregion
}

/// <summary>
/// 메모리 배압(Backpressure) 모니터 계약입니다.
/// <para>
/// 현재 프로세스 및 시스템의 메모리 상태를 감시하여,
/// 대용량(Bulk) 작업의 스로틀링 또는 일시 중단 여부를 판단하는 데 사용됩니다.
/// </para>
/// </summary>
public interface IMemoryPressureMonitor
{
    #region [메모리 상태] 임계치 판단 및 부하율

    /// <summary>
    /// 메모리 사용량이 위험 임계치(예: 85%)를 초과했는지 여부입니다.
    /// <para>
    /// <c>true</c>인 경우, 대량 처리(Bulk Insert/Read 등)를
    /// 지연하거나 일시 중단하는 정책을 적용할 수 있습니다.
    /// </para>
    /// </summary>
    bool IsCritical { get; }

    /// <summary>
    /// 현재 메모리 부하율입니다.
    /// <para>
    /// 값의 범위는 <c>0.0 ~ 1.0</c>이며,
    /// 시스템 또는 프로세스 기준으로 계산됩니다.
    /// </para>
    /// </summary>
    double LoadFactor { get; }

    #endregion
}

/// <summary>
/// 상태 보존형 복구(Resumable) 저장소 계약입니다.
/// <para>
/// 대용량 배치 작업이나 스트리밍 처리 중
/// 작업이 중단되더라도 이전 진행 지점(Cursor)부터
/// 다시 이어서 처리할 수 있도록 상태를 외부 저장소에 기록합니다.
/// </para>
/// <para>
/// 외부 저장소 예:
/// <list type="bullet">
/// <item>Redis</item>
/// <item>Key-Value DB</item>
/// <item>파일 시스템</item>
/// </list>
/// </para>
/// </summary>
public interface IResumableStateStore
{
    #region [상태 저장] 처리 커서 기록

    /// <summary>
    /// 쿼리 또는 배치 작업의 마지막 처리 커서(Cursor)를 저장합니다.
    /// <para>
    /// 커서 타입은 작업 특성에 따라 자유롭게 정의할 수 있습니다.
    /// (예: ID, Timestamp, 복합 키 등)
    /// </para>
    /// </summary>
    /// <typeparam name="TCursor">커서 타입</typeparam>
    /// <param name="instanceKey">DB 인스턴스 또는 실행 컨텍스트 식별자</param>
    /// <param name="queryKey">쿼리/작업 식별자</param>
    /// <param name="cursor">저장할 커서 값</param>
    /// <param name="ct">취소 토큰</param>
    Task SaveCursorAsync<TCursor>(
        string instanceKey,
        string queryKey,
        TCursor cursor,
        CancellationToken ct);

    #endregion

    #region [상태 복원] 마지막 처리 지점 조회

    /// <summary>
    /// 저장된 마지막 처리 커서(Cursor)를 조회합니다.
    /// <para>
    /// 저장된 값이 없으면 <c>null</c>을 반환하여
    /// 처음부터 작업을 시작하도록 유도할 수 있습니다.
    /// </para>
    /// </summary>
    /// <typeparam name="TCursor">커서 타입</typeparam>
    /// <param name="instanceKey">DB 인스턴스 또는 실행 컨텍스트 식별자</param>
    /// <param name="queryKey">쿼리/작업 식별자</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>저장된 커서 값 또는 null</returns>
    Task<TCursor?> GetLastCursorAsync<TCursor>(
        string instanceKey,
        string queryKey,
        CancellationToken ct);

    #endregion
}

#endregion
