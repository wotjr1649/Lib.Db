// ============================================================================
// 파일: Lib.Db.Execution/SqlDbExecutor.cs
// 설명: [Architecture] 최상위 통합 실행기
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lib.Db.Contracts;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.Diagnostics;
using Lib.Db.Execution.Executors;

namespace Lib.Db.Execution;

/// <summary>
/// .NET 10 / C# 14 기반의 고성능 통합 SQL Server 실행 엔진입니다.
/// </summary>
/// <remarks>
/// <para><b>[설계의도 (Design Rationale)]</b></para>
/// <list type="bullet">
/// <item><description><strong>전략 패턴 기반 이중 모드</strong>: Resilient(회복탄력성) 모드와 Transactional(트랜잭션) 모드를 런타임에 전환하여 단일 실행기로 다양한 실행 컨텍스트를 지원합니다.</description></item>
/// <item><description><strong>AOT 호환성</strong>: Reflection을 제거하고 Source Generator 기반 매퍼(`IGeneratedMapper`)를 사용하여 Native AOT 환경에서 동작합니다.</description></item>
/// <item><description><strong>Zero-Allocation DB 액세스</strong>: ArrayPool, Span&lt;T&gt;, 구조체 열거자를 활용하여 GC 압력을 최소화합니다.</description></item>
/// <item><description><strong>자동 회복</strong>: Polly Pipeline을 통해 Deadlock, Timeout, Connection 실패 시 자동 재시도 및 Circuit Breaker로 장애를 격리합니다.</description></item>
/// </list>
/// </remarks>
internal sealed partial class SqlDbExecutor(
    IDbExecutionStrategy strategy,
    ISchemaService schemaService,
    IMapperFactory mapperFactory,
    InterceptorChain interceptorChain,
    IEnumerable<IDbInterceptor> userInterceptors,
    LibDbOptions options,
    ILogger<SqlDbExecutor> logger
) : IDbExecutor
{
    #region 상수 및 필드

    // [Optimization] Activity 이름 캐싱
    private const string ActivityNameQuery = "DB Query";
    private const string ActivityNameProcedure = "DB Procedure";
    private const string ActivityNameCommand = "DB Command";
    private const string RedactedCommandText = "[redacted]";
    private const string ActivityErrorDescription = "db.execution.failed";
    // [MARS Validation] 연결 문자열별 MARS 활성화 여부 캐시
    // 앱당 연결 문자열 종류는 통상 1~5개이므로 크기 상한/Clear 불필요
    // → Count >= N 체크 + Clear() 조합은 멀티스레드 환경에서 비원자적이었음(제거)
    private static readonly ConcurrentDictionary<string, bool> s_marsEnabledCache = new();

    private readonly IDbExecutionStrategy _strategy = strategy;
    private readonly ISchemaService _schemaService = schemaService;
    private readonly IMapperFactory _mapperFactory = mapperFactory;
    private readonly InterceptorChain _interceptorChain = interceptorChain;
    private readonly IDbInterceptor[] _userInterceptors = userInterceptors.ToArray();
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
        EnsureRawSqlAllowed(commandText, commandType);

        string diagnosticCommandText = GetDiagnosticCommandText(commandText, commandType);
        string diagnosticInstance = DbDiagnosticRedactor.RedactInstanceId(instanceHash.Value) ?? instanceHash.Value;

        // ---------------------------------------------------------------------
        // [Observability] Start Activity & Metric
        // ---------------------------------------------------------------------
        using Activity? activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("SqlDbExecutor.ExecuteAsync")
            : null;

        if (activity != null)
        {
            activity.SetTag("db.system", "mssql");
            activity.SetTag("db.operation", commandType.ToString());
            activity.SetTag("db.statement", diagnosticCommandText);
            activity.SetTag("db.command_type", commandType.ToString());
            activity.SetTag("libdb.instance", diagnosticInstance);
        }

        if (_options.EnableObservability)
        {
            LibDbTelemetry.DbRequestsTotal.Add(1,
                new KeyValuePair<string, object?>("operation", "ExecuteAsync"),
                new KeyValuePair<string, object?>("instance", diagnosticInstance));
        }

        long startTime = _options.EnableObservability ? Stopwatch.GetTimestamp() : 0;

        try
        {
            DbRequest<TParams?> request = new DbRequest<TParams?>(
                instanceHash.ToString(),
                commandText,
                commandType,
                parameters,
                ct,
                IsTransactional: _strategy.IsTransactional);

            // Use ExecuteStreamAsync to get the DataReader
            using DbDataReader? reader = await _strategy.ExecuteStreamAsync(request, async (conn, token) =>
            {
                using SqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = commandText;
                cmd.CommandType = commandType;
                cmd.CommandTimeout = options?.CommandTimeout ?? _options.DefaultCommandTimeoutSeconds;

                _strategy.EnlistTransaction(cmd);

                // Bind Parameters
                if (parameters != null)
                {
                    // Use _mapperFactory to get the mapper
                    ISqlMapper<TParams> mapper = _mapperFactory.GetMapper<TParams>();
                    mapper.MapParameters(cmd, parameters, null);
                }

                return await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);

            if (reader != null)
            {
                // Map results
                ISqlMapper<TResult> resultMapper = _mapperFactory.GetMapper<TResult>();
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
                double duration = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
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
        DbRequest<TParams> req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        return ExecutePipelineAsync(req, options, async (cmd, token) =>
        {
            CommandBehavior behavior = CommandTypeToSingleRowBehavior(commandType, _strategy.IsTransactional);

            await using SqlDataReader reader = await cmd.ExecuteReaderAsync(behavior, token)
                .ConfigureAwait(false);

            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                return default;

            ISqlMapper<TResult> mapper = _mapperFactory.GetMapper<TResult>();
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
        DbRequest<TParams> req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        return ExecutePipelineAsync(req, options, async (cmd, token) =>
        {
            object? val = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);

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
        DbRequest<TParams> req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        return ExecutePipelineAsync(req, options, async (cmd, token) =>
        {
            int affected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);

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
        EnsureRawSqlAllowed(commandText, commandType);

        // [Dry-Run] 설계 – EmptyGridReader 반환 정책 유지
        if (_options.EnableDryRun)
        {
            LogDryRunStream(_logger, GetDiagnosticCommandText(commandText, commandType));
            return new EmptyGridReader();
        }

        DbRequest<TParams> req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        System.Data.Common.DbDataReader? rawReader;
        long startTicks = Stopwatch.GetTimestamp();

        using (Activity? activity = LibDbTelemetry.ActivitySource.StartActivity("DB QueryMultiple"))
        {
            string diagnosticCommandText = GetDiagnosticCommandText(commandText, commandType);
            activity?.SetTag("db.system", "mssql");
            activity?.SetTag("db.operation", commandType.ToString());
            activity?.SetTag("db.statement", diagnosticCommandText);
            activity?.SetTag("db.instance", DbDiagnosticRedactor.RedactInstanceId(instanceHash));

            try
            {
                rawReader = await _strategy.ExecuteStreamAsync(
                    req,
                    async (conn, token) =>
                    {
                        SqlCommand cmd = new SqlCommand(commandText, conn)
                        {
                            CommandType = commandType,
                            CommandTimeout = options.CommandTimeout ?? _options.DefaultCommandTimeoutSeconds
                        };

                        _strategy.EnlistTransaction(cmd);

                        // [MARS Validation] QueryMultipleAsync 사용 시 MARS 설정 필수 검증
                        // (성능 영향을 줄이기 위해 최초 1회만 파싱 후 캐싱)
                        ValidateMarsEnabled(conn);

                        await PrepareParametersAsync(cmd, parameters, instanceHash, options, token)
                            .ConfigureAwait(false);

                        DbCommandInterceptionContext ctx = new DbCommandInterceptionContext(instanceHash, token);
                        await _interceptorChain.OnExecutingAsync(cmd, ctx).ConfigureAwait(false);

                        if (ctx.SuppressExecution)
                        {
                            LogMockingExecution(_logger, diagnosticCommandText);
                            return (ctx.MockResult as SqlDataReader)!;
                        }

                        CommandBehavior behavior = CommandBehavior.Default;
                        if (!_strategy.IsTransactional)
                            behavior |= CommandBehavior.CloseConnection;

                        return await cmd.ExecuteReaderAsync(behavior, token).ConfigureAwait(false);
                    },
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ActivityErrorDescription);
                _logger.LogWarning(ex,
                    "다중 결과 쿼리 실행 중 오류가 발생했습니다. (Command: {CommandText})",
                    diagnosticCommandText);
                throw LibDbExceptionFactory.CreateCommandExecutionFailed(ex);
            }
        }

        #region 다중 결과 쿼리 - 리더 획득 시간 메트릭

        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTicks);

        DbRequestInfo info = new DbRequestInfo(
            InstanceId: DbDiagnosticRedactor.RedactInstanceId(instanceHash),
            DbSystem: "mssql",
            Operation: commandType.ToString(),
            CommandKind: commandType.ToString());

        DbMetrics.TrackDuration(elapsed, info);

        #endregion

        if (rawReader is null)
        {
            LibDbExceptionFactory.ThrowInvalidOperation(
                "QueryMultipleAsync 실행 결과가 null입니다. " +
                "인터셉터에서 SuppressExecution 되었는지 확인해 주세요.");
        }

        SqlGridReader gridReader = new SqlGridReader(rawReader, _mapperFactory);

        // Resilient 경로에서는 MonitoredSqlDataReader가 연결 수명/메트릭을 관리하고,
        // Transactional 경로에서는 외부 트랜잭션이 연결 수명을 관리합니다.
        // 따라서 여기에서 별도의 연결 종료를 강제하지 않습니다.

        return gridReader;
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
    /// - 기타 예외는 LibDbExceptionFactory를 통해 원문 SQL을 제외한 예외로 래핑합니다.<br/>
    /// - Activity는 예외 발생 시 Error 상태로 설정되어 분산 추적 시스템에 전달됩니다.
    /// </para>
    /// </summary>
    private async Task<TResult> ExecutePipelineAsync<TParams, TResult>(
        DbRequest<TParams> request,
        DbExecutionOptions execOptions,
        Func<SqlCommand, CancellationToken, Task<TResult>> operation)
    {
        EnsureRawSqlAllowed(request.CommandText, request.CommandType);

        // [Dry-Run] Text 기반 쓰기 명령은 실제 실행을 건너뜁니다.
        if (_options.EnableDryRun && IsWriteOperation(request.CommandText, request.CommandType))
        {
            LogDryRunExecution(_logger, GetDiagnosticCommandText(request.CommandText, request.CommandType));
            return default!;
        }

        string diagnosticCommandText = GetDiagnosticCommandText(request.CommandText, request.CommandType);

        // [사용자 인터셉터] Executing 단계 — 인터셉터가 0개면 건너뜀
        DbInterceptionContext? userCtx = null;
        if (_userInterceptors.Length > 0)
        {
            userCtx = new DbInterceptionContext
            {
                CommandText = request.CommandText,
                DiagnosticCommandText = diagnosticCommandText,
                CommandType = request.CommandType,
                InstanceName = DbDiagnosticRedactor.RedactInstanceId(request.InstanceHash) ?? request.InstanceHash
            };

            foreach (IDbInterceptor interceptor in _userInterceptors)
            {
                try
                {
                    DbInterceptionResult interceptResult = await interceptor
                        .OnExecutingAsync(userCtx, request.CancellationToken)
                        .ConfigureAwait(false);

                    if (interceptResult == DbInterceptionResult.Suppress)
                    {
                        LogMockingExecution(_logger, diagnosticCommandText);
                        return default!;
                    }
                }
                catch (Exception interceptEx)
                {
                    // 인터셉터 예외는 로깅 후 무시 (실행 파이프라인 차단하지 않음)
                    _logger.LogWarning(interceptEx,
                        "[UserInterceptor] OnExecutingAsync 실행 중 오류가 발생했습니다. (Command: {CommandText})",
                        diagnosticCommandText);
                }
            }
        }

        // [Optimization] Activity 이름 할당 제거
        string activityName = request.CommandType switch
        {
            CommandType.Text => ActivityNameQuery,
            CommandType.StoredProcedure => ActivityNameProcedure,
            _ => ActivityNameCommand
        };

        using Activity? activity = LibDbTelemetry.ActivitySource.StartActivity(activityName);
        activity?.SetTag("db.system", "mssql");
        activity?.SetTag("db.operation", request.CommandType.ToString());
        activity?.SetTag("db.statement", diagnosticCommandText);
        activity?.SetTag("db.instance", DbDiagnosticRedactor.RedactInstanceId(request.InstanceHash));

        long pipelineStartTicks = Stopwatch.GetTimestamp();

        try
        {
            TResult result = await _strategy.ExecuteAsync(request, async (conn, token) =>
            {
                await using SqlCommand cmd = new SqlCommand(request.CommandText, conn)
                {
                    CommandType = request.CommandType,
                    CommandTimeout = execOptions.CommandTimeout ?? _options.DefaultCommandTimeoutSeconds
                };

                _strategy.EnlistTransaction(cmd);

                // 스키마 조회 + 파라미터 매핑
                await PrepareParametersAsync(cmd, request.Parameters, request.InstanceHash, execOptions, token)
                    .ConfigureAwait(false);

                // Interceptor: Executing 단계 (내부 인터셉터)
                DbCommandInterceptionContext ctx = new DbCommandInterceptionContext(request.InstanceHash, token);
                await _interceptorChain.OnExecutingAsync(cmd, ctx).ConfigureAwait(false);

                if (ctx.SuppressExecution)
                {
                    LogMockingExecution(_logger, diagnosticCommandText);

                    if (ctx.MockResult is TResult casted)
                        return casted;

                    return default!;
                }

                // 내부 try/catch 제거 – 실제 실패 시 상위 catch 한 곳에서만
                // Activity 상태 설정 + 로깅을 수행합니다.
                long startTicks = Stopwatch.GetTimestamp();
                TResult? innerResult = await operation(cmd, token).ConfigureAwait(false);

                long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTicks);

                DbRequestInfo info = new DbRequestInfo(
                    InstanceId: DbDiagnosticRedactor.RedactInstanceId(request.InstanceHash),
                    DbSystem: "mssql",
                    Operation: request.CommandType.ToString(),
                    CommandKind: request.CommandType.ToString()
                );

                // 전역 메트릭 – 단일 명령 실행 시간 기록
                DbMetrics.TrackDuration(elapsed, info);

                DbCommandExecutedEventData executedEvent = new DbCommandExecutedEventData(
                    DurationUs: TicksToMicroseconds(elapsedTicks),
                    Result: innerResult
                );

                await _interceptorChain.OnExecutedAsync(cmd, executedEvent).ConfigureAwait(false);

                return innerResult;
            }, request.CancellationToken).ConfigureAwait(false);

            // [사용자 인터셉터] Executed 단계
            if (_userInterceptors.Length > 0 && userCtx is not null)
            {
                long pipelineElapsedMs = (long)Stopwatch.GetElapsedTime(pipelineStartTicks).TotalMilliseconds;
                userCtx.ElapsedMs = pipelineElapsedMs;
                userCtx.Result = result;

                foreach (IDbInterceptor interceptor in _userInterceptors)
                {
                    try
                    {
                        await interceptor.OnExecutedAsync(userCtx, request.CancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception interceptEx)
                    {
                        _logger.LogWarning(interceptEx,
                            "[UserInterceptor] OnExecutedAsync 실행 중 오류가 발생했습니다. (Command: {CommandText})",
                            diagnosticCommandText);
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ActivityErrorDescription);

            // [사용자 인터셉터] Error 단계
            if (_userInterceptors.Length > 0 && userCtx is not null)
            {
                long pipelineElapsedMs = (long)Stopwatch.GetElapsedTime(pipelineStartTicks).TotalMilliseconds;
                userCtx.ElapsedMs = pipelineElapsedMs;
                userCtx.Exception = ex;

                foreach (IDbInterceptor interceptor in _userInterceptors)
                {
                    try
                    {
                        await interceptor.OnErrorAsync(userCtx, request.CancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception interceptEx)
                    {
                        _logger.LogWarning(interceptEx,
                            "[UserInterceptor] OnErrorAsync 실행 중 오류가 발생했습니다. (Command: {CommandText})",
                            diagnosticCommandText);
                    }
                }
            }

            _logger.LogWarning(ex,
                "[Executor] DB 파이프라인 실행 중 오류가 발생했습니다. " +
                "(Command: {CommandText}, Instance: {InstanceId}, CommandType: {CommandType})",
                diagnosticCommandText,
                DbDiagnosticRedactor.RedactInstanceId(request.InstanceHash),
                request.CommandType);

            // [Modify] SqlException은 포장하지 않고 그대로 전파해야 Polly/Test가 정상 동작함
            if (ex is SqlException)
                throw;

            throw LibDbExceptionFactory.CreateCommandExecutionFailed(ex);
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
        using var scope = DbExecutionContextScope.Enter(
            instanceHash,
            cmd.CommandText,
            cmd.CommandType,
            _strategy.IsTransactional);

        SpSchema? schema = null;

        if (cmd.CommandType == CommandType.StoredProcedure)
        {
            // 전략 기본 모드 vs. 명령 단위 오버라이드
            SchemaResolutionMode mode = execOptions.SchemaModeOverride ?? _strategy.DefaultSchemaMode;

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
                        "[Schema] SP 스키마 조회 실패. 스키마 없이 파라미터 매핑을 진행합니다. (Command: {CommandText})",
                        GetDiagnosticCommandText(cmd.CommandText, cmd.CommandType));
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
        EnsureRawSqlAllowed(commandText, commandType);

        // [Dry-Run] Text 기반 쓰기 스트리밍은 실행을 건너뜁니다.
        if (_options.EnableDryRun && IsWriteOperation(commandText, commandType))
        {
            LogDryRunStream(_logger, GetDiagnosticCommandText(commandText, commandType));
            yield break;
        }

        DbRequest<TParams> req = new DbRequest<TParams>(instanceHash, commandText, commandType, parameters, ct, _strategy.IsTransactional);

        System.Data.Common.DbDataReader? reader;
        long startTicks = Stopwatch.GetTimestamp();

        using (Activity? activity = LibDbTelemetry.ActivitySource.StartActivity("DB QueryStream"))
        {
            string diagnosticCommandText = GetDiagnosticCommandText(commandText, commandType);
            activity?.SetTag("db.system", "mssql");
            activity?.SetTag("db.operation", commandType.ToString());
            activity?.SetTag("db.statement", diagnosticCommandText);
            activity?.SetTag("db.instance", DbDiagnosticRedactor.RedactInstanceId(instanceHash));

            try
            {
                reader = await _strategy.ExecuteStreamAsync(
                    req,
                    async (conn, token) =>
                    {
                        SqlCommand cmd = new SqlCommand(commandText, conn)
                        {
                            CommandType = commandType,
                            CommandTimeout = options.CommandTimeout ?? _options.DefaultCommandTimeoutSeconds
                        };

                        _strategy.EnlistTransaction(cmd);

                        await PrepareParametersAsync(cmd, parameters, instanceHash, options, token)
                            .ConfigureAwait(false);

                        DbCommandInterceptionContext ctx = new DbCommandInterceptionContext(instanceHash, token);
                        await _interceptorChain.OnExecutingAsync(cmd, ctx).ConfigureAwait(false);

                        if (ctx.SuppressExecution)
                        {
                            LogMockingExecution(_logger, diagnosticCommandText);
                            return default(SqlDataReader)!;
                        }

                        CommandBehavior behavior = CommandBehavior.Default;
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
                activity?.SetStatus(ActivityStatusCode.Error, ActivityErrorDescription);
                _logger.LogWarning(ex,
                    "Streaming 쿼리 실행 중 오류가 발생했습니다. (Command: {CommandText})",
                    diagnosticCommandText);
                throw LibDbExceptionFactory.CreateCommandExecutionFailed(ex);
            }
        }

        #region Streaming 쿼리 - 리더 획득 시간 메트릭

        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTicks);

        DbRequestInfo info = new DbRequestInfo(
            InstanceId: DbDiagnosticRedactor.RedactInstanceId(instanceHash),
            DbSystem: "mssql",
            Operation: commandType.ToString(),
            CommandKind: commandType.ToString());

        DbMetrics.TrackDuration(elapsed, info);

        #endregion

        if (reader is null)
            yield break;

        ISqlMapper<TResult> mapper = _mapperFactory.GetMapper<TResult>();

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
        CommandBehavior behavior = CommandBehavior.SingleRow;

        if (!isTransactional)
            behavior |= CommandBehavior.CloseConnection;

        return behavior;
    }

    /// <summary>
    /// 텍스트 기반 쓰기 명령(INSERT/UPDATE/DELETE/MERGE) 여부를 판별합니다.
    /// </summary>
    private static bool IsWriteOperation(string cmdText, CommandType commandType)
    {
        if (commandType != CommandType.Text)
            return false;

        string token = GetFirstSqlToken(cmdText);
        return token.Equals("INSERT", StringComparison.OrdinalIgnoreCase)
            || token.Equals("UPDATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("MERGE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("TRUNCATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ALTER", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DROP", StringComparison.OrdinalIgnoreCase)
            || token.Equals("EXEC", StringComparison.OrdinalIgnoreCase)
            || token.Equals("EXECUTE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("GRANT", StringComparison.OrdinalIgnoreCase)
            || token.Equals("REVOKE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DENY", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureRawSqlAllowed(string commandText, CommandType commandType)
    {
        if (commandType != CommandType.Text || _options.RawSqlPolicy == RawSqlPolicy.Allow)
            return;

        if (_options.RawSqlPolicy == RawSqlPolicy.DenyAllText)
        {
            throw new InvalidOperationException(
                "Raw SQL text execution is disabled by LibDbOptions.RawSqlPolicy. " +
                "Use Procedure(...) for stored procedures or change RawSqlPolicy explicitly.");
        }

        if (_options.RawSqlPolicy == RawSqlPolicy.DenyWriteText &&
            IsWriteOperation(commandText, commandType))
        {
            throw new InvalidOperationException(
                "Mutating Raw SQL text execution is disabled by LibDbOptions.RawSqlPolicy. " +
                "Use stored procedures or parameterized read-only SQL.");
        }
    }

    private static string GetFirstSqlToken(string sql)
    {
        ReadOnlySpan<char> span = sql.AsSpan().TrimStart();

        while (!span.IsEmpty)
        {
            if (span[0] == ';')
            {
                span = span[1..].TrimStart();
                continue;
            }

            if (span.StartsWith("--", StringComparison.Ordinal))
            {
                int lineBreak = span.IndexOfAny('\r', '\n');
                if (lineBreak < 0)
                    return string.Empty;

                span = span[(lineBreak + 1)..].TrimStart();
                continue;
            }

            if (span.StartsWith("/*", StringComparison.Ordinal))
            {
                int commentLength = GetBlockCommentLength(span);
                if (commentLength < 0)
                    return string.Empty;

                span = span[commentLength..].TrimStart();
                continue;
            }

            break;
        }

        int tokenLength = 0;
        while (tokenLength < span.Length && char.IsLetter(span[tokenLength]))
        {
            tokenLength++;
        }

        return tokenLength == 0 ? string.Empty : span[..tokenLength].ToString();
    }

    private static int GetBlockCommentLength(ReadOnlySpan<char> span)
    {
        int depth = 1;
        int position = 2;

        while (position < span.Length - 1)
        {
            if (span[position] == '/' && span[position + 1] == '*')
            {
                depth++;
                position += 2;
                continue;
            }

            if (span[position] == '*' && span[position + 1] == '/')
            {
                depth--;
                position += 2;
                if (depth == 0)
                    return position;

                continue;
            }

            position++;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long TicksToMicroseconds(long ticks)
        => ticks * 1_000_000L / Stopwatch.Frequency;

    #endregion

    #region 유틸리티 - 문자열 처리 및 로깅

    private string GetDiagnosticCommandText(string commandText, CommandType commandType)
        => _options.IncludeParametersInTrace
            ? commandText
            : GetDiagnosticCommandKind(commandType);

    private static string GetDiagnosticCommandKind(CommandType commandType)
        => commandType switch
        {
            CommandType.Text => "Text",
            CommandType.StoredProcedure => "StoredProcedure",
            CommandType.TableDirect => "TableDirect",
            _ => RedactedCommandText
        };

    /// <summary>
    /// MarsPolicy에 따라 MARS 활성화 여부를 검증합니다.
    /// <para>
    /// <b>[정책 분기]</b><br/>
    /// - <c>ForceEnable</c>: 등록 시점에 이미 MARS가 주입되었으므로 검증 건너뜀.<br/>
    /// - <c>Disabled</c>: QueryMultipleAsync 사용 자체를 금지하며 즉시 예외를 발생시킵니다.<br/>
    /// - <c>Auto</c>: 연결 문자열을 파싱하여 MARS 설정 여부를 확인합니다 (기존 동작).
    /// </para>
    /// </summary>
    private void ValidateMarsEnabled(System.Data.Common.DbConnection conn)
    {
        // [ForceEnable] 등록 시 PostConfigure에서 이미 MARS를 주입했으므로 검증 불필요
        if (_options.Mars == Lib.Db.Configuration.MarsPolicy.ForceEnable)
            return;

        // [Disabled] 정책적으로 MARS 사용 금지 — 즉시 예외
        if (_options.Mars == Lib.Db.Configuration.MarsPolicy.Disabled)
        {
            throw new InvalidOperationException(
                "MarsPolicy가 Disabled로 설정되어 QueryMultipleAsync를 사용할 수 없습니다. " +
                "LibDbOptions.Mars를 Auto 또는 ForceEnable로 변경하세요.");
        }

        // [Auto] 연결 문자열 파싱으로 MARS 활성화 여부 확인 (캐시 적용, 연결당 1회)
        string connStr = conn.ConnectionString;
        if (s_marsEnabledCache.TryGetValue(connStr, out bool enabled))
        {
            if (!enabled)
                ThrowMarsRequired();
            return;
        }

        // 파싱 및 검증 (Slow Path - 연결당 1회)
        Microsoft.Data.SqlClient.SqlConnectionStringBuilder builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
        bool isEnabled = builder.MultipleActiveResultSets;

        // 연결 문자열은 앱당 1~5개 수준이므로 크기 제한 없이 단순 추가
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

    #endregion
    #endregion

}
