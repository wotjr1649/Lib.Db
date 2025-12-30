using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lib.Db.Diagnostics;

/// <summary>
/// Lib.Db의 OpenTelemetry 관측 가능성(Observability)을 위한 중앙 텔레메트리 관리 클래스입니다.
/// <para>ActivitySource와 Meter를 정의하고, 주요 메트릭을 생성합니다.</para>
/// </summary>
public static class LibDbTelemetry
{
    #region Core Sources
    public const string SourceName = "Lib.Db";

    /// <summary>
    /// Lib.Db 전용 ActivitySource입니다. 추적(Tracing) 데이터 생성에 사용됩니다.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>
    /// Lib.Db 전용 Meter입니다. 메트릭(Metrics) 데이터 생성에 사용됩니다.
    /// </summary>
    public static readonly Meter Meter = new(SourceName);

    #endregion

    #region Metrics (Counters & Histograms)

    // A) DB Metrics
    public static readonly Counter<long> DbRequestsTotal = Meter.CreateCounter<long>(
        "libdb.db_requests_total",
        description: "Total number of DB requests executed by SqlDbExecutor.");

    public static readonly Histogram<double> DbRequestDuration = Meter.CreateHistogram<double>(
        "libdb.db_request_duration_ms",
        unit: "ms",
        description: "Duration of DB requests in milliseconds.");

    // B) Cache Metrics
    public static readonly Counter<long> CacheRequestsTotal = Meter.CreateCounter<long>(
        "libdb.cache_requests_total",
        description: "Total number of Cache operations (Set/Get/Remove).");

    public static readonly Histogram<double> CacheOpDuration = Meter.CreateHistogram<double>(
        "libdb.cache_op_duration_ms",
        unit: "ms",
        description: "Duration of Cache operations in milliseconds.");

    public static readonly Counter<long> CacheCleanupTotal = Meter.CreateCounter<long>(
        "libdb.cache_cleanup_total",
        description: "Total number of Cache cleanup cycles.");

    public static readonly ObservableGauge<long> CacheBytesFreed = Meter.CreateObservableGauge<long>(
        "libdb.cache_bytes_freed",
        () => 0, // Placeholder: Actual value updated via callbacks if needed, or better use dedicated Gauge reporting 
        unit: "bytes",
        description: "Bytes freed during cache cleanup cycles."
    );
    #endregion
}
