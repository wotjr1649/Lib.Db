// ============================================================================
// 파일: Unit/SchemaFlushServiceTests.cs
// 설명: SchemaFlushService cache key redaction 회귀 테스트
// ============================================================================

using System.Reflection;
using Lib.Db.Caching;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Extensions;
using Lib.Db.Schema;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SchemaFlushServiceTests
{
    [Fact]
    public async Task FlushAndCheck_ShouldStoreLastKnownEpochBySafeCacheIdentity()
    {
        string basePath = Path.Combine(
            Path.GetTempPath(),
            "LibDbEpochTests",
            Guid.NewGuid().ToString("N"));
        string rawInstance = "Raw:InstanceMaterialForFlushTest;Segment=Delta;";
        EpochStore epochStore = new(basePath, NullLogger<EpochStore>.Instance);
        RecordingSchemaService schemaService = new();
        SchemaFlushService flushService = new(
            epochStore,
            schemaService,
            NullLogger<SchemaFlushService>.Instance);

        try
        {
            await flushService.FlushAsync(rawInstance, TestContext.Current.CancellationToken);

            MemoryCache cache = GetLastKnownEpochs(flushService);
            cache.TryGetValue(rawInstance, out _).Should().BeFalse();
            cache.TryGetValue<long>(SchemaCacheIdentity.ForCache(rawInstance), out long epoch)
                .Should()
                .BeTrue();
            epoch.Should().Be(1);

            bool synced = await flushService.CheckAndSyncEpochAsync(
                rawInstance,
                TestContext.Current.CancellationToken);

            synced.Should().BeFalse();
            schemaService.FlushCalls.Should().Be(1);
            schemaService.LastFlushedInstance.Should().Be(rawInstance);
        }
        finally
        {
            flushService.Dispose();
            epochStore.Dispose();
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task AddSchemaFlushCoordination_ShouldAvoidEpochFilesystem_WhenEpochDisabled()
    {
        string basePath = Path.Combine(
            Path.GetTempPath(),
            "LibDbEpochDisabledTests",
            Guid.NewGuid().ToString("N"));
        RecordingSchemaService schemaService = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(new LibDbOptions
        {
            EnableSharedMemoryCache = false,
            EnableEpochCoordination = false
        });
        services.AddSingleton<ISchemaService>(schemaService);
        services.AddSchemaFlushCoordination(basePath);

        try
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            Directory.Exists(basePath).Should().BeFalse();

            EpochStore epochStore = provider.GetRequiredService<EpochStore>();
            epochStore.GetEpoch("DisabledInstance").Should().Be(0);
            epochStore.IncrementEpoch("DisabledInstance").Should().Be(0);
            Directory.Exists(basePath).Should().BeFalse();

            ISchemaFlushCoordinator coordinator = provider.GetRequiredService<ISchemaFlushCoordinator>();
            await coordinator.FlushAsync("DisabledInstance", TestContext.Current.CancellationToken);

            Directory.Exists(basePath).Should().BeFalse();
            schemaService.FlushCalls.Should().Be(1);
            schemaService.LastFlushedInstance.Should().Be("DisabledInstance");
        }
        finally
        {
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public async Task FlushTvpAsync_ShouldIncrementEpochAndCallTargetedSchemaFlush()
    {
        string basePath = Path.Combine(
            Path.GetTempPath(),
            "LibDbEpochTvpTests",
            Guid.NewGuid().ToString("N"));
        string rawInstance = "Raw:InstanceMaterialForTvpFlushTest;Segment=Delta;";
        EpochStore epochStore = new(basePath, NullLogger<EpochStore>.Instance);
        RecordingSchemaService schemaService = new();
        SchemaFlushService flushService = new(
            epochStore,
            schemaService,
            NullLogger<SchemaFlushService>.Instance);

        try
        {
            await flushService.FlushTvpAsync(
                rawInstance,
                "tvp.Tvp_Tvp_AllTypes",
                TestContext.Current.CancellationToken);

            MemoryCache cache = GetLastKnownEpochs(flushService);
            cache.TryGetValue(rawInstance, out _).Should().BeFalse();
            cache.TryGetValue<long>(SchemaCacheIdentity.ForCache(rawInstance), out long epoch)
                .Should()
                .BeTrue();
            epoch.Should().Be(1);

            schemaService.FlushCalls.Should().Be(0);
            schemaService.TvpFlushCalls.Should().Be(1);
            schemaService.LastFlushedInstance.Should().Be(rawInstance);
            schemaService.LastFlushedTvp.Should().Be("tvp.Tvp_Tvp_AllTypes");
        }
        finally
        {
            flushService.Dispose();
            epochStore.Dispose();
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, recursive: true);
        }
    }

    private static MemoryCache GetLastKnownEpochs(SchemaFlushService service)
    {
        FieldInfo field = typeof(SchemaFlushService).GetField(
            "_lastKnownEpochs",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (MemoryCache)field.GetValue(service)!;
    }

    private sealed class RecordingSchemaService : ISchemaService
    {
        public int FlushCalls { get; private set; }

        public int TvpFlushCalls { get; private set; }

        public string? LastFlushedInstance { get; private set; }

        public string? LastFlushedTvp { get; private set; }

        public Task<PreloadResult> PreloadSchemaAsync(
            IEnumerable<string> schemaNames,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SpSchema> GetSpSchemaAsync(
            string spName,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<TvpSchema> GetTvpSchemaAsync(
            string tvpName,
            string instanceHash,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task FlushSchemaAsync(string instanceHash, CancellationToken ct)
        {
            FlushCalls++;
            LastFlushedInstance = instanceHash;
            return Task.CompletedTask;
        }

        public Task FlushTvpAsync(string tvpName, string instanceHash, CancellationToken ct)
        {
            TvpFlushCalls++;
            LastFlushedInstance = instanceHash;
            LastFlushedTvp = tvpName;
            return Task.CompletedTask;
        }

        public void InvalidateSpSchema(string spName, string instanceHash) =>
            throw new NotSupportedException();

        public void InvalidateTvpSchema(string tvpName, string instanceHash) =>
            throw new NotSupportedException();
    }
}
