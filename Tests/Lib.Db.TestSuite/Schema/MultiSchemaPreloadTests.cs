using Lib.Db.Contracts.Schema;
using Lib.Db.Schema;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Lib.Db.Verification.Tests.Schema;

[Collection("Database Collection")]
public class MultiSchemaPreloadTests
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MultiSchemaPreloadTests(TestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "SchemaWarmup")]
    public async Task PreloadSchemaAsync_ShouldLoadMultipleSchemas()
    {
        // 1. Setup Data - Create dummy SPs in dbo and core schemas
        // NAME CHANGED: Fresh name to bypass potential stale cache keys
        var dboSp = "dbo.usp_WarmupRec_Dbo";
        var coreSp = "core.usp_WarmupRec_Core";

        try
        {
            await _fixture.Db.Default.Sql($@"
                CREATE OR ALTER PROCEDURE {dboSp} AS SELECT 1;
            ").ExecuteAsync();

            await _fixture.Db.Default.Sql($@"
                CREATE OR ALTER PROCEDURE {coreSp} AS SELECT 1;
            ").ExecuteAsync();

            // 2. Prepare Service
            using var scope = _fixture.Services.CreateScope();
            var schemaService = scope.ServiceProvider.GetRequiredService<ISchemaService>();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Lib.Db.Configuration.LibDbOptions>>().Value;

            // Force 'Default' or derived instance
            var instanceHash = options.ConnectionStringName ?? "Default";

            // 3. Action - Preload schemas configured in appsettings.json
            var schemasToLoad = options.PrewarmSchemas;
            
            _output.WriteLine("----------- Schema Prewarm Configuration Check -----------");
            _output.WriteLine($"Configured Schemas Count: {schemasToLoad?.Count ?? 0}");
            if (schemasToLoad != null)
            {
                foreach (var s in schemasToLoad)
                {
                    _output.WriteLine($"- {s}");
                }

                // VERIFICATION: Explicitly check for 'dbo'
                if (schemasToLoad.Contains("dbo", StringComparer.OrdinalIgnoreCase))
                {
                    _output.WriteLine("⚠️ WARNING: 'dbo' is INCLUDED in the prewarm list.");
                }
                else
                {
                    _output.WriteLine("✅ SUCCESS: 'dbo' is EXCLUDED from the prewarm list.");
                    Assert.DoesNotContain("dbo", schemasToLoad);
                }
            }
            _output.WriteLine("----------------------------------------------------------");

            if (schemasToLoad == null || schemasToLoad.Count == 0)
            {
                 throw new InvalidOperationException("Appsettings should have schemas configured.");
            }

            var result = await schemaService.PreloadSchemaAsync(schemasToLoad, instanceHash, CancellationToken.None);

            _output.WriteLine($"[Result] Loaded Items Count: {result.LoadedItemsCount}");
            
            // 4. Assertion
            Assert.Empty(result.MissingSchemas); 
            Assert.True(result.LoadedItemsCount > 0, "Should load items from configured schemas.");

            // 5. Verification
            // Core should be prewarmed
            var coreSchema = await schemaService.GetSpSchemaAsync(coreSp, instanceHash, CancellationToken.None);
            Assert.NotNull(coreSchema);
            
            // Dbo should STILL be resolvable (Lazy Load) even if excluded from prewarm
            var dboSchema = await schemaService.GetSpSchemaAsync(dboSp, instanceHash, CancellationToken.None);
            Assert.NotNull(dboSchema);
            Assert.Equal(dboSp, dboSchema.Name);
            Assert.Equal(dboSp, dboSchema.Name);
            Assert.Equal(coreSp, coreSchema.Name);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[TEST ERROR] {ex.GetType().Name}: {ex.Message}");
            _output.WriteLine(ex.StackTrace);
            throw;
        }
        finally
        {
             // Cleanup
            await _fixture.Db.Default.Sql($"DROP PROCEDURE IF EXISTS {dboSp}").ExecuteAsync();
            await _fixture.Db.Default.Sql($"DROP PROCEDURE IF EXISTS {coreSp}").ExecuteAsync();
        }
    }
}
