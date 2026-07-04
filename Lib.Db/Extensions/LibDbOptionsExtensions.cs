// ============================================================================
// 파일: Lib.Db/Extensions/LibDbOptionsExtensions.cs
// 설명: LibDbOptions 설정을 위한 확장 메서드
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System;
using System.Globalization;
using Lib.Db.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// LibDbOptions 설정을 위한 확장 메서드입니다.
/// <para>
/// <b>[표준 패턴]</b> .NET Options Pattern (OptionsBuilder) 활용
/// </para>
/// </summary>
public static class LibDbOptionsExtensions
{
    /// <summary>
    /// 운영 환경 권장 보안 기본값을 적용합니다.
    /// <para>
    /// 연결 문자열은 <see cref="ConnectionSecurityProfile.Production"/> 기준으로 검증하고,
    /// Raw SQL은 기본적으로 쓰기/권한/운영 계열 Text 명령을 차단합니다.
    /// </para>
    /// </summary>
    /// <param name="options">보안 기본값을 적용할 옵션 인스턴스</param>
    /// <returns>체이닝을 위한 동일 옵션 인스턴스</returns>
    public static LibDbOptions UseProductionSecurityDefaults(this LibDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
        options.AllowProductionTrustServerCertificateWaiver = false;
        options.AllowProductionSaLoginWaiver = false;
        options.IncludeParametersInTrace = false;

        if (options.RawSqlPolicy == RawSqlPolicy.Allow)
            options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;

        return options;
    }

    /// <summary>
    /// <see cref="OptionsBuilder{TOptions}"/> 구성 파이프라인에 운영 환경 권장 보안 기본값을 추가합니다.
    /// </summary>
    /// <param name="builder">Lib.Db 옵션 빌더</param>
    /// <returns>체이닝을 위한 동일 옵션 빌더</returns>
    public static OptionsBuilder<LibDbOptions> UseProductionSecurityDefaults(
        this OptionsBuilder<LibDbOptions> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Configure(static options => options.UseProductionSecurityDefaults());
    }

    /// <summary>
    /// LibDbOptions를 구성하고 등록합니다. (표준 OptionsBuilder 패턴)
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configure">Options 설정 델리게이트</param>
    /// <returns>추가 구성을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> AddLibDbOptions(
        this IServiceCollection services,
        Action<LibDbOptions>? configure = null)
    {
        OptionsBuilder<LibDbOptions> builder = services.AddOptions<LibDbOptions>()
                              .ValidateOnStart();

        if (configure != null)
        {
            builder.Configure(configure);
        }

        // ✅ v2.0: Options Validator 등록
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LibDbOptions>, Lib.Db.Configuration.LibDbOptionsValidator>());

        // Options → Singleton도 등록 (역호환성)
        // 기존 코드에서 LibDbOptions를 직접 주입받는 경우 대비
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<IOptions<LibDbOptions>>().Value);

        return builder;
    }

    /// <summary>
    /// IConfiguration에서 LibDbOptions를 바인딩합니다.
    /// <para>
    /// <b>[사용 예시]</b><br/>
    /// appsettings.json에 "LibDb" 섹션 정의 후 자동 바인딩
    /// </para>
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configuration">구성 객체</param>
    /// <param name="sectionName">섹션 이름 (기본: "LibDb")</param>
    /// <returns>추가 구성을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> AddLibDbOptionsFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "LibDb")
    {
        OptionsBuilder<LibDbOptions> builder = services.AddOptions<LibDbOptions>()
                              .Configure(options => BindLibDbOptions(options, configuration.GetSection(sectionName)))
                              .ValidateOnStart();

        // ✅ v2.0: Options Validator 등록
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LibDbOptions>, Lib.Db.Configuration.LibDbOptionsValidator>());

        // Singleton 등록 (역호환성)
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<IOptions<LibDbOptions>>().Value);

        return builder;
    }

    /// <summary>
    /// Manual Binding Helper to replace reflection-based Bind and avoid AOT warnings (SYSLIB1100).
    /// </summary>
    private static void BindLibDbOptions(LibDbOptions options, IConfigurationSection section)
    {
        if (TryGetEnum(section, "Mars", out MarsPolicy mars))
            options.Mars = mars;
        if (TryGetBool(section, "EnableSchemaCaching", out bool enableSchemaCaching))
            options.EnableSchemaCaching = enableSchemaCaching;
        if (TryGetInt(section, "SchemaRefreshIntervalSeconds", out int schemaRefreshIntervalSeconds))
            options.SchemaRefreshIntervalSeconds = schemaRefreshIntervalSeconds;
        if (TryGetBool(section, "EnableDryRun", out bool enableDryRun))
            options.EnableDryRun = enableDryRun;
        if (TryGetEnum(section, "RawSqlPolicy", out RawSqlPolicy rawSqlPolicy))
            options.RawSqlPolicy = rawSqlPolicy;
        if (TryGetEnum(section, "ConnectionSecurityProfile", out ConnectionSecurityProfile securityProfile))
            options.ConnectionSecurityProfile = securityProfile;
        if (TryGetBool(section, "AllowProductionTrustServerCertificateWaiver", out bool allowTrustWaiver))
            options.AllowProductionTrustServerCertificateWaiver = allowTrustWaiver;
        if (TryGetBool(section, "AllowProductionSaLoginWaiver", out bool allowSaWaiver))
            options.AllowProductionSaLoginWaiver = allowSaWaiver;
        if (TryGetBool(section, "StrictRequiredParameterCheck", out bool strictRequiredParameterCheck))
            options.StrictRequiredParameterCheck = strictRequiredParameterCheck;
        if (TryGetEnum(section, "TvpValidationMode", out TvpValidationMode tvpValidationMode))
            options.TvpValidationMode = tvpValidationMode;
        if (TryGetBool(section, "EnableGeneratedTvpBinder", out bool enableGeneratedTvpBinder))
            options.EnableGeneratedTvpBinder = enableGeneratedTvpBinder;

        List<string> connectionStringNames = GetStringList(section.GetSection("ConnectionStringNames"));
        if (connectionStringNames.Count > 0)
            options.ConnectionStringNames = connectionStringNames;

        IConfigurationSection watchedInstancesSection = section.GetSection("WatchedInstances");
        if (watchedInstancesSection.Exists())
            options.WatchedInstances = GetStringList(watchedInstancesSection);

        IConfigurationSection prewarmSection = section.GetSection("PrewarmSchemas");
        if (prewarmSection.Exists())
            options.PrewarmSchemas = GetStringList(prewarmSection);

        IConfigurationSection includePatternsSection = section.GetSection("PrewarmIncludePatterns");
        if (includePatternsSection.Exists())
            options.PrewarmIncludePatterns = GetStringList(includePatternsSection);

        IConfigurationSection excludePatternsSection = section.GetSection("PrewarmExcludePatterns");
        if (excludePatternsSection.Exists())
            options.PrewarmExcludePatterns = GetStringList(excludePatternsSection);

        if (TryGetInt(section, "DefaultCommandTimeoutSeconds", out int defaultCommandTimeoutSeconds))
            options.DefaultCommandTimeoutSeconds = defaultCommandTimeoutSeconds;
        if (TryGetInt(section, "BulkCommandTimeoutSeconds", out int bulkCommandTimeoutSeconds))
            options.BulkCommandTimeoutSeconds = bulkCommandTimeoutSeconds;
        if (TryGetInt(section, "BulkBatchSize", out int bulkBatchSize))
            options.BulkBatchSize = bulkBatchSize;
        if (TryGetLong(section, "TvpMemoryWarningThresholdBytes", out long tvpMemoryWarningThresholdBytes))
            options.TvpMemoryWarningThresholdBytes = tvpMemoryWarningThresholdBytes;
        if (TryGetInt(section, "ResumableQueryMaxRetries", out int resumableQueryMaxRetries))
            options.ResumableQueryMaxRetries = resumableQueryMaxRetries;
        if (TryGetInt(section, "ResumableQueryBaseDelayMs", out int resumableQueryBaseDelayMs))
            options.ResumableQueryBaseDelayMs = resumableQueryBaseDelayMs;
        if (TryGetInt(section, "ResumableQueryMaxDelayMs", out int resumableQueryMaxDelayMs))
            options.ResumableQueryMaxDelayMs = resumableQueryMaxDelayMs;
        if (TryGetBool(section, "EnableResilience", out bool enableResilience))
            options.EnableResilience = enableResilience;

        BindResilienceOptions(options.Resilience, section.GetSection("Resilience"));

        if (TryGetInt(section, "MaxCacheSize", out int maxCacheSize))
            options.MaxCacheSize = maxCacheSize;
        if (TryGetInt(section, "SchemaSnapshotWarningThreshold", out int schemaSnapshotWarningThreshold))
            options.SchemaSnapshotWarningThreshold = schemaSnapshotWarningThreshold;

        BindSharedMemoryCacheOptions(options.SharedMemoryCache, section.GetSection("SharedMemoryCache"));
        if (TryGetNullableBool(section, "EnableSharedMemoryCache", out bool? enableSharedMemoryCache))
            options.EnableSharedMemoryCache = enableSharedMemoryCache;
        if (TryGetNullableBool(section, "EnableEpochCoordination", out bool? enableEpochCoordination))
            options.EnableEpochCoordination = enableEpochCoordination;
        if (TryGetInt(section, "EpochCheckIntervalSeconds", out int epochCheckIntervalSeconds))
            options.EpochCheckIntervalSeconds = epochCheckIntervalSeconds;

        BindChaosOptions(options.Chaos, section.GetSection("Chaos"));

        if (TryGetInt(section, "HealthCheckThrottleSeconds", out int healthCheckThrottleSeconds))
            options.HealthCheckThrottleSeconds = healthCheckThrottleSeconds;
        if (TryGetInt(section, "HealthCheckTimeoutSeconds", out int healthCheckTimeoutSeconds))
            options.HealthCheckTimeoutSeconds = healthCheckTimeoutSeconds;
        if (TryGetBool(section, "EnableObservability", out bool enableObservability))
            options.EnableObservability = enableObservability;
        else if (TryGetBool(section, "EnableOpenTelemetry", out bool enableOpenTelemetry))
            options.EnableObservability = enableOpenTelemetry;
        if (TryGetBool(section, "IncludeParametersInTrace", out bool includeParametersInTrace))
            options.IncludeParametersInTrace = includeParametersInTrace;
        if (TryGetInt(section, "SchemaLockCleanupThreshold", out int schemaLockCleanupThreshold))
            options.SchemaLockCleanupThreshold = schemaLockCleanupThreshold;
        if (TryGetInt(section, "SchemaLockCleanupIntervalMs", out int schemaLockCleanupIntervalMs))
            options.SchemaLockCleanupIntervalMs = schemaLockCleanupIntervalMs;
        if (TryGetInt(section, "PrewarmMaxConcurrency", out int prewarmMaxConcurrency))
            options.PrewarmMaxConcurrency = prewarmMaxConcurrency;
    }

    private static void BindResilienceOptions(LibDbOptions.ResilienceOptions options, IConfigurationSection section)
    {
        if (!section.Exists())
            return;

        if (TryGetInt(section, "MaxRetryCount", out int maxRetryCount))
            options.MaxRetryCount = maxRetryCount;
        if (TryGetInt(section, "BaseRetryDelayMs", out int baseRetryDelayMs))
            options.BaseRetryDelayMs = baseRetryDelayMs;
        if (TryGetInt(section, "MaxRetryDelayMs", out int maxRetryDelayMs))
            options.MaxRetryDelayMs = maxRetryDelayMs;
        if (TryGetBool(section, "UseRetryJitter", out bool useRetryJitter))
            options.UseRetryJitter = useRetryJitter;
        if (TryGetEnum(section, "RetryBackoffType", out LibDbOptions.RetryBackoffType retryBackoffType))
            options.RetryBackoffType = retryBackoffType;
        if (TryGetInt(section, "CircuitBreakerThreshold", out int circuitBreakerThreshold))
            options.CircuitBreakerThreshold = circuitBreakerThreshold;
        if (TryGetInt(section, "CircuitBreakerSamplingDurationMs", out int circuitBreakerSamplingDurationMs))
            options.CircuitBreakerSamplingDurationMs = circuitBreakerSamplingDurationMs;
        if (TryGetInt(section, "CircuitBreakerBreakDurationMs", out int circuitBreakerBreakDurationMs))
            options.CircuitBreakerBreakDurationMs = circuitBreakerBreakDurationMs;
        if (TryGetDouble(section, "CircuitBreakerFailureRatio", out double circuitBreakerFailureRatio))
            options.CircuitBreakerFailureRatio = circuitBreakerFailureRatio;
    }

    private static void BindSharedMemoryCacheOptions(SharedMemoryCacheOptions options, IConfigurationSection section)
    {
        if (!section.Exists())
            return;

        if (section["BasePath"] is string basePath)
            options.BasePath = basePath;
        if (TryGetEnum(section, "Scope", out Lib.Db.Caching.CacheScope scope))
            options.Scope = scope;
        if (TryGetLong(section, "MaxCacheSizeBytes", out long maxCacheSizeBytes))
            options.MaxCacheSizeBytes = maxCacheSizeBytes;
        if (section["IsolationKey"] is string isolationKey)
            options.IsolationKey = isolationKey;
    }

    private static void BindChaosOptions(ChaosOptions options, IConfigurationSection section)
    {
        if (!section.Exists())
            return;

        if (TryGetBool(section, "Enabled", out bool enabled))
            options.Enabled = enabled;
        if (TryGetDouble(section, "ExceptionRate", out double exceptionRate))
            options.ExceptionRate = exceptionRate;
        if (TryGetDouble(section, "LatencyRate", out double latencyRate))
            options.LatencyRate = latencyRate;
        if (TryGetInt(section, "MinLatencyMs", out int minLatencyMs))
            options.MinLatencyMs = minLatencyMs;
        if (TryGetInt(section, "MaxLatencyMs", out int maxLatencyMs))
            options.MaxLatencyMs = maxLatencyMs;
    }

    private static List<string> GetStringList(IConfigurationSection section)
    {
        if (!section.Exists())
            return [];

        List<string> values = [];
        foreach (IConfigurationSection child in section.GetChildren())
        {
            if (child.Value is not null)
                values.Add(child.Value);
        }
        return values;
    }

    private static bool TryGetBool(IConfigurationSection section, string key, out bool value)
        => bool.TryParse(section[key], out value);

    private static bool TryGetNullableBool(IConfigurationSection section, string key, out bool? value)
    {
        if (bool.TryParse(section[key], out bool parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetInt(IConfigurationSection section, string key, out int value)
        => int.TryParse(section[key], out value);

    private static bool TryGetLong(IConfigurationSection section, string key, out long value)
        => long.TryParse(section[key], out value);

    private static bool TryGetDouble(IConfigurationSection section, string key, out double value)
        => double.TryParse(section[key], NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryGetEnum<TEnum>(IConfigurationSection section, string key, out TEnum value)
        where TEnum : struct
        => Enum.TryParse(section[key], ignoreCase: true, out value);

    /// <summary>
    /// Options 구성 후 추가 검증을 수행합니다.
    /// </summary>
    /// <param name="builder">OptionsBuilder</param>
    /// <param name="validation">검증 조건</param>
    /// <param name="failureMessage">실패 시 메시지</param>
    /// <returns>체이닝을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> WithValidation(
        this OptionsBuilder<LibDbOptions> builder,
        Func<LibDbOptions, bool> validation,
        string failureMessage)
    {
        return builder.Validate(validation, failureMessage);
    }

    /// <summary>
    /// Options 구성 후 PostConfigure를 수행합니다.
    /// <para>
    /// <b>[사용 시나리오]</b> 다른 서비스 기반 동적 설정
    /// </para>
    /// </summary>
    /// <param name="builder">OptionsBuilder</param>
    /// <param name="postConfigure">PostConfigure 델리게이트</param>
    /// <returns>체이닝을 위한 OptionsBuilder</returns>
    public static OptionsBuilder<LibDbOptions> WithPostConfigure(
        this OptionsBuilder<LibDbOptions> builder,
        Action<LibDbOptions> postConfigure)
    {
        builder.PostConfigure(postConfigure);
        return builder;
    }
}
