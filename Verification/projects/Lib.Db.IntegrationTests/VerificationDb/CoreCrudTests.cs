// ============================================================================
// 파일: VerificationDb/CoreCrudTests.cs
// 설명: 기본 CRUD + TVP 대량 삽입 통합 테스트 (TestSuite 01_BasicCrudTests + IT01 병합 이관)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 핵심 CRUD 연산 검증 테스트.
/// <para><b>[설계 의도]</b> TestSuite의 01_BasicCrudTests.cs + IT01_RealDbConnectionTests.cs를
/// IntegrationTests로 이관하여 MultiDbFixture 기반으로 통합한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class CoreCrudTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region Insert 테스트

    /// <summary>
    /// 사용자 삽입 후 신규 UserId가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task Insert_ShouldReturnNewId()
    {
        // Arrange
        string uniqueEmail = $"crud_insert_{Guid.NewGuid():N}@test.com";

        // Act
        DbResult<int> scalarResult = await _db
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "CrudInsertTest", Email = uniqueEmail, Age = (int?)25 })
            .ExecuteScalarAsync<int>();

        // Assert
        scalarResult.IsSuccess.Should().BeTrue();
        scalarResult.Value.Should().BeGreaterThan(0, "신규 UserId는 양수여야 합니다.");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [core].[Users] WHERE Email = '{uniqueEmail}'")
            .ExecuteAsync();
    }

    #endregion

    #region GetUser 테스트

    /// <summary>
    /// 유효한 UserId로 조회 시 사용자 정보가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task GetUser_ValidId_ReturnsUser()
    {
        // Act
        DbResult<CoreUser?> result = await _db
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 1 })
            .QuerySingleAsync<CoreUser>();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.UserId.Should().Be(1);
        result.Value.UserName.Should().Be("Alice");
    }

    #endregion

    #region SearchUsers 테스트

    /// <summary>
    /// 이름 패턴 검색 시 일치하는 사용자가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task SearchUsers_ByNamePattern_ReturnsMatches()
    {
        // Act
        DbResult<IAsyncEnumerable<CoreUser>> streamResult = await _db
            .Procedure("core.usp_Core_Search_Users")
            .With(new { SearchTerm = "Alice" })
            .QueryAsync<CoreUser>();

        // Assert
        streamResult.IsSuccess.Should().BeTrue();
        List<CoreUser> users = await streamResult.Value!.ToListAsync();
        users.Should().NotBeEmpty();
        users.Should().Contain(u => u.UserName.Contains("Alice"));
    }

    #endregion

    #region ExecuteScalar 테스트

    /// <summary>
    /// Users 테이블의 행 수를 ExecuteScalar로 조회하여 올바른 값이 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task ExecuteScalar_Count_ReturnsCorrectCount()
    {
        // Act
        DbResult<int> scalarResult = await _db
            .Sql("SELECT COUNT(*) FROM [core].[Users]")
            .ExecuteScalarAsync<int>();

        // Assert
        scalarResult.IsSuccess.Should().BeTrue();
        scalarResult.Value.Should().BeGreaterThanOrEqualTo(3, "시드 데이터 Alice, Bob, Charlie가 존재해야 합니다.");
    }

    #endregion

    #region BulkInsert TVP 테스트

    /// <summary>
    /// TVP를 사용한 대량 삽입이 정상 동작하고 삽입 행 수가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task BulkInsert_WithTvp_ReturnsRowsAffected()
    {
        // Arrange
        string suffix = Guid.NewGuid().ToString("N")[..8];
        List<CoreUserTvp> users =
        [
            new() { UserName = $"Bulk1_{suffix}", Email = $"bulk1_{suffix}@test.com", Age = 20 },
            new() { UserName = $"Bulk2_{suffix}", Email = $"bulk2_{suffix}@test.com", Age = 30 },
            new() { UserName = $"Bulk3_{suffix}", Email = $"bulk3_{suffix}@test.com", Age = 40 }
        ];

        // Act
        DbResult<int> scalarResult = await _db
            .Procedure("core.usp_Core_Bulk_Insert_Users")
            .With(new { Users = users })
            .ExecuteScalarAsync<int>();

        // Assert
        scalarResult.IsSuccess.Should().BeTrue();
        scalarResult.Value.Should().Be(3, "3명의 사용자가 삽입되어야 합니다.");

        // Cleanup
        await _db
            .Sql($"DELETE FROM [core].[Users] WHERE UserName LIKE 'Bulk%_{suffix}'")
            .ExecuteAsync();
    }

    #endregion
}
