using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Execution;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;


namespace Lib.Db.Verification.Tests.Integration;

[Collection("Database Collection")]
public class IT04_Resumable_ShouldSaveAndResumeCursor_RealDb
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;
    private IDbContext Db => _fixture.Db;

    public IT04_Resumable_ShouldSaveAndResumeCursor_RealDb(TestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Phase", "4")]
    public async Task IT04_Execute_ShouldSaveAndResumeCursor()
    {
        // 1. Setup Data
        // Truncate first to ensure predictable identity
        await Db.Default.Sql("TRUNCATE TABLE [adv].[ResumableLogs]").ExecuteAsync();
        
        // Generate 50 logs. LogId 1..50
        await Db.Default.Procedure("[adv].[usp_Adv_GenerateLogs]")
            .With(new { Count = 50 })
            .ExecuteAsync();

        var instanceHash = "Default";

        // Ensure cursor is clean
        await Db.Default.Sql("DELETE FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = instanceHash })
            .ExecuteAsync();

        _output.WriteLine("DEBUG: Proceeding to Resumable Query...");

        // 2. Execution 1: Read Partial (First 8 items using TOP 5)
        // Batch 1: 5 items. (Should Save 5)
        // Batch 2: 3 items. (Interrupted)
        
        using var scope = _fixture.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<Lib.Db.Contracts.Execution.IDbExecutor>();

        int itemsConsumed = 0;
        int? lastConsumedId = null;

        // Use TOP 5 to force frequent batches
        Func<int, string> queryBuilder = (cursor) => 
            $"SELECT TOP 5 LogId, Message, CreatedAt FROM [adv].[ResumableLogs] WHERE LogId > {cursor} ORDER BY LogId ASC";

        var consumedItems = new List<int>();
        await foreach (var log in executor
            .QueryResumableAsync<int, LogDto>(
                queryBuilder: queryBuilder,
                cursorSelector: selector => selector.LogId,
                instanceHash: instanceHash,
                initialCursor: 0))
        {
            consumedItems.Add(log.LogId);
            itemsConsumed++;
            lastConsumedId = log.LogId;
            
            // Break after 8 items (Batch 1 Done + 3 items from Batch 2)
            if (itemsConsumed >= 8) break; 
        }
        
        Assert.Equal(8, itemsConsumed);
        Assert.Equal(8, lastConsumedId); // 1..8

        // Wait a bit to ensure async save (though it should be awaited)
        await Task.Delay(200);

        // 3. Verify Cursor State in DB
        // Expect Cursor = 5 (Last fully completed batch).
        var savedCursorJson = await Db.Default.Sql("SELECT TOP 1 CursorValue FROM [core].[CursorState] WHERE InstanceHash = @Hash")
            .With(new { Hash = instanceHash })
            .QuerySingleAsync<string>();
        
        Assert.Equal("5", savedCursorJson); // Json serialized int

        // 4. Execution 2: Resume
        // Should start from 5.
        // First item should be 6.
        
        int resumeCount = 0;
        int? resumeFirstId = null;

        await foreach (var log in executor
            .QueryResumableAsync<int, LogDto>(
                queryBuilder: queryBuilder,
                cursorSelector: selector => selector.LogId,
                instanceHash: instanceHash,
                initialCursor: 0)) // Initial 0, but Store should return 5.
        {
            resumeCount++;
            if (resumeFirstId == null) resumeFirstId = log.LogId;
        }

        // 5. Verification
        // Total logs 50. Saved Cursor 5.
        // Resume should fetch 6..50 (45 items).
        // The first execution consumed 6..8. Dupes 6,7,8 will be re-fetched.
        
        Assert.Equal(6, resumeFirstId);
        Assert.Equal(45, resumeCount); // 6 to 50 = 45 items.

        // Cleanup
        await Db.Default.Sql("TRUNCATE TABLE [adv].[ResumableLogs]").ExecuteAsync();
        await Db.Default.Sql("DELETE FROM [core].[CursorState] WHERE InstanceHash = @Hash")
             .With(new { Hash = instanceHash })
             .ExecuteAsync();
    }

    private class LogDto
    {
        public int LogId { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
