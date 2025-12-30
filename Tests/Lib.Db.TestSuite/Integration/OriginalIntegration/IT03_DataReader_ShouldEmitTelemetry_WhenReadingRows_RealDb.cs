using Lib.Db.Contracts.Entry;
using Lib.Db.Verification.Tests.Infrastructure;
using Xunit;

namespace Lib.Db.Verification.Tests.Integration;

[Collection("Database Collection")]
public class IT03_DataReader_ShouldEmitTelemetry_WhenReadingRows_RealDb
{
    private readonly TestDatabaseFixture _fixture;
    private IDbContext Db => _fixture.Db;

    public IT03_DataReader_ShouldEmitTelemetry_WhenReadingRows_RealDb(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Type", "RealDb")]
    public async Task IT03_Execute_ShouldEmitTelemetry_WhenReadingRows()
    {
        // Arrange
        // 1. Setup Telemetry Listener
        using var harness = new TelemetryTestHarness("Lib.Db");

        // 2. Setup Data (Insert a user to ensure we have data to read)
        var userName = $"TelUser_{Guid.NewGuid()}";
        var userId = await Db.Default.Procedure("[core].[usp_Core_Insert_User]")
            .With(new { UserName = userName, Email = $"{userName}@test.com", Age = 30 })
            .ExecuteScalarAsync<int>();

        // Act
        // 3. Execute Reader (Get User) - This triggers MonitoredSqlDataReader
        var user = await Db.Default.Procedure("[core].[usp_Core_Get_User]")
            .With(new { UserId = userId })
            .QuerySingleAsync<UserDto>();

        // Assert
        // 4. Verify Result
        Assert.NotNull(user);
        Assert.Equal(userName, user.UserName);

        // 5. Verify Telemetry
        // The reader consumption should trigger metrics recording.
        // DbMetrics uses "db.client.operation.duration" (Histogram) and "db.client.connections.usage" (UpDownCounter)
        
        // Note: DbRequestsTotal seems unused in current code path, checking Duration instead.
        var durations = harness.GetDoubles("db.client.operation.duration");
        // var connections = harness.GetInts("db.client.connections.usage"); 

        Assert.NotEmpty(durations);

        // Verify Tags (Optional but good)
        // var durationMetric = durations.Last();
        
        // Note: s_connActive is UpDownCounter<int>, so use GetInts
        // var activeConns = harness.GetInts("db.client.connections.usage");
        // Assert.NotEmpty(activeConns);

        // Cleanup (Data Row)
        await Db.Default.Sql("DELETE FROM [core].[Users] WHERE UserId = @UserId")
            .With(new { UserId = userId })
            .ExecuteAsync();
    }

    private class UserDto
    {
        public int UserId { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public int? Age { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
