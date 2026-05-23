// ============================================================================
// 파일: Schema/MultiSchemaPreloadTests.cs
// 설명: 다중 스키마 프리로드 통합 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.IntegrationTests.Infrastructure;
using Lib.Db.Schema;
using Microsoft.Extensions.Caching.Hybrid;

namespace Lib.Db.IntegrationTests.Schema;

[Collection("MultiDb")]
public sealed class MultiSchemaPreloadTests
{
    private readonly MultiDbFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MultiSchemaPreloadTests(MultiDbFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "SchemaWarmup")]
    public async Task PreloadSchemaAsync_ShouldLoadMultipleSchemas()
    {
        string dboSp = "dbo.usp_WarmupRec_Dbo";
        string coreSp = "core.usp_WarmupRec_Core";

        try
        {
            await _fixture.Verification.Sql($@"
                CREATE OR ALTER PROCEDURE {dboSp} AS SELECT 1;
            ").ExecuteAsync(TestContext.Current.CancellationToken);

            await _fixture.Verification.Sql($@"
                CREATE OR ALTER PROCEDURE {coreSp} AS SELECT 1;
            ").ExecuteAsync(TestContext.Current.CancellationToken);

            using IServiceScope scope = _fixture.Services.CreateScope();
            ISchemaService schemaService = scope.ServiceProvider.GetRequiredService<ISchemaService>();
            LibDbOptions options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LibDbOptions>>().Value;

            string instanceHash = options.ConnectionStringNames.Count > 0 ? options.ConnectionStringNames[0] : "Default";

            IReadOnlyList<string> schemasToLoad = options.PrewarmSchemas;

            _output.WriteLine("----------- Schema Prewarm Configuration Check -----------");
            _output.WriteLine($"Configured Schemas Count: {schemasToLoad?.Count ?? 0}");
            if (schemasToLoad != null)
            {
                foreach (string s in schemasToLoad)
                {
                    _output.WriteLine($"- {s}");
                }

                if (schemasToLoad.Contains("dbo", StringComparer.OrdinalIgnoreCase))
                {
                    _output.WriteLine("WARNING: 'dbo' is INCLUDED in the prewarm list.");
                }
                else
                {
                    _output.WriteLine("SUCCESS: 'dbo' is EXCLUDED from the prewarm list.");
                    Assert.DoesNotContain("dbo", schemasToLoad);
                }
            }
            _output.WriteLine("----------------------------------------------------------");

            if (schemasToLoad == null || schemasToLoad.Count == 0)
            {
                throw new InvalidOperationException("Appsettings should have schemas configured.");
            }

            PreloadResult result = await schemaService.PreloadSchemaAsync(schemasToLoad, instanceHash, CancellationToken.None);

            _output.WriteLine($"[Result] Loaded Items Count: {result.LoadedItemsCount}");

            Assert.Empty(result.MissingSchemas);
            Assert.True(result.LoadedItemsCount > 0, "Should load items from configured schemas.");

            SpSchema coreSchema = await schemaService.GetSpSchemaAsync(coreSp, instanceHash, CancellationToken.None);
            Assert.NotNull(coreSchema);

            SpSchema dboSchema = await schemaService.GetSpSchemaAsync(dboSp, instanceHash, CancellationToken.None);
            Assert.NotNull(dboSchema);
            Assert.Equal(dboSp, dboSchema.Name);
            Assert.Equal(coreSp, coreSchema.Name);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[TEST ERROR] {ex.GetType().Name}: {ex.Message}");
            _output.WriteLine(ex.StackTrace ?? string.Empty);
            throw;
        }
        finally
        {
            await _fixture.Verification.Sql($"DROP PROCEDURE IF EXISTS {dboSp}").ExecuteAsync(TestContext.Current.CancellationToken);
            await _fixture.Verification.Sql($"DROP PROCEDURE IF EXISTS {coreSp}").ExecuteAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "SchemaCache")]
    public async Task GetSpSchemaAsync_ShouldRecoverFromInvalidCachedSchemaPayload()
    {
        string spName = "dbo.usp_CacheRecovery";

        try
        {
            await _fixture.Verification.Sql($@"
                CREATE OR ALTER PROCEDURE {spName} AS SELECT 1;
            ").ExecuteAsync(TestContext.Current.CancellationToken);

            using IServiceScope scope = _fixture.Services.CreateScope();
            ISchemaService schemaService = scope.ServiceProvider.GetRequiredService<ISchemaService>();
            HybridCache cache = scope.ServiceProvider.GetRequiredService<HybridCache>();
            LibDbOptions options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LibDbOptions>>().Value;

            string instanceHash = options.ConnectionStringNames.Count > 0 ? options.ConnectionStringNames[0] : "Default";
            string cacheKey = $"Sch:v2:{instanceHash}:SP:{spName}";

            TvpSchema wrongPayload = new()
            {
                Name = spName,
                VersionToken = 1,
                LastCheckedAt = DateTime.UtcNow,
                Columns = []
            };

            await cache.SetAsync(cacheKey, wrongPayload, cancellationToken: CancellationToken.None);

            SpSchema schema = await schemaService.GetSpSchemaAsync(spName, instanceHash, CancellationToken.None);

            Assert.Equal(spName, schema.Name);
            Assert.NotNull(schema.Parameters);
        }
        finally
        {
            await _fixture.Verification.Sql($"DROP PROCEDURE IF EXISTS {spName}").ExecuteAsync(TestContext.Current.CancellationToken);
        }
    }
}
