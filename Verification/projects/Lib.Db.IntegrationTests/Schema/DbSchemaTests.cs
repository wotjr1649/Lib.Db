// ============================================================================
// 파일: Schema/DbSchemaTests.cs
// 설명: SchemaService 단위 테스트 (FlushHooks, WarmCache, NegativeCache, StaleRefresh)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;
using System.Reflection;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.Schema;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lib.Db.IntegrationTests.Schema;

public sealed class DbSchemaTests
{
    private readonly Mock<ISchemaRepository> _mockRepo;
    private readonly HybridCache _hybridCache;
    private readonly LibDbOptions _options;
    private readonly ILogger<SchemaService> _logger;

    public DbSchemaTests()
    {
        _mockRepo = new Mock<ISchemaRepository>();
        _options = new LibDbOptions { SchemaRefreshIntervalSeconds = 10 };
        _logger = NullLogger<SchemaService>.Instance;

        ServiceCollection services = new();
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromMinutes(5),
                Expiration = TimeSpan.FromMinutes(5)
            };
        });
        ServiceProvider sp = services.BuildServiceProvider();
        _hybridCache = sp.GetRequiredService<HybridCache>();
    }

    private SchemaService CreateService(IEnumerable<SchemaFlushHook>? hooks = null)
    {
        return new SchemaService(_hybridCache, _mockRepo.Object, _options, _logger, hooks);
    }

    #region DS-01: FlushHooks Initialization

    [Fact]
    public void DS_01_FlushHooks_Initialization_ShouldWork()
    {
        using SchemaService service1 = CreateService(null);
        SchemaFlushHook[] hooks1 = GetFlushHooks(service1);
        Assert.NotNull(hooks1);
        Assert.Empty(hooks1);

        SchemaFlushHook[] hookArr = [new("Hook1", () => { })];
        using SchemaService service2 = CreateService(hookArr);
        SchemaFlushHook[] hooks2 = GetFlushHooks(service2);
        Assert.Single(hooks2);
        Assert.Same(hookArr, hooks2);

        List<SchemaFlushHook> hookList = [new("Hook2", () => { })];
        using SchemaService service3 = CreateService(hookList);
        SchemaFlushHook[] hooks3 = GetFlushHooks(service3);
        Assert.Single(hooks3);
        Assert.IsType<SchemaFlushHook[]>(hooks3);
        Assert.Equal("Hook2", hooks3[0].Name);
    }

    private SchemaFlushHook[] GetFlushHooks(SchemaService service)
    {
        FieldInfo? field = typeof(SchemaService).GetField("_flushHooks", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (SchemaFlushHook[])field.GetValue(service)!;
    }

    #endregion

    #region DS-04: Negative Cache

    [Fact]
    public async Task DS_04_NegativeCache_ShouldCacheMiss_AndThrow()
    {
        string missingTvp = "dbo.MissingTvp";
        string hash = "instance1";

        _mockRepo.Setup(r => r.GetTvpMetadataAsync(missingTvp, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TvpMetadata(0, []));

        using SchemaService service = CreateService();

        InvalidOperationException ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetTvpSchemaAsync(missingTvp, hash, CancellationToken.None));

        Assert.Contains("[스키마 조회 실패]", ex1.Message);
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(missingTvp, hash, It.IsAny<CancellationToken>()), Times.Once);

        InvalidOperationException ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetTvpSchemaAsync(missingTvp, hash, CancellationToken.None));

        Assert.Contains("[Negative Cache]", ex2.Message);
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(missingTvp, hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DS_04_RawInstance_ShouldBeRedactedInSchemaMissMessages()
    {
        string missingTvp = "dbo.MissingRawTvp";
        string rawInstance = "Raw:InstanceMaterialForRedactionTest;Segment=Alpha;";

        _mockRepo.Setup(r => r.GetTvpMetadataAsync(missingTvp, rawInstance, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TvpMetadata(0, []));

        using SchemaService service = CreateService();

        InvalidOperationException ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetTvpSchemaAsync(missingTvp, rawInstance, CancellationToken.None));

        ex1.Message.Should().Contain("Raw:[redacted]");
        ex1.Message.Should().NotContain(rawInstance);
        ex1.Message.Should().NotContain("Segment=Alpha");

        InvalidOperationException ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetTvpSchemaAsync(missingTvp, rawInstance, CancellationToken.None));

        ex2.Message.Should().Contain("Raw:[redacted]");
        ex2.Message.Should().NotContain(rawInstance);
        ex2.Message.Should().NotContain("Segment=Alpha");
    }

    [Fact]
    public void DS_05_RawInstance_ShouldBeHashedInNegativeCacheKey()
    {
        string objectName = "dbo.MissingRawKeyTvp";
        string rawInstance = "Raw:InstanceMaterialForKeyOnly;Segment=Beta;";

        try
        {
            NegativeCache.Clear();
            NegativeCache.RecordMissing(rawInstance, objectName, "TvpType");

            FieldInfo field = typeof(NegativeCache)
                .GetField("s_missingObjects", BindingFlags.NonPublic | BindingFlags.Static)!;
            ConcurrentDictionary<string, InvalidOperationException> dictionary =
                (ConcurrentDictionary<string, InvalidOperationException>)field.GetValue(null)!;

            string key = dictionary.Keys.Single(k => k.Contains(objectName, StringComparison.Ordinal));

            key.Should().StartWith("raw-sha256-");
            key.Should().NotContain(rawInstance);
            key.Should().NotContain("Segment=Beta");
        }
        finally
        {
            NegativeCache.Clear();
        }
    }

    [Fact]
    public void DS_06_RawInstance_ShouldUseDeterministicSafeSchemaCacheIdentity()
    {
        string rawInstance = "Raw:InstanceMaterialForSnapshotKey;Segment=Gamma;";

        string first = SchemaCacheIdentity.ForCache(rawInstance);
        string second = SchemaCacheIdentity.ForCache(rawInstance);

        first.Should().Be(second);
        first.Should().StartWith("raw-sha256-");
        first.Should().NotContain(rawInstance);
        first.Should().NotContain("Segment=Gamma");
    }

    #endregion

    #region DS-02: Warm Cache Hit

    [Fact]
    public async Task DS_02_WarmCache_Hit_ShouldNotCallDb()
    {
        string tvpName = "dbo.MyTvp";
        string hash = "instance2";
        TvpMetadata mockMeta = new(123, new List<TvpColumnInfo>());

        _mockRepo.Setup(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(mockMeta);

        using SchemaService service = CreateService();

        TvpSchema schema1 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        Assert.Equal(123, schema1.VersionToken);
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Once);

        TvpSchema schema2 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        Assert.Equal(123, schema2.VersionToken);

        _mockRepo.Verify(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DS-03: Stale Refresh

    [Fact]
    public async Task DS_03_Stale_Refresh_ShouldTriggerRefresh_AndCallDb()
    {
        string tvpName = "dbo.StaleTvp";
        string hash = "instance3";
        _options.SchemaRefreshIntervalSeconds = 1;

        _mockRepo.SetupSequence(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TvpMetadata(100, []))
                 .ReturnsAsync(new TvpMetadata(200, []));

        _mockRepo.Setup(r => r.GetTvpVersionAsync(tvpName, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(200);

        using SchemaService service = CreateService();

        TvpSchema schema1 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        Assert.Equal(100, schema1.VersionToken);

        await Task.Delay(1500);

        TvpSchema schema2 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);

        Assert.Equal(200, schema2.VersionToken);

        _mockRepo.Verify(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockRepo.Verify(r => r.GetTvpVersionAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
