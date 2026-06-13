// ============================================================================
// 파일: Lib.Db/Diagnostics/DbDiagnostics.cs
// 설명: 무할당(Zero-Allocation) 분산 추적 및 고성능 로깅 + 메트릭 허브
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;


namespace Lib.Db.Diagnostics;

#region 요청 컨텍스트 (DbRequestInfo)

/// <summary>
/// OpenTelemetry 표준 태그와 라이브러리 전용 커스텀 추적 정보를 담는 경량 구조체입니다.
/// <para>
/// <see langword="readonly"/> <see langword="record struct"/> 로 선언되어 스택 메모리에 할당되므로
/// GC 부하가 사실상 0에 가깝습니다.
/// </para>
/// </summary>
public readonly record struct DbRequestInfo(
    string? InstanceId = null,    // 사용자 정의: 논리 인스턴스 해시 ID
    string? DbSystem = "mssql",   // OTel: db.system
    string? DbName = null,        // OTel: db.name
    string? DbUser = null,        // OTel: db.user
    string? ServerAddress = null, // OTel: server.address
    int? ServerPort = null,       // OTel: server.port
    string? Operation = null,     // OTel: db.operation (예: "SELECT", "EXEC", "BULK", "TVP")
    string? Target = null,        // OTel: db.sql.table / SP 이름 / TVP 이름 등
    string? CommandKind = null,   // Custom: StoredProcedure / Text / TableDirect 등
    bool IsTransactional = false, // Custom: 트랜잭션 여부
    string? CorrelationId = null  // Custom: 상위 호출과 연계하기 위한 Correlation Id
)
{
    /// <summary>
    /// 주어진 <see cref="DbExecutionContext"/> 를 기반으로 <see cref="DbRequestInfo"/> 를 생성합니다.
    /// </summary>
    /// <param name="context">현재 실행 중인 DB 컨텍스트 (없으면 null 허용)</param>
    /// <param name="operation">
    /// OTel db.operation (명령 유형, 예: "EXEC", "TVP").
    /// null 이면 <see cref="DbExecutionContext.CommandType"/> 로부터 유추합니다.
    /// </param>
    /// <param name="target">
    /// 대상 테이블/뷰/TVP/프로시저 이름 등.
    /// null 이면 SQL 원문 노출 방지를 위해 대상 태그를 생략합니다.
    /// </param>
    internal static DbRequestInfo FromExecutionContext(
        DbExecutionContext? context,
        string? operation = null,
        string? target = null)
    {
        if (context is null)
        {
            // ExecutionContext 없이 최소 정보만 담는 케이스
            return new DbRequestInfo(
                Operation: operation,
                Target: target
            );
        }

        DbExecutionContext value = context.Value;

        // 1) Operation: 명시값 우선, 없으면 CommandType 기준으로 유추
        string op = !string.IsNullOrWhiteSpace(operation)
            ? operation
            : value.CommandType switch
            {
                CommandType.StoredProcedure => "EXEC",
                CommandType.Text => "TEXT",
                CommandType.TableDirect => "TABLE_DIRECT",
                _ => value.CommandType.ToString()
            };

        // 2) Target: 명시값 없으면 SQL 원문 노출 방지를 위해 생략
        string? tgt = !string.IsNullOrWhiteSpace(target)
            ? target
            : null;

        // 3) CommandKind: CommandType 자체를 의미 있게 구분
        string cmdKind = value.CommandType switch
        {
            CommandType.StoredProcedure => "StoredProcedure",
            CommandType.Text => "Text",
            CommandType.TableDirect => "TableDirect",
            _ => value.CommandType.ToString()
        };

        return new DbRequestInfo(
            InstanceId: DbDiagnosticRedactor.RedactInstanceId(value.InstanceName),
            DbSystem: "mssql",
            DbName: null, // DbExecutionContext에 DatabaseName을 추가하면 여기에서 채웁니다.
            DbUser: null,
            ServerAddress: null,
            ServerPort: null,
            Operation: op,
            Target: tgt,
            CommandKind: cmdKind,
            IsTransactional: value.IsTransactional,
            CorrelationId: value.CorrelationId
        );
    }

    /// <summary>
    /// <see cref="DbExecutionContextScope.Current"/> 를 기반으로 <see cref="DbRequestInfo"/> 를 생성합니다.
    /// <para>
    /// 호출 시점의 스코프에 ExecutionContext가 없으면 최소 정보만을 포함한
    /// <see cref="DbRequestInfo"/> 를 반환합니다.
    /// </para>
    /// </summary>
    /// <param name="operation">OTel db.operation (명령 유형, 예: "EXEC", "TVP")</param>
    /// <param name="target">대상 테이블/뷰/TVP/프로시저 이름 등</param>
    public static DbRequestInfo FromCurrentScope(string? operation = null, string? target = null)
        => FromExecutionContext(DbExecutionContextScope.Current, operation, target);
}

#endregion

#region 고성능 로거 (FastLogger)

/// <summary>
/// 로그 레벨이 활성화된 경우에만 문자열 보간을 평가하는 제로 할당 고성능 로거 확장입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// 표준 <c>ILogger</c>는 로그 레벨 비활성 시에도 보간 문자열을 평가하여 불필요한 할당이 발생합니다.<br/>
/// <see cref="FastLogger"/>는 C# 10+ <c>[InterpolatedStringHandler]</c>를 활용하여
/// 로그 레벨 확인 후에만 문자열을 구성하므로 핫 패스에서 GC 압력을 제거합니다.
/// </para>
/// </summary>
public static class FastLogger
{
    /// <summary>
    /// <see cref="LogLevel.Debug"/> 가 활성화된 경우에만 보간 문자열을 평가하여 디버그 로그를 기록합니다.
    /// </summary>
    /// <param name="logger">로그를 기록할 <see cref="ILogger"/> 인스턴스입니다.</param>
    /// <param name="handler">컴파일러가 생성하는 보간 문자열 핸들러입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogFastDebug(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] ref FastDebugLogHandler handler)
    {
        if (handler.IsEnabled)
            logger.LogDebug(handler.GetFormattedText());
    }

    /// <summary>
    /// <see cref="LogLevel.Information"/> 이 활성화된 경우에만 보간 문자열을 평가하여 정보 로그를 기록합니다.
    /// </summary>
    /// <param name="logger">로그를 기록할 <see cref="ILogger"/> 인스턴스입니다.</param>
    /// <param name="handler">컴파일러가 생성하는 보간 문자열 핸들러입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogFastInfo(this ILogger logger, [InterpolatedStringHandlerArgument("logger")] ref FastInfoLogHandler handler)
    {
        if (handler.IsEnabled)
            logger.LogInformation(handler.GetFormattedText());
    }

    /// <summary>
    /// <see cref="LogLevel.Warning"/> 이 활성화된 경우에만 보간 문자열을 평가하여 경고 로그를 기록합니다.
    /// </summary>
    /// <param name="logger">로그를 기록할 <see cref="ILogger"/> 인스턴스입니다.</param>
    /// <param name="ex">오류 타입만 구조화 필드로 남길 예외입니다.</param>
    /// <param name="handler">컴파일러가 생성하는 보간 문자열 핸들러입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogFastWarn(this ILogger logger, Exception? ex, [InterpolatedStringHandlerArgument("logger", "ex")] ref FastWarnLogHandler handler)
    {
        if (handler.IsEnabled)
        {
            string message = handler.GetFormattedText();
            if (ex is null)
            {
                logger.LogWarning("{Message}", message);
            }
            else
            {
                logger.LogWarning("{Message} (ErrorType: {ErrorType})", message, ex.GetType().Name);
            }
        }
    }
}

/// <summary>
/// <see cref="LogLevel.Debug"/> 레벨 활성 시에만 보간 문자열을 평가하는 핸들러입니다.
/// </summary>
[InterpolatedStringHandler]
public ref struct FastDebugLogHandler
{
    private DefaultInterpolatedStringHandler _inner;

    /// <summary>
    /// 디버그 로그 레벨이 활성화되어 보간 문자열을 구성해야 하는지 나타냅니다.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// <see cref="LogLevel.Debug"/> 활성 상태에 따라 보간 문자열 핸들러를 초기화합니다.
    /// </summary>
    /// <param name="literalLength">보간 문자열의 리터럴 문자 길이입니다.</param>
    /// <param name="formattedCount">보간 문자열에 포함된 형식 지정 값 개수입니다.</param>
    /// <param name="logger">로그 레벨 확인에 사용할 로거입니다.</param>
    /// <param name="isEnabled">디버그 로그 활성 여부입니다.</param>
    public FastDebugLogHandler(int literalLength, int formattedCount, ILogger logger, out bool isEnabled)
    {
        isEnabled = logger.IsEnabled(LogLevel.Debug);
        IsEnabled = isEnabled;
        _inner = isEnabled ? new(literalLength, formattedCount) : default;
    }

    /// <summary>
    /// 로그 레벨이 활성화된 경우 리터럴 문자열을 버퍼에 추가합니다.
    /// </summary>
    /// <param name="s">추가할 리터럴 문자열입니다.</param>
    public void AppendLiteral(string s)
    {
        if (IsEnabled)
            _inner.AppendLiteral(s);
    }

    /// <summary>
    /// 로그 레벨이 활성화된 경우 형식 지정 값을 버퍼에 추가합니다.
    /// </summary>
    /// <typeparam name="T">형식 지정 값의 타입입니다.</typeparam>
    /// <param name="value">추가할 값입니다.</param>
    public void AppendFormatted<T>(T value)
    {
        if (IsEnabled)
            _inner.AppendFormatted(value);
    }

    /// <summary>
    /// 구성된 로그 메시지를 반환하고 내부 버퍼를 비웁니다.
    /// </summary>
    /// <returns>로그 레벨이 활성화된 경우 구성된 메시지, 그렇지 않으면 빈 문자열입니다.</returns>
    public string GetFormattedText()
        => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
}

/// <summary>
/// <see cref="LogLevel.Information"/> 레벨 활성 시에만 보간 문자열을 평가하는 핸들러입니다.
/// </summary>
[InterpolatedStringHandler]
public ref struct FastInfoLogHandler
{
    private DefaultInterpolatedStringHandler _inner;

    /// <summary>
    /// 정보 로그 레벨이 활성화되어 보간 문자열을 구성해야 하는지 나타냅니다.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// <see cref="LogLevel.Information"/> 활성 상태에 따라 보간 문자열 핸들러를 초기화합니다.
    /// </summary>
    /// <param name="literalLength">보간 문자열의 리터럴 문자 길이입니다.</param>
    /// <param name="formattedCount">보간 문자열에 포함된 형식 지정 값 개수입니다.</param>
    /// <param name="logger">로그 레벨 확인에 사용할 로거입니다.</param>
    /// <param name="isEnabled">정보 로그 활성 여부입니다.</param>
    public FastInfoLogHandler(int literalLength, int formattedCount, ILogger logger, out bool isEnabled)
    {
        isEnabled = logger.IsEnabled(LogLevel.Information);
        IsEnabled = isEnabled;
        _inner = isEnabled ? new(literalLength, formattedCount) : default;
    }

    /// <summary>
    /// 로그 레벨이 활성화된 경우 리터럴 문자열을 버퍼에 추가합니다.
    /// </summary>
    /// <param name="s">추가할 리터럴 문자열입니다.</param>
    public void AppendLiteral(string s)
    {
        if (IsEnabled)
            _inner.AppendLiteral(s);
    }

    /// <summary>
    /// 로그 레벨이 활성화된 경우 형식 지정 값을 버퍼에 추가합니다.
    /// </summary>
    /// <typeparam name="T">형식 지정 값의 타입입니다.</typeparam>
    /// <param name="value">추가할 값입니다.</param>
    public void AppendFormatted<T>(T value)
    {
        if (IsEnabled)
            _inner.AppendFormatted(value);
    }

    /// <summary>
    /// 구성된 로그 메시지를 반환하고 내부 버퍼를 비웁니다.
    /// </summary>
    /// <returns>로그 레벨이 활성화된 경우 구성된 메시지, 그렇지 않으면 빈 문자열입니다.</returns>
    public string GetFormattedText()
        => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
}

/// <summary>
/// <see cref="LogLevel.Warning"/> 레벨 활성 시에만 보간 문자열을 평가하는 핸들러입니다.
/// </summary>
[InterpolatedStringHandler]
public ref struct FastWarnLogHandler
{
    private DefaultInterpolatedStringHandler _inner;

    /// <summary>
    /// 경고 로그 레벨이 활성화되어 보간 문자열을 구성해야 하는지 나타냅니다.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// <see cref="LogLevel.Warning"/> 활성 상태에 따라 보간 문자열 핸들러를 초기화합니다.
    /// </summary>
    /// <param name="literalLength">보간 문자열의 리터럴 문자 길이입니다.</param>
    /// <param name="formattedCount">보간 문자열에 포함된 형식 지정 값 개수입니다.</param>
    /// <param name="logger">로그 레벨 확인에 사용할 로거입니다.</param>
    /// <param name="ex">오류 타입 필드 계산에만 사용하는 예외입니다. 레벨 확인에는 사용하지 않습니다.</param>
    /// <param name="isEnabled">경고 로그 활성 여부입니다.</param>
    public FastWarnLogHandler(int literalLength, int formattedCount, ILogger logger, Exception? ex, out bool isEnabled)
    {
        _ = ex;
        isEnabled = logger.IsEnabled(LogLevel.Warning);
        IsEnabled = isEnabled;
        _inner = isEnabled ? new(literalLength, formattedCount) : default;
    }

    /// <summary>
    /// 로그 레벨이 활성화된 경우 리터럴 문자열을 버퍼에 추가합니다.
    /// </summary>
    /// <param name="s">추가할 리터럴 문자열입니다.</param>
    public void AppendLiteral(string s)
    {
        if (IsEnabled)
            _inner.AppendLiteral(s);
    }

    /// <summary>
    /// 로그 레벨이 활성화된 경우 형식 지정 값을 버퍼에 추가합니다.
    /// </summary>
    /// <typeparam name="T">형식 지정 값의 타입입니다.</typeparam>
    /// <param name="value">추가할 값입니다.</param>
    public void AppendFormatted<T>(T value)
    {
        if (IsEnabled)
            _inner.AppendFormatted(value);
    }

    /// <summary>
    /// 구성된 로그 메시지를 반환하고 내부 버퍼를 비웁니다.
    /// </summary>
    /// <returns>로그 레벨이 활성화된 경우 구성된 메시지, 그렇지 않으면 빈 문자열입니다.</returns>
    public string GetFormattedText()
        => IsEnabled ? _inner.ToStringAndClear() : string.Empty;
}

#endregion

#region 통합 OTel 메트릭 허브 (DbMetrics)

/// <summary>
/// 라이브러리 전체의 성능 및 상태 지표를 수집하는 중앙 메트릭 허브입니다.
/// <para>
/// <see cref="TagList"/> (struct)와 정적 <see cref="Meter"/> 를 사용하여 힙 할당을 최소화합니다.
/// </para>
/// </summary>
public static class DbMetrics
{
    // =========================================================================
    // 2.1. 상수 및 미터 정의
    // =========================================================================


    // OTel 표준 태그 키
    private const string AttrDbSystem = "db.system";
    private const string AttrDbName = "db.name";
    private const string AttrDbUser = "db.user";
    private const string AttrServerAddr = "server.address";
    private const string AttrServerPort = "server.port";
    private const string AttrDbOperation = "db.operation";
    private const string AttrDbTable = "db.sql.table";

    // Custom Attributes
    private const string AttrInstanceId = "libdb.instance.id";
    private const string AttrRetryReason = "libdb.retry.reason";
    private const string AttrSchemaKind = "libdb.schema.kind";
    private const string AttrCacheHit = "libdb.cache.hit";
    private const string AttrTvpName = "libdb.tvp.name";
    private const string AttrCommandKind = "libdb.command.kind";
    private const string AttrTransactional = "libdb.transactional";
    private const string AttrCorrelationId = "libdb.correlation.id";

    // =========================================================================
    // 2.2. 메트릭 기기 정의 (Instruments)
    // =========================================================================

    // 1. Connection
    private static readonly UpDownCounter<int> s_connActive =
        LibDbTelemetry.Meter.CreateUpDownCounter<int>("db.client.connections.usage", "{connections}", "현재 활성 DB 연결 수");

    // 2. Query & Resilience
    private static readonly Histogram<double> s_queryDuration =
        LibDbTelemetry.Meter.CreateHistogram<double>("db.client.operation.duration", "ms", "DB 작업 수행 시간");

    private static readonly Counter<int> s_retries =
        LibDbTelemetry.Meter.CreateCounter<int>("db.client.resilience.retries", "{retries}", "재시도 발생 횟수");

    // 3. Schema & Cache
    private static readonly Counter<int> s_schemaRefresh =
        LibDbTelemetry.Meter.CreateCounter<int>("libdb.schema.refresh", "{ops}", "스키마 갱신 시도 횟수");

    private static readonly Counter<int> s_cacheHits =
        LibDbTelemetry.Meter.CreateCounter<int>("libdb.schema.cache.hits", "{hits}", "스키마 캐시 적중 횟수");

    private static readonly Counter<int> s_cacheMisses =
        LibDbTelemetry.Meter.CreateCounter<int>("libdb.schema.cache.misses", "{misses}", "스키마 캐시 미스 횟수");

    private static readonly Counter<long> s_cacheBytesFreed =
        LibDbTelemetry.Meter.CreateCounter<long>("libdb.cache.bytes_freed", "By", "캐시 정리로 반환된 바이트 수");

    // 4. Bulk & TVP
    private static readonly Counter<long> s_bulkRows =
        LibDbTelemetry.Meter.CreateCounter<long>("libdb.bulk.rows", "{rows}", "벌크 삽입된 총 행 수");

    private static readonly Counter<long> s_tvpBytes =
        LibDbTelemetry.Meter.CreateCounter<long>("db.client.tvp.bytes", "By", "TVP 전송 바이트 수");

    // =========================================================================
    // 2.3. 메트릭 전역 활성 플래그 및 Reset 지원
    // =========================================================================

    /// <summary>
    /// 메트릭 수집 전역 활성/비활성 상태를 나타내는 플래그입니다.
    /// <para>
    /// <c>false</c> 로 설정되면 모든 Track* 메서드는 즉시 반환하며,
    /// <see cref="Meter"/> 자체는 그대로 유지됩니다.
    /// </para>
    /// </summary>
    private static volatile bool s_enabled;

    /// <summary>
    /// 메트릭 수집 전역 활성/비활성 상태입니다.
    /// <para>
    /// - 기본값: <c>false</c><br/>
    /// - <c>false</c> 로 설정하면 모든 Track* 호출이 무시됩니다.
    /// </para>
    /// </summary>
    public static bool IsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => s_enabled;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => s_enabled = value;
    }

    /// <summary>
    /// 테스트 환경에서 메트릭 관련 전역 상태를 초기화합니다.
    /// <para>
    /// 운영 환경에서는 호출하지 않는 것을 권장합니다.
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ResetForTesting()
    {
        // 현재는 On/Off 플래그만 관리하지만,
        // 추후 샘플링/Threshold 등의 전역 설정이 추가되면 이곳에서 함께 초기화합니다.
        s_enabled = false;
    }

    // =========================================================================
    // 2.4. 고성능 태깅 헬퍼 (Tagging Helper)
    // =========================================================================

    /// <summary>
    /// <see cref="DbRequestInfo"/> 의 필드를 OTel 태그 리스트에 매핑합니다.
    /// <para>값이 null이거나 비어있는 필드는 태그에서 자동으로 제외됩니다.</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillTags(ref TagList tags, in DbRequestInfo info)
    {
        // 1. 필수 식별자
        if (info.InstanceId is { Length: > 0 })
            tags.Add(AttrInstanceId, DbDiagnosticRedactor.RedactInstanceId(info.InstanceId));
        if (info.Operation is { Length: > 0 })
            tags.Add(AttrDbOperation, info.Operation);

        // 2. OTel 표준 속성 (Rich Context)
        if (info.DbSystem is { Length: > 0 })
            tags.Add(AttrDbSystem, info.DbSystem);
        if (info.DbName is { Length: > 0 })
            tags.Add(AttrDbName, info.DbName);
        if (info.DbUser is { Length: > 0 })
            tags.Add(AttrDbUser, info.DbUser);
        if (info.ServerAddress is { Length: > 0 })
            tags.Add(AttrServerAddr, info.ServerAddress);
        if (info.ServerPort.HasValue)
            tags.Add(AttrServerPort, info.ServerPort.Value);

        // 3. 커스텀 속성 (CommandKind / Transaction / Correlation)
        if (info.CommandKind is { Length: > 0 })
            tags.Add(AttrCommandKind, info.CommandKind);
        tags.Add(AttrTransactional, info.IsTransactional);
        if (!string.IsNullOrWhiteSpace(info.CorrelationId))
            tags.Add(AttrCorrelationId, info.CorrelationId);

        // 4. 대상 객체 (테이블/뷰/TVP/프로시저 등)
        if (info.Target is { Length: > 0 })
            tags.Add(AttrDbTable, info.Target);
    }

    // =========================================================================
    // 2.5. Scope 기반 간편 API (ExecutionContextScope 사용)
    // =========================================================================

    #region Scope 기반 편의 API - Connection / Duration

    /// <summary>
    /// 현재 실행 스코프를 사용하여 활성 DB 연결 수를 증가시킵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackConnectionOpen()
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope();
        TrackConnectionOpen(in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 활성 DB 연결 수를 증가시킵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackConnectionOpenFromScope()
        => TrackConnectionOpen();

    /// <summary>
    /// 현재 실행 스코프를 사용하여 활성 DB 연결 수를 감소시킵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackConnectionClose()
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope();
        TrackConnectionClose(in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 활성 DB 연결 수를 감소시킵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackConnectionCloseFromScope()
        => TrackConnectionClose();

    /// <summary>
    /// 현재 스코프의 DB 요청 컨텍스트를 사용하여 쿼리 실행 시간을 기록합니다.
    /// </summary>
    /// <param name="elapsed">쿼리 실행 소요 시간</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackDuration(TimeSpan elapsed)
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope();
        TrackDuration(elapsed, in info);
    }

    /// <summary>
    /// 현재 스코프의 DB 요청 컨텍스트를 사용하여 쿼리 실행 시간을 기록합니다.
    /// </summary>
    /// <param name="elapsed">쿼리 실행 소요 시간</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackDurationFromScope(TimeSpan elapsed)
        => TrackDuration(elapsed);

    #endregion

    #region Scope 기반 편의 API - Retry / Bulk / Schema / Cache / TVP

    /// <summary>
    /// 현재 실행 스코프를 사용하여 재시도 발생 횟수를 기록합니다.
    /// </summary>
    /// <param name="reason">재시도 사유 태그 값입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackRetry(string reason)
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope();
        TrackRetry(reason, in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 재시도 발생 횟수를 기록합니다.
    /// </summary>
    /// <param name="reason">재시도 사유 태그 값입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackRetryFromScope(string reason)
        => TrackRetry(reason);

    /// <summary>
    /// 현재 실행 스코프를 사용하여 벌크 삽입 행 수를 기록합니다.
    /// </summary>
    /// <param name="rows">벌크 삽입된 행 수입니다.</param>
    /// <param name="tableName">대상 테이블 이름입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackBulkRows(long rows, string tableName)
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope(operation: "BULK", target: tableName);
        TrackBulkRows(rows, tableName, in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 벌크 삽입 행 수를 기록합니다.
    /// </summary>
    /// <param name="rows">벌크 삽입된 행 수입니다.</param>
    /// <param name="tableName">대상 테이블 이름입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackBulkRowsFromScope(long rows, string tableName)
        => TrackBulkRows(rows, tableName);

    /// <summary>
    /// 현재 실행 스코프를 사용하여 스키마 갱신 시도를 기록합니다.
    /// </summary>
    /// <param name="success">스키마 갱신 성공 여부입니다.</param>
    /// <param name="kind">스키마 갱신 종류 태그 값입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackSchemaRefresh(bool success, string kind)
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope(operation: "SCHEMA_REFRESH", target: kind);
        TrackSchemaRefresh(success, kind, in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 스키마 갱신 시도를 기록합니다.
    /// </summary>
    /// <param name="success">스키마 갱신 성공 여부입니다.</param>
    /// <param name="kind">스키마 갱신 종류 태그 값입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackSchemaRefreshFromScope(bool success, string kind)
        => TrackSchemaRefresh(success, kind);

    /// <summary>
    /// 현재 실행 스코프를 사용하여 스키마 캐시 적중을 기록합니다.
    /// </summary>
    /// <param name="kind">캐시 종류 태그 값입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackCacheHit(string kind)
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope(operation: "SCHEMA_CACHE", target: kind);
        TrackCacheHit(kind, in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 스키마 캐시 적중을 기록합니다.
    /// </summary>
    /// <param name="kind">캐시 종류 태그 값입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackCacheHitFromScope(string kind)
        => TrackCacheHit(kind);

    /// <summary>
    /// 현재 실행 스코프를 사용하여 TVP 전송 바이트 수를 기록합니다.
    /// </summary>
    /// <param name="bytes">전송된 TVP 바이트 수입니다.</param>
    /// <param name="tvpName">TVP 타입 이름입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackTvpUsage(long bytes, string tvpName)
    {
        DbRequestInfo info = DbRequestInfo.FromCurrentScope(operation: "TVP", target: tvpName);
        TrackTvpUsage(bytes, tvpName, in info);
    }

    /// <summary>
    /// 현재 실행 스코프를 사용하여 TVP 전송 바이트 수를 기록합니다.
    /// </summary>
    /// <param name="bytes">전송된 TVP 바이트 수입니다.</param>
    /// <param name="tvpName">TVP 타입 이름입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackTvpUsageFromScope(long bytes, string tvpName)
        => TrackTvpUsage(bytes, tvpName);

    #endregion

    // =========================================================================
    // 2.6. 명시적 DbRequestInfo 기반 코어 API
    // =========================================================================

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 활성 DB 연결 수를 증가시킵니다.
    /// </summary>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackConnectionOpen(in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList();
        FillTags(ref tags, info);
        s_connActive.Add(1, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 활성 DB 연결 수를 감소시킵니다.
    /// </summary>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackConnectionClose(in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList();
        FillTags(ref tags, info);
        s_connActive.Add(-1, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 쿼리 실행 시간을 기록합니다.
    /// </summary>
    /// <param name="elapsed">쿼리 실행 소요 시간입니다.</param>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackDuration(TimeSpan elapsed, in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList();
        FillTags(ref tags, info);
        s_queryDuration.Record(elapsed.TotalMilliseconds, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 재시도 발생 횟수를 기록합니다.
    /// </summary>
    /// <param name="reason">재시도 사유 태그 값입니다.</param>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackRetry(string reason, in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList { { AttrRetryReason, reason } };
        FillTags(ref tags, info);
        s_retries.Add(1, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 벌크 삽입 행 수를 기록합니다.
    /// </summary>
    /// <param name="rows">벌크 삽입된 행 수입니다.</param>
    /// <param name="tableName">대상 테이블 이름입니다.</param>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackBulkRows(long rows, string tableName, in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList();
        FillTags(ref tags, info);

        // 테이블명이 DbRequestInfo.Target에 없거나 다를 수 있으므로 명시적으로 덮어쓰기
        tags.Add(AttrDbTable, tableName);

        s_bulkRows.Add(rows, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 스키마 갱신 시도를 기록합니다.
    /// </summary>
    /// <param name="success">스키마 갱신 성공 여부입니다.</param>
    /// <param name="kind">스키마 갱신 종류 태그 값입니다.</param>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackSchemaRefresh(bool success, string kind, in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList
        {
            { AttrSchemaKind, kind },
            { "success",      success }
        };

        FillTags(ref tags, info);
        s_schemaRefresh.Add(1, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 스키마 캐시 적중을 기록합니다.
    /// </summary>
    /// <param name="kind">캐시 종류 태그 값입니다.</param>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackCacheHit(string kind, in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList
        {
            { AttrSchemaKind, kind },
            { AttrCacheHit,   true }
        };

        FillTags(ref tags, info);
        s_cacheHits.Add(1, tags);
    }

    /// <summary>
    /// 지정한 DB 요청 컨텍스트로 TVP 전송 바이트 수를 기록합니다.
    /// </summary>
    /// <param name="bytes">전송된 TVP 바이트 수입니다.</param>
    /// <param name="tvpName">TVP 타입 이름입니다.</param>
    /// <param name="info">메트릭 태그로 사용할 DB 요청 컨텍스트입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackTvpUsage(long bytes, string tvpName, in DbRequestInfo info)
    {
        if (!s_enabled)
            return;

        TagList tags = new TagList { { AttrTvpName, tvpName } };
        FillTags(ref tags, info);
        s_tvpBytes.Add(bytes, tags);
    }

    /// <summary>
    /// 컨텍스트 태그 없이 스키마 캐시 적중 카운터를 증가시킵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncrementCacheHit()
    {
        if (!s_enabled)
            return;

        s_cacheHits.Add(1);
    }

    /// <summary>
    /// 컨텍스트 태그 없이 스키마 캐시 미스 카운터를 증가시킵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncrementCacheMiss()
    {
        if (!s_enabled)
            return;

        s_cacheMisses.Add(1);
    }

    /// <summary>
    /// 캐시 정리로 반환된 바이트 수를 기록합니다.
    /// </summary>
    /// <param name="bytes">반환된 바이트 수입니다.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TrackCacheBytesFreed(long bytes)
    {
        if (!s_enabled)
            return;

        s_cacheBytesFreed.Add(bytes);
    }
}

#endregion

#region 감시형 데이터 리더 (Monitored Proxy)

/// <summary>
/// <see cref="SqlDataReader"/> 를 래핑(Wrapping)하여 리소스 수명 주기를 감시하는 프록시 리더입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 스트리밍 쿼리 실행 시, 리더가 종료(<see cref="DisposeAsync"/> / <see cref="Dispose(bool)"/>)되는
/// 정확한 시점에 DB 연결 메트릭(Connection Close)을 기록하여 모니터링 정확도를 보장합니다.
/// </para>
/// </summary>
internal sealed class MonitoredSqlDataReader : DbDataReader
{
    private readonly SqlDataReader _inner;
    private readonly SqlConnection _connection;
    private readonly DbRequestInfo _info;
    private bool _disposed;

    public MonitoredSqlDataReader(SqlDataReader inner, SqlConnection connection, DbRequestInfo info)
    {
        _inner = inner;
        _connection = connection;
        _info = info;
    }

    /// <summary>
    /// 비동기 방식으로 리더와 연결을 해제하고, 연결 종료 메트릭을 기록합니다.
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            _connection.Dispose();
        }
        finally
        {
            DbMetrics.TrackConnectionClose(in _info);
        }
    }

    /// <summary>
    /// 동기 방식으로 리더와 연결을 해제하고, 연결 종료 메트릭을 기록합니다.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            base.Dispose(disposing);
            _inner.Dispose();
            _connection.Dispose();
        }
        finally
        {
            DbMetrics.TrackConnectionClose(in _info);
        }
    }

    // ========================================================================
    // DbDataReader 위임 구현 (Delegate Implementations)
    // ========================================================================

    public override int Depth => _inner.Depth;
    public override int FieldCount => _inner.FieldCount;
    public override bool HasRows => _inner.HasRows;
    public override bool IsClosed => _inner.IsClosed;
    public override int RecordsAffected => _inner.RecordsAffected;
    public override int VisibleFieldCount => _inner.VisibleFieldCount;

    public override object this[int ordinal] => _inner[ordinal];
    public override object this[string name] => _inner[name];

    public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => _inner.GetChar(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
    public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2093",
        Justification = "Monitored reader forwards schema Type values only; Lib.Db does not reflect over returned members here.")]
    public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
    public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);
    public override string GetName(int ordinal) => _inner.GetName(ordinal);
    public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
    public override string GetString(int ordinal) => _inner.GetString(ordinal);
    public override object GetValue(int ordinal) => _inner.GetValue(ordinal);
    public override int GetValues(object[] values) => _inner.GetValues(values);
    public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);
    public override bool NextResult() => _inner.NextResult();
    public override bool Read() => _inner.Read();

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        => _inner.IsDBNullAsync(ordinal, cancellationToken);

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        => _inner.NextResultAsync(cancellationToken);

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        => _inner.ReadAsync(cancellationToken);

    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        => _inner.GetFieldValueAsync<T>(ordinal, cancellationToken);

    public override T GetFieldValue<T>(int ordinal) => _inner.GetFieldValue<T>(ordinal);
    public override DataTable? GetSchemaTable() => _inner.GetSchemaTable();
    public override Task<DataTable?> GetSchemaTableAsync(CancellationToken cancellationToken = default)
        => _inner.GetSchemaTableAsync(cancellationToken);
    public override Stream GetStream(int ordinal) => _inner.GetStream(ordinal);
    public override TextReader GetTextReader(int ordinal) => _inner.GetTextReader(ordinal);

    public override System.Collections.IEnumerator GetEnumerator()
        => ((System.Collections.IEnumerable)_inner).GetEnumerator();
}

#endregion
