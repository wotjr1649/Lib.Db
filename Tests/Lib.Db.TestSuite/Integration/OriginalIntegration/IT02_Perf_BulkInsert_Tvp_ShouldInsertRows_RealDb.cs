using Lib.Db.Contracts.Entry;
using Lib.Db.Verification.Tests;
using System.Data;
using Xunit;

namespace Lib.Db.Verification.Tests.Integration;

[Collection("Database Collection")]
public class IT02_Perf_BulkInsert_Tvp_ShouldInsertRows_RealDb
{
    private readonly TestDatabaseFixture _fixture;
    private IDbContext Db => _fixture.Db;

    public IT02_Perf_BulkInsert_Tvp_ShouldInsertRows_RealDb(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Type", "RealDb")]
    public async Task IT02_Execute_ShouldInsertMultipleRows_RealDb()
    {
        // Arrange
        var rand = new Random();
        var batchNumber = rand.Next(100000, 999999);
        var rowCount = 50;
        
        var items = Enumerable.Range(0, rowCount)
            .Select(i => new PerfBulkInsertItem 
            { 
                BatchNumber = batchNumber, 
                Data = $"Row_{i}_{Guid.NewGuid()}" 
            })
            .ToList();

        // Act - 1. Execute Bulk Insert Proc (TVP) via SqlDbExecutor
        // Using DataTable to explicitly map to [perf].[Tvp_Perf_BulkInsert]
        var tvpTable = new DataTable();
        tvpTable.Columns.Add("BatchNumber", typeof(int));
        tvpTable.Columns.Add("Data", typeof(string));

        foreach (var item in items)
        {
            tvpTable.Rows.Add(item.BatchNumber, item.Data);
        }

        var rowsAffected = await Db.Default.Procedure("[perf].[usp_Perf_Bulk_Insert]")
            .With(new { Items = tvpTable }) // Pass DataTable as TVP
            .ExecuteScalarAsync<int>();

        // Assert - 1. Check Rows Affected
        Assert.Equal(rowCount, rowsAffected);

        // Act - 2. Verify Data via Direct Select Proc
        var insertedRows = await Db.Default.Procedure("[perf].[usp_Perf_Query_With_Param]")
            .With(new { BatchNumber = batchNumber })
            .QueryAsync<PerfBulkInsertResult>()
            .ToListAsync();

        // Assert - 2. Data Persistence
        Assert.Equal(rowCount, insertedRows.Count);
        Assert.All(insertedRows, r => Assert.Equal(batchNumber, r.BatchNumber));
        Assert.Contains(insertedRows, r => r.Data.StartsWith("Row_0_"));
        Assert.Contains(insertedRows, r => r.Data.StartsWith($"Row_{rowCount-1}_"));

        // Cleanup
        await Db.Default.Sql("DELETE FROM [perf].[BulkTest] WHERE BatchNumber = @BatchNumber")
            .With(new { BatchNumber = batchNumber })
            .ExecuteAsync();
    }

    // POCO for TVP Input
    // Must match [perf].[Tvp_Perf_BulkInsert] structure: BatchNumber (int), Data (nvarchar)
    private class PerfBulkInsertItem
    {
        public int BatchNumber { get; set; }
        public string? Data { get; set; }
    }

    // POCO for Result
    private class PerfBulkInsertResult
    {
        public long Id { get; set; }
        public int BatchNumber { get; set; }
        public string? Data { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
