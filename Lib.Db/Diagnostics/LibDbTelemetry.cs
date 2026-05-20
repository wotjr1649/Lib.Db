// ============================================================================
// 파일: Lib.Db/Diagnostics/LibDbTelemetry.cs
// 설명: Lib.Db 텔레메트리 — ActivitySource 및 Meter 기반 계측 인프라
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace Lib.Db.Diagnostics;

/// <summary>
/// Lib.Db의 OpenTelemetry 관측 가능성(Observability)을 위한 중앙 텔레메트리 관리 클래스입니다.
/// <para>ActivitySource와 Meter를 정의하고, 주요 메트릭을 생성합니다.</para>
/// </summary>
public static class LibDbTelemetry
{
    #region Core Sources
    /// <summary>Lib.Db 텔레메트리 소스 이름입니다.</summary>
    public const string SourceName = "Lib.Db";

    /// <summary>Lib.Db 라이브러리 버전입니다.</summary>
    public const string Version = LibDbBuildInfo.Version;

    /// <summary>
    /// Lib.Db 전용 ActivitySource입니다. 추적(Tracing) 데이터 생성에 사용됩니다.
    /// <para><b>[단일 인스턴스 원칙]</b> 라이브러리 전체에서 이 인스턴스만 사용해야 합니다. 로컬 ActivitySource 선언은 금지됩니다.</para>
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary>
    /// Lib.Db 전용 Meter입니다. 메트릭(Metrics) 데이터 생성에 사용됩니다.
    /// <para><b>[단일 인스턴스 원칙]</b> 라이브러리 전체에서 이 인스턴스만 사용해야 합니다. 로컬 Meter 선언은 금지됩니다.</para>
    /// </summary>
    public static readonly Meter Meter = new(SourceName, Version);

    #endregion

    #region Metrics (Counters & Histograms)

    // A) DB Metrics
    /// <summary>SqlDbExecutor에서 실행된 총 DB 요청 수를 추적하는 카운터입니다.</summary>
    public static readonly Counter<long> DbRequestsTotal = Meter.CreateCounter<long>(
        "libdb.db_requests_total",
        description: "Total number of DB requests executed by SqlDbExecutor.");

    /// <summary>DB 요청 소요 시간(밀리초)을 기록하는 히스토그램입니다.</summary>
    public static readonly Histogram<double> DbRequestDuration = Meter.CreateHistogram<double>(
        "libdb.db_request_duration_ms",
        unit: "ms",
        description: "Duration of DB requests in milliseconds.");

    // A-2) Connection Pool Metrics
    /// <summary>연결 획득 소요 시간(밀리초)을 기록하는 히스토그램입니다.</summary>
    public static readonly Histogram<double> ConnectionAcquireDuration = Meter.CreateHistogram<double>(
        "libdb.connection.acquire_duration_ms",
        unit: "ms",
        description: "연결 획득 소요 시간");

    /// <summary>연결 풀 대기 발생 횟수를 추적하는 카운터입니다.</summary>
    public static readonly Counter<long> ConnectionPoolWaits = Meter.CreateCounter<long>(
        "libdb.connection.pool_waits",
        description: "연결 풀 대기 횟수");

    /// <summary>연결 풀 타임아웃 발생 횟수를 추적하는 카운터입니다.</summary>
    public static readonly Counter<long> ConnectionPoolTimeouts = Meter.CreateCounter<long>(
        "libdb.connection.pool_timeouts",
        description: "연결 풀 타임아웃 횟수");

    // B) Cache Metrics
    /// <summary>캐시 연산(Set/Get/Remove) 총 횟수를 추적하는 카운터입니다.</summary>
    public static readonly Counter<long> CacheRequestsTotal = Meter.CreateCounter<long>(
        "libdb.cache_requests_total",
        description: "Total number of Cache operations (Set/Get/Remove).");

    /// <summary>캐시 연산 소요 시간(밀리초)을 기록하는 히스토그램입니다.</summary>
    public static readonly Histogram<double> CacheOpDuration = Meter.CreateHistogram<double>(
        "libdb.cache_op_duration_ms",
        unit: "ms",
        description: "Duration of Cache operations in milliseconds.");

    /// <summary>캐시 정리 사이클 총 횟수를 추적하는 카운터입니다.</summary>
    public static readonly Counter<long> CacheCleanupTotal = Meter.CreateCounter<long>(
        "libdb.cache_cleanup_total",
        description: "Total number of Cache cleanup cycles.");

    // [내부 상태] 캐시 정리에서 해제된 누적 바이트 수를 원자적으로 추적합니다.
    // ObservableGauge 콜백에서 Interlocked.Read로 안전하게 읽습니다.
    private static long s_totalBytesFreed;

    /// <summary>
    /// 캐시 정리 사이클에서 해제된 바이트 수를 누적 기록합니다.
    /// <para>
    /// <b>[설계 의도]</b><br/>
    /// SharedMemoryCache, CacheMaintenanceService 등 캐시 해제 발생 지점에서 호출합니다.<br/>
    /// Interlocked.Add를 사용하여 멀티스레드 환경에서 원자적으로 누적합니다.
    /// </para>
    /// </summary>
    /// <param name="bytes">해제된 바이트 수</param>
    public static void RecordBytesFreed(long bytes) =>
        Interlocked.Add(ref s_totalBytesFreed, bytes);

    /// <summary>캐시 정리 사이클에서 해제된 총 바이트를 관측하는 게이지 메트릭입니다.</summary>
    public static readonly ObservableGauge<long> CacheBytesFreed = Meter.CreateObservableGauge<long>(
        "libdb.cache_bytes_freed",
        () => Interlocked.Read(ref s_totalBytesFreed),
        unit: "bytes",
        description: "캐시 정리 사이클에서 해제된 총 바이트"
    );
    #endregion
}
