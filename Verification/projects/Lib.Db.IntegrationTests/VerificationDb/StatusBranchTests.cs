// ============================================================================
// 파일: VerificationDb/StatusBranchTests.cs
// 설명: test.usp_Status_Branch_Logic SP의 상태 분기(NEW/ACTIVE/VIP) 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// test.usp_Status_Branch_Logic SP의 상태 분기 로직을 검증하는 테스트.
/// <para><b>[설계 의도]</b> 주문 건수에 따라 NEW(0건), ACTIVE(1~4건), VIP(5건 이상)
/// 상태가 올바르게 분기되는지 OUTPUT 파라미터 + 결과셋으로 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class StatusBranchTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;
    private readonly IDbSession _session = fixture.Session;

    #endregion

    #region SB01: 주문 없는 유저 → NEW

    /// <summary>
    /// 주문이 없는 신규 유저에 대해 Status='NEW', OrderCount=0이 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task SB01_StatusBranch_NewUser_NoOrders_ReturnsNew()
    {
        // Arrange — 주문 없는 신규 유저 생성
        string uniqueEmail = $"sb01_new_{Guid.NewGuid():N}@test.com";
        DbResult<int> insertResult = await _db
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "SB01_NewUser", Email = uniqueEmail, Age = 25 })
            .ExecuteScalarAsync<int>();
        insertResult.IsSuccess.Should().BeTrue();
        int newUserId = insertResult.Value;

        // Act — 상태 분기 호출
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("test.usp_Status_Branch_Logic")
            .With(new { UserId = newUserId, Status = "" })
            .QueryAsync<Dictionary<string, object?>>();

        // Assert
        result.IsSuccess.Should().BeTrue();

        Dictionary<string, object?>? row = null;
        await foreach (Dictionary<string, object?> item in result.Value!)
        {
            row = item;
            break;
        }
        row.Should().NotBeNull();
        row!["Status"]?.ToString().Should().Be("NEW");
        Convert.ToInt32(row["OrderCount"]).Should().Be(0);
    }

    #endregion

    #region SB02: 주문 1~4건 유저 → ACTIVE

    /// <summary>
    /// 주문 1~4건을 가진 유저에 대해 Status='ACTIVE'가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task SB02_StatusBranch_ActiveUser_FewOrders_ReturnsActive()
    {
        // Arrange — 유저 생성 후 주문 3건 삽입
        string uniqueEmail = $"sb02_active_{Guid.NewGuid():N}@test.com";
        DbResult<int> insertResult = await _db
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "SB02_ActiveUser", Email = uniqueEmail, Age = 30 })
            .ExecuteScalarAsync<int>();
        insertResult.IsSuccess.Should().BeTrue();
        int newUserId = insertResult.Value;

        for (int i = 0; i < 3; i++)
        {
            DbResult<int> orderResult = await _db
                .Sql((FormattableString)$"INSERT INTO [core].[Orders] (UserId, ProductId, Quantity, TotalPrice) VALUES ({newUserId}, 1, 1, 100.00)")
                .ExecuteAsync();
            orderResult.IsSuccess.Should().BeTrue();
        }

        // Act — 상태 분기 호출
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("test.usp_Status_Branch_Logic")
            .With(new { UserId = newUserId, Status = "" })
            .QueryAsync<Dictionary<string, object?>>();

        // Assert
        result.IsSuccess.Should().BeTrue();

        Dictionary<string, object?>? row = null;
        await foreach (Dictionary<string, object?> item in result.Value!)
        {
            row = item;
            break;
        }
        row.Should().NotBeNull();
        row!["Status"]?.ToString().Should().Be("ACTIVE");
        Convert.ToInt32(row["OrderCount"]).Should().BeInRange(1, 4);
    }

    #endregion

    #region SB03: 주문 5건 이상 유저 → VIP

    /// <summary>
    /// 주문 5건 이상을 가진 유저에 대해 Status='VIP'가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task SB03_StatusBranch_VipUser_ManyOrders_ReturnsVip()
    {
        // Arrange — 유저 생성 후 주문 6건 삽입
        string uniqueEmail = $"sb03_vip_{Guid.NewGuid():N}@test.com";
        DbResult<int> insertResult = await _db
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "SB03_VipUser", Email = uniqueEmail, Age = 35 })
            .ExecuteScalarAsync<int>();
        insertResult.IsSuccess.Should().BeTrue();
        int newUserId = insertResult.Value;

        for (int i = 0; i < 6; i++)
        {
            DbResult<int> orderResult = await _db
                .Sql((FormattableString)$"INSERT INTO [core].[Orders] (UserId, ProductId, Quantity, TotalPrice) VALUES ({newUserId}, 1, 1, 100.00)")
                .ExecuteAsync();
            orderResult.IsSuccess.Should().BeTrue();
        }

        // Act — 상태 분기 호출
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("test.usp_Status_Branch_Logic")
            .With(new { UserId = newUserId, Status = "" })
            .QueryAsync<Dictionary<string, object?>>();

        // Assert
        result.IsSuccess.Should().BeTrue();

        Dictionary<string, object?>? row = null;
        await foreach (Dictionary<string, object?> item in result.Value!)
        {
            row = item;
            break;
        }
        row.Should().NotBeNull();
        row!["Status"]?.ToString().Should().Be("VIP");
        Convert.ToInt32(row["OrderCount"]).Should().BeGreaterThanOrEqualTo(5);
    }

    #endregion
}
