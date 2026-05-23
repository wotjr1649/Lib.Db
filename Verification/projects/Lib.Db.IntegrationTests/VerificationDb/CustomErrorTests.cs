// ============================================================================
// 파일: VerificationDb/CustomErrorTests.cs
// 설명: 사용자 정의 에러(50001~50003) + 복구 흐름 + 한국어 메시지 전파 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// THROW 50001/50002/50003 사용자 정의 에러를 검증하는 테스트.
/// <para><b>[설계 의도]</b> test.usp_Error_Custom_50001 SP의 다양한 분기 로직에서
/// DbErrorKind.UserDefined + 정확한 SqlErrorCode가 반환되는지, 그리고 에러 후
/// 정상 복구 흐름이 가능한지를 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class CustomErrorTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region CE01: 주문 미존재 — 50001

    /// <summary>
    /// 존재하지 않는 OrderId로 VALIDATE 시 50001 UserDefined 에러가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task CE01_Custom_50001_OrderNotFound_ReturnsUserDefined()
    {
        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = 99999, Action = "VALIDATE" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.UserDefined);
        result.Error!.Value.SqlErrorCode.Should().Be(50001);
    }

    #endregion

    #region CE02: 재시도 초과 — 50002

    /// <summary>
    /// RETRY 액션 시 50002 UserDefined 에러가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task CE02_Custom_50002_RetryExceeded_ReturnsUserDefined()
    {
        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = 1, Action = "RETRY" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.UserDefined);
        result.Error!.Value.SqlErrorCode.Should().Be(50002);
    }

    #endregion

    #region CE03: 알 수 없는 액션 — 50003

    /// <summary>
    /// UNKNOWN 액션 시 50003 UserDefined 에러가 반환되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task CE03_Custom_50003_UnknownAction_ReturnsUserDefined()
    {
        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = 1, Action = "UNKNOWN" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Kind.Should().Be(DbErrorKind.UserDefined);
        result.Error!.Value.SqlErrorCode.Should().Be(50003);
    }

    #endregion

    #region CE04: 에러 복구 흐름

    /// <summary>
    /// 50001 에러 수신 후, 주문을 INSERT하고 다시 VALIDATE하면 성공하는 복구 흐름을 검증한다.
    /// </summary>
    [Fact]
    public async Task CE04_Custom_Error_RecoveryFlow_ShouldRecoverAfterError()
    {
        // Step 1 — 존재하지 않는 OrderId로 VALIDATE → 50001 에러
        DbResult<int> errorResult = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = 99999, Action = "VALIDATE" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        errorResult.IsSuccess.Should().BeFalse();
        errorResult.Error!.Value.SqlErrorCode.Should().Be(50001);

        // Step 2 — 사용자 + 주문 삽입하여 복구
        string uniqueEmail = $"recovery_{Guid.NewGuid():N}@test.com";
        DbResult<int> insertUserResult = await _db
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "RecoveryUser", Email = uniqueEmail, Age = (int?)30 })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        insertUserResult.IsSuccess.Should().BeTrue();
        int newUserId = insertUserResult.Value;

        // Orders 테이블에 직접 주문 삽입
        DbResult<int> insertOrderResult = await _db
            .Sql($"INSERT INTO [core].[Orders] (UserId, ProductId, Quantity, TotalPrice) VALUES ({newUserId}, 1, 1, 100.00); SELECT CAST(SCOPE_IDENTITY() AS INT);")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        insertOrderResult.IsSuccess.Should().BeTrue();
        int newOrderId = insertOrderResult.Value;

        // Step 3 — 새 OrderId로 VALIDATE → 이번엔 성공
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> successResult = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = newOrderId, Action = "VALIDATE" })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        successResult.IsSuccess.Should().BeTrue();

        // Cleanup
        await _db.Sql($"DELETE FROM [core].[Orders] WHERE OrderId = {newOrderId}").ExecuteAsync(TestContext.Current.CancellationToken);
        await _db.Sql($"DELETE FROM [core].[Users] WHERE Email = '{uniqueEmail}'").ExecuteAsync(TestContext.Current.CancellationToken);
    }

    #endregion

    #region CE05: 한국어 메시지 전파

    /// <summary>
    /// 50001 에러의 InnerException.Message에 한국어 '주문' 키워드가 포함되는지 검증한다.
    /// <para>Lib.Db는 DbError.Message를 표준 형식으로 래핑하므로,
    /// 원본 SQL THROW 메시지는 InnerException에서 확인한다.</para>
    /// </summary>
    [Fact]
    public async Task CE05_Custom_Error_MessagePropagation_ShouldContainKorean()
    {
        // Act
        DbResult<int> result = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = 99999, Action = "VALIDATE" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();

        // Lib.Db가 래핑한 메시지에서 "사용자 정의 오류" 키워드 확인
        result.Error!.Value.Message.Should().Contain("사용자 정의 오류");

        // InnerException에서 원본 한국어 THROW 메시지 확인
        result.Error!.Value.InnerException.Should().NotBeNull();
        result.Error!.Value.InnerException!.Message.Should().Contain("주문",
            "SQL THROW 원본 메시지에 한국어 '주문' 키워드가 포함되어야 합니다.");
    }

    #endregion
}
