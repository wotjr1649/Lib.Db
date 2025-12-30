
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Core;
using Lib.Db.Schema;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lib.Db.Verification.Tests.Schema;

public class DbSchemaTests
{
    private readonly Mock<ISchemaRepository> _mockRepo;
    private readonly HybridCache _hybridCache;
    private readonly LibDbOptions _options;
    private readonly ILogger<SchemaService> _logger;

    public DbSchemaTests()
    {
        _mockRepo = new Mock<ISchemaRepository>();
        _options = new LibDbOptions { SchemaRefreshIntervalSeconds = 10 }; // Default 10s
        _logger = NullLogger<SchemaService>.Instance;

        // Setup Real HybridCache (In-Memory)
        var services = new ServiceCollection();
        services.AddHybridCache(options => 
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromMinutes(5),
                Expiration = TimeSpan.FromMinutes(5)
            };
        });
        var sp = services.BuildServiceProvider();
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
        // 1. Null Input
        using var service1 = CreateService(null);
        var hooks1 = GetFlushHooks(service1);
        Assert.NotNull(hooks1);
        Assert.Empty(hooks1);

        // 2. Array Input
        var hookArr = new SchemaFlushHook[] { new("Hook1", () => { }) };
        using var service2 = CreateService(hookArr);
        var hooks2 = GetFlushHooks(service2);
        Assert.Single(hooks2);
        Assert.Same(hookArr, hooks2); // Should check if it stores same reference if optimization is used, or equivalent

        // 3. List Input (Enumerable)
        var hookList = new List<SchemaFlushHook> { new("Hook2", () => { }) };
        using var service3 = CreateService(hookList);
        var hooks3 = GetFlushHooks(service3);
        Assert.Single(hooks3);
        Assert.IsType<SchemaFlushHook[]>(hooks3); // Should be converted to array
        Assert.Equal("Hook2", hooks3[0].Name);
    }

    private SchemaFlushHook[] GetFlushHooks(SchemaService service)
    {
        var field = typeof(SchemaService).GetField("_flushHooks", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (SchemaFlushHook[])field.GetValue(service)!;
    }

    #endregion

    #region DS-04: Negative Cache

    [Fact]
    public async Task DS_04_NegativeCache_ShouldCacheMiss_AndThrow()
    {
        // Arrange
        var missingTvp = "dbo.MissingTvp";
        var hash = "instance1";

        // Setup: Repo returns Version=0 (Missing)
        _mockRepo.Setup(r => r.GetTvpMetadataAsync(missingTvp, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TvpMetadata(0, []));

        using var service = CreateService();

        // Act 1: First Call -> Should call DB, Cache NullMarker, Throw InvalidOperationException
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.GetTvpSchemaAsync(missingTvp, hash, CancellationToken.None));
        
        Assert.Contains("[스키마 조회 실패]", ex1.Message);
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(missingTvp, hash, It.IsAny<CancellationToken>()), Times.Once);

        // Act 2: Second Call -> Should NOT call DB, Hit Negative Cache, Throw InvalidOperationException
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.GetTvpSchemaAsync(missingTvp, hash, CancellationToken.None));
        
        Assert.Contains("[Negative Cache]", ex2.Message);
        
        // Verify DB called only once
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(missingTvp, hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion


    #region DS-02: Warm Cache Hit

    [Fact]
    public async Task DS_02_WarmCache_Hit_ShouldNotCallDb()
    {
        // Arrange
        var tvpName = "dbo.MyTvp";
        var hash = "instance2";
        // Correct constructor usage for TvpMetadata record
        var mockMeta = new TvpMetadata(123, new List<TvpColumnInfo>());

        // Setup: Repo returns valid metadata
        _mockRepo.Setup(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(mockMeta);

        using var service = CreateService();

        // Act 1: Cold Call -> DB Call
        var schema1 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        Assert.Equal(123, schema1.VersionToken);
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Once);

        // Act 2: Warm Call -> No DB Call
        var schema2 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        Assert.Equal(123, schema2.VersionToken);
        
        // Assert: DB Called EXACTLY Once
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DS-03: Stale Refresh

    [Fact]
    public async Task DS_03_Stale_Refresh_ShouldTriggerRefresh_AndCallDb()
    {
        // Arrange
        var tvpName = "dbo.StaleTvp";
        var hash = "instance3";
        _options.SchemaRefreshIntervalSeconds = 1; // Minimum 1s allowed by Range attribute

        // Setup: Repo sequence
        // 1. Initial Load (Version 100)
        // 2. Refresh Check (GetTvpVersion -> 200) -> Updated
        // 3. Load New Schema (Version 200)
        _mockRepo.SetupSequence(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new TvpMetadata(100, [])) // Initial
                 .ReturnsAsync(new TvpMetadata(200, [])); // Refresh Load

        _mockRepo.Setup(r => r.GetTvpVersionAsync(tvpName, hash, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(200); // Version Check triggers update

        using var service = CreateService();

        // Act 1: Initial Load
        var schema1 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        Assert.Equal(100, schema1.VersionToken);

        // Wait for stale (> 1000ms base + jitter)
        // Jitter logic: Interval * (0.9 + 0.2 * Random)
        // Max Jitter = 1.0 * (0.9 + 0.2) = 1.1s
        // So waiting 1200ms should be safe to ensure staleness
        await Task.Delay(1500); 

        // Act 2: Stale Call -> Should Refresh
        var schema2 = await service.GetTvpSchemaAsync(tvpName, hash, CancellationToken.None);
        
        // Assert: Version Updated
        Assert.Equal(200, schema2.VersionToken);
        
        // Verify Calls:
        // Metadata: 2 times (Init + Refresh Load)
        // Version: 1 time (Refresh Check)
        _mockRepo.Verify(r => r.GetTvpMetadataAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockRepo.Verify(r => r.GetTvpVersionAsync(tvpName, hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

