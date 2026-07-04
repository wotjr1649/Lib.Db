// ============================================================================
// 파일: Unit/RuntimeUtilityCoverageTests.cs
// 설명: 런타임/확장/인프라 소형 유틸리티 커버리지 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Reflection;
using System.Text.Json;
using Lib.Db.Caching;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Lib.Db.Core;
using Lib.Db.Diagnostics;
using Lib.Db.Execution.Bulk;
using Lib.Db.Execution.Executors;
using Lib.Db.Extensions;
using Lib.Db.Fluent;
using Lib.Db.Infrastructure;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class RuntimeUtilityCoverageTests
{
    [Fact]
    public void LibDbRuntime_ShouldConfigureAndResetStaticState()
    {
        try
        {
            LibDbRuntime.ResetForTesting();
            LibDbRuntime.IsConfigured.Should().BeFalse();
            DbMetrics.IsEnabled.Should().BeFalse();

            LibDbRuntime.Configure(new LibDbOptions { MaxCacheSize = 1_000 }, static (_, _) => true, enableMetrics: false);
            LibDbRuntime.IsConfigured.Should().BeTrue();
            DbMetrics.IsEnabled.Should().BeFalse();

            LibDbRuntime.ConfigureTvpValidation(static (_, _) => false);
            LibDbRuntime.IsConfigured.Should().BeTrue();

            LibDbRuntime.ConfigureMetrics(enabled: true);
            DbMetrics.IsEnabled.Should().BeTrue();

            LibDbRuntime.Configure(new LibDbOptions { EnableObservability = false });
            DbMetrics.IsEnabled.Should().BeFalse();

            LibDbRuntime.Configure(new LibDbOptions { EnableObservability = false }, enableMetrics: true);
            DbMetrics.IsEnabled.Should().BeTrue();
        }
        finally
        {
            LibDbRuntime.ResetForTesting();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddHighPerformanceDb_ShouldApplyEnableObservabilityToDbMetrics(bool enabled)
    {
        try
        {
            DbMetrics.ResetForTesting();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddLibDb(options =>
            {
                LibDbOptions valid = TestOptionsFactory.CreateValidOptions();
                options.ConnectionStringNames = valid.ConnectionStringNames;
                options.ConnectionStrings = valid.ConnectionStrings;
                options.EnableObservability = enabled;
                options.EnableSharedMemoryCache = false;
            });

            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            _ = provider.GetRequiredService<LibDbOptions>();

            DbMetrics.IsEnabled.Should().Be(enabled);
        }
        finally
        {
            DbMetrics.ResetForTesting();
        }
    }

    [Fact]
    public void AddLibDbConfiguration_ShouldLetExplicitEnableObservabilityFalseOverrideLegacyOpenTelemetry()
    {
        try
        {
            DbMetrics.ResetForTesting();
            DbMetrics.IsEnabled = true;

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] =
                        "Server=localhost;Database=TEST;Integrated Security=True;Encrypt=True;TrustServerCertificate=True",
                    ["LibDb:ConnectionStringNames:0"] = "Default",
                    ["LibDb:EnableOpenTelemetry"] = "true",
                    ["LibDb:EnableObservability"] = "false",
                    ["LibDb:EnableSharedMemoryCache"] = "false"
                })
                .Build();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddLibDb(configuration);

            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            LibDbOptions options = provider.GetRequiredService<LibDbOptions>();

            options.EnableObservability.Should().BeFalse();
            DbMetrics.IsEnabled.Should().BeFalse();
        }
        finally
        {
            DbMetrics.ResetForTesting();
        }
    }

    [Fact]
    public void LibDbRuntime_ShouldRejectNullOptions()
    {
        Action act = () => LibDbRuntime.Configure(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void AdvancedSnapshotExtensions_ShouldOverrideBuilderAndRejectForeignStage()
    {
        var builder = new DbRequestBuilder(Mock.Of<IDbExecutor>(), "Default");
        IParameterStage stage = builder;
        IParameterStage foreignStage = Mock.Of<IParameterStage>();

        stage.UseSnapshotOnlyUnsafe().Should().BeSameAs(stage);
        stage.UseServiceOnlyUnsafe().Should().BeSameAs(stage);
        stage.UseSnapshotPreferredUnsafe().Should().BeSameAs(stage);

        foreignStage.Invoking(s => s.UseSnapshotOnlyUnsafe())
            .Should()
            .Throw<InvalidOperationException>();
        foreignStage.Invoking(s => s.UseServiceOnlyUnsafe())
            .Should()
            .Throw<InvalidOperationException>();
        foreignStage.Invoking(s => s.UseSnapshotPreferredUnsafe())
            .Should()
            .Throw<InvalidOperationException>();
    }

    [Fact]
    public void LibDbExceptionFactory_ShouldCreateConsistentExceptions()
    {
        Action nullArg = () => LibDbExceptionFactory.ThrowArgumentNull("dependency", "caller");
        Action badArg = () => LibDbExceptionFactory.ThrowArgument("bad", "value", "caller");
        Action invalid = () => LibDbExceptionFactory.ThrowInvalidOperation("nope", "caller");
        Action unsupported = () => LibDbExceptionFactory.ThrowNotSupported("unsupported");
        Action disposed = () => LibDbExceptionFactory.ThrowObjectDisposed("Thing");

        nullArg.Should().Throw<ArgumentNullException>().WithParameterName("dependency");
        badArg.Should().Throw<ArgumentException>().WithParameterName("value");
        invalid.Should().Throw<InvalidOperationException>().WithMessage("*nope*");
        unsupported.Should().Throw<NotSupportedException>().WithMessage("unsupported");
        disposed.Should().Throw<ObjectDisposedException>().WithMessage("*Thing*");

        LibDbExceptionFactory.CreateFailedToCreateAccessor("Dto").Should().BeOfType<InvalidOperationException>();
        LibDbExceptionFactory.CreateTvpValidationFailed("dbo.T", "shape").Message.Should().Contain("dbo.T");
        LibDbExceptionFactory.CreateCommandExecutionFailed(new TimeoutException("provider timeout")).InnerException.Should().BeNull();
        LibDbExceptionFactory.CreateSchemaMismatch("dbo.usp", 123).Message.Should().Contain("123");
    }

    [Fact]
    public void MutexHelper_ShouldValidateArgumentsAndCreateUsableMutex()
    {
        ILogger logger = NullLogger.Instance;

        Action nullName = () => MutexHelper.CreateProcessMutex(null!, logger);
        Action nullLogger = () => MutexHelper.CreateProcessMutex("coverage", null!);

        nullName.Should().Throw<ArgumentNullException>().WithParameterName("logicalName");
        nullLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");

        string secretLikeName = "Lib.Db.Coverage.Password=mutex-secret." + Guid.NewGuid().ToString("N");
        string safeName = MutexHelper.BuildSafeMutexName(secretLikeName);
        safeName.Should().NotContain(secretLikeName);
        safeName.Should().NotContain("Password=");
        safeName.Should().StartWith("Lib.Db.");

        using Mutex mutex = MutexHelper.CreateProcessMutex(secretLikeName, logger);
        mutex.WaitOne(0).Should().BeTrue();
        mutex.ReleaseMutex();
    }

    [Fact]
    public void SystemMemoryMonitor_ShouldCacheLoadFactorAndExposeCriticalFlag()
    {
        var monitor = new SystemMemoryMonitor();

        double first = monitor.LoadFactor;
        double second = monitor.LoadFactor;

        first.Should().BeInRange(0.0, 1.0);
        second.Should().BeInRange(0.0, 1.0);
        monitor.IsCritical.Should().Be(monitor.LoadFactor > 0.85);
    }

    [Fact]
    public void HybridCacheExtensions_ShouldRegisterHybridCacheAndApplyConfiguration()
    {
        ServiceCollection services = new();
        bool configured = false;

        IServiceCollection returned = services.AddLibDbHybridCache(options =>
        {
            configured = true;
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(30),
                LocalCacheExpiration = TimeSpan.FromSeconds(10)
            };
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;

        returned.Should().BeSameAs(services);
        configured.Should().BeTrue();
        provider.GetRequiredService<HybridCache>().Should().NotBeNull();
    }

    [Fact]
    public void LibDbOptionsExtensions_ShouldApplySecurityDefaultsAndBindConfiguration()
    {
        LibDbOptions direct = new() { RawSqlPolicy = RawSqlPolicy.Allow, IncludeParametersInTrace = true };

        direct.UseProductionSecurityDefaults().Should().BeSameAs(direct);
        direct.ConnectionSecurityProfile.Should().Be(ConnectionSecurityProfile.Production);
        direct.RawSqlPolicy.Should().Be(RawSqlPolicy.DenyWriteText);
        direct.IncludeParametersInTrace.Should().BeFalse();

        string sharedMemoryBasePath = Path.Combine(Path.GetTempPath(), "LibDbBinderParity");
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LibDb:EnableSchemaCaching"] = "false",
                ["LibDb:SchemaRefreshIntervalSeconds"] = "12",
                ["LibDb:EnableDryRun"] = "true",
                ["LibDb:RawSqlPolicy"] = "DenyAllText",
                ["LibDb:ConnectionSecurityProfile"] = "Production",
                ["LibDb:AllowProductionTrustServerCertificateWaiver"] = "true",
                ["LibDb:AllowProductionSaLoginWaiver"] = "true",
                ["LibDb:Mars"] = "ForceEnable",
                ["LibDb:ConnectionStringNames:0"] = "Primary",
                ["LibDb:ConnectionStringNames:1"] = "Reporting",
                ["LibDb:WatchedInstances:0"] = "Primary",
                ["LibDb:PrewarmSchemas:0"] = "dbo",
                ["LibDb:PrewarmSchemas:1"] = "audit",
                ["LibDb:PrewarmIncludePatterns:0"] = "usp_*",
                ["LibDb:PrewarmExcludePatterns:0"] = "usp_Archive*",
                ["LibDb:StrictRequiredParameterCheck"] = "false",
                ["LibDb:TvpValidationMode"] = "LogOnly",
                ["LibDb:EnableGeneratedTvpBinder"] = "false",
                ["LibDb:DefaultCommandTimeoutSeconds"] = "45",
                ["LibDb:BulkCommandTimeoutSeconds"] = "700",
                ["LibDb:BulkBatchSize"] = "6000",
                ["LibDb:TvpMemoryWarningThresholdBytes"] = "20971520",
                ["LibDb:ResumableQueryMaxRetries"] = "6",
                ["LibDb:ResumableQueryBaseDelayMs"] = "150",
                ["LibDb:ResumableQueryMaxDelayMs"] = "6000",
                ["LibDb:Resilience:MaxRetryCount"] = "5",
                ["LibDb:Resilience:BaseRetryDelayMs"] = "25",
                ["LibDb:Resilience:MaxRetryDelayMs"] = "2500",
                ["LibDb:Resilience:UseRetryJitter"] = "false",
                ["LibDb:Resilience:RetryBackoffType"] = "Linear",
                ["LibDb:Resilience:CircuitBreakerThreshold"] = "7",
                ["LibDb:Resilience:CircuitBreakerSamplingDurationMs"] = "1000",
                ["LibDb:Resilience:CircuitBreakerBreakDurationMs"] = "2000",
                ["LibDb:Resilience:CircuitBreakerFailureRatio"] = "0.25",
                ["LibDb:EnableResilience"] = "true",
                ["LibDb:MaxCacheSize"] = "2000",
                ["LibDb:SchemaSnapshotWarningThreshold"] = "3000",
                ["LibDb:SharedMemoryCache:BasePath"] = sharedMemoryBasePath,
                ["LibDb:SharedMemoryCache:Scope"] = "Machine",
                ["LibDb:SharedMemoryCache:MaxCacheSizeBytes"] = "2097152",
                ["LibDb:SharedMemoryCache:IsolationKey"] = "binder-tenant",
                ["LibDb:EnableSharedMemoryCache"] = "true",
                ["LibDb:EnableEpochCoordination"] = "true",
                ["LibDb:EpochCheckIntervalSeconds"] = "9",
                ["LibDb:Chaos:Enabled"] = "true",
                ["LibDb:Chaos:ExceptionRate"] = "0.2",
                ["LibDb:Chaos:LatencyRate"] = "0.3",
                ["LibDb:Chaos:MinLatencyMs"] = "10",
                ["LibDb:Chaos:MaxLatencyMs"] = "20",
                ["LibDb:HealthCheckThrottleSeconds"] = "4",
                ["LibDb:HealthCheckTimeoutSeconds"] = "5",
                ["LibDb:EnableObservability"] = "true",
                ["LibDb:IncludeParametersInTrace"] = "true",
                ["LibDb:SchemaLockCleanupThreshold"] = "1100",
                ["LibDb:SchemaLockCleanupIntervalMs"] = "61000",
                ["LibDb:PrewarmMaxConcurrency"] = "2"
            })
            .Build();

        services.AddLibDbOptions(options =>
            {
                options.MaxCacheSize = 1_000;
                options.ConnectionStrings["Primary"] = "Server=localhost;Database=PrimaryDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
                options.ConnectionStrings["Reporting"] = "Server=localhost;Database=ReportingDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True";
            })
            .UseProductionSecurityDefaults()
            .WithValidation(static options => options.MaxCacheSize > 0, "cache")
            .WithPostConfigure(static options => options.HealthCheckTimeoutSeconds = 3);
        services.AddLibDbOptionsFromConfiguration(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<LibDbOptions> options = provider.GetRequiredService<IOptions<LibDbOptions>>();

        options.Value.HealthCheckTimeoutSeconds.Should().Be(3);
        options.Value.EnableSchemaCaching.Should().BeFalse();
        options.Value.SchemaRefreshIntervalSeconds.Should().Be(12);
        options.Value.EnableDryRun.Should().BeTrue();
        options.Value.RawSqlPolicy.Should().Be(RawSqlPolicy.DenyAllText);
        options.Value.ConnectionSecurityProfile.Should().Be(ConnectionSecurityProfile.Production);
        options.Value.AllowProductionTrustServerCertificateWaiver.Should().BeTrue();
        options.Value.AllowProductionSaLoginWaiver.Should().BeTrue();
        options.Value.StrictRequiredParameterCheck.Should().BeFalse();
        options.Value.TvpValidationMode.Should().Be(TvpValidationMode.LogOnly);
        options.Value.EnableGeneratedTvpBinder.Should().BeFalse();
        options.Value.Mars.Should().Be(MarsPolicy.ForceEnable);
        options.Value.ConnectionStringNames.Should().Equal("Primary", "Reporting");
        options.Value.WatchedInstances.Should().Equal("Primary");
        options.Value.PrewarmSchemas.Should().Equal("dbo", "audit");
        options.Value.PrewarmIncludePatterns.Should().Equal("usp_*");
        options.Value.PrewarmExcludePatterns.Should().Equal("usp_Archive*");
        options.Value.DefaultCommandTimeoutSeconds.Should().Be(45);
        options.Value.BulkCommandTimeoutSeconds.Should().Be(700);
        options.Value.BulkBatchSize.Should().Be(6000);
        options.Value.TvpMemoryWarningThresholdBytes.Should().Be(20L * 1024L * 1024L);
        options.Value.ResumableQueryMaxRetries.Should().Be(6);
        options.Value.ResumableQueryBaseDelayMs.Should().Be(150);
        options.Value.ResumableQueryMaxDelayMs.Should().Be(6000);
        options.Value.EnableResilience.Should().BeTrue();
        options.Value.Resilience.MaxRetryCount.Should().Be(5);
        options.Value.Resilience.BaseRetryDelayMs.Should().Be(25);
        options.Value.Resilience.MaxRetryDelayMs.Should().Be(2500);
        options.Value.Resilience.UseRetryJitter.Should().BeFalse();
        options.Value.Resilience.RetryBackoffType.Should().Be(LibDbOptions.RetryBackoffType.Linear);
        options.Value.Resilience.CircuitBreakerThreshold.Should().Be(7);
        options.Value.Resilience.CircuitBreakerSamplingDurationMs.Should().Be(1000);
        options.Value.Resilience.CircuitBreakerBreakDurationMs.Should().Be(2000);
        options.Value.Resilience.CircuitBreakerFailureRatio.Should().Be(0.25);
        options.Value.MaxCacheSize.Should().Be(2000);
        options.Value.SchemaSnapshotWarningThreshold.Should().Be(3000);
        options.Value.SharedMemoryCache.BasePath.Should().Be(sharedMemoryBasePath);
        options.Value.SharedMemoryCache.Scope.Should().Be(CacheScope.Machine);
        options.Value.SharedMemoryCache.MaxCacheSizeBytes.Should().Be(2L * 1024L * 1024L);
        options.Value.SharedMemoryCache.IsolationKey.Should().Be("binder-tenant");
        options.Value.EnableSharedMemoryCache.Should().BeTrue();
        options.Value.EnableEpochCoordination.Should().BeTrue();
        options.Value.EpochCheckIntervalSeconds.Should().Be(9);
        options.Value.Chaos.Enabled.Should().BeTrue();
        options.Value.Chaos.ExceptionRate.Should().Be(0.2);
        options.Value.Chaos.LatencyRate.Should().Be(0.3);
        options.Value.Chaos.MinLatencyMs.Should().Be(10);
        options.Value.Chaos.MaxLatencyMs.Should().Be(20);
        options.Value.HealthCheckThrottleSeconds.Should().Be(4);
        options.Value.EnableObservability.Should().BeTrue();
        options.Value.IncludeParametersInTrace.Should().BeTrue();
        options.Value.SchemaLockCleanupThreshold.Should().Be(1100);
        options.Value.SchemaLockCleanupIntervalMs.Should().Be(61000);
        options.Value.PrewarmMaxConcurrency.Should().Be(2);
    }

    [Fact]
    public void LibDbOptionsExtensions_ShouldClearExplicitlyEmptyListSections()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LibDb:WatchedInstances"] = string.Empty,
                ["LibDb:PrewarmIncludePatterns"] = string.Empty,
                ["LibDb:PrewarmExcludePatterns"] = string.Empty
            })
            .Build();

        ServiceCollection services = new();
        services.AddLibDbOptions(options =>
        {
            LibDbOptions valid = TestOptionsFactory.CreateValidOptions();
            options.ConnectionStringNames = valid.ConnectionStringNames;
            options.ConnectionStrings = valid.ConnectionStrings;
            options.WatchedInstances = ["legacy"];
            options.PrewarmIncludePatterns = ["legacy-include"];
            options.PrewarmExcludePatterns = ["legacy-exclude"];
        });
        services.AddLibDbOptionsFromConfiguration(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        LibDbOptions options = provider.GetRequiredService<IOptions<LibDbOptions>>().Value;

        options.WatchedInstances.Should().BeEmpty();
        options.PrewarmIncludePatterns.Should().BeEmpty();
        options.PrewarmExcludePatterns.Should().BeEmpty();
    }

    [Fact]
    public void LibDbOptionsExtensions_ShouldRejectBlankSharedMemoryCacheBasePath()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LibDb:SharedMemoryCache:BasePath"] = "   "
            })
            .Build();

        ServiceCollection services = new();
        services.AddLibDbOptions(options =>
        {
            LibDbOptions valid = TestOptionsFactory.CreateValidOptions();
            options.ConnectionStringNames = valid.ConnectionStringNames;
            options.ConnectionStrings = valid.ConnectionStrings;
        });
        services.AddLibDbOptionsFromConfiguration(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        Action act = () => _ = provider.GetRequiredService<IOptions<LibDbOptions>>().Value;

        act.Should().Throw<ArgumentException>()
            .WithMessage("*BasePath*");
    }

    [Fact]
    public void ArraySegmentEnumerable_ShouldEnumerateDirectAndInterfacePaths()
    {
        var enumerable = new ArraySegmentEnumerable<int>([1, 2, 3, 4], 3);
        List<int> direct = [];

        foreach (int item in enumerable)
            direct.Add(item);

        IEnumerator<int> generic = ((IEnumerable<int>)enumerable).GetEnumerator();
        IEnumerator nonGeneric = ((IEnumerable)enumerable).GetEnumerator();

        direct.Should().Equal(1, 2, 3);
        generic.MoveNext().Should().BeTrue();
        generic.Current.Should().Be(1);
        generic.Reset();
        generic.MoveNext().Should().BeTrue();
        Action readBeforeMoveNext = () => _ = nonGeneric.Current;
        readBeforeMoveNext.Should().Throw<IndexOutOfRangeException>();
        nonGeneric.MoveNext().Should().BeTrue();
        nonGeneric.Current.Should().Be(1);
        generic.Dispose();
        (nonGeneric as IDisposable)?.Dispose();
    }

    [Fact]
    public void StringPreprocessor_ShouldRemoveBracketsTrimWhitespaceAndCutAtNull()
    {
        string unchanged = "dbo.Table";

        StringPreprocessor.RemoveBrackets(null!).Should().BeNull();
        StringPreprocessor.RemoveBrackets("").Should().Be("");
        StringPreprocessor.RemoveBrackets(unchanged).Should().BeSameAs(unchanged);
        StringPreprocessor.RemoveBrackets("[dbo].[Table]").Should().Be("dbo.Table");
        StringPreprocessor.RemoveBrackets("[]").Should().Be("");

        StringPreprocessor.Sanitize((string?)null).ToString().Should().Be("");
        StringPreprocessor.Sanitize(" \u200B value \0 tail ").ToString().Should().Be("value");
        StringPreprocessor.Sanitize(" \u200B \u00A0 ").ToString().Should().Be("");
    }

    [Fact]
    public void DbObjectName_ShouldParseFormatAndRejectInvalidInputs()
    {
        DbObjectName<SpTrait> explicitName = DbObjectName<SpTrait>.Parse("sales.usp_Save");
        DbObjectName<TvpTrait> defaultSchema = DbObjectName<TvpTrait>.Parse("Tvp_User");

        explicitName.Schema.Should().Be("sales");
        explicitName.Name.Should().Be("usp_Save");
        explicitName.FullName.Should().Be("sales.usp_Save");
        explicitName.ToString(null, null).Should().Be("sales.usp_Save");
        ((string)explicitName).Should().Be("sales.usp_Save");
        defaultSchema.FullName.Should().Be("dbo.Tvp_User");
        ((DbObjectName<SpTrait>)"dbo.usp_Read").FullName.Should().Be("dbo.usp_Read");

        DbObjectName<SpTrait>.TryParse((string?)null, null, out _).Should().BeFalse();
        DbObjectName<SpTrait>.TryParse("dbo.", null, out _).Should().BeFalse();
        new Action(() => DbObjectName<SpTrait>.Parse("   ", null)).Should().Throw<ArgumentException>();
        new Action(() => DbObjectName<SpTrait>.Parse(".missing", null)).Should().Throw<ArgumentException>();
        new Action(() => _ = new DbObjectName<SpTrait>("", "x")).Should().Throw<ArgumentException>();
        new Action(() => _ = new DbObjectName<SpTrait>("dbo", "")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task JsonMappingExtensions_ShouldMapDictionaryRowsAndAsyncStreams()
    {
        var row = new Dictionary<string, object?>
        {
            ["Payload"] = "{\"id\":5,\"name\":\"json\"}",
            ["Empty"] = " "
        };

        row.MapJsonColumn<JsonPayload>("Payload").Should().Be(new JsonPayload(5, "json"));
        row.MapJsonColumn<JsonPayload>("Missing").Should().BeNull();
        row.MapJsonColumn<JsonPayload>("Empty").Should().BeNull();
        row.MapJsonColumn<JsonPayload>("Payload", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            .Should()
            .Be(new JsonPayload(5, "json"));

        List<(Dictionary<string, object?> Row, JsonPayload? Json)> mapped = [];
        await foreach ((Dictionary<string, object?> source, JsonPayload? json) in CreateJsonRows().WithJsonColumnAsync<JsonPayload>("Payload"))
            mapped.Add((source, json));

        mapped.Should().HaveCount(2);
        mapped[0].Json.Should().Be(new JsonPayload(6, "stream"));
        mapped[1].Json.Should().BeNull();
    }

    [Fact]
    public void ObjectDataReader_ShouldExposeBulkReaderShapeAndTypedValues()
    {
        BulkCoverageRow[] rows =
        [
            new(1, "one", null, true, (byte)2, 'A', new DateTime(2026, 5, 18), 1.25m, 2.5d, 3.5f, Guid.Parse("29c8ad48-3b64-4357-8d9d-c8300d3ac9d7"), (short)4, 5L)
        ];
        PropertyInfo[] props = typeof(BulkCoverageRow).GetProperties();
        using var reader = new ObjectDataReader<BulkCoverageRow>(((IEnumerable<BulkCoverageRow>)rows).GetEnumerator(), props);

        reader.FieldCount.Should().Be(props.Length);
        reader.Read().Should().BeTrue();
        reader.GetName(0).Should().Be(nameof(BulkCoverageRow.Id));
        reader.GetOrdinal("name").Should().Be(1);
        reader.Invoking(r => r.GetOrdinal("missing")).Should().Throw<IndexOutOfRangeException>();
        reader.GetValue(2).Should().Be(DBNull.Value);
        reader.IsDBNull(2).Should().BeTrue();
        reader[0].Should().Be(1);
        reader[nameof(BulkCoverageRow.Name)].Should().Be("one");
        reader.GetBoolean(3).Should().BeTrue();
        reader.GetByte(4).Should().Be(2);
        reader.GetChar(5).Should().Be('A');
        reader.GetDateTime(6).Should().Be(new DateTime(2026, 5, 18));
        reader.GetDecimal(7).Should().Be(1.25m);
        reader.GetDouble(8).Should().Be(2.5d);
        reader.GetFloat(9).Should().Be(3.5f);
        reader.GetGuid(10).Should().Be(Guid.Parse("29c8ad48-3b64-4357-8d9d-c8300d3ac9d7"));
        reader.GetInt16(11).Should().Be(4);
        reader.GetInt32(0).Should().Be(1);
        reader.GetInt64(12).Should().Be(5L);
        reader.GetString(1).Should().Be("one");
        reader.GetFieldType(0).Should().Be(typeof(int));
        reader.GetDataTypeName(0).Should().Be(nameof(Int32));
        reader.GetValues(new object[2]).Should().Be(2);
        reader.GetSchemaTable().Should().BeNull();
        reader.NextResult().Should().BeFalse();
        reader.Depth.Should().Be(0);
        reader.RecordsAffected.Should().Be(-1);
        reader.Invoking(r => r.GetBytes(0, 0, null, 0, 0)).Should().Throw<NotSupportedException>();
        reader.Invoking(r => r.GetChars(0, 0, null, 0, 0)).Should().Throw<NotSupportedException>();
        reader.Invoking(r => r.GetData(0)).Should().Throw<NotSupportedException>();

        reader.Close();
        reader.IsClosed.Should().BeTrue();
        reader.Read().Should().BeFalse();
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> CreateJsonRows()
    {
        await Task.Yield();
        yield return new Dictionary<string, object?> { ["Payload"] = "{\"id\":6,\"name\":\"stream\"}" };
        yield return new Dictionary<string, object?> { ["Payload"] = "" };
    }

    private sealed record JsonPayload(int Id, string Name);

    private sealed record BulkCoverageRow(
        int Id,
        string Name,
        string? Nullable,
        bool Flag,
        byte Tiny,
        char Letter,
        DateTime CreatedAt,
        decimal Amount,
        double Ratio,
        float Real,
        Guid TraceId,
        short Small,
        long Big);
}
