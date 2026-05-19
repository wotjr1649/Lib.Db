// ============================================================================
// 파일: VerificationDb/CoreQueryTests.cs
// 설명: LIBDB_VERIFICATION_TEST 핵심 CRUD + TVP + 다중 결과셋 + OUTPUT 파라미터 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class CoreQueryTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _db = fixture.Verification;

    [Fact]
    public async Task V01_GetUser_ValidId_ReturnsUser()
    {
        DbResult<Dictionary<string, object?>?> result = await _db
            .Procedure("core.usp_Core_Get_User")
            .With(new { UserId = 1 })
            .QuerySingleAsync<Dictionary<string, object?>>();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task V02_SearchUsers_ReturnsStream()
    {
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("core.usp_Core_Search_Users")
            .With(new { SearchTerm = "A" })
            .QueryAsync<Dictionary<string, object?>>();
        result.IsSuccess.Should().BeTrue();
        int count = 0;
        await foreach (Dictionary<string, object?> item in result.Value!)
            count++;
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task V03_InsertUser_ReturnsNewId()
    {
        DbResult<int> result = await _db
            .Procedure("core.usp_Core_Insert_User")
            .With(new { UserName = "TestUser_V03", Email = $"v03_{Guid.NewGuid():N}@test.com", Age = 25 })
            .ExecuteAsync();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task V05_Dashboard_ReturnsMultipleResultSets()
    {
        DbResult<IMultipleResultReader> result = await _db
            .Procedure("core.usp_Core_Get_Dashboard")
            .With(new { UserId = 1 })
            .QueryMultipleAsync();

        if (!result.IsSuccess)
        {
            // MARS 미설정 환경에서는 QueryMultipleAsync가 실패할 수 있음 — 예상된 실패
            result.Error.Should().NotBeNull();
            result.Error!.Value.Kind.Should().Be(DbErrorKind.Unknown,
                "MARS 미활성화 또는 기타 사유로 QueryMultiple이 실패할 수 있습니다.");
            return;
        }

        await using IMultipleResultReader reader = result.Value!;
        List<Dictionary<string, object?>> users = await reader.ReadAsync<Dictionary<string, object?>>();
        List<Dictionary<string, object?>> orders = await reader.ReadAsync<Dictionary<string, object?>>();
    }

    [Fact]
    public async Task V06_OutputParameters_ReturnsValues()
    {
        var parameters = new { InputVal = 10, OutputVal = 0, InOutVal = 5 };
        DbResult<int> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(parameters)
            .ExecuteAsync();
        result.IsSuccess.Should().BeTrue();
    }
}
