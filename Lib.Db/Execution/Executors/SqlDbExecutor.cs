// ============================================================================
// 파일: Lib.Db.Execution/SqlDbExecutor.cs
// 설명: [Architecture] 최상위 통합 실행기
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Diagnostics;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Executors;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Lib.Db.Execution;

/// <summary>
/// .NET 10 / C# 14 기반의 고성능 통합 SQL Server 실행 엔진입니다.
/// </summary>
/// <remarks>
/// <para><b>[설계의도 (Design Rationale)]</b></para>
/// <list type="bullet">
/// <item><description><strong>전략 패턴 기반 이중 모드</strong>: Resilient(회복탄력성) 모드와 Transactional(트랜잭션) 모드를 런타임에 전환하여 단일 실행기로 다양한 실행 컨텍스트를 지원합니다.</description></item>
/// <item><description><strong>AOT 호환성</strong>: Reflection을 제거하고 Source Generator 기반 매퍼(`IGeneratedMapper`)를 사용하여 Native AOT 환경에서 동작합니다.</description></item>
/// <item><description><strong>Zero-Allocation DB 액세스</strong>: ArrayPool, Span&lt;T&gt;, 구조체 열거자를 활용하여 GC 압력을 최소화하고 대용량 처리 성능을 극대화합니다.</description></item>
/// <item><description><strong>자동 회복</strong>: Polly Pipeline을 통해 Deadlock, Timeout, Connection 실패 시 자동 재시도 및 Circuit Breaker로 장애를 격리합니다.</description></item>
/// </list>
/// 
/// <para><strong>💡 핵심 기능 (Core Features)</strong></para>
/// <list type="bullet">
/// <item><description><strong>Resilient Strategy</strong>: Polly Retry, CircuitBreaker, Deadlock Winner Detection, Self-Healing Schema Refresh</description></item>
/// <item><description><strong>Transactional Strategy</strong>: 기존 SqlConnection/SqlTransaction 컨텍스트 공유, Fast-Fail 전략</description></item>
/// <item><description><strong>Bulk Pipeline</strong>: Channel 기반 비동기 스트리밍, ArrayPool Zero-Allocation 배치 처리</description></item>
/// <item><description><strong>Resumable Query</strong>: 커서 기반 중단-재개 가능 대용량 스트리밍, Redis 등 외부 상태 저장소 연동</description></item>
/// <item><description><strong>Adaptive Batching</strong>: GC 메트릭 기반 메모리 배압 감지 및 배치 크기 동적 조정(AdaptiveBatchSizer)</description></item>
/// <item><description><strong>Chaos Engineering</strong>: 개발/테스트 환경에서 의도적 지연/예외 주입으로 회복탄력성 검증</description></item>
/// </list>
/// 
/// <para><strong>⚡ 성능 특성 (Performance)</strong></para>
/// <list type="bullet">
/// <item><description><strong>메모리 할당</strong>: Zero-Allocation (ArrayPool, Span&lt;T&gt;, 구조체 열거자)</description></item>
/// <item><description><strong>시간 복잡도</strong>: O(1) 명령 준비, O(N) 결과 매핑</description></item>
/// <item><description><strong>DB I/O</strong>: Balanced (읽기/쓰기 모두 최적화)</description></item>
/// <item><description><strong>Batching</strong>: 적응형 배치 크기 (Memory Pressure 감지 시 자동 축소)</description></item>
/// <item><description><strong>분산 추적</strong>: OpenTelemetry Activity로 성능 병목 지점 실시간 식별</description></item>
/// </list>
/// 
/// <para><strong>🔐 데이터 무결성 (Data Integrity)</strong></para>
/// <list type="bullet">
/// <item><description><strong>Transaction 범위</strong>: Strategy에 따라 자동 트랜잭션 참여 (`EnlistTransaction`)</description></item>
/// <item><description><strong>Isolation Level</strong>: Read Committed (SQL Server 기본값), 명시적 변경 가능</description></item>
/// <item><description><strong>SQL Injection 방어</strong>: 모든 식별자(테이블명, 컬럼명)를 Quote 메서드로 이스케이프 처리</description></item>
/// <item><description><strong>Temp Table 충돌 방지</strong>: MERGE/DELETE 시 `#Tmp_GUID` 형식으로 고유 임시 테이블 생성</description></item>
/// </list>
/// 
/// <para><strong>⚠️ 예외 처리 (Exceptions)</strong></para>
/// <list type="bullet">
/// <item><description><strong>Transient Errors</strong>: SqlException 1205(Deadlock), -2(Timeout), 53/233(Connection) 자동 재시도</description></item>
/// <item><description><strong>Non-Transient Errors</strong>: 즉시 전파, Activity.SetStatus(Error) 설정</description></item>
/// <item><description><strong>Resumable Query</strong>: 최대 재시도 횟수 초과 시 중단, 무한 루프 방지(stuckCount 메커니즘)</description></item>
/// <item><description><strong>예외 래핑</strong>: LibDbExceptionFactory로 컨텍스트(CommandText, InstanceId) 포함</description></item>
/// </list>
/// 
/// <para><strong>🔒 스레드 안전성 (Thread Safety)</strong></para>
/// <list type="bullet">
/// <item><description><strong>Thread-Safe</strong>: 모든 public 메서드는 동시 호출 가능</description></item>
/// <item><description><strong>Stateless 설계</strong>: 필드는 불변 의존성만 보유, 메서드 로컬 상태만 사용</description></item>
/// <item><description><strong>Channel</strong>: Thread-safe Producer-Consumer 패턴</description></item>
/// <item><description><strong>ArrayPool</strong>: Thread-safe 리소스 풀</description></item>
/// <item><description><strong>주의</strong>: Transactional 모드에서는 외부 트랜잭션의 수명 주기를 준수해야 합니다.</description></item>
/// </list>
/// 
/// <para><strong>🔧 유지보수 및 확장성 (Maintenance)</strong></para>
/// <list type="bullet">
/// <item><description><strong>Interceptor Chain</strong>: OnExecuting/OnExecuted 확장 포인트로 로깅, 모킹, 검증 로직 주입 가능</description></item>
/// <item><description><strong>Dry-Run 모드</strong>: `LibDbOptions.EnableDryRun` 활성화 시 쓰기 작업 건너뛰어 CI/테스트 안전성 확보</description></item>
/// <item><description><strong>Chaos Engineering</strong>: IChaosInjector로 지연/예외 주입하여 회복탄력성 검증</description></item>
/// <item><description><strong>Observability</strong>: DbMetrics (Prometheus), OpenTelemetry Activity (Zipkin/Jaeger) 통합</description></item>
/// <item><description><strong>Breaking Change 위험</strong>: IDbExecutionStrategy, ISchemaService 인터페이스 변경 시 영향도 높음</description></item>
/// </list>
/// 
/// <para><strong>📊 사용 시나리오</strong></para>
/// <list type="bullet">
/// <item><description><strong>Resilient 모드</strong>: 일반 API 요청, 배치 작업, 자동 재시도 필요 시</description></item>
/// <item><description><strong>Transactional 모드</strong>: 외부 트랜잭션 범위 내, 복수 작업 원자성 보장 필요 시</description></item>
/// <item><description><strong>Bulk Pipeline</strong>: 대용량 데이터 INSERT/UPDATE/DELETE, 메모리 효율 중요 시</description></item>
/// <item><description><strong>Resumable Query</strong>: 장시간 스트리밍, 중단 후 재개 가능성 필요 시</description></item>
/// </list>
/// </remarks>
internal sealed partial class SqlDbExecutor(
    IDbExecutionStrategy strategy,
    ISchemaService schemaService,
    IMapperFactory mapperFactory,
    IResumableStateStore resumableStore,
    IMemoryPressureMonitor memoryMonitor,
    IChaosInjector chaosInjector,
    InterceptorChain interceptorChain,
    LibDbOptions options,
    ILogger<SqlDbExecutor> logger
) : IDbExecutor
{
    #region 상수 및 필드

    private static readonly ActivitySource s_activitySource = new(typeof(SqlDbExecutor).Assembly.GetName().Name!);

    // [Optimization] Bulk Temp Table 이름 충돌 방지용 카운터 (프로세스 재시작 시 풀링된 세션과 충돌 방지 위해 Ticks로 초기화)
    private static long s_bulkCounter = DateTime.UtcNow.Ticks;

    // [Optimization] Activity 이름 캐싱
    private const string ActivityNameQuery = "DB Query";
    private const string ActivityNameProcedure = "DB Procedure";
    private const string ActivityNameCommand = "DB Command";
    // [MARS Validation] 연결 문자열별 MARS 활성화 여부 캐시
    private static readonly ConcurrentDictionary<string, bool> s_marsEnabledCache = new();

    private readonly IDbExecutionStrategy _strategy = strategy;
    private readonly ISchemaService _schemaService = schemaService;
    private readonly IMapperFactory _mapperFactory = mapperFactory;
    private readonly IResumableStateStore _resumableStore = resumableStore;
    private readonly IMemoryPressureMonitor _memoryMonitor = memoryMonitor;
    private readonly IChaosInjector _chaosInjector = chaosInjector;
    private readonly InterceptorChain _interceptorChain = interceptorChain;
    private readonly LibDbOptions _options = options;
    private readonly ILogger<SqlDbExecutor> _logger = logger;

    #endregion

    #region 표준 쿼리 실행 (Standard Query Execution)

    /// <summary>
    /// SQL 쿼리를 실행하여 결과를 비동기 스트림으로 반환합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입</typeparam>
    /// <typeparam name="TResult">결과 행 매핑 타입</typeparam>
    /// <param name="commandText">실행할 SQL 명령 텍스트 또는 저장 프로시저 이름</param>
    /// <param name="parameters">SQL 파라미터 객체 (null 허용)</param>
    /// <param name="instanceHash">DB 인스턴스 해시 (메트릭 및 추적용)</param>
    /// <param name="commandType">명령 타입 (StoredProcedure 또는 Text)</param>
    /// <param name="options">실행 옵션 (타임아웃, 스키마 모드 등)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>결과 행을 순차적으로 yield하는 비동기 스트림</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시 (Deadlock, Timeout, Connection 실패 등)</exception>
    /// <exception cref="OperationCanceledException">취소 토큰이 신호를 받을 경우</exception>
    /// <remarks>
    /// <para><strong>💡 구현 로직</strong></para>
    /// <list type="bullet">
    /// <item>Dry-Run 모드에서는 쓰기 작업 건너뜀</item>
    /// <item>SqlDataReader를 사용하여 행 단위 스트리밍</item>
    /// <item>Source Generator 기반 매퍼로 Zero-Reflection 매핑</item>
    /// </list>
    /// 
    /// <para><strong>📊 성능 고려사항</strong></para>
    /// <list type="bullet">
    /// <item>메모리 할당: Minimal (스트리밍 방식)</item>
    /// <item>DB I/O: 1회 Round-trip, 행 단위 Fetch</item>
    /// <item>BLOB 타입: SequentialAccess 활성화로 메모리 효율 극대화</item>
    /// </list>
    /// </remarks>
    public IAsyncEnumerable<TResult> QueryAsync<TParams, TResult>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct)
        => QueryStreamCoreAsync<TParams, TResult>(commandText, parameters, instanceHash, commandType, options, ct);

    /// <summary>
    /// SQL 쿼리를 실행하여 결과를 비동기 스트림으로 반환합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입</typeparam>
    /// <typeparam name="TResult">결과 행 매핑 타입</typeparam>
    /// <param name="commandText">실행할 SQL 명령 텍스트 또는 저장 프로시저 이름</param>
    /// <param name="parameters">SQL 파라미터 객체 (null 허용)</param>
    /// <param name="instanceHash">DB 인스턴스 해시 (메트릭 및 추적용)</param>
    /// <param name="commandType">명령 타입 (StoredProcedure 또는 Text)</param>
    /// <param name="options">실행 옵션 (타임아웃, 스키마 모드 등)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>결과 행을 순차적으로 yield하는 비동기 스트림</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시 (Deadlock, Timeout, Connection 실패 등)</exception>
    /// <exception cref="OperationCanceledException">취소 토큰이 신호를 받을 경우</exception>
    /// <remarks>
    /// <para><strong>💡 구현 로직</strong></para>
    /// <list type="bullet">
    /// <item>Dry-Run 모드에서는 쓰기 작업 건너뜀</item>
    /// <item>SqlDataReader를 사용하여 행 단위 스트리밍</item>
    /// <item>Source Generator 기반 매퍼로 Zero-Reflection 매핑</item>
    /// </list>
    /// 
    /// <para><strong>📊 성능 고려사항</strong></para>
    /// <list type="bullet">
    /// <item>메모리 할당: Minimal (스트리밍 방식)</item>
    /// <item>DB I/O: 1회 Round-trip, 행 단위 Fetch</item>
    /// <item>BLOB 타입: SequentialAccess 활성화로 메모리 효율 극대화</item>
    /// </list>
    /// </remarks>
    public async IAsyncEnumerable<TResult> ExecuteAsync<TParams, TResult>(
        string commandText,
        TParams? parameters,
        DbInstanceId instanceHash,
        CommandType commandType = CommandType.Text,
        DbExecutionOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // ---------------------------------------------------------------------
        // [Observability] Start Activity & Metric
        // ---------------------------------------------------------------------
        using var activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("SqlDbExecutor.ExecuteAsync")
            : null;

        if (activity != null)
        {
            activity.SetTag("db.system", "mssql");
            activity.SetTag("db.operation", commandText);
            activity.SetTag("db.command_type", commandType.ToString());
            activity.SetTag("libdb.instance", instanceHash.Value);
        }

        if (_options.EnableObservability)
        {
            LibDbTelemetry.DbRequestsTotal.Add(1,
                new KeyValuePair<string, object?>("operation", "ExecuteAsync"),
                new KeyValuePair<string, object?>("instance", instanceHash.Value));
        }

        long startTime = _options.EnableObservability ? Stopwatch.GetTimestamp() : 0;

        try
        {
            var request = new DbRequest<TParams?>(
                instanceHash.ToString(),
                commandText,
                commandType,
                parameters,
                ct,
                IsTransactional: _strategy.IsTransactional);

            // Use ExecuteStreamAsync to get the DataReader
            using var reader = await _strategy.ExecuteStreamAsync(request, async (conn, token) =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = commandText;
                cmd.CommandType = commandType;

                if (options?.CommandTimeout != null)
                    cmd.CommandTimeout = options.Value.CommandTimeout.Value;

                _strategy.EnlistTransaction(cmd);

                // Bind Parameters
                if (parameters != null)
                {
                    // Use _mapperFactory to get the mapper
                    var mapper = _mapperFactory.GetMapper<TParams>();
                    mapper.MapParameters(cmd, parameters, null);
                }

                return await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

            if (reader != null)
            {
                // Map results
                var resultMapper = _mapperFactory.GetMapper<TResult>();
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    yield return resultMapper.MapResult(reader);
                }
            }
        }
        finally
        {
            if (_options.EnableObservability)
            {
                var duration = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
                LibDbTelemetry.DbRequestDuration.Record(duration,
                    new KeyValuePair<string, object?>("operation", "ExecuteAsync"));
            }
        }
    }
    /// <summary>
    /// SQL 쿼리를 실행하여 단일 행을 반환합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입</typeparam>
    /// <typeparam name="TResult">결과 행 매핑 타입</typeparam>
    /// <param name="commandText">실행할 SQL 명령 텍스트 또는 저장 프로시저 이름</param>
    /// <param name="parameters">SQL 파라미터 객체 (null 허용)</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="commandType">명령 타입</param>
    /// <param name="options">실행 옵션</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>첫 번째 행을 매핑한 결과, 행이 없으면 default(TResult)</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시</exception>
    /// <remarks>
    /// <para><strong>📊 성능 특성</strong></para>
    /// <list type="bullet">
    /// <item>CommandBehavior.SingleRow 사용으로 DB 부하 최소화</item>
    /// <item>Resilient 모드: CloseConnection으로 연결 즉시 반환</item>
    /// <item>Transactional 모드: 외부 트랜잭션 유지</item>
    /// </list>
    /// </remarks>
    public Task<TResult?> QuerySingleAsync<TParams, TResult>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct)
    {
        var req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        return ExecutePipelineAsync(req, options, async (cmd, token) =>
        {
            var behavior = CommandTypeToSingleRowBehavior(commandType, _strategy.IsTransactional);

            await using var reader = await cmd.ExecuteReaderAsync(behavior, token)
                .ConfigureAwait(false);

            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return default;

            var mapper = _mapperFactory.GetMapper<TResult>();
            return mapper.MapResult(reader);
        });
    }

    /// <summary>
    /// SQL 명령을 실행하여 첫 번째 행의 첫 번째 컬럼 값을 반환합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입</typeparam>
    /// <typeparam name="TScalar">반환할 스칼라 값 타입</typeparam>
    /// <param name="commandText">실행할 SQL 명령</param>
    /// <param name="parameters">SQL 파라미터</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="commandType">명령 타입</param>
    /// <param name="options">실행 옵션</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>스칼라 값, null 또는 DBNull이면 default(TScalar)</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시</exception>
    /// <exception cref="InvalidCastException">타입 변환 실패 시</exception>
    /// <remarks>
    /// <para><strong>🔧 특수 기능</strong></para>
    /// <list type="bullet">
    /// <item>byte[] → Stream 자동 변환: BLOB 데이터를 MemoryStream으로 반환</item>
    /// <item>DBNull 안전 처리: DBNull을 default(TScalar)로 변환</item>
    /// </list>
    /// </remarks>
    public Task<TScalar?> ExecuteScalarAsync<TParams, TScalar>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct)
    {
        var req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        return ExecutePipelineAsync(req, options, async (cmd, token) =>
        {
            var val = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);

            if (val is null or DBNull)
                return default;

            // BLOB → Stream 자동 변환 지원
            if (typeof(TScalar) == typeof(Stream) && val is byte[] bytes)
                return (TScalar)(object)new MemoryStream(bytes);

            return (TScalar)val;
        });
    }

    /// <summary>
    /// INSERT, UPDATE, DELETE 등 행 수정 SQL 명령을 실행하고 영향받은 행 수를 반환합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입</typeparam>
    /// <param name="commandText">실행할 SQL 명령</param>
    /// <param name="parameters">SQL 파라미터 (OUTPUT 파라미터 지원)</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="commandType">명령 타입</param>
    /// <param name="options">실행 옵션</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>영향받은 행 수</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시</exception>
    /// <remarks>
    /// <para><strong>🔧 OUTPUT 파라미터</strong></para>
    /// <list type="bullet">
    /// <item>실행 후 MapOutputParameters로 OUTPUT 파라미터 값을 원본 객체에 역매핑</item>
    /// <item>Stored Procedure 호출 시 유용</item>
    /// </list>
    /// </remarks>
    public Task<int> ExecuteNonQueryAsync<TParams>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct)
    {
        var req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        return ExecutePipelineAsync(req, options, async (cmd, token) =>
        {
            var affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            _mapperFactory
                .GetMapper<TParams>()
                .MapOutputParameters(cmd, parameters);

            return affected;
        });
    }

    /// <summary>
    /// SQL 쿼리를 실행하여 다중 결과 셋을 반환합니다.
    /// </summary>
    /// <typeparam name="TParams">파라미터 객체 타입</typeparam>
    /// <param name="commandText">실행할 SQL 명령 (주로 Stored Procedure)</param>
    /// <param name="parameters">SQL 파라미터</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="commandType">명령 타입</param>
    /// <param name="options">실행 옵션</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>IMultipleResultReader - 다중 결과 셋을 순차적으로 읽을 수 있는 리더</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시</exception>
    /// <exception cref="InvalidOperationException">인터셉터에서 SuppressExecution 시 리더가 null인 경우</exception>
    /// <remarks>
    /// <para><strong>💡 사용 시나리오</strong></para>
    /// <list type="bullet">
    /// <item>Stored Procedure에서 여러 SELECT 문 실행</item>
    /// <item>단일 호출로 여러 테이블 데이터 조회</item>
    /// <item>SqlGridReader로 타입 안전한 매핑</item>
    /// </list>
    /// 
    /// <para><strong>⚠️ 주의사항</strong></para>
    /// <list type="bullet">
    /// <item>Dry-Run 모드: EmptyGrid Reader 반환</item>
    /// <item>리더 수명: Resilient 모드는 MonitoredSqlDataReader가 관리, Transactional 모드는 외부 트랜잭션이 관리</item>
    /// <item>사용 후 반드시 Dispose 호출</item>
    /// </list>
    /// </remarks>
    public async Task<IMultipleResultReader> QueryMultipleAsync<TParams>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        CancellationToken ct)
    {
        // [Dry-Run] 설계 – EmptyGridReader 반환 정책 유지
        if (_options.EnableDryRun)
        {
            LogDryRunStream(_logger, commandText);
            return new EmptyGridReader();
        }

        var req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        System.Data.Common.DbDataReader? rawReader;
        long startTicks = Stopwatch.GetTimestamp();

        using (var activity = s_activitySource.StartActivity("DB QueryMultiple"))
        {
            activity?.SetTag("db.system", "mssql");
            activity?.SetTag("db.operation", commandType.ToString());
            activity?.SetTag("db.statement", commandText);
            activity?.SetTag("db.instance", instanceHash);

            try
            {
                rawReader = await _strategy.ExecuteStreamAsync(
                    req,
                    async (conn, token) =>
                    {
                        var cmd = new SqlCommand(commandText, conn)
                        {
                            CommandType = commandType,
                            CommandTimeout = _options.DefaultCommandTimeoutSeconds
                        };

                        _strategy.EnlistTransaction(cmd);

                        // [MARS Validation] QueryMultipleAsync 사용 시 MARS 설정 필수 검증
                        // (성능 영향을 줄이기 위해 최초 1회만 파싱 후 캐싱)
                        ValidateMarsEnabled(conn);

                        await PrepareParametersAsync(cmd, parameters, instanceHash, options, token)
                            .ConfigureAwait(false);

                        var ctx = new DbCommandInterceptionContext(instanceHash, token);
                        await _interceptorChain.OnExecutingAsync(cmd, ctx).ConfigureAwait(false);

                        if (ctx.SuppressExecution)
                        {
                            LogMockingExecution(_logger, commandText);
                            return (ctx.MockResult as SqlDataReader)!;
                        }

                        var behavior = CommandBehavior.Default;
                        if (!_strategy.IsTransactional)
                            behavior |= CommandBehavior.CloseConnection;

                        return await cmd.ExecuteReaderAsync(behavior, token).ConfigureAwait(false);
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogWarning(ex,
                    "다중 결과 쿼리 실행 중 오류가 발생했습니다. (SQL: {CommandText})",
                    commandText);
                throw LibDbExceptionFactory.CreateCommandExecutionFailed(commandText, ex);
            }
        }

        #region 다중 결과 쿼리 - 리더 획득 시간 메트릭

        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        var elapsed = TimeSpan.FromTicks(elapsedTicks);

        var info = new DbRequestInfo(
            InstanceId: instanceHash,
            DbSystem: "mssql",
            Operation: commandType.ToString(),
            Target: commandText);

        DbMetrics.TrackDuration(elapsed, info);

        #endregion

        if (rawReader is null)
        {
            LibDbExceptionFactory.ThrowInvalidOperation(
                "QueryMultipleAsync 실행 결과가 null입니다. " +
                "인터셉터에서 SuppressExecution 되었는지 확인해 주세요.");
        }

        var gridReader = new SqlGridReader(rawReader, _mapperFactory);

        // Resilient 경로에서는 MonitoredSqlDataReader가 연결 수명/메트릭을 관리하고,
        // Transactional 경로에서는 외부 트랜잭션이 연결 수명을 관리합니다.
        // 따라서 여기에서 별도의 연결 종료를 강제하지 않습니다.

        return gridReader;
    }

    #endregion

    #region 대량 작업 (Bulk Operations)

    /// <summary>
    /// 대용량 데이터를 SqlBulkCopy를 사용하여 테이블에 고속으로 삽입합니다.
    /// </summary>
    /// <typeparam name="T">삽입할 데이터 타입</typeparam>
    /// <param name="destinationTableName">대상 테이블명</param>
    /// <param name="data">삽입할 데이터 컬렉션</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="SqlException">Bulk Insert 실패 시</exception>
    /// <remarks>
    /// <para><strong>⚡ 성능 최적화</strong></para>
    /// <list type="bullet">
    /// <item>ArrayPool 사용: 배치 버퍼를 풀에서 대여하여 Zero-Allocation</item>
    /// <item>AdaptiveBatchSizer: 메모리 압력과 처리 시간 기반 배치 크기 동적 조정</item>
    /// <item>SqlBulkCopy: SQL Server 최적화 대량 삽입</item>
    /// </list>
    /// 
    /// <para><strong>🔧 동작 방식</strong></para>
    /// <list type="bullet">
    /// <item>데이터를 배치로 분할 (기본 5000행)</item>
    /// <item>Memory Pressure 감지 시 배치 크기 축소</item>
    /// <item>각 배치마다 DbMetrics에 처리 시간 및 행 수 기록</item>
    /// </list>
    /// </remarks>
    public Task BulkInsertAsync<T>(
        string destinationTableName,
        IEnumerable<T> data,
        string instanceHash,
        CancellationToken ct)
        => BulkInsertInternalAsync(destinationTableName, data, instanceHash, ct);

    /// <summary>
    /// Temp Table과 MERGE 문을 사용하여 대용량 데이터를 고속으로 업데이트합니다.
    /// </summary>
    /// <typeparam name="T">업데이트할 데이터 타입</typeparam>
    /// <param name="targetTableName">대상 테이블명</param>
    /// <param name="data">업데이트할 데이터 컬���션</param>
    /// <param name="keyColumns">매칭 기준 컬럼명 배열 (Primary Key)</param>
    /// <param name="updateColumns">업데이트할 컬럼명 배열</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="SqlException">Bulk Update 실패 시</exception>
    /// <remarks>
    /// <para><strong>💡 구현 로직</strong></para>
    /// <list type="number">
    /// <item>임시 테이블 생성: #Tmp_GUID 형식으로 충돌 방지</item>
    /// <item>Bulk Insert: 업데이트할 데이터를 임시 테이블에 삽입</item>
    /// <item>MERGE: MERGE INTO 문으로 원본 테이블 업데이트</item>
    /// <item>정리: 임시 테이블 삭제</item>
    /// </list>
    /// 
    /// <para><strong>📊 성능 특성</strong></para>
    /// <list type="bullet">
    /// <item>단일 행 UPDATE보다 수백배 빠름 (대량 데이터 시)</item>
    /// <item>Transaction 내에서 실행되어 원자성 보장</item>
    /// <item>Log 최소화 옵션 활용 가능</item>
    /// </list>
    /// </remarks>
    public Task BulkUpdateAsync<T>(
        string targetTableName,
        IEnumerable<T> data,
        string[] keyColumns,
        string[] updateColumns,
        string instanceHash,
        CancellationToken ct)
        => BulkUpdateOptimizedAsync(targetTableName, data, keyColumns, updateColumns, instanceHash, ct);

    /// <summary>
    /// Temp Table과 INNER JOIN을 사용하여 대용량 데이터를 고속으로 삭제합니다.
    /// </summary>
    /// <typeparam name="T">삭제 기준 데이터 타입</typeparam>
    /// <param name="targetTableName">대상 테이블명</param>
    /// <param name="data">삭제 기준 데이터 컬렉션</param>
    /// <param name="keyColumns">매칭 기준 컬럼명 배열 (Primary Key)</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="SqlException">Bulk Delete 실패 시</exception>
    /// <remarks>
    /// <para><strong>💡 구현 로직</strong></para>
    /// <list type="number">
    /// <item>임시 테이블 생성: #Del_GUID 형식</item>
    /// <item>Bulk Insert: 삭제 기준 데이터를 임시 테이블에 삽입</item>
    /// <item>DELETE JOIN: DELETE FROM target INNER JOIN temp로 대량 삭제</item>
    /// <item>정리: 임시 테이블 삭제</item>
    /// </list>
    /// 
    /// <para><strong>⚠️ 주의사항</strong></para>
    /// <list type="bullet">
    /// <item>Foreign Key 제약: CASCADE 옵션 확인 필요</item>
    /// <item>Trigger: DELETE 트리거가 있다면 성능 영향 고려</item>
    /// </list>
    /// </remarks>
    public Task BulkDeleteAsync<T>(
        string targetTableName,
        IEnumerable<T> data,
        string[] keyColumns,
        string instanceHash,
        CancellationToken ct)
        => BulkDeleteOptimizedAsync(targetTableName, data, keyColumns, instanceHash, ct);

    /// <summary>
    /// Channel 기반 비동기 스트리밍으로 대용량 데이터를 파이프라인 방식으로 삽입합니다.
    /// </summary>
    /// <typeparam name="T">삽입할 데이터 타입</typeparam>
    /// <param name="tableName">대상 테이블명</param>
    /// <param name="reader">데이터 스트림 소스 (ChannelReader)</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="batchSize">배치 크기 (기본 5000행)</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="SqlException">Bulk Insert 실패 시</exception>
    /// <remarks>
    /// <para><strong>💡 사용 시나리오</strong></para>
    /// <list type="bullet">
    /// <item>실시간 데이터 스트림 처리 (IoT, 로그 수집 등)</item>
    /// <item>Producer-Consumer 패턴으로 메모리 효율 극대화</item>
    /// <item>Channel로 Backpressure 자동 관리</item>
    /// </list>
    /// 
    /// <para><strong>⚡ 성능 특성</strong></para>
    /// <list type="bullet">
    /// <item>Dry-Run 모드: DrainChannelAsync로 Producer 블로킹 방지</item>
    /// <item>AdaptiveBatchSizer: 메모리 압력 감지 시 배치 크기 동적 조정</item>
    /// <item>Zero-Allocation: ArrayPool 사용</item>
    /// </list>
    /// </remarks>
    public Task BulkInsertPipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string instanceHash,
        int batchSize = 5000,
        CancellationToken ct = default)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunBulk(_logger, "BULK INSERT PIPELINE", tableName);
            return DrainChannelAsync(reader, ct);
        }

        return BulkPipelineInternalAsync(tableName, reader, instanceHash, batchSize, ct, FlushBulkAsync);
    }

    /// <summary>
    /// Channel 기반 비동기 스트리밍으로 대용량 데이터를 파이프라인 방식으로 업데이트합니다.
    /// </summary>
    /// <typeparam name="T">업데이트할 데이터 타입</typeparam>
    /// <param name="tableName">대상 테이블명</param>
    /// <param name="reader">데이터 스트림 소스</param>
    /// <param name="keyColumns">매칭 기준 컬럼명 배열</param>
    /// <param name="updateColumns">업데이트할 컬럼명 배열</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="batchSize">배치 크기 (기본 5000행)</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="SqlException">Bulk Update 실패 시</exception>
    /// <remarks>
    /// <para><strong>💡 동작 방식</strong></para>
    /// <list type="bullet">
    /// <item>Channel에서 데이터 읽기 → 배치 크기 도달 → BulkUpdateAsync 호출</item>
    /// <item>ProcessChannelBatchAsync 헬퍼 사용</item>
    /// <item>List&lt;T&gt; 버퍼로 배치 수집 (ArrayPool 미사용)</item>
    /// </list>
    /// </remarks>
    public Task BulkUpdatePipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string[] keyColumns,
        string[] updateColumns,
        string instanceHash,
        int batchSize = 5000,
        CancellationToken ct = default)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunBulk(_logger, "BULK UPDATE PIPELINE", tableName);
            return DrainChannelAsync(reader, ct);
        }

        return ProcessChannelBatchAsync(reader, batchSize, ct,
            batch => BulkUpdateAsync(tableName, batch, keyColumns, updateColumns, instanceHash, ct));
    }

    /// <summary>
    /// Channel 기반 비동기 스트리밍으로 대용량 데이터를 파이프라인 방식으로 삭제합니다.
    /// </summary>
    /// <typeparam name="T">삭제 기준 데이터 타입</typeparam>
    /// <param name="tableName">대상 테이블명</param>
    /// <param name="reader">데이터 스트림 소스</param>
    /// <param name="keyColumns">매칭 기준 컬럼명 배열</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="batchSize">배치 크기 (기본 5000행)</param>
    /// <param name="ct">취소 토큰</param>
    /// <exception cref="SqlException">Bulk Delete 실패 시</exception>
    /// <remarks>
    /// <para><strong>💡 동작 방식</strong></para>
    /// <list type="bullet">
    /// <item>Channel에서 데이터 읽기 → 배치 크기 도달 → BulkDeleteAsync 호출</item>
    /// <item>Dry-Run 모드: DrainChannelAsync로 데이터 소비하여 Producer 블로킹 방지</item>
    /// </list>
    /// </remarks>
    public Task BulkDeletePipelineAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string[] keyColumns,
        string instanceHash,
        int batchSize = 5000,
        CancellationToken ct = default)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunBulk(_logger, "BULK DELETE PIPELINE", tableName);
            return DrainChannelAsync(reader, ct);
        }

        return ProcessChannelBatchAsync(reader, batchSize, ct,
            batch => BulkDeleteAsync(tableName, batch, keyColumns, instanceHash, ct));
    }



    private async Task BulkInsertInternalAsync<T>(
        string table,
        IEnumerable<T> data,
        string instanceHash,
        CancellationToken ct,
        SqlConnection? externalConn = null,
        SqlTransaction? externalTran = null,
        bool keepIdentity = false)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunBulk(_logger, "BULK INSERT", table);
            return;
        }

        int initialBatch = _options.BulkBatchSize;
        var sizer = new AdaptiveBatchSizer(initialBatch);

        T[] buffer = ArrayPool<T>.Shared.Rent(sizer.MaxSize);

        try
        {
            foreach (var chunk in data.Chunk(sizer.MaxSize))
            {
                int count = chunk.Length;
                chunk.CopyTo(buffer, 0);

                if (_memoryMonitor.IsCritical)
                    sizer.Throttle();

                long startTicks = Stopwatch.GetTimestamp();

                if (externalConn != null)
                {
                    await FlushBulkToConnectionAsync(
                        externalConn, externalTran, table, buffer, count, instanceHash, ct, keepIdentity).ConfigureAwait(false);
                }
                else
                {
                    await FlushBulkAsync(table, buffer, count, instanceHash, ct).ConfigureAwait(false);
                }

                #region 배치 실행 시간 메트릭 기록

                long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
                var elapsed = TimeSpan.FromTicks(elapsedTicks);

                var info = new DbRequestInfo(
                    InstanceId: instanceHash,
                    DbSystem: "mssql",
                    Operation: "BULK INSERT",
                    Target: table);

                DbMetrics.TrackDuration(elapsed, info);

                #endregion

                // 메모리 부하 및 처리량 기반으로 다음 배치 크기 조정
                sizer.Adjust(elapsed, count, _memoryMonitor.LoadFactor);
            }
        }
        finally
        {
            ArrayPool<T>.Shared.Return(
                buffer,
                clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    private async Task BulkUpdateOptimizedAsync<T>(
        string table,
        IEnumerable<T> data,
        string[] keys,
        string[] updates,
        string instanceHash,
        CancellationToken ct)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunBulk(_logger, "BULK UPDATE", table);
            return;
        }

        var req = new DbRequest<object?>(
             instanceHash,
             $"BULK UPDATE {table}",
             CommandType.Text,
             null,
             ct,
             IsTransactional: _strategy.IsTransactional);

        await _strategy.ExecuteAsync(req, async (conn, token) =>
        {
            var tran = _strategy.CurrentTransaction;
            // [Optimization] Guid.NewGuid() 할당 제거 -> Interlocked Counter
            var tempInfo = Interlocked.Increment(ref s_bulkCounter);
            var tempTable = $"#Tmp_{tempInfo:X}";

            // 1. Create Temp Table
            // SELECT INTO creates the table with the same schema as source
            using (var cmd = new SqlCommand($"SELECT TOP 0 * INTO {tempTable} FROM {Quote(table)}", conn, tran))
            {
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            try
            {
                // 2. Bulk Insert into Temp Table (reuse connection)
                // [Fix] KeepIdentity: true to preserve Id values from DTO into IDENTITY column of temp table
                await BulkInsertInternalAsync(tempTable, data, instanceHash, token, conn, tran, keepIdentity: true).ConfigureAwait(false);

                // 3. Merge
                var mergeSql = BuildMergeSql(table, tempTable, keys, updates);
                using (var cmd = new SqlCommand(mergeSql, conn, tran))
                {
                    cmd.CommandTimeout = _options.BulkCommandTimeoutSeconds;
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            finally
            {
                // 4. Drop Temp Table
                using (var cmd = new SqlCommand($"IF OBJECT_ID('tempdb..{tempTable}') IS NOT NULL DROP TABLE {tempTable}", conn, tran))
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            return 0;
        }, ct).ConfigureAwait(false);
    }

    private async Task BulkDeleteOptimizedAsync<T>(
        string table,
        IEnumerable<T> data,
        string[] keys,
        string instanceHash,
        CancellationToken ct)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunBulk(_logger, "BULK DELETE", table);
            return;
        }

        var req = new DbRequest<object?>(
             instanceHash,
             $"BULK DELETE {table}",
             CommandType.Text,
             null,
             ct,
             IsTransactional: _strategy.IsTransactional);

        await _strategy.ExecuteAsync(req, async (conn, token) =>
        {
            var tran = _strategy.CurrentTransaction;
            // [Optimization] Guid.NewGuid() 할당 제거 -> Interlocked Counter
            var tempInfo = Interlocked.Increment(ref s_bulkCounter);
            var tempTable = $"#Del_{tempInfo:X}";

            // 1. Create Temp Table
            using (var cmd = new SqlCommand($"SELECT TOP 0 * INTO {tempTable} FROM {Quote(table)}", conn, tran))
            {
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            try
            {
                // 2. Bulk Insert into Temp Table (reuse connection)
                // [Fix] KeepIdentity: true to preserve Id values
                await BulkInsertInternalAsync(tempTable, data, instanceHash, token, conn, tran, keepIdentity: true).ConfigureAwait(false);

                // 3. Delete
                // [Optimization] LINQ Select + Join 제거
                // var joinOn = string.Join(" AND ", keys.Select(k => $"T.{Quote(k)} = S.{Quote(k)}"));
                var sbJoin = new StringBuilder();
                for (int i = 0; i < keys.Length; i++)
                {
                    if (i > 0) sbJoin.Append(" AND ");
                    var q = Quote(keys[i]);
                    sbJoin.Append("T.").Append(q).Append(" = S.").Append(q);
                }
                var joinOn = sbJoin.ToString();

                var deleteSql = $"DELETE T FROM {Quote(table)} T INNER JOIN {tempTable} S ON {joinOn}";

                using (var cmd = new SqlCommand(deleteSql, conn, tran))
                {
                    cmd.CommandTimeout = _options.BulkCommandTimeoutSeconds;
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            finally
            {
                using (var cmd = new SqlCommand($"IF OBJECT_ID('tempdb..{tempTable}') IS NOT NULL DROP TABLE {tempTable}", conn, tran))
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
            return 0;
        }, ct).ConfigureAwait(false);
    }

    private async Task FlushBulkAsync<T>(
        string table,
        T[] buffer,
        int count,
        string instanceHash,
        CancellationToken ct)
    {
        var req = new DbRequest<object?>(
            instanceHash,
            $"BULK INSERT {table}",
            CommandType.Text,
            null,
            ct,
            IsTransactional: _strategy.IsTransactional);

        await _strategy.ExecuteAsync(
            req,
            async (conn, token) =>
            {
                await FlushBulkToConnectionAsync(conn, _strategy.CurrentTransaction, table, buffer, count, instanceHash, token).ConfigureAwait(false);
                return count;
            },
            ct).ConfigureAwait(false);
    }

    private async Task FlushBulkToConnectionAsync<T>(
        SqlConnection conn,
        SqlTransaction? tran,
        string table,
        T[] buffer,
        int count,
        string instanceHash,
        CancellationToken ct,
        bool keepIdentity = false)
    {
        var options = SqlBulkCopyOptions.TableLock;
        if (keepIdentity)
            options |= SqlBulkCopyOptions.KeepIdentity;

        using var bulk = new SqlBulkCopy(
            conn,
            options,
            tran)
        {
            DestinationTableName = table,
            BatchSize = count,
            BulkCopyTimeout = _options.BulkCommandTimeoutSeconds
        };

        var enumerable = new ArraySegmentEnumerable<T>(buffer, count);
        using var reader = DbBinder.ToDataReader(enumerable);

        // [Fix] 컬럼 매핑 명시
        if (reader.FieldCount > 0)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                bulk.ColumnMappings.Add(name, name);
            }
        }

        await bulk.WriteToServerAsync(reader, ct).ConfigureAwait(false);

        // Bulk Row 수 기반 메트릭
        DbMetrics.TrackBulkRows(
            count,
            table,
            new DbRequestInfo(InstanceId: instanceHash, Operation: "BULK INSERT", Target: table));
    }

    private Task ExecuteNonQuerySimpleAsync(
        string sql,
        string instanceHash,
        CancellationToken ct)
    {
        var req = new DbRequest<object?>(
            instanceHash,
            sql,
            CommandType.Text,
            null,
            ct,
            IsTransactional: _strategy.IsTransactional);

        return ExecutePipelineAsync(
            req,
            DbExecutionOptions.Default,
            async (cmd, token) => await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false));
    }



    private async Task BulkPipelineInternalAsync<T>(
        string tableName,
        ChannelReader<T> reader,
        string instanceHash,
        int initialBatchSize,
        CancellationToken ct,
        Func<string, T[], int, string, CancellationToken, Task> flusher)
    {
        int batch = _memoryMonitor.IsCritical ? 1000 : initialBatchSize;
        var sizer = new AdaptiveBatchSizer(batch);

        T[] buffer = ArrayPool<T>.Shared.Rent(sizer.MaxSize);
        int count = 0;

        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (reader.TryRead(out var item))
                {
                    buffer[count++] = item;

                    if (count >= sizer.Current)
                    {
                        if (_memoryMonitor.IsCritical)
                            sizer.Throttle();

                        long start = Stopwatch.GetTimestamp();

                        await flusher(tableName, buffer, count, instanceHash, ct).ConfigureAwait(false);

                        var elapsed = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - start);

                        sizer.Adjust(elapsed, count, _memoryMonitor.LoadFactor);

                        count = 0;
                    }
                }
            }

            if (count > 0)
            {
                await flusher(tableName, buffer, count, instanceHash, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<T>.Shared.Return(
                buffer,
                clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    private static async Task ProcessChannelBatchAsync<T>(
        ChannelReader<T> reader,
        int batchSize,
        CancellationToken ct,
        Func<List<T>, Task> processor)
    {
        var batch = new List<T>(batchSize);

        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    await processor(batch).ConfigureAwait(false);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
        {
            await processor(batch).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dry-Run 모드에서 ChannelReader를 비워주기 위한 Drain 헬퍼입니다.
    /// </summary>
    private static async Task DrainChannelAsync<T>(ChannelReader<T> reader, CancellationToken ct)
    {
        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (reader.TryRead(out _)) { }
        }
    }

    #endregion

    #region 공개 API - 재개 가능 쿼리

    /// <typeparam name="TCursor">커서 타입</typeparam>
    /// <typeparam name="TResult">결과 행 타입</typeparam>
    /// <param name="queryBuilder">커서 값을 받아 SQL 쿼리 생성</param>
    /// <param name="cursorSelector">결과 행에서 커서 값 추출</param>
    /// <param name="instanceHash">DB 인스턴스 해시</param>
    /// <param name="initialCursor">초기 커서 값</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>재개 가능한 결과 스트림</returns>
    /// <exception cref="SqlException">SQL 실행 실패 시</exception>
    /// <exception cref="InvalidOperationException">커서 정체로 무한 루프 판단 시</exception>
    /// <remarks>
    /// <para><strong>💡 사용 시나리오</strong></para>
    /// <list type="bullet">
    /// <item>대용량 스트리밍 중단 후 재개</item>
    /// <item>장시간 ETL/마이그레이션</item>
    /// <item>Redis 등 외부 상태 저장소 연동</item>
    /// </list>
    /// </remarks>
    public IAsyncEnumerable<TResult> QueryResumableAsync<TCursor, TResult>(
        Func<TCursor, string> queryBuilder,
        Func<TResult, TCursor> cursorSelector,
        string instanceHash,
        TCursor initialCursor = default!,
        CancellationToken ct = default)
        => QueryResumableInternalAsync(
            $"Resumable_{instanceHash}_{typeof(TResult).Name}",
            queryBuilder,
            cursorSelector,
            instanceHash,
            initialCursor,
            ct);

    private async IAsyncEnumerable<TResult> QueryResumableInternalAsync<TCursor, TResult>(
        string queryKey,
        Func<TCursor, string> queryBuilder,
        Func<TResult, TCursor> cursorSelector,
        string instanceHash,
        TCursor initialCursor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_options.EnableDryRun)
        {
            LogDryRunResumable(_logger);
            yield break;
        }

        // 외부 저장소(예: Redis)에 보존된 마지막 커서 복원
        var lastCursor = await _resumableStore
            .GetLastCursorAsync<TCursor>(instanceHash, queryKey, ct)
            .ConfigureAwait(false) ?? initialCursor;

        int consecutiveErrors = 0;
        int maxRetries = _options.ResumableQueryMaxRetries;
        int baseDelayMs = _options.ResumableQueryBaseDelayMs;
        int maxDelayMs = _options.ResumableQueryMaxDelayMs;

        // ✅ [무한 루프 방지] 커서 진행 검증기
        TCursor? previousCursor = lastCursor;
        int stuckCount = 0;
        const int MaxStuckIterations = 3; // 3회 연속 정체 시 중단

        while (!ct.IsCancellationRequested)
        {
            var sql = queryBuilder(lastCursor);

            IAsyncEnumerator<TResult>? enumerator = null;
            bool hasRowsInBatch = false;
            TCursor? batchLastCursor = default;

            // 스트림 생성
            try
            {
                var stream = QueryStreamCoreAsync<object?, TResult>(
                    sql,
                    null!,
                    instanceHash,
                    CommandType.Text,
                    DbExecutionOptions.Default,
                    ct);

                enumerator = stream.GetAsyncEnumerator(ct);
            }
            catch (SqlException ex) when (IsTransient(ex) && consecutiveErrors < maxRetries)
            {
                consecutiveErrors++;
                var delay = ComputeBackoffDelay(consecutiveErrors, baseDelayMs, maxDelayMs);

                _logger.LogWarning(ex,
                    "[Resumable] 스트림 생성 중 일시적 오류 발생. {Retry}/{Max}회 재시도 예정. (대기: {Delay}ms, 커서: {Cursor})",
                    consecutiveErrors,
                    maxRetries,
                    delay.TotalMilliseconds,
                    lastCursor);

                DbMetrics.TrackRetry(
                    "resumable_stream_create",
                    new DbRequestInfo(InstanceId: instanceHash));

                await Task.Delay(delay, ct).ConfigureAwait(false);
                goto ContinueOuterLoop;
            }

            if (enumerator is null)
                yield break;

            while (true)
            {
                bool moved;

                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (SqlException ex) when (IsTransient(ex) && consecutiveErrors < maxRetries)
                {
                    consecutiveErrors++;
                    var delay = ComputeBackoffDelay(consecutiveErrors, baseDelayMs, maxDelayMs);

                    _logger.LogWarning(ex,
                        "[Resumable] 스트림 열거 중 일시적 오류 발생. {Retry}/{Max}회 재시도 예정. (대기: {Delay}ms, 커서: {Cursor})",
                        consecutiveErrors,
                        maxRetries,
                        delay.TotalMilliseconds,
                        lastCursor);

                    DbMetrics.TrackRetry(
                        "resumable_stream_iterate",
                        new DbRequestInfo(InstanceId: instanceHash));

                    await enumerator.DisposeAsync().ConfigureAwait(false);
                    await Task.Delay(delay, ct).ConfigureAwait(false);

                    goto ContinueOuterLoop;
                }
                catch (Exception ex)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);

                    _logger.LogError(ex,
                        "[Resumable] 스트림 열거 중 치명적 오류 발생. 재시도를 중단합니다. (커서: {Cursor})",
                        lastCursor);

                    throw;
                }

                if (!moved)
                    break;

                hasRowsInBatch = true;
                consecutiveErrors = 0;

                var item = enumerator.Current;
                yield return item;

                batchLastCursor = cursorSelector(item);
            }

            await enumerator.DisposeAsync().ConfigureAwait(false);

            if (!hasRowsInBatch || batchLastCursor is null)
                yield break;

            // ✅ [커서 진행 검증] 커서가 변경되었는지 확인
            var comparer = EqualityComparer<TCursor>.Default;
            if (comparer.Equals(batchLastCursor, previousCursor))
            {
                stuckCount++;

                _logger.LogWarning(
                    "[Resumable Query] 커서가 진행하지 않음. 정체 횟수: {StuckCount}/{Max} (현재 커서: {Cursor})",
                    stuckCount, MaxStuckIterations, batchLastCursor);

                // 3회 연속 정체 시 무한 루프로 간주하고 중단
                if (stuckCount >= MaxStuckIterations)
                {
                    _logger.LogError(
                        "[Resumable Query] 커서가 {Count}회 연속 정체되어 무한 루프로 간주합니다. 중단합니다. (커서: {Cursor})",
                        MaxStuckIterations, batchLastCursor);

                    throw new InvalidOperationException(
                        $"Resumable Query에서 커서가 진행하지 않아 무한 루프로 판단되어 중단되었습니다. " +
                        $"커서 값: {batchLastCursor}, 연속 정체 횟수: {stuckCount}. " +
                        $"queryBuilder 또는 cursorSelector 로직을 확인하세요.");
                }
            }
            else
            {
                // 커서가 진행됨 - 카운터 리셋
                stuckCount = 0;
            }

            lastCursor = batchLastCursor;
            previousCursor = lastCursor;

            // 커서 저장 – 다음 배치 또는 프로세스 재시작 시 복원
            await _resumableStore
                .SaveCursorAsync(instanceHash, queryKey, lastCursor, ct)
                .ConfigureAwait(false);

        ContinueOuterLoop:
            continue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTransient(SqlException ex)
        => ex.Number is 1205 or -2 or 53 or 233;

    private static TimeSpan ComputeBackoffDelay(int retry, int baseDelayMs, int maxDelayMs)
    {
        if (baseDelayMs <= 0)
            baseDelayMs = 100;

        if (maxDelayMs <= 0)
            maxDelayMs = 5000;

        var raw = baseDelayMs * Math.Pow(2, retry - 1);
        var clamped = Math.Min(raw, maxDelayMs);

        return TimeSpan.FromMilliseconds(clamped);
    }

    #endregion

    #region 내부 로직 - 실행 파이프라인 및 헬퍼

    /// <summary>
    /// Scalar / NonQuery / SingleRow 등 "단일 결과"를 처리하는 공통 실행 파이프라인입니다.
    /// <para>
    /// <b>[파이프라인 단계]</b><br/>
    /// 1. Dry-Run 검사: 쓰기 작업(INSERT/UPDATE/DELETE/MERGE) 시 실제 실행을 건너뜁니다.<br/>
    /// 2. OpenTelemetry Activity 시작: 분산 추적을 위한 컨텍스트 생성 (db.system, db.operation, db.statement 태그 포함)<br/>
    /// 3. Chaos Injection: 개발/테스트 환경에서 지연 또는 예외를 의도적으로 주입하여 회복탄력성 검증<br/>
    /// 4. Strategy 실행: Resilient 또는 Transactional 전략에 따라 연결 획득 및 트랜잭션 관리<br/>
    /// 5. 스키마 조회 및 파라미터 매핑: SchemaService를 통해 SP 메타데이터를 조회하고 파라미터 바인딩<br/>
    /// 6. Interceptor Executing: 실행 전 인터셉터 체인 호출 (로깅, 모킹, 검증 등)<br/>
    /// 7. 명령 실행: 실제 DbCommand.ExecuteXxxAsync 호출<br/>
    /// 8. 메트릭 기록: 실행 시간(Duration)을 DbMetrics에 기록<br/>
    /// 9. Interceptor Executed: 실행 후 인터셉터 체인 호출 (성능 로깅, 결과 변환 등)<br/><br/>
    /// <b>[예외 처리 전략]</b><br/>
    /// - SqlException은 Polly Resilience Pipeline이 처리할 수 있도록 그대로 전파합니다.<br/>
    /// - 기타 예외는 LibDbExceptionFactory를 통해 컨텍스트 정보(CommandText, InstanceId)를 포함한 예외로 래핑합니다.<br/>
    /// - Activity는 예외 발생 시 Error 상태로 설정되어 분산 추적 시스템에 전달됩니다.
    /// </para>
    /// </summary>
    private async Task<TResult> ExecutePipelineAsync<TParams, TResult>(
        DbRequest<TParams> request,
        DbExecutionOptions execOptions,
        Func<SqlCommand, CancellationToken, Task<TResult>> operation)
    {
        // [Dry-Run] Text 기반 쓰기 명령은 실제 실행을 건너뜁니다.
        if (_options.EnableDryRun && IsWriteOperation(request.CommandText))
        {
            LogDryRunExecution(_logger, request.CommandText);
            return default!;
        }

        // [Optimization] Activity 이름 할당 제거
        string activityName = request.CommandType switch
        {
            CommandType.Text => ActivityNameQuery,
            CommandType.StoredProcedure => ActivityNameProcedure,
            _ => ActivityNameCommand
        };

        using var activity = s_activitySource.StartActivity(activityName);
        activity?.SetTag("db.system", "mssql");
        activity?.SetTag("db.operation", request.CommandType.ToString());
        activity?.SetTag("db.statement", request.CommandText);
        activity?.SetTag("db.instance", request.InstanceHash);

        // Chaos(지연/예외) 주입 – 개발/테스트 환경에서 회복탄력성 검증용
        await _chaosInjector.InjectAsync(request.CancellationToken).ConfigureAwait(false);

        try
        {
            return await _strategy.ExecuteAsync(request, async (conn, token) =>
            {
                await using var cmd = new SqlCommand(request.CommandText, conn)
                {
                    CommandType = request.CommandType,
                    CommandTimeout = execOptions.CommandTimeout ?? _options.DefaultCommandTimeoutSeconds
                };

                _strategy.EnlistTransaction(cmd);

                // 스키마 조회 + 파라미터 매핑
                await PrepareParametersAsync(cmd, request.Parameters, request.InstanceHash, execOptions, token)
                    .ConfigureAwait(false);

                // Interceptor: Executing 단계
                var ctx = new DbCommandInterceptionContext(request.InstanceHash, token);
                await _interceptorChain.OnExecutingAsync(cmd, ctx).ConfigureAwait(false);

                if (ctx.SuppressExecution)
                {
                    LogMockingExecution(_logger, request.CommandText);

                    if (ctx.MockResult is TResult casted)
                        return casted;

                    return default!;
                }

                // 내부 try/catch 제거 – 실제 실패 시 상위 catch 한 곳에서만
                // Activity 상태 설정 + 로깅을 수행합니다.
                long startTicks = Stopwatch.GetTimestamp();
                var result = await operation(cmd, token).ConfigureAwait(false);

                long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
                var elapsed = TimeSpan.FromTicks(elapsedTicks);

                var info = new DbRequestInfo(
                    InstanceId: request.InstanceHash,
                    DbSystem: "mssql",
                    Operation: request.CommandType.ToString(),
                    Target: request.CommandText
                );

                // 전역 메트릭 – 단일 명령 실행 시간 기록
                DbMetrics.TrackDuration(elapsed, info);

                var executedEvent = new DbCommandExecutedEventData(
                    DurationUs: TicksToMicroseconds(elapsedTicks),
                    Result: result
                );

                await _interceptorChain.OnExecutedAsync(cmd, executedEvent).ConfigureAwait(false);

                return result;
            }, request.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex,
                "[Executor] DB 파이프라인 실행 중 오류가 발생했습니다. " +
                "(SQL: {CommandText}, Instance: {InstanceId}, CommandType: {CommandType})",
                request.CommandText,
                request.InstanceHash,
                request.CommandType);

            // [Modify] SqlException은 포장하지 않고 그대로 전파해야 Polly/Test가 정상 동작함
            if (ex is SqlException)
                throw;

            throw LibDbExceptionFactory.CreateCommandExecutionFailed(request.CommandText, ex);
        }
    }

    /// <summary>
    /// SP 스키마 조회 전략과 옵션을 고려하여 파라미터를 매핑합니다.
    /// </summary>
    private async ValueTask PrepareParametersAsync<TParams>(
        SqlCommand cmd,
        TParams parameters,
        string instanceHash,
        DbExecutionOptions execOptions,
        CancellationToken ct)
    {
        SpSchema? schema = null;

        if (cmd.CommandType == CommandType.StoredProcedure)
        {
            // 전략 기본 모드 vs. 명령 단위 오버라이드
            var mode = execOptions.SchemaModeOverride ?? _strategy.DefaultSchemaMode;

            if (mode != SchemaResolutionMode.None)
            {
                try
                {
                    // 실제 Snapshot/Service/Fallback 전략은 ISchemaService 내부에 위임
                    schema = await _schemaService
                        .GetSpSchemaAsync(cmd.CommandText, instanceHash, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // SnapshotOnly 모드는 실패 시 바로 예외 전파
                    if (mode == SchemaResolutionMode.SnapshotOnly)
                        throw;

                    _logger.LogWarning(ex,
                        "[Schema] SP '{SpName}' 스키마 조회 실패. 스키마 없이 파라미터 매핑을 진행합니다.",
                        cmd.CommandText);
                }
            }
        }

        _mapperFactory
            .GetMapper<TParams>()
            .MapParameters(cmd, parameters, schema);
    }

    /// <summary>
    /// 스트리밍 방식으로 쿼리를 실행하고 결과를 비동기적으로 열거합니다.
    /// <para>
    /// <b>[핵심 기능]</b><br/>
    /// - 메모리 최소화: 전체 결과를 메모리에 적재하지 않고 한 행씩 처리<br/>
    /// - Dry-Run 지원: 쓰기 작업 시 실행 건너뜀<br/>
    /// - Activity 추적: OpenTelemetry 분산 추적 태그 설정<br/>
    /// </para>
    /// </summary>
    private async IAsyncEnumerable<TResult> QueryStreamCoreAsync<TParams, TResult>(
        string commandText,
        TParams parameters,
        string instanceHash,
        CommandType commandType,
        DbExecutionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // [Dry-Run] Text 기반 쓰기 스트리밍은 실행을 건너뜁니다.
        if (_options.EnableDryRun && IsWriteOperation(commandText))
        {
            LogDryRunStream(_logger, commandText);
            yield break;
        }

        var req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        System.Data.Common.DbDataReader? reader;
        long startTicks = Stopwatch.GetTimestamp();

        using (var activity = s_activitySource.StartActivity("DB QueryStream"))
        {
            activity?.SetTag("db.system", "mssql");
            activity?.SetTag("db.operation", commandType.ToString());
            activity?.SetTag("db.statement", commandText);
            activity?.SetTag("db.instance", instanceHash);

            try
            {
                reader = await _strategy.ExecuteStreamAsync(
                    req,
                    async (conn, token) =>
                    {
                        var cmd = new SqlCommand(commandText, conn)
                        {
                            CommandType = commandType,
                            CommandTimeout = _options.DefaultCommandTimeoutSeconds
                        };

                        _strategy.EnlistTransaction(cmd);

                        await PrepareParametersAsync(cmd, parameters, instanceHash, options, token)
                            .ConfigureAwait(false);

                        var ctx = new DbCommandInterceptionContext(instanceHash, token);
                        await _interceptorChain.OnExecutingAsync(cmd, ctx).ConfigureAwait(false);

                        if (ctx.SuppressExecution)
                        {
                            LogMockingExecution(_logger, commandText);
                            return default(SqlDataReader)!;
                        }

                        var behavior = CommandBehavior.Default;
                        if (!_strategy.IsTransactional)
                            behavior |= CommandBehavior.CloseConnection;

                        // BLOB/Stream 매핑 시 SequentialAccess 활성화
                        if (typeof(TResult) == typeof(Stream) || typeof(TResult) == typeof(byte[]))
                            behavior |= CommandBehavior.SequentialAccess;

                        return await cmd.ExecuteReaderAsync(behavior, token).ConfigureAwait(false);
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogWarning(ex,
                    "Streaming 쿼리 실행 중 오류가 발생했습니다. (SQL: {CommandText})",
                    commandText);
                throw LibDbExceptionFactory.CreateCommandExecutionFailed(commandText, ex);
            }
        }

        #region Streaming 쿼리 - 리더 획득 시간 메트릭

        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        var elapsed = TimeSpan.FromTicks(elapsedTicks);

        var info = new DbRequestInfo(
            InstanceId: instanceHash,
            DbSystem: "mssql",
            Operation: commandType.ToString(),
            Target: commandText);

        DbMetrics.TrackDuration(elapsed, info);

        #endregion

        if (reader is null)
            yield break;

        var mapper = _mapperFactory.GetMapper<TResult>();

        try
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                yield return mapper.MapResult(reader);
            }
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static CommandBehavior CommandTypeToSingleRowBehavior(CommandType type, bool isTransactional)
    {
        var behavior = CommandBehavior.SingleRow;

        if (!isTransactional)
            behavior |= CommandBehavior.CloseConnection;

        return behavior;
    }

    /// <summary>
    /// 텍스트 기반 쓰기 명령(INSERT/UPDATE/DELETE/MERGE) 여부를 판별합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsWriteOperation(string cmdText)
        => cmdText.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
        || cmdText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
        || cmdText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
        || cmdText.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long TicksToMicroseconds(long ticks)
        => ticks * 1_000_000L / Stopwatch.Frequency;

    #endregion

    #region 유틸리티 - 문자열 처리 및 로깅

    private static string Quote(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        // 이미 전체가 감싸져 있는 경우 ([dbo].[Table] 형태는 여기서 걸러지지 않음)
        // 단순하게 [ ] 로만 시작/끝나는 경우는 내부 점검 필요.
        // 여기서는 "Schema.Table" 처리를 위해 Split 로직을 우선합니다.

        var parts = identifier.Split('.');
        if (parts.Length > 1)
        {
            return string.Join(".", parts.Select(p => QuotePart(p)));
        }

        return QuotePart(identifier);

        static string QuotePart(string part)
        {
            // [Security Fix] 기존 대괄호를 제거한 뒤, 닫는 대괄호를 이스케이프(']' -> ']]')하여 인젝션을 방지합니다.
            var trimmed = part.Trim('[', ']');
            return $"[{trimmed.Replace("]", "]]")}]";
        }
    }

    private void ValidateMarsEnabled(System.Data.Common.DbConnection conn)
    {
        // 캐시 확인 (Fast Path)
        string connStr = conn.ConnectionString;
        if (s_marsEnabledCache.TryGetValue(connStr, out bool enabled))
        {
            if (!enabled) ThrowMarsRequired();
            return;
        }

        // 파싱 및 검증 (Slow Path - 연결당 1회)
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
        bool isEnabled = builder.MultipleActiveResultSets;

        s_marsEnabledCache[connStr] = isEnabled;

        if (!isEnabled)
        {
            ThrowMarsRequired();
        }
    }

    private static void ThrowMarsRequired()
    {
        throw new InvalidOperationException(
            "QueryMultipleAsync를 안전하게 사용하려면 ConnectionString에 'MultipleActiveResultSets=True' 설정이 필요합니다. " +
            "(설정 예: Server=...; Database=...; MultipleActiveResultSets=True;)");
    }

    private static string BuildMergeSql(string target, string source, string[] keys, string[] updates)
    {
        var sb = new StringBuilder();

        // [Fix] Quote 메서드를 사용하여 스키마(dbo.)가 포함된 테이블명을 올바르게 처리합니다.
        // [Fix] Quote 메서드를 사용하여 스키마(dbo.)가 포함된 테이블명을 올바르게 처리합니다.
        sb.Append($"MERGE INTO {Quote(target)} AS T USING {Quote(source)} AS S ON (");

        // [Optimization] LINQ Select + Join 제거
        // sb.Append(string.Join(" AND ", keys.Select(k => $"T.{Quote(k)} = S.{Quote(k)}")));
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) sb.Append(" AND ");
            var q = Quote(keys[i]);
            sb.Append("T.").Append(q).Append(" = S.").Append(q);
        }

        sb.Append(") WHEN MATCHED THEN UPDATE SET ");

        // sb.Append(string.Join(", ", updates.Select(c => $"T.{Quote(c)} = S.{Quote(c)}")));
        for (int i = 0; i < updates.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var q = Quote(updates[i]);
            sb.Append("T.").Append(q).Append(" = S.").Append(q);
        }
        sb.Append(';');

        return sb.ToString();
    }

    #region 로깅 메서드

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "[MOCK] '{CommandText}' 실행이 인터셉터에 의해 모킹되었습니다.")]
    private static partial void LogMockingExecution(ILogger logger, string commandText);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[DRY-RUN] '{CommandText}' 실행을 건너뜁니다.")]
    private static partial void LogDryRunExecution(ILogger logger, string commandText);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[DRY-RUN] Streaming Query '{CommandText}' 실행을 건너뜁니다.")]
    private static partial void LogDryRunStream(ILogger logger, string commandText);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[DRY-RUN] {Operation} '{TableName}' 실행을 건너뜁니다.")]
    private static partial void LogDryRunBulk(ILogger logger, string operation, string tableName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "[DRY-RUN] Resumable Query 실행을 건너뜁니다.")]
    private static partial void LogDryRunResumable(ILogger logger);

    #endregion
    #endregion

}
