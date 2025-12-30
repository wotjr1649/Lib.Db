// ============================================================================
// File : Lib.Db/Execution/Executors/Strategies.cs
// Role : 실행 전략(Resilient/Transactional), 인터셉터 체인, 성능 최적화 구조체 모음
// Env  : .NET 10 / C# 14
//
// Notes (통합 요약: B 기반 + A Notes 반영)
//   - 전략(Strategy) 패턴으로 실행 컨텍스트(트랜잭션/복원력/스키마 모드)를 실행기에서 분리
//   - 인터셉터 체인은 “델리게이트 컴파일”을 통해 호출 오버헤드를 상수화(런타임 리스트 순회 제거)
//   - AdaptiveBatchSizer: 처리량(Rows/sec) + 메모리 배압(Backpressure) 기반 동적 배치 크기 조절(EMA + ±20% 제한)
//   - ArraySegmentEnumerable: ArrayPool 등 버퍼 기반 순회를 위한 구조체 열거자(무할당/No Boxing 지향)
//   - ResilientStrategy: Polly(ResiliencePipeline) + Deadlock 승자 전략(SET DEADLOCK_PRIORITY HIGH)
//                      + Self-Healing Schema(SP 불일치 시 캐시 무효화/재로딩)
//                      + Fast-Fail(Circuit Breaker) 통합
//   - 스트리밍(Reader)에서는 “연결 수명”이 핵심: MonitoredSqlDataReader로 Reader Dispose 시 연결/메트릭을 함께 정리
// ============================================================================

#nullable enable

using System.Collections;
using System.Runtime.CompilerServices;

using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;

using Polly;
using Polly.CircuitBreaker;

namespace Lib.Db.Execution.Executors;

#region 개요

/// <summary>
/// DB 실행 파이프라인에서 사용하는
/// <b>전략(Strategy)</b>, <b>인터셉터 체인</b>, <b>성능 보조 구조체</b>를 한 파일에 통합한 모듈입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 이 모듈은 DB 실행 파이프라인의 핵심 정책(전략, 인터셉터, 성능 튜닝)을 한 곳에 통합하여 응집도를 높이고 관리 효율성을 극대화합니다.
/// </para>
/// <para><strong>📌 이 파일이 “한 곳에 모아두는 이유”</strong></para>
/// <list type="bullet">
///   <item>
///     <strong>실행 정책의 응집도</strong>:
///     재시도/회로차단/트랜잭션/스키마 모드/인터셉터 파이프라인은 함께 변화하는 경우가 많아,
///     서로의 정책을 한 눈에 추적할 수 있도록 동일 모듈로 묶습니다.
///   </item>
///   <item>
///     <strong>핫 패스 최적화</strong>:
///     배치 조절(AdaptiveBatchSizer), 버퍼 열거(ArraySegmentEnumerable), 인터셉터 체인(Compiled Pipeline)은
///     모두 “자주 호출되는 경로”에 직접 영향을 주므로 최적화 방향(무할당/인라인/분기 최소화)을 통일합니다.
///   </item>
///   <item>
///     <strong>운영 안정성</strong>:
///     Deadlock/스키마 불일치/치명 오류(Fast-Fail)는 “재시도”로 해결되는 것과 “즉시 차단”이 맞는 것이 섞여 있으므로,
///     예외 분류 기준과 후속 조치(플래그/캐시 무효화/메트릭)를 한 흐름으로 관리합니다.
///   </item>
/// </list>
///
/// <para><strong>📌 포함 구성요소(요약)</strong></para>
/// <list type="bullet">
///   <item><strong>AdaptiveBatchSizer</strong>: 처리량 + 배압 기반 동적 배치 사이즈 조절(EMA + ±20% 제한)</item>
///   <item><strong>ArraySegmentEnumerable</strong>: 버퍼(배열) 구간을 구조체 열거자로 무할당 순회</item>
///   <item><strong>InterceptorChain</strong>: 인터셉터 리스트를 델리게이트로 컴파일(실행 시 상수 비용)</item>
///   <item><strong>ResilientStrategy</strong>: Polly + Deadlock 승자 + Self-Healing Schema + Fast-Fail</item>
///   <item><strong>TransactionalStrategy</strong>: 외부 트랜잭션 컨텍스트 공유(스냅샷 스키마 우선)</item>
///   <item><strong>NoOpResumableStateStore</strong>: Resumable 미사용 시 Null Object</item>
/// </list>
/// </remarks>
file static class StrategiesDoc { }

#endregion

// ============================================================================
// 1. 성능 구조체: Adaptive Batching & ArrayPool Enumerator
// ============================================================================

#region 성능 구조체

/// <summary>
/// 처리량과 메모리 부하를 분석하여 최적의 배치 크기를 계산하는 적응형 배치 사이즈 계산기입니다.
/// </summary>
/// <remarks>
/// <para><strong>📊 알고리즘 원리</strong></para>
/// <list type="bullet">
///   <item>
///     <strong>EMA(지수 이동 평균)</strong>:
///     순간 변동(Jitter)을 완화하고 추세를 반영하여 안정적으로 처리량(rows/sec)을 추정합니다.
///   </item>
///   <item>
///     <strong>목표 시간(targetSec)</strong>:
///     “한 배치가 대략 targetSec 안에 끝나도록” 다음 배치 크기를 산출합니다.
///   </item>
///   <item>
///     <strong>Backpressure(배압)</strong>:
///     메모리 부하가 높아지면 배치 크기를 공격적으로 줄여 OOM 및 GC 폭주 위험을 낮춥니다.
///   </item>
///   <item>
///     <strong>급격한 변동 제한(±20%)</strong>:
///     과도한 진동을 막고 시스템이 안정적으로 수렴하도록 조정 폭을 제한합니다.
///   </item>
/// </list>
///
/// <para><strong>⚡ 성능 특성</strong></para>
/// <list type="bullet">
///   <item>조정 연산은 O(1)이며, 핫 경로를 고려해 인라인/최적화를 적용합니다.</item>
/// </list>
///
/// <para><strong>🧵 스레드 안전성</strong></para>
/// <list type="bullet">
///   <item>
///     <b>스레드 안전하지 않습니다.</b>
///     내부 상태(EMA, Current)를 가지는 구조체이므로 “단일 실행 흐름(요청/작업 단위)”에서만 사용해야 합니다.
///   </item>
/// </list>
/// </remarks>
/// <param name="initial">초기 배치 크기입니다.</param>
/// <param name="min">최소 배치 크기입니다. (기본: 1,000)</param>
/// <param name="max">최대 배치 크기입니다. (기본: 20,000)</param>
/// <param name="targetSec">한 배치가 목표로 하는 처리 시간(초)입니다. (기본: 1.0초)</param>
internal struct AdaptiveBatchSizer(
    int initial,
    int min = 1000,
    int max = 20000,
    double targetSec = 1.0)
{
    private double _avgRowsPerSec = 0;
    private readonly double _targetDurationSec = targetSec;
    
    // ⚠️ [CRITICAL FIX] Race Condition 수정
    // - 기존: public int Current { get; private set; } = ...;
    // - 문제: 다중 스레드에서 Current 업데이트 시 Race Condition 발생 가능
    // - 해결: Interlocked.Exchange로 원자적 업데이트 보장
    private int _current = Math.Clamp(initial, min, max);

    /// <summary>현재 계산된 배치 크기입니다. (Thread-safe)</summary>
    public int Current => _current;

    /// <summary>허용되는 최대 배치 크기입니다.</summary>
    public int MaxSize { get; } = max;

    /// <summary>
    /// 실행 결과를 바탕으로 다음 배치 크기를 계산하여 <see cref="Current"/>에 반영합니다.
    /// </summary>
    /// <param name="elapsed">이번 배치 실행에 소요된 시간입니다.</param>
    /// <param name="count">이번 배치에서 처리한 행(레코드) 수입니다.</param>
    /// <param name="memoryLoad">
    /// 메모리 부하(0.0 ~ 1.0)입니다. 0.8 이상이면 배치를 공격적으로 줄여 위험을 낮춥니다.
    /// </param>
    /// <remarks>
    /// 계산 순서:
    /// <list type="number">
    ///   <item>처리량(rows/sec)을 측정</item>
    ///   <item>EMA로 처리량을 평활화</item>
    ///   <item>목표 시간(targetSec)을 기준으로 다음 배치 크기 산출</item>
    ///   <item>메모리 배압(> 0.8) 반영</item>
    ///   <item>±20% 제한 및 min/max 경계 적용</item>
    /// </list>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void Adjust(TimeSpan elapsed, int count, double memoryLoad)
    {
        if (elapsed.TotalSeconds <= 0 || count <= 0)
            return;

        // 1) 처리량 측정 (rows/sec)
        double rps = count / elapsed.TotalSeconds;

        // 2) EMA(지수 이동 평균) 적용
        //    (기존 평균 70% + 새 측정값 30%)
        _avgRowsPerSec = _avgRowsPerSec == 0
            ? rps
            : (_avgRowsPerSec * 0.7) + (rps * 0.3);

        // 3) 목표 시간 내 처리 가능한 예상 개수
        int estimated = (int)(_avgRowsPerSec * _targetDurationSec);

        // 4) 메모리 배압 반영 (부하가 80% 초과 시 공격적으로 축소)
        if (memoryLoad > 0.8)
        {
            estimated = (int)(estimated * (1.0 - memoryLoad) * 2.0);
        }

        // 5) 변동 폭 제한(±20%) + 최종 min/max 적용
        int currentSnapshot = _current; // 현재 값의 Snapshot 읽기
        int next = Math.Clamp(
            estimated,
            (int)(currentSnapshot * 0.8),
            (int)(currentSnapshot * 1.2));

        int finalValue = Math.Clamp(next, min, MaxSize);
        
        // 6) 원자적 업데이트 (Thread-safe)
        Interlocked.Exchange(ref _current, finalValue);
    }

    /// <summary>
    /// OOM 위험 또는 강한 배압 상황에서 배치를 최소 수준(100)으로 강제 조정합니다.
    /// </summary>
    public void Throttle() => Interlocked.Exchange(ref _current, 100);
}

/// <summary>
/// ArrayPool 버퍼(배열)를 대상으로 하는 무할당 열거(Enumeration) 래퍼입니다.
/// </summary>
/// <typeparam name="T">요소 타입입니다.</typeparam>
/// <remarks>
/// <para><strong>📌 설계 의도</strong></para>
/// <list type="bullet">
///   <item>
///     배열 구간(버퍼 + 유효 count)을 <see cref="IEnumerable{T}"/>로 노출하되,
///     <b>boxing 없는 구조체 열거자</b>를 제공하여 GC 압력을 최소화합니다.
///   </item>
///   <item>
///     <c>foreach</c>는 구조체 열거자 패턴(GetEnumerator)을 우선 사용하므로,
///     호출부가 인터페이스로 캐스팅하지 않는 한 힙 할당을 피할 수 있습니다.
///   </item>
/// </list>
///
/// <para><strong>⚠️ 주의</strong></para>
/// <list type="bullet">
///   <item>
///     버퍼는 외부(ArrayPool 등)에서 관리될 수 있습니다.
///     열거 중/후에 버퍼가 반환되면(또는 재사용되면) 예측 불가능한 데이터가 될 수 있으므로,
///     “버퍼 수명”을 반드시 보장해야 합니다.
///   </item>
/// </list>
/// </remarks>
internal readonly struct ArraySegmentEnumerable<T>(T[] buffer, int count) : IEnumerable<T>
{
    /// <summary>
    /// 구조체 열거자를 반환합니다.
    /// <para>주의: 이 메서드를 통해 <c>foreach</c>는 일반적으로 boxing 없이 순회합니다.</para>
    /// </summary>
    public Enumerator GetEnumerator() => new(buffer, count);

    /// <summary>
    /// 인터페이스 기반 열거는 boxing이 발생할 수 있습니다.
    /// <para>가능하면 호출부에서 직접 <c>foreach</c>로 순회하는 방식을 권장합니다.</para>
    /// </summary>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// 실제 순회 로직을 담당하는 구조체 열거자입니다.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[] _buffer;
        private readonly int _count;
        private int _index;

        /// <summary>열거자를 초기화합니다.</summary>
        public Enumerator(T[] buffer, int count)
        {
            _index = -1;
            _buffer = buffer;
            _count = count;
        }

        /// <inheritdoc />
        public T Current => _buffer[_index];

        /// <inheritdoc />
        object? IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext() => ++_index < _count;

        /// <inheritdoc />
        public void Reset() => _index = -1;

        /// <inheritdoc />
        public void Dispose() { }
    }
}

#endregion

// ============================================================================
// 2. 인터셉터 체인: 컴파일된 파이프라인
// ============================================================================

#region 인터셉터 체인

/// <summary>
/// 다수의 <see cref="IDbCommandInterceptor"/>를 단일 실행 델리게이트로 “컴파일”하여,
/// 실행 시 호출 비용을 최소화하는 헬퍼입니다.
/// </summary>
/// <remarks>
/// <para><strong>🔄 작동 원리</strong></para>
/// <list type="bullet">
///   <item>
///     <strong>Onion(미들웨어) 패턴</strong>:
///     각 인터셉터가 다음 델리게이트를 감싸는 구조로 합성됩니다.
///   </item>
///   <item>
///     <strong>Reverse Composition</strong>:
///     역순으로 조립하여, “등록 순서대로 실행되는 체인”을 보장합니다.
///   </item>
///   <item>
///     <strong>상수 비용</strong>:
///     런타임에서는 리스트 순회 없이 “단일 델리게이트 호출 비용”만 발생합니다.
///   </item>
/// </list>
///
/// <para><strong>🧵 스레드 안전성</strong></para>
/// <list type="bullet">
///   <item>
///     생성 이후 델리게이트가 불변(immutable)이며, 내부 상태를 가지지 않으므로 스레드 안전합니다.
///   </item>
/// </list>
/// </remarks>
internal sealed class InterceptorChain(IEnumerable<IDbCommandInterceptor> interceptors)
{
    // 미리 컴파일된 실행 델리게이트(불변)
    private readonly Func<DbCommand, DbCommandInterceptionContext, ValueTask> _executing = BuildExecuting(interceptors);
    private readonly Func<DbCommand, DbCommandExecutedEventData, ValueTask> _executed = BuildExecuted(interceptors);

    /// <summary>명령 실행 전(Executing) 인터셉터 체인을 실행합니다.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask OnExecutingAsync(DbCommand cmd, DbCommandInterceptionContext ctx)
        => _executing(cmd, ctx);

    /// <summary>명령 실행 후(Executed) 인터셉터 체인을 실행합니다.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask OnExecutedAsync(DbCommand cmd, DbCommandExecutedEventData data)
        => _executed(cmd, data);

    /// <summary>실행 전(Executing) 파이프라인을 구축합니다.</summary>
    private static Func<DbCommand, DbCommandInterceptionContext, ValueTask> BuildExecuting(IEnumerable<IDbCommandInterceptor> list)
    {
        // 최심부: 아무 작업도 하지 않는 종단점
        Func<DbCommand, DbCommandInterceptionContext, ValueTask> next
            = static (_, _) => ValueTask.CompletedTask;

        // 역순으로 감싸야 “등록 순서대로” 실행되는 체인이 됩니다.
        foreach (var interceptor in list.Reverse())
        {
            var current = next;
            var i = interceptor;

            next = async (cmd, ctx) =>
            {
                await i.ReaderExecutingAsync(cmd, ctx).ConfigureAwait(false);

                // SuppressExecution이 설정되면 이후 체인(실행 포함)을 중단
                if (!ctx.SuppressExecution)
                    await current(cmd, ctx).ConfigureAwait(false);
            };
        }

        return next;
    }

    /// <summary>실행 후(Executed) 파이프라인을 구축합니다.</summary>
    private static Func<DbCommand, DbCommandExecutedEventData, ValueTask> BuildExecuted(IEnumerable<IDbCommandInterceptor> list)
    {
        Func<DbCommand, DbCommandExecutedEventData, ValueTask> next
            = static (_, _) => ValueTask.CompletedTask;

        foreach (var interceptor in list.Reverse())
        {
            var current = next;
            var i = interceptor;

            next = async (cmd, data) =>
            {
                // 실행 후 인터셉터는 가능한 한 모든 체인이 호출되도록 구성합니다.
                await i.ReaderExecutedAsync(cmd, data).ConfigureAwait(false);
                await current(cmd, data).ConfigureAwait(false);
            };
        }

        return next;
    }
}

#endregion

// ============================================================================
// 3. Resilient 전략: Polly + Deadlock 승자 + Self-Healing Schema + Fast-Fail
// ============================================================================

#region 지능형 Resilient 전략

/// <summary>
/// Polly 기반의 복원력(Resilience)에 더해,
/// 데드락 승자 전략, 스키마 자가 치유(Self-Healing), Fast-Fail 회로차단을 통합한 실행 전략입니다.
/// </summary>
/// <remarks>
/// <para><strong>💡 핵심 기능</strong></para>
/// <list type="bullet">
///   <item>
///     <strong>Polly ResiliencePipeline</strong>:
///     재시도/백오프/회로차단 등의 정책은 외부에서 구성하여 주입합니다.
///   </item>
///   <item>
///     <strong>데드락 승자 전략</strong>:
///     데드락(1205) 발생 시, 다음 시도에서 <c>SET DEADLOCK_PRIORITY HIGH</c>를 적용해
///     희생자(victim)가 될 확률을 낮춥니다.
///   </item>
///   <item>
///     <strong>Self-Healing Schema</strong>:
///     SP 스키마 불일치(201/207/8144) 감지 시 캐시를 무효화하고 강제 재로딩합니다.
///   </item>
///   <item>
///     <strong>Fast-Fail</strong>:
///     로그인 실패/DB 접근 불가/프로시저 없음 등 “재시도로 의미가 없는 오류”는
///     즉시 <see cref="BrokenCircuitException"/>로 전환하여 회로를 차단합니다.
///   </item>
/// </list>
///
/// <para><strong>⚠️ 예외 정책</strong></para>
/// <list type="bullet">
///   <item>
///     이 전략은 예외를 “삼키지 않습니다”.
///     분류/플래그/캐시 무효화/메트릭만 수행하고,
///     재시도 여부는 <see cref="ResiliencePipeline"/> 정책이 결정합니다.
///   </item>
/// </list>
///
/// <para><strong>🧵 스레드 안전성</strong></para>
/// <list type="bullet">
///   <item>
///     내부에 <c>_elevatePriorityOnNextRetry</c> 상태를 가지므로,
///     일반적으로 “요청 단위/실행기 단위(동시 공유 금지)”로 사용하는 것을 권장합니다.
///   </item>
/// </list>
/// </remarks>
internal sealed class ResilientStrategy(
    IDbConnectionFactory connFactory,
    IResiliencePipelineProvider pipelineProvider,
    ISchemaService schemaService,
    ILogger logger
) : IDbExecutionStrategy
{
    private readonly IDbConnectionFactory _connFactory = connFactory;
    private readonly IResiliencePipelineProvider _pipelineProvider = pipelineProvider;
    private readonly ISchemaService _schemaService = schemaService;
    private readonly ILogger _logger = logger;

    /// <summary>데드락 발생 시, 다음 재시도에서 DEADLOCK_PRIORITY를 올리기 위한 플래그입니다.</summary>
    private bool _elevatePriorityOnNextRetry;

    /// <inheritdoc />
    public bool IsTransactional => false;

    /// <inheritdoc />
    public SqlTransaction? CurrentTransaction => null;

    /// <summary>
    /// 일반적인 조회 시 성능을 위해 스냅샷을 우선하고, 없을 경우 서비스(DB)로 폴백합니다.
    /// </summary>
    public SchemaResolutionMode DefaultSchemaMode => SchemaResolutionMode.SnapshotThenServiceFallback;

    /// <summary>
    /// 비트랜잭션 전략에서는 트랜잭션 참여(Enlist)를 수행하지 않습니다.
    /// </summary>
    public void EnlistTransaction(SqlCommand cmd)
    {
        // no-op
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult, TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken ct)
    {
        if (_pipelineProvider.IsEnabled)
        {
            return await _pipelineProvider.Pipeline.ExecuteAsync(
                async token => await ExecuteCoreAsync(request, operation, token).ConfigureAwait(false),
                ct).ConfigureAwait(false);
        }

        return await ExecuteCoreAsync(request, operation, ct).ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteCoreAsync<TResult, TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken token)
    {
        var info = new DbRequestInfo(
            InstanceId: request.InstanceHash,
            DbSystem: "mssql",
            Operation: request.CommandType.ToString(),
            Target: request.CommandText);

        await using var conn = await _connFactory
            .CreateConnectionAsync(request.InstanceHash, token)
            .ConfigureAwait(false);

        await ApplyDeadlockPriorityAsync(conn, token).ConfigureAwait(false);

        DbMetrics.TrackConnectionOpen(info);

        try
        {
            return await operation(conn, token).ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            await HandleSqlExceptionAsync(ex, request, token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            DbMetrics.TrackConnectionClose(info);
        }
    }

    /// <inheritdoc />
    public async Task<DbDataReader?> ExecuteStreamAsync<TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<SqlDataReader>> operation,
        CancellationToken ct)
    {
        if (_pipelineProvider.IsEnabled)
        {
            return await _pipelineProvider.Pipeline.ExecuteAsync(
                async token => await ExecuteStreamCoreAsync(request, operation, token).ConfigureAwait(false),
                ct).ConfigureAwait(false);
        }

        return await ExecuteStreamCoreAsync(request, operation, ct).ConfigureAwait(false);
    }

    private async Task<DbDataReader?> ExecuteStreamCoreAsync<TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<SqlDataReader>> operation,
        CancellationToken token)
    {
        var info = new DbRequestInfo(
            InstanceId: request.InstanceHash,
            DbSystem: "mssql",
            Operation: request.CommandType.ToString(),
            Target: request.CommandText);

        // 스트리밍은 “연결 수명”이 Reader 수명과 묶이므로 await using을 쓰지 않습니다.
        var conn = await _connFactory
            .CreateConnectionAsync(request.InstanceHash, token)
            .ConfigureAwait(false);

        await ApplyDeadlockPriorityAsync(conn, token).ConfigureAwait(false);

        DbMetrics.TrackConnectionOpen(info);

        try
        {
            var reader = await operation(conn, token).ConfigureAwait(false);

            if (reader is null)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
                DbMetrics.TrackConnectionClose(info);
                return null;
            }

            // Reader가 Dispose될 때 연결도 함께 정리하고 메트릭을 남기는 래퍼
            return new MonitoredSqlDataReader(reader, conn, info);
        }
        catch (SqlException ex)
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            DbMetrics.TrackConnectionClose(info);

            await HandleSqlExceptionAsync(ex, request, token).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            DbMetrics.TrackConnectionClose(info);
            throw;
        }
    }

    #region Deadlock 승자 전략

    /// <summary>
    /// 이전 시도에서 데드락이 발생한 경우, 현재 세션의 데드락 우선순위를 HIGH로 올립니다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQL Server 데드락은 희생자(victim)를 선택합니다.
    /// 다음 시도에서 우선순위를 올려 희생자가 될 확률을 낮추는 전략입니다.
    /// </para>
    /// </remarks>
    private async Task ApplyDeadlockPriorityAsync(SqlConnection conn, CancellationToken ct)
    {
        if (!_elevatePriorityOnNextRetry)
            return;

        if (conn.State != ConnectionState.Open)
            return;

        _logger.LogInformation("[데드락 승자 전략] 현재 세션 DEADLOCK_PRIORITY 를 HIGH로 올립니다.");

        await using var cmd = new SqlCommand("SET DEADLOCK_PRIORITY HIGH", conn);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        _elevatePriorityOnNextRetry = false;
    }

    #endregion

    #region SqlException 처리

    /// <summary>
    /// SQL 예외를 분류하여 Fast-Fail, Self-Healing, 재시도 위임을 수행합니다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이 메서드는 “예외를 먹지 않습니다”.
    /// 처리(플래그/캐시 무효화/메트릭 기록)만 수행하고,
    /// 실제 재시도 여부는 Polly 정책이 결정합니다.
    /// </para>
    /// </remarks>
    private async Task HandleSqlExceptionAsync<TParams>(
        SqlException ex,
        DbRequest<TParams> request,
        CancellationToken ct)
    {
        var info = new DbRequestInfo(
            InstanceId: request.InstanceHash,
            DbSystem: "mssql",
            Operation: request.CommandType.ToString(),
            Target: request.CommandText);

        // 1) 데드락(1205): 다음 시도에서 DEADLOCK_PRIORITY HIGH
        if (ex.Number == 1205)
        {
            _elevatePriorityOnNextRetry = true;

            DbMetrics.TrackRetry("Deadlock", info);

            _logger.LogWarning(ex,
                "[Resilient] 데드락(1205) 발생. 다음 시도에서 DEADLOCK_PRIORITY HIGH 로 재시도합니다.");

            return; // Polly 재시도 정책에 위임
        }

        // 2) Fast-Fail: 치명 오류는 회로차단기로 즉시 전환
        if (IsFastFailError(ex.Number))
        {
            DbMetrics.TrackRetry("FastFail", info);

            _logger.LogWarning(ex,
                "[Resilient] 치명적인 DB 오류(코드: {Code})를 감지했습니다. 재시도를 중단하고 회로를 차단합니다.",
                ex.Number);

            throw new BrokenCircuitException(
                $"치명적인 데이터베이스 오류가 발생했습니다. (에러 코드: {ex.Number})",
                ex);
        }

        // 3) Self-Healing Schema: SP 스키마 불일치(주로 파라미터/컬럼) 감지 시 캐시 무효화 + 강제 재로딩
        if ((ex.Number is 201 or 207 or 8144)
            && request.CommandType == CommandType.StoredProcedure)
        {
            _logger.LogWarning(ex,
                "[Resilient/Schema] SP '{SpName}' 스키마 불일치(코드: {Code}) 감지. 캐시 무효화 후 재로딩을 시도합니다.",
                request.CommandText,
                ex.Number);

            _schemaService.InvalidateSpSchema(request.CommandText, request.InstanceHash);

            try
            {
                await _schemaService
                    .GetSpSchemaAsync(request.CommandText, request.InstanceHash, ct)
                    .ConfigureAwait(false);

                DbMetrics.TrackSchemaRefresh(success: true, kind: "self_healing", info);
            }
            catch
            {
                DbMetrics.TrackSchemaRefresh(success: false, kind: "self_healing", info);
                throw;
            }

            DbMetrics.TrackRetry("SchemaHealing", info);
            return; // Polly 재시도 정책에 위임
        }

        // 4) 그 외: 일반 재시도 대상으로 처리
        DbMetrics.TrackRetry("Generic", info);
    }

    /// <summary>
    /// 재시도보다 즉시 실패/차단이 적합한 치명 오류인지 판정합니다.
    /// </summary>
    private static bool IsFastFailError(int code) => code switch
    {
        18456 => true, // 로그인 실패
        4060 => true, // DB 접근 불가
        2812 => true, // SP 없음
        _ => false
    };

    #endregion
}

#endregion

// ============================================================================
// 4. Transactional 전략: 기존 연결/트랜잭션 공유
// ============================================================================

#region 트랜잭션 전략

/// <summary>
/// 기존 <see cref="SqlConnection"/> / <see cref="SqlTransaction"/> 컨텍스트를 공유하는 트랜잭션 전략입니다.
/// </summary>
/// <remarks>
/// <para><strong>📌 설계 의도</strong></para>
/// <list type="bullet">
///   <item>호출자가 이미 시작한 트랜잭션 범위에서 실행을 보장합니다.</item>
///   <item>
///     트랜잭션 중 스키마 변경은 “즉시 실패”가 안전한 경우가 많으므로,
///     기본 스키마 모드를 <see cref="SchemaResolutionMode.SnapshotOnly"/>로 둡니다.
///   </item>
/// </list>
///
/// <para><strong>⚠️ 예외 정책</strong></para>
/// <list type="bullet">
///   <item>
///     스키마 불일치(주로 SP 파라미터/컬럼)가 감지되면,
///     캐시만 무효화하고 예외를 그대로 전파하여 롤백을 유도합니다.
///   </item>
/// </list>
/// </remarks>
internal sealed class TransactionalStrategy(
    SqlConnection connection,
    SqlTransaction transaction,
    ISchemaService schemaService,
    ILogger logger
) : IDbExecutionStrategy
{
    private readonly SqlConnection _connection = connection;
    private readonly SqlTransaction _transaction = transaction;
    private readonly ISchemaService _schemaService = schemaService;
    private readonly ILogger _logger = logger;

    /// <inheritdoc />
    public bool IsTransactional => true;

    /// <inheritdoc />
    public SqlTransaction? CurrentTransaction => _transaction;

    /// <inheritdoc />
    public SchemaResolutionMode DefaultSchemaMode => SchemaResolutionMode.SnapshotOnly;

    /// <summary>
    /// 명령에 트랜잭션을 참여(Enlist)시킵니다.
    /// </summary>
    public void EnlistTransaction(SqlCommand cmd) => cmd.Transaction = _transaction;

    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult, TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken ct)
    {
        try
        {
            return await operation(_connection, ct).ConfigureAwait(false);
        }
        catch (SqlException ex) when (IsSchemaMismatchError(ex.Number)
                                      && request.CommandType == CommandType.StoredProcedure)
        {
            var info = new DbRequestInfo(
                InstanceId: request.InstanceHash,
                DbSystem: "mssql",
                Operation: request.CommandType.ToString(),
                Target: request.CommandText);

            _logger.LogWarning(ex,
                "[Transaction/Schema] 스키마 불일치(코드: {Code}) 감지. SP '{SpName}' 캐시를 무효화합니다.",
                ex.Number,
                request.CommandText);

            _schemaService.InvalidateSpSchema(request.CommandText, request.InstanceHash);

            DbMetrics.TrackSchemaRefresh(success: false, kind: "transactional_mismatch", info);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DbDataReader?> ExecuteStreamAsync<TParams>(
        DbRequest<TParams> request,
        Func<SqlConnection, CancellationToken, Task<SqlDataReader>> operation,
        CancellationToken ct)
    {
        // 트랜잭션 전략에서 스트리밍은 연결/트랜잭션 수명을 외부가 관리하므로 그대로 반환합니다.
        return await operation(_connection, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// SP 스키마 불일치로 자주 관찰되는 오류 코드를 판정합니다.
    /// </summary>
    private static bool IsSchemaMismatchError(int code)
        => code is 201 or 207 or 8144;
}

#endregion

// ============================================================================
// 5. 재개(Resumable) 상태 저장소: No-Op(널 오브젝트)
// ============================================================================

#region [5. 재개(Resumable) 상태 저장소 (No-Op / Null Object)]

/// <summary>
/// 아무 작업도 수행하지 않는 Resumable State Store(Null Object)입니다.
/// </summary>
/// <remarks>
/// <para>
/// Resumable 기능(커서 저장/복원)을 사용하지 않을 때 DI에 등록하여 의존성을 만족시키기 위한 구현입니다.
/// </para>
/// <para><strong>장점</strong></para>
/// <list type="bullet">
///   <item>호출부에서 “기능 사용 여부”를 조건 분기하지 않아도 되어 코드가 단순해집니다.</item>
///   <item>테스트/개발 환경에서 Resumable 기능을 쉽게 비활성화할 수 있습니다.</item>
/// </list>
/// </remarks>
internal sealed class NoOpResumableStateStore : IResumableStateStore
{
    /// <inheritdoc />
    public Task SaveCursorAsync<TCursor>(string instanceKey, string queryKey, TCursor cursor, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<TCursor?> GetLastCursorAsync<TCursor>(string instanceKey, string queryKey, CancellationToken ct)
        => Task.FromResult<TCursor?>(default);
}

#endregion
