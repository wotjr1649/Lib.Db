// ============================================================================
// 01_BasicCrudTests.cs
// 목적: 기본 CRUD 연산 검증
// 시나리오: 5개
// 수정: Seed 데이터 활용으로 단순화
// ============================================================================

using Lib.Db.Contracts.Entry;

namespace Lib.Db.Verification.Tests;

[Collection("Database Collection")]
public class BasicCrudTests(TestDatabaseFixture fixture)
{
    private readonly IDbContext _db = fixture.Db;

    [Fact]
    public async Task Scenario_01_1_Select_Single_ShouldReturnUser()
    {
        // Arrange - Seed 데이터 Alice (UserId=1) 사용

        // Act
        var user = await _db.Default
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 1 })
            .QuerySingleAsync<CoreUser>();

        // Assert
        Assert.NotNull(user);
        Assert.Equal(1, user.UserId);
        Assert.Equal("Alice", user.UserName);
        Assert.Equal("alice@test.com", user.Email);
    }

    [Fact]
    public async Task Scenario_01_2_Insert_ShouldReturnNewId()
    {
        // Act
        var newUserId = await _db.Default
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "NewUser", Email = "newuser@test.com", Age = (int?)25 })
            .ExecuteScalarAsync<int>();

        // Assert
        Assert.True(newUserId > 3); // Seed 데이터는 1,2,3

        // Cleanup
        await _db.Default
            .Sql("DELETE FROM [core].[Users] WHERE UserId = @UserId")
            .With(new { UserId = newUserId })
            .ExecuteAsync();
    }

    [Fact]
    public async Task Scenario_01_3_BulkInsert_WithTVP_ShouldInsertMultiple()
    {
        // Arrange
        var users = new List<CoreUserTvp>
        {
            new() { UserName = "Bulk1", Email = "bulk1@test.com", Age = 20 },
            new() { UserName = "Bulk2", Email = "bulk2@test.com", Age = 30 },
            new() { UserName = "Bulk3", Email = "bulk3@test.com", Age = 40 }
        };

        // Act
        var rowsAffected = await _db.Default
            .Procedure("core.usp_Core_Bulk_Insert_Users")
            .With(new { Users = users })
            .ExecuteScalarAsync<int>();

        // Assert
        Assert.Equal(3, rowsAffected);

        // Cleanup
        await _db.Default
            .Sql("DELETE FROM [core].[Users] WHERE UserName LIKE 'Bulk%'")
            .ExecuteAsync();
    }

    [Fact]
    public async Task Scenario_01_4_Query_WithLike_ShouldReturnMatches()
    {
        // Arrange - Seed 데이터 Alice 사용

        // Act
        var users = await _db.Default
            .Procedure("core.usp_Core_Search_Users")
            .With(new { SearchTerm = "Alice" })
            .QueryAsync<CoreUser>()
            .ToListAsync();

        // Assert
        Assert.NotEmpty(users);
        Assert.Contains(users, u => u.UserName.Contains("Alice"));
    }

    [Fact]
    public async Task Scenario_01_5_ExecuteScalar_ShouldReturnCount()
    {
        // Arrange - Seed 데이터 3명 사용

        // Act
        var count = await _db.Default
            .Sql("SELECT COUNT(*) FROM [core].[Users]")
            .ExecuteScalarAsync<int>();

        // Assert
        Assert.True(count >= 3, "최소 Seed 데이터 3명은 존재해야 함");
    }
}
