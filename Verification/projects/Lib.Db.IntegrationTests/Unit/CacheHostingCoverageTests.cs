// ============================================================================
// 파일: Unit/CacheHostingCoverageTests.cs
// 설명: 캐시/호스팅 인프라 기본 동작 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Text;
using Lib.Db.Caching;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Lib.Db.Extensions;
using Lib.Db.Hosting;
using Lib.Db.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class CacheHostingCoverageTests
{
    [Fact]
    public void CacheMaintenanceService_ShouldValidateConstructorArguments()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<CacheMaintenanceService> logger = loggerFactory.CreateLogger<CacheMaintenanceService>();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        Action nullProvider = () => new CacheMaintenanceService(null!, logger, TimeSpan.FromMilliseconds(1));
        Action nullLogger = () => new CacheMaintenanceService(provider, null!, TimeSpan.FromMilliseconds(1));
        Action invalidInterval = () => new CacheMaintenanceService(provider, logger, TimeSpan.Zero);

        nullProvider.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
        nullLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        invalidInterval.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("checkInterval");

        using var service = new CacheMaintenanceService(provider, logger);
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task CacheMaintenanceService_ShouldSkipWhenDistributedCacheIsNotSharedMemory()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<CacheMaintenanceService> logger = loggerFactory.CreateLogger<CacheMaintenanceService>();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var service = new CacheMaintenanceService(provider, logger, TimeSpan.FromMilliseconds(1));

        await service.PerformMaintenanceCycleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CacheMaintenanceService_ShouldCompactSharedMemoryCache()
    {
        string basePath = CreateTempDirectory();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var cache = new SharedMemoryCache(
            Options.Create(new SharedMemoryCacheOptions { BasePath = basePath }),
            loggerFactory.CreateLogger<SharedMemoryCache>());
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IDistributedCache>(cache)
            .BuildServiceProvider();
        var service = new CacheMaintenanceService(
            provider,
            loggerFactory.CreateLogger<CacheMaintenanceService>(),
            TimeSpan.FromMilliseconds(1));

        await service.PerformMaintenanceCycleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void CacheInternalHelpers_ShouldHashConfiguredIsolationKeyForMutexPrefix()
    {
        const string secretIsolationKey =
            "Raw:Server=prod;Database=TenantA;User Id=sa;Password=cache-secret";
        var options = new SharedMemoryCacheOptions
        {
            Scope = CacheScope.Machine,
            IsolationKey = secretIsolationKey
        };

        string prefix = CacheInternalHelpers.GetMutexPrefix(options);

        prefix.Should().StartWith("Global\\Lib.Db.Cache_");
        prefix.Should().NotContain(secretIsolationKey);
        prefix.Should().NotContain("Password=");
        prefix.Should().NotContain("TenantA");
        prefix.Should().Contain(CacheInternalHelpers.BuildSafeIsolationKey(secretIsolationKey));
    }

    [Fact]
    public void SharedMemoryCache_ShouldProtectPayloadBytesOnDiskAndReadAcrossInstances()
    {
        string basePath = CreateTempDirectory();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var options = new SharedMemoryCacheOptions
        {
            BasePath = basePath,
            IsolationKey = "payload-protection-test"
        };
        byte[] payload = Encoding.UTF8.GetBytes("libdb-cache-plaintext-marker");

        using (var writer = new SharedMemoryCache(
            Options.Create(options),
            loggerFactory.CreateLogger<SharedMemoryCache>()))
        {
            writer.Set(
                "protected-key",
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
        }

        string cacheFile = Directory.EnumerateFiles(basePath, "*.cache", SearchOption.AllDirectories)
            .Should().ContainSingle()
            .Subject;
        byte[] persisted = File.ReadAllBytes(cacheFile);
        persisted.AsSpan().IndexOf(payload).Should().Be(-1);

        using var reader = new SharedMemoryCache(
            Options.Create(options),
            loggerFactory.CreateLogger<SharedMemoryCache>());
        reader.Get("protected-key").Should().Equal(payload);
    }

    [Fact]
    public async Task CacheMaintenanceService_ShouldKeepRunningWhenMaintenanceCycleThrows()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var service = new CacheMaintenanceService(
            new ThrowingScopeProvider(),
            loggerFactory.CreateLogger<CacheMaintenanceService>(),
            TimeSpan.FromMilliseconds(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CacheMaintenanceService_ShouldRunSuccessfulHostedCycle()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var service = new CacheMaintenanceService(
            provider,
            loggerFactory.CreateLogger<CacheMaintenanceService>(),
            TimeSpan.FromMilliseconds(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CacheMaintenanceService_ExecuteAsync_ShouldStopWhenTickSourceReturnsFalse()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var ticks = new ScriptedTickSource(false);
        var service = new CacheMaintenanceService(
            provider,
            loggerFactory.CreateLogger<CacheMaintenanceService>(),
            TimeSpan.FromMilliseconds(1),
            () => ticks);

        await ExecuteCacheMaintenanceAsync(service, TestContext.Current.CancellationToken);

        ticks.WaitCalls.Should().Be(1);
        ticks.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CacheMaintenanceService_ExecuteAsync_ShouldContinueAfterCycleExceptionThenStop()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var ticks = new ScriptedTickSource(true, false);
        var service = new CacheMaintenanceService(
            new ThrowingScopeProvider(),
            loggerFactory.CreateLogger<CacheMaintenanceService>(),
            TimeSpan.FromMilliseconds(1),
            () => ticks);

        await ExecuteCacheMaintenanceAsync(service, TestContext.Current.CancellationToken);

        ticks.WaitCalls.Should().Be(2);
        ticks.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CacheMaintenanceService_ExecuteAsync_ShouldTreatCancellationAsShutdown()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var ticks = new ScriptedTickSource(true);
        var service = new CacheMaintenanceService(
            provider,
            loggerFactory.CreateLogger<CacheMaintenanceService>(),
            TimeSpan.FromMilliseconds(1),
            () => ticks);

        await ExecuteCacheMaintenanceAsync(service, cts.Token);

        ticks.WaitCalls.Should().Be(0);
        ticks.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void GlobalCacheEpoch_ShouldStartAtZeroAndIncrementAcrossInstances()
    {
        string basePath = CreateTempDirectory();
        using var first = new GlobalCacheEpoch(basePath);

        first.Current.Should().Be(0);
        first.Increment().Should().Be(1);
        first.Increment().Should().Be(2);

        using var second = new GlobalCacheEpoch(Options.Create(new SharedMemoryCacheOptions { BasePath = basePath }));
        second.Current.Should().Be(2);
        second.Increment().Should().Be(3);
    }

    [Fact]
    public void GlobalCacheEpoch_ShouldRecoverAbandonedMutex()
    {
        string basePath = CreateTempDirectory();
        var options = new SharedMemoryCacheOptions
        {
            BasePath = basePath,
            IsolationKey = "epoch-abandoned-" + Guid.NewGuid().ToString("N")
        };
        string mutexName = CacheInternalHelpers.GetMutexPrefix(options) + "EpochMutex";
        AbandonMutex(static name => new Mutex(false, name), mutexName);

        using var epoch = new GlobalCacheEpoch(Options.Create(options));

        epoch.Increment().Should().Be(1);
    }

    [Fact]
    public void PassiveProcessSlotAllocator_ShouldAlwaysExposeNoSlot()
    {
        var allocator = new PassiveProcessSlotAllocator();

        allocator.SlotId.Should().Be(-1);
        allocator.IsLeader.Should().BeFalse();
        allocator.HasSlot.Should().BeFalse();
    }

    [Fact]
    public void CacheTopologyDetector_ShouldReportMissingDistributedCacheAsLocalOnly()
    {
        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache: null);

        state.Kind.Should().Be(LibDbCacheTopologyKind.LocalOnly);
        state.HasVerifiedProviderBackedL2.Should().BeFalse();
        state.ProviderTypeName.Should().BeNull();
    }

    [Fact]
    public void CacheTopologyDetector_ShouldReportMemoryDistributedCacheAsLocalMemory()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache);

        state.Kind.Should().Be(LibDbCacheTopologyKind.LocalMemoryDistributedCache);
        state.HasVerifiedProviderBackedL2.Should().BeFalse();
        state.ProviderTypeName.Should().Contain(nameof(MemoryDistributedCache));
    }

    [Fact]
    public void CacheTopologyDetector_ShouldReportSharedMemoryCacheAsSharedMemoryOptIn()
    {
        string basePath = CreateTempDirectory();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        using var cache = new SharedMemoryCache(
            Options.Create(new SharedMemoryCacheOptions { BasePath = basePath }),
            loggerFactory.CreateLogger<SharedMemoryCache>());

        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache);

        state.Kind.Should().Be(LibDbCacheTopologyKind.SharedMemoryOptIn);
        state.HasVerifiedProviderBackedL2.Should().BeFalse();
        state.ProviderTypeName.Should().Contain(nameof(SharedMemoryCache));
    }

    [Fact]
    public void CacheTopologyDetector_ShouldReportUnknownDistributedCacheAsUnverified()
    {
        var cache = new RecordingDistributedCache();

        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache);

        state.Kind.Should().Be(LibDbCacheTopologyKind.UnverifiedDistributedCache);
        state.HasVerifiedProviderBackedL2.Should().BeFalse();
        state.ProviderTypeName.Should().Contain(nameof(RecordingDistributedCache));
    }

    [Fact]
    public void CacheTopologyDetector_ShouldReportTrustedCustomProviderAsVerifiedL2()
    {
        var cache = new RecordingDistributedCache();
        LibDbCacheTopologyOptions options = new();
        options.TrustedProviderTypeNames.Add(cache.GetType().FullName!);

        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache, options);

        state.Kind.Should().Be(LibDbCacheTopologyKind.VerifiedProviderBackedL2);
        state.HasVerifiedProviderBackedL2.Should().BeTrue();
        state.ProviderTypeName.Should().Contain(nameof(RecordingDistributedCache));
    }

    [Fact]
    public void CacheTopologyDetector_ShouldUseTrustedProviderOptionsFromServiceProvider()
    {
        var cache = new RecordingDistributedCache();
        LibDbCacheTopologyOptions options = new();
        options.TrustedProviderTypeNames.Add(cache.GetType().FullName!);
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IDistributedCache>(cache)
            .AddSingleton(options)
            .BuildServiceProvider();

        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(provider);

        state.Kind.Should().Be(LibDbCacheTopologyKind.VerifiedProviderBackedL2);
        state.HasVerifiedProviderBackedL2.Should().BeTrue();
    }

    [Fact]
    public void AddLibDbTrustedDistributedCacheProvider_ShouldMarkCustomProviderAsVerifiedL2()
    {
        var cache = new RecordingDistributedCache();
        using ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IDistributedCache>(cache)
            .AddLibDbTrustedDistributedCacheProvider<RecordingDistributedCache>()
            .BuildServiceProvider();

        LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(provider);

        state.Kind.Should().Be(LibDbCacheTopologyKind.VerifiedProviderBackedL2);
        state.HasVerifiedProviderBackedL2.Should().BeTrue();
        state.ProviderTypeName.Should().Contain(nameof(RecordingDistributedCache));
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldInvalidateOnlyExistingTaggedEntries()
    {
        var cache = new LibDbAotHybridCache();
        int factoryCalls = 0;

        ValueTask<string> Factory(string value, CancellationToken _)
        {
            factoryCalls++;
            return ValueTask.FromResult(value);
        }

        string first = await cache.GetOrCreateAsync(
            "schema",
            "first",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);
        string firstHit = await cache.GetOrCreateAsync(
            "schema",
            "unexpected",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);

        await cache.RemoveByTagAsync("instance", TestContext.Current.CancellationToken);

        string second = await cache.GetOrCreateAsync(
            "schema",
            "second",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);
        string secondHit = await cache.GetOrCreateAsync(
            "schema",
            "third",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);

        first.Should().Be("first");
        firstHit.Should().Be("first");
        second.Should().Be("second");
        secondHit.Should().Be("second");
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldCoalesceConcurrentMissesForSameKey()
    {
        var cache = new LibDbAotHybridCache();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        TaskCompletionSource firstFactoryStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFactory = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;

        async ValueTask<string> Factory(string value, CancellationToken token)
        {
            int call = Interlocked.Increment(ref factoryCalls);
            if (call == 1)
            {
                firstFactoryStarted.SetResult();
            }

            await releaseFactory.Task.WaitAsync(token);
            return value;
        }

        Task<string>[] requests = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () => await cache.GetOrCreateAsync(
                "schema",
                "value",
                Factory,
                tags: ["instance"],
                cancellationToken: cts.Token), cts.Token))
            .ToArray();

        await firstFactoryStarted.Task.WaitAsync(cts.Token);
        await Task.Delay(50, cts.Token);
        releaseFactory.SetResult();
        string[] results = await Task.WhenAll(requests);

        results.Should().OnlyContain(result => result == "value");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldNotCancelSharedProducerWhenOriginalCallerCancels()
    {
        var cache = new LibDbAotHybridCache();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using CancellationTokenSource firstCaller = new();
        TaskCompletionSource factoryStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFactory = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;

        async ValueTask<string> Factory(string value, CancellationToken token)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.SetResult();
            await releaseFactory.Task.WaitAsync(token);
            return value;
        }

        Task<string> first = cache.GetOrCreateAsync(
            "schema",
            "value",
            Factory,
            tags: ["instance"],
            cancellationToken: firstCaller.Token).AsTask();

        await factoryStarted.Task.WaitAsync(timeout.Token);

        Task<string> second = cache.GetOrCreateAsync(
            "schema",
            "value",
            Factory,
            tags: ["instance"],
            cancellationToken: timeout.Token).AsTask();

        await firstCaller.CancelAsync();
        releaseFactory.SetResult();

        await first.Awaiting(task => task)
            .Should()
            .ThrowAsync<OperationCanceledException>();
        string result = await second;

        result.Should().Be("value");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldCancelSharedProducerWhenAllCallersCancel()
    {
        var cache = new LibDbAotHybridCache();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using CancellationTokenSource firstCaller = new();
        using CancellationTokenSource secondCaller = new();
        TaskCompletionSource factoryStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource producerCancelled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<string> Factory(string value, CancellationToken token)
        {
            factoryStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return value;
            }
            catch (OperationCanceledException)
            {
                producerCancelled.SetResult();
                throw;
            }
        }

        Task<string> first = cache.GetOrCreateAsync(
            "schema",
            "value",
            Factory,
            tags: ["instance"],
            cancellationToken: firstCaller.Token).AsTask();

        await factoryStarted.Task.WaitAsync(timeout.Token);

        Task<string> second = cache.GetOrCreateAsync(
            "schema",
            "value",
            Factory,
            tags: ["instance"],
            cancellationToken: secondCaller.Token).AsTask();

        await firstCaller.CancelAsync();
        first.IsCanceled.Should().BeTrue();
        producerCancelled.Task.IsCompleted.Should().BeFalse();

        await secondCaller.CancelAsync();
        await producerCancelled.Task.WaitAsync(timeout.Token);

        second.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldTreatWildcardInvalidationAsGenerationBoundary()
    {
        var cache = new LibDbAotHybridCache();
        int factoryCalls = 0;

        ValueTask<string> Factory(string value, CancellationToken _)
        {
            factoryCalls++;
            return ValueTask.FromResult(value);
        }

        await cache.SetAsync("schema", "before", tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);
        await cache.RemoveByTagAsync("*", TestContext.Current.CancellationToken);

        string after = await cache.GetOrCreateAsync(
            "schema",
            "after",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);
        string afterHit = await cache.GetOrCreateAsync(
            "schema",
            "miss",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);

        after.Should().Be("after");
        afterHit.Should().Be("after");
        factoryCalls.Should().Be(1);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldNotAbsorbInvalidationDuringFactory()
    {
        var cache = new LibDbAotHybridCache();
        int factoryCalls = 0;

        ValueTask<string> Factory(string value, CancellationToken _)
        {
            factoryCalls++;
            cache.RemoveByTagAsync("instance", TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return ValueTask.FromResult(value);
        }

        string first = await cache.GetOrCreateAsync(
            "schema",
            "first",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);
        string second = await cache.GetOrCreateAsync(
            "schema",
            "second",
            Factory,
            tags: ["instance"], cancellationToken: TestContext.Current.CancellationToken);

        first.Should().Be("first");
        second.Should().Be("second");
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldUseConfiguredDefaultExpiration()
    {
        var cache = new LibDbAotHybridCache(new HybridCacheOptions
        {
            DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMilliseconds(1)
            }
        });
        int factoryCalls = 0;

        ValueTask<string> Factory(string value, CancellationToken _)
        {
            factoryCalls++;
            return ValueTask.FromResult(value);
        }

        string first = await cache.GetOrCreateAsync("schema", "first", Factory, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        string second = await cache.GetOrCreateAsync("schema", "second", Factory, cancellationToken: TestContext.Current.CancellationToken);

        first.Should().Be("first");
        second.Should().Be("second");
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldNotStoreOversizedKeysOrPayloads()
    {
        var cache = new LibDbAotHybridCache(new HybridCacheOptions
        {
            MaximumKeyLength = 4,
            MaximumPayloadBytes = 3
        });
        int longKeyCalls = 0;
        int largePayloadCalls = 0;

        ValueTask<string> LongKeyFactory(string value, CancellationToken _)
        {
            longKeyCalls++;
            return ValueTask.FromResult(value);
        }

        ValueTask<string> LargePayloadFactory(string value, CancellationToken _)
        {
            largePayloadCalls++;
            return ValueTask.FromResult(value);
        }

        string longKeyFirst = await cache.GetOrCreateAsync("schema", "first", LongKeyFactory, cancellationToken: TestContext.Current.CancellationToken);
        string longKeySecond = await cache.GetOrCreateAsync("schema", "second", LongKeyFactory, cancellationToken: TestContext.Current.CancellationToken);
        string payloadFirst = await cache.GetOrCreateAsync("key", "large", LargePayloadFactory, cancellationToken: TestContext.Current.CancellationToken);
        string payloadSecond = await cache.GetOrCreateAsync("key", "tiny", LargePayloadFactory, cancellationToken: TestContext.Current.CancellationToken);

        longKeyFirst.Should().Be("first");
        longKeySecond.Should().Be("second");
        payloadFirst.Should().Be("large");
        payloadSecond.Should().Be("tiny");
        longKeyCalls.Should().Be(2);
        largePayloadCalls.Should().Be(2);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldNotStoreUnsupportedReferencePayloads()
    {
        var cache = new LibDbAotHybridCache();
        int factoryCalls = 0;

        ValueTask<object> Factory(int value, CancellationToken _)
        {
            factoryCalls++;
            return ValueTask.FromResult<object>(new object());
        }

        object first = await cache.GetOrCreateAsync("schema", 1, Factory, cancellationToken: TestContext.Current.CancellationToken);
        object second = await cache.GetOrCreateAsync("schema", 2, Factory, cancellationToken: TestContext.Current.CancellationToken);

        first.Should().NotBeSameAs(second);
        factoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task LibDbAotHybridCache_ShouldApplyPayloadLimitToSchemaModels()
    {
        var cache = new LibDbAotHybridCache(new HybridCacheOptions
        {
            MaximumPayloadBytes = 96
        });
        int spCalls = 0;
        int tvpCalls = 0;

        ValueTask<SpSchema> SpFactory(int _, CancellationToken __)
        {
            spCalls++;
            return ValueTask.FromResult(new SpSchema
            {
                Name = "dbo.usp_LargeSchema",
                VersionToken = 1,
                Parameters =
                [
                    new("@Alpha", null, 0, SqlDbType.Int, ParameterDirection.Input, 0, 0, false, false),
                    new("@Beta", "dbo.T_Beta", 0, SqlDbType.Structured, ParameterDirection.Input, 0, 0, false, false)
                ]
            });
        }

        ValueTask<TvpSchema> TvpFactory(int _, CancellationToken __)
        {
            tvpCalls++;
            return ValueTask.FromResult(new TvpSchema
            {
                Name = "dbo.T_LargeSchema",
                VersionToken = 1,
                Columns =
                [
                    new("Alpha", 1, 4, 0, SqlDbType.Int, 0, 0, false, false, false),
                    new("Beta", 2, 64, 1, SqlDbType.NVarChar, 0, 0, false, false, false)
                ]
            });
        }

        SpSchema spFirst = await cache.GetOrCreateAsync("sp", 0, SpFactory, cancellationToken: TestContext.Current.CancellationToken);
        SpSchema spSecond = await cache.GetOrCreateAsync("sp", 0, SpFactory, cancellationToken: TestContext.Current.CancellationToken);
        TvpSchema tvpFirst = await cache.GetOrCreateAsync("tvp", 0, TvpFactory, cancellationToken: TestContext.Current.CancellationToken);
        TvpSchema tvpSecond = await cache.GetOrCreateAsync("tvp", 0, TvpFactory, cancellationToken: TestContext.Current.CancellationToken);

        spFirst.Should().NotBeSameAs(spSecond);
        tvpFirst.Should().NotBeSameAs(tvpSecond);
        spCalls.Should().Be(2);
        tvpCalls.Should().Be(2);
    }

    [Fact]
    public void AddLibDbHybridCache_ShouldApplyConfigureCallbackWhenDynamicCodeIsDisabled()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        var services = new ServiceCollection();

        services.AddLibDbHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(12)
            };
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        HybridCacheOptions options = provider.GetRequiredService<IOptions<HybridCacheOptions>>().Value;
        provider.GetRequiredService<HybridCache>().Should().BeOfType<LibDbAotHybridCache>();
        options.DefaultEntryOptions?.Expiration.Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void RegisterLibDbCoreServices_ShouldUseAotHybridCacheWhenDynamicCodeIsDisabled()
    {
        using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
        var services = new ServiceCollection();

        services.RegisterLibDbCoreServices();

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<HybridCache>().Should().BeOfType<LibDbAotHybridCache>();
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldValidateInternalConstructorArguments()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();

        Action nullFactory = () => new ProcessSlotAllocator("invalid", logger, null!, maxSlots: 1);
        Action invalidSlots = () => new ProcessSlotAllocator("invalid", logger, static (_, _) => new Mutex(), maxSlots: 0);

        nullFactory.Should().Throw<ArgumentNullException>().WithParameterName("mutexFactory");
        invalidSlots.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxSlots");
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldAcquireLeaderSlotForUniqueIsolationKeyAndReleaseOnDispose()
    {
        string isolationKey = "coverage-" + Guid.NewGuid().ToString("N");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();

        using (var allocator = new ProcessSlotAllocator(isolationKey, logger))
        {
            allocator.HasSlot.Should().BeTrue();
            allocator.SlotId.Should().Be(0);
            allocator.IsLeader.Should().BeTrue();
        }

        using var reacquired = new ProcessSlotAllocator(isolationKey, logger);
        reacquired.HasSlot.Should().BeTrue();
        reacquired.SlotId.Should().Be(0);
        reacquired.IsLeader.Should().BeTrue();
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldRecoverAbandonedSlot()
    {
        string isolationKey = "abandoned-" + Guid.NewGuid().ToString("N");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();
        AbandonMutex(
            name => MutexHelper.CreateProcessMutex(name, logger),
            ProcessSlotAllocator.BuildMutexLogicalName(isolationKey, 0));

        using var allocator = new ProcessSlotAllocator(isolationKey, logger);

        allocator.HasSlot.Should().BeTrue();
        allocator.SlotId.Should().BeGreaterThanOrEqualTo(0);
        allocator.IsLeader.Should().Be(allocator.SlotId == 0);
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldEnterPassiveModeWhenConfiguredSlotsAreHeld()
    {
        string isolationKey = "passive-" + Guid.NewGuid().ToString("N");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();
        using ManualResetEventSlim release = new();
        using CountdownEvent ready = new(2);
        List<Thread> holders = StartSlotHolderThreads(isolationKey, logger, slotCount: 2, ready, release);

        try
        {
            ready.Wait(TestContext.Current.CancellationToken);

            using var allocator = new ProcessSlotAllocator(
                isolationKey,
                logger,
                static (name, log) => MutexHelper.CreateProcessMutex(name, log),
                maxSlots: 2);

            allocator.HasSlot.Should().BeFalse();
            allocator.SlotId.Should().Be(-1);
            allocator.IsLeader.Should().BeFalse();
        }
        finally
        {
            release.Set();
            foreach (Thread holder in holders)
                holder.Join();
        }
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldDisposeFailedMutexAndStayPassive()
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();

        using var allocator = new ProcessSlotAllocator(
            "disposed-" + Guid.NewGuid().ToString("N"),
            logger,
            static (_, _) =>
            {
                var mutex = new Mutex();
                mutex.Dispose();
                return mutex;
            },
            maxSlots: 1);

        allocator.HasSlot.Should().BeFalse();
    }

    [Fact]
    public void ProcessSlotAllocator_DisposeShouldSwallowReleaseFailures()
    {
        string isolationKey = "dispose-twice-" + Guid.NewGuid().ToString("N");
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();
        var allocator = new ProcessSlotAllocator(isolationKey, logger);

        allocator.Dispose();
        Action secondDispose = allocator.Dispose;

        secondDispose.Should().NotThrow();
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "LibDbCoverage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AbandonMutex(Func<string, Mutex> createMutex, string name)
    {
        Exception? threadError = null;
        using ManualResetEventSlim acquired = new();
        Thread thread = new(() =>
        {
            try
            {
                Mutex mutex = createMutex(name);
                mutex.WaitOne();
                acquired.Set();
            }
            catch (Exception ex)
            {
                threadError = ex;
                acquired.Set();
            }
        });

        thread.Start();
        acquired.Wait(TestContext.Current.CancellationToken);
        thread.Join();
        threadError.Should().BeNull();
    }

    private static List<Thread> StartSlotHolderThreads(
        string isolationKey,
        ILogger<ProcessSlotAllocator> logger,
        int slotCount,
        CountdownEvent ready,
        ManualResetEventSlim release)
    {
        List<Thread> threads = new(slotCount);

        for (int i = 0; i < slotCount; i++)
        {
            int slot = i;
            Thread thread = new(() =>
            {
                Mutex mutex = MutexHelper.CreateProcessMutex(
                    ProcessSlotAllocator.BuildMutexLogicalName(isolationKey, slot),
                    logger);
                try
                {
                    mutex.WaitOne();
                    ready.Signal();
                    release.Wait();
                    mutex.ReleaseMutex();
                }
                finally
                {
                    mutex.Dispose();
                }
            });
            thread.Start();
            threads.Add(thread);
        }

        return threads;
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldNotPassRawIsolationKeyToMutexFactory()
    {
        const string secretIsolationKey =
            "Raw:Server=prod;Database=TenantA;User Id=sa;Password=slot-secret";
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        ILogger<ProcessSlotAllocator> logger = loggerFactory.CreateLogger<ProcessSlotAllocator>();
        List<string> observedNames = [];

        using var allocator = new ProcessSlotAllocator(
            secretIsolationKey,
            logger,
            (name, _) =>
            {
                observedNames.Add(name);
                return new Mutex();
            },
            maxSlots: 1);

        observedNames.Should().ContainSingle();
        observedNames[0].Should().NotContain(secretIsolationKey);
        observedNames[0].Should().NotContain("Password=");
        observedNames[0].Should().Contain(ProcessSlotAllocator.BuildMutexLogicalName(secretIsolationKey, 0));
        allocator.HasSlot.Should().BeTrue();
    }

    private static async Task ExecuteCacheMaintenanceAsync(
        CacheMaintenanceService service,
        CancellationToken cancellationToken)
    {
        MethodInfo method = typeof(CacheMaintenanceService).GetMethod(
            "ExecuteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (Task)method.Invoke(service, [cancellationToken])!;
        await task;
    }

    private sealed class ScriptedTickSource(params bool[] ticks) : ICacheMaintenanceTickSource
    {
        private readonly Queue<bool> _ticks = new(ticks);

        public int WaitCalls { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask<bool> WaitForNextTickAsync(CancellationToken stoppingToken)
        {
            stoppingToken.ThrowIfCancellationRequested();
            WaitCalls++;
            return ValueTask.FromResult(_ticks.Count > 0 && _ticks.Dequeue());
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingScopeProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IServiceScopeFactory)
                ? new ThrowingScopeFactory()
                : null;
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("scope failure");
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Get(string key)
        {
            return _values.TryGetValue(key, out byte[]? value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _values[key] = value;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _values.Remove(key);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }
}
