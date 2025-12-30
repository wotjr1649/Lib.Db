// ============================================================================
// 파일: Lib.Db/Configuration/Internal/LibDbConfig.cs
// 설명: AOT 바인딩 호환성을 위한 Shadow DTO (Configuration Binding Model)
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.Collections.Generic;
using Lib.Db.Contracts.Core; // TvpValidationMode

namespace Lib.Db.Configuration.Internal;

/// <summary>
/// appsettings.json 바인딩을 위한 내부용 DTO입니다.
/// <para>
/// <b>[설계의도]</b><br/>
/// C# 14 'field' 키워드를 사용하여 바인딩 시점부터 엄격한 유효성 검사(Self-Validation)를 수행합니다.
/// 잘못된 설정값 유입을 원천 차단하여 시스템 안정성을 높입니다.
/// </para>
/// </summary>
internal sealed class LibDbConfig
{
    // [1] 연결 및 인프라
    public Dictionary<string, string> ConnectionStrings
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = [];

    // [2] 스키마 캐싱
    public bool EnableSchemaCaching { get; set; } = true;
    
    public int SchemaRefreshIntervalSeconds
    {
        get;
        set
        {
            if (value is < 1 or > 86400)
                throw new ArgumentOutOfRangeException(nameof(value), value, "1초 ~ 1일(86400초) 사이여야 합니다.");
            field = value;
        }
    } = 60;

    public List<string> WatchedInstances { get; set; } = [];
    public List<string> PrewarmSchemas { get; set; } = ["dbo"];
    public List<string> PrewarmIncludePatterns { get; set; } = [];
    public List<string> PrewarmExcludePatterns { get; set; } = [];

    // [3] 쿼리 실행 정책
    public bool EnableDryRun { get; set; } = false;
    public bool StrictRequiredParameterCheck { get; set; } = true;

    // [4] 데이터 직렬화 및 검증
    public TvpValidationMode TvpValidationMode { get; set; } = TvpValidationMode.Strict;
    public bool EnableGeneratedTvpBinder { get; set; } = true;

    // [5] 타임아웃
    public int DefaultCommandTimeoutSeconds
    {
        get;
        set
        {
            if (value is < 1 or > 600)
                throw new ArgumentOutOfRangeException(nameof(value), value, "1초 ~ 600초 사이여야 합니다.");
            field = value;
        }
    } = 30;

    public int BulkCommandTimeoutSeconds
    {
        get;
        set
        {
            if (value is < 1 or > 3600)
                throw new ArgumentOutOfRangeException(nameof(value), value, "1초 ~ 3600초 사이여야 합니다.");
            field = value;
        }
    } = 600;

    public int BulkBatchSize
    {
        get;
        set
        {
            if (value is < 100 or > 100_000)
                throw new ArgumentOutOfRangeException(nameof(value), value, "100 ~ 100,000건 사이여야 합니다.");
            field = value;
        }
    } = 5000;

    // [6] 리소스 관리
    public long TvpMemoryWarningThresholdBytes
    {
        get;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "0보다 커야 합니다.");
            field = value;
        }
    } = 10 * 1024 * 1024;

    public int ResumableQueryMaxRetries
    {
        get;
        set
        {
            if (value is < 0 or > 20)
                throw new ArgumentOutOfRangeException(nameof(value), value, "0 ~ 20회 사이여야 합니다.");
            field = value;
        }
    } = 5;

    public int ResumableQueryBaseDelayMs
    {
        get;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "0ms 이상이어야 합니다.");
            field = value;
        }
    } = 100;

    public int ResumableQueryMaxDelayMs
    {
        get;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "0ms 이상이어야 합니다.");
            field = value;
        }
    } = 5000;

    // [7] 재시도 및 복구
    public bool EnableResilience { get; set; } = false;
    public LibDbOptions.ResilienceOptions Resilience { get; set; } = new();

    // [8] 회복 탄력성 (캐시)
    public int MaxCacheSize
    {
        get;
        set
        {
            if (value < 1000)
                throw new ArgumentOutOfRangeException(nameof(value), value, "최소 1,000 이상이어야 합니다.");
            field = value;
        }
    } = 10_000;

    public int SchemaSnapshotWarningThreshold
    {
        get;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "0보다 커야 합니다.");
            field = value;
        }
    } = 5_000;

    // [9] L2 캐시 (Shadow DTO)
    public SharedMemoryCacheConfig SharedMemoryCache { get; set; } = new();
    public bool? EnableSharedMemoryCache { get; set; }
    public bool? EnableEpochCoordination { get; set; }
    public int EpochCheckIntervalSeconds { get; set; } = 5;

    // [10] 카오스
    public ChaosOptions Chaos { get; set; } = new();

    // [11] 관측 가능성
    public int HealthCheckThrottleSeconds
    {
        get;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "0초보다 커야 합니다.");
            field = value;
        }
    } = 1;

    public int HealthCheckTimeoutSeconds
    {
        get;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "0초보다 커야 합니다.");
            field = value;
        }
    } = 2;

    public bool EnableOpenTelemetry { get; set; } = false;
    public bool EnableObservability { get; set; } = false;
    public bool IncludeParametersInTrace { get; set; } = false;

    // [12] 내부 튜닝
    public int SchemaLockCleanupThreshold
    {
        get;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "0보다 커야 합니다.");
            field = value;
        }
    } = 1000;

    public int SchemaLockCleanupIntervalMs
    {
        get;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "0ms보다 커야 합니다.");
            field = value;
        }
    } = 60000;

    public int PrewarmMaxConcurrency 
    { 
        get; 
        set 
        {
            if (value is < 0 or > 1024)
                throw new ArgumentOutOfRangeException(nameof(value), value, "0 ~ 1024 사이여야 합니다.");
            field = value;
        } 
    } = 0;

    /// <summary>
    /// 바인딩된 설정을 실제 런타임 옵션에 적용합니다.
    /// </summary>
    public void ApplyTo(LibDbOptions options)
    {
        // ... (이전과 동일한 매핑 로직) ...
        // [1]
        options.ConnectionStrings = this.ConnectionStrings;
        
        // [2]
        options.EnableSchemaCaching = this.EnableSchemaCaching;
        options.SchemaRefreshIntervalSeconds = this.SchemaRefreshIntervalSeconds;
        options.WatchedInstances = this.WatchedInstances;
        options.PrewarmSchemas = this.PrewarmSchemas;
        options.PrewarmIncludePatterns = this.PrewarmIncludePatterns;
        options.PrewarmExcludePatterns = this.PrewarmExcludePatterns;

        // [3]
        options.EnableDryRun = this.EnableDryRun;
        options.StrictRequiredParameterCheck = this.StrictRequiredParameterCheck;

        // [4]
        options.TvpValidationMode = this.TvpValidationMode;
        options.EnableGeneratedTvpBinder = this.EnableGeneratedTvpBinder;

        // [5]
        options.DefaultCommandTimeoutSeconds = this.DefaultCommandTimeoutSeconds;
        options.BulkCommandTimeoutSeconds = this.BulkCommandTimeoutSeconds;
        options.BulkBatchSize = this.BulkBatchSize;

        // [6]
        options.TvpMemoryWarningThresholdBytes = this.TvpMemoryWarningThresholdBytes;
        options.ResumableQueryMaxRetries = this.ResumableQueryMaxRetries;
        options.ResumableQueryBaseDelayMs = this.ResumableQueryBaseDelayMs;
        options.ResumableQueryMaxDelayMs = this.ResumableQueryMaxDelayMs;

        // [7]
        options.EnableResilience = this.EnableResilience;
        options.Resilience = this.Resilience;

        // [8]
        options.MaxCacheSize = this.MaxCacheSize;
        options.SchemaSnapshotWarningThreshold = this.SchemaSnapshotWarningThreshold;

        // [9]
        options.SharedMemoryCache.BasePath = this.SharedMemoryCache.BasePath;
        options.SharedMemoryCache.Scope = this.SharedMemoryCache.Scope;
        options.SharedMemoryCache.MaxCacheSizeBytes = this.SharedMemoryCache.MaxCacheSizeBytes;
        options.SharedMemoryCache.IsolationKey = this.SharedMemoryCache.IsolationKey;

        options.EnableSharedMemoryCache = this.EnableSharedMemoryCache;
        options.EnableEpochCoordination = this.EnableEpochCoordination;
        options.EpochCheckIntervalSeconds = this.EpochCheckIntervalSeconds;

        // [10]
        options.Chaos = this.Chaos;

        // [11]
        options.HealthCheckThrottleSeconds = this.HealthCheckThrottleSeconds;
        options.HealthCheckTimeoutSeconds = this.HealthCheckTimeoutSeconds;
        options.EnableOpenTelemetry = this.EnableOpenTelemetry;
        options.EnableObservability = this.EnableObservability;
        options.IncludeParametersInTrace = this.IncludeParametersInTrace;

        // [12]
        options.SchemaLockCleanupThreshold = this.SchemaLockCleanupThreshold;
        options.SchemaLockCleanupIntervalMs = this.SchemaLockCleanupIntervalMs;
        options.PrewarmMaxConcurrency = this.PrewarmMaxConcurrency;
    }
}

internal sealed class SharedMemoryCacheConfig
{
    public string BasePath 
    { 
        get; 
        set => field = string.IsNullOrWhiteSpace(value) 
            ? throw new ArgumentException("Null/Empty 불가", nameof(value)) 
            : value; 
    } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lib.Db.Cache");

    public Lib.Db.Caching.CacheScope Scope { get; set; } = Lib.Db.Caching.CacheScope.User;

    public long MaxCacheSizeBytes 
    { 
        get; 
        set 
        {
            if (value < 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(value), "1MB 이상이어야 합니다.");
            field = value;
        } 
    } = 1024L * 1024L * 1024L;

    public string? IsolationKey { get; set; }
}
