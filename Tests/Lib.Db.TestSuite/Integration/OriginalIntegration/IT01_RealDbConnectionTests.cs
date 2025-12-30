using Lib.Db.Contracts.Entry;
using Lib.Db.Verification.Tests;
using Xunit;

namespace Lib.Db.Verification.Tests.Integration;

[Collection("Database Collection")]
public class IT01_RealDbConnectionTests
{
    private readonly TestDatabaseFixture _fixture;
    private IDbContext Db => _fixture.Db;

    public IT01_RealDbConnectionTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Type", "RealDb")]
    public async Task IT01_Execute_ShouldInsertAndSelect_RealDb()
    {
        // Arrange
        var uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var userName = $"ItUser_{uniqueSuffix}";
        var email = $"it_{uniqueSuffix}@test.com";

        // Act - 1. Execute Stored Procedure to Insert (Uses SqlDbExecutor pipeline)
        var newUserId = await Db.Default.Procedure("[core].[usp_Core_Insert_User]")
            .With(new { UserName = userName, Email = email, Age = 99 })
            .ExecuteScalarAsync<int>();

        // Assert - 1. Check Return Value
        Assert.True(newUserId > 0, $"Expected NewUserId > 0, but got {newUserId}");

        // Act - 2. Verify via Direct Query (Uses SqlDbExecutor pipeline in Text mode)
        var user = await Db.Default.Sql("SELECT UserName, Email FROM [core].[Users] WHERE UserId = @UserId")
            .With(new { UserId = newUserId })
            // Using a simple DTO or anonymous type if mapping supported, or Reader
            .QuerySingleAsync<UserDto>(); // Defining local DTO or using dynamic

        // Assert - 2. Data Persistence
        Assert.Equal(userName, user.UserName);
        Assert.Equal(email, user.Email);

        // Cleanup - Explicitly remove to keep DB clean (though Setup handles Truncate usually)
        await Db.Default.Sql("DELETE FROM [core].[Users] WHERE UserId = @UserId")
            .With(new { UserId = newUserId })
            .ExecuteAsync();
    }

    private class UserDto
    {
        public string UserName { get; set; } = "";
        public string Email { get; set; } = "";
    }
}
