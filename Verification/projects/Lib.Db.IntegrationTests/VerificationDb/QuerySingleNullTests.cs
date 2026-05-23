// ============================================================================
// 파일: VerificationDb/QuerySingleNullTests.cs
// 설명: QuerySingleAsync가 0행 결과를 null로 반환하는지 검증하는 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// QuerySingleAsync 호출 시 0행 결과가 Value=null로 반환되는지 검증하는 테스트.
/// <para><b>[설계 의도]</b> SP가 결과를 반환하지 않는 경우(존재하지 않는 UserId),
/// DbResult.IsSuccess=true이면서 Value=null인 정상 흐름을 검증한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class QuerySingleNullTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;

    #endregion

    #region QSN01: 존재하지 않는 UserId — Value가 null

    /// <summary>
    /// core.usp_Core_Get_User에 존재하지 않는 UserId=99999를 전달하면
    /// IsSuccess=true이고 Value=null인지 검증한다.
    /// </summary>
    [Fact]
    public async Task QuerySingleAsync_NonExistentUser_ReturnsNull()
    {
        // Act — 존재하지 않는 UserId
        DbResult<Dictionary<string, object?>?> result = await _db
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 99999 })
            .QuerySingleAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);

        // Assert — 0행이면 IsSuccess=true, Value=null
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull("존재하지 않는 UserId에 대해 0행 결과는 null이어야 합니다.");
    }

    #endregion
}
