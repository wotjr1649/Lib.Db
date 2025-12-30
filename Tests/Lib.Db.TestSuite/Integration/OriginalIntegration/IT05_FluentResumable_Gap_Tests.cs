using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Lib.Db.Verification.Tests.Integration;

[Collection("Database Collection")]
public class IT05_FluentResumable_Gap_Tests
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;
    private IDbContext Db => _fixture.Db;

    public IT05_FluentResumable_Gap_Tests(TestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Phase", "5")]
    public async Task IT05_FluentResumable_ShouldNotPersistCursor_ComparedTo_IDbExecutor_RealDb()
    {
        // ---------------------------------------------------------------------
        // 1. Data Preparation (Determinism)
        // ---------------------------------------------------------------------
        // Truncate logs and cursor state to ensure clean state
        await Db.Default.Sql("TRUNCATE TABLE [adv].[ResumableLogs]").ExecuteAsync();
        
        // Use a unique instance hash for this test to avoid collision
        var testInstanceHash = "IT05_GapTest";
        
        await Db.Default.Sql("DELETE FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = testInstanceHash })
            .ExecuteAsync();

        // Generate 20 test logs
        await Db.Default.Procedure("[adv].[usp_Adv_GenerateLogs]")
            .With(new { Count = 20 })
            .ExecuteAsync();
            
        // Shared query components
        Func<int, string> queryBuilder = (cursor) => 
            $"SELECT TOP 5 LogId, Message, CreatedAt FROM [adv].[ResumableLogs] WHERE LogId > {cursor} ORDER BY LogId ASC";
            
        Func<LogDto, int> cursorSelector = (log) => log.LogId;

        // ---------------------------------------------------------------------
        // 2. Scenario A: Fluent API Execution (The Gap Target)
        // ---------------------------------------------------------------------
        _output.WriteLine("[Scenario A] Fluent API Execution");
        
        // We cannot easily set 'InstanceHash' dynamically in Fluent API unless it was configured in DI or Builder creation.
        // Usually Fluent API uses the instance name registered in DI (e.g. "Default").
        // To be fair, let's assume we are testing the "Default" instance behavior via Db.Default.
        // However, to permit side-by-side comparison without noise, IF Fluent API supported custom hash, we'd use it.
        // Since Db.Default is bound to "Default", we checking "Default" hash for Fluent, 
        // and we will use "Default" hash for IDbExecutor as well to match.
        
        // Let's use "Default" as the hash because that's what Db.Default uses.
        var targetInstanceHash = "Default";
        
        // Ensure clean slate for "Default"
        await Db.Default.Sql("DELETE FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = targetInstanceHash })
            .ExecuteAsync();

        int fluentConsumed = 0;
        await foreach (var item in Db.Default
            .QueryResumableAsync<int, LogDto>(
                queryBuilder, 
                cursorSelector, 
                initialCursor: 0))
        {
            fluentConsumed++;
            // Consuming > 5 items (e.g. 6) to ensure the FIRST batch (5 items) is fully completed and verified.
            // If we stop at exactly 5, the executor might be suspended at 'yield return' before saving.
            if (fluentConsumed >= 6) break;
        }
        
        Assert.Equal(6, fluentConsumed);

        // Verify Persistence (Wait slightly for any potential background save)
        await Task.Delay(500);
        
        var fluentStoredCursor = await Db.Default.Sql("SELECT TOP 1 CursorValue FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = targetInstanceHash })
            .QuerySingleAsync<string>();
            
        // EXPECTATION (Refactored): Fluent API DOES persist cursor now.
        _output.WriteLine($"[Scenario A Result] Fluent Stored Cursor: {fluentStoredCursor ?? "NULL"}");
        Assert.NotNull(fluentStoredCursor);
        Assert.Equal("5", fluentStoredCursor); // The first batch (TOP 5) ended at 5

        // ---------------------------------------------------------------------
        // 3. Scenario B: IDbExecutor Execution (Reference Check)
        // ---------------------------------------------------------------------
        _output.WriteLine("[Scenario B] IDbExecutor Execution");
        
        // Reset Cursor State for Scenario B
        await Db.Default.Sql("DELETE FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = targetInstanceHash })
            .ExecuteAsync();

        using var scope = _fixture.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IDbExecutor>();
        
        int executorConsumed = 0;
        
        // We use the SAME instance hash "Default" to be strictly comparable, 
        // confirming that IF it was working, it WOULD have written to this key.
        await foreach (var item in executor.QueryResumableAsync<int, LogDto>(
            queryBuilder,
            cursorSelector,
            instanceHash: targetInstanceHash, // Explicitly passing "Default"
            initialCursor: 0))
        {
            executorConsumed++;
             // Consuming > 5 items (e.g. 6) to ensure the FIRST batch (5 items) completes.
             if (executorConsumed >= 6) break;
        }
        
        Assert.Equal(6, executorConsumed);
        
        // Verify Persistence
        await Task.Delay(500); // Wait for fire-and-forget save
        
        var executorStoredCursor = await Db.Default.Sql("SELECT TOP 1 CursorValue FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = targetInstanceHash })
            .QuerySingleAsync<string>();
            
        _output.WriteLine($"[Scenario B Result] IDbExecutor Stored Cursor: {executorStoredCursor ?? "NULL"}");
        
        // EXPECTATION: IDbExecutor DOES persist cursor.
        Assert.NotNull(executorStoredCursor);
        
        // Verify value is 5 (since we consumed 5 items 1..5, and batch size is effectively controlled by TOP 5 in queryBuilder or internal batching)
        // With manual break at 5, and queryBuilder returning TOP 5, the first batch is exactly 5.
        // It should have saved the last cursor of the batch.
        Assert.Equal("5", executorStoredCursor);

        // ---------------------------------------------------------------------
        // 4. Conclusion
        // ---------------------------------------------------------------------
        // If we reached here, the GAP is CLOSED.
        // Fluent API returned "5" (Persistence Active).
        // IDbExecutor returned "5" (Persistence Active).
        _output.WriteLine("Confirmed: Fluent API now persists cursor state correctly, matching IDbExecutor.");

        // Cleanup
        await Db.Default.Sql("TRUNCATE TABLE [adv].[ResumableLogs]").ExecuteAsync();
        await Db.Default.Sql("DELETE FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = targetInstanceHash })
            .ExecuteAsync();
    }
    
    // Test DTO
    private class LogDto
    {
        public int LogId { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
