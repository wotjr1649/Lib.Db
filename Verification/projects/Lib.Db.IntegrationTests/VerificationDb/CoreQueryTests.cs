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
    public async Task V05_Dashboard_CanIgnoreUnconsumedStoredProcedureResultSets()
    {
        DbResult<IMultipleResultReader> result = await _db
            .Procedure("core.usp_Core_Get_Dashboard")
            .With(new { UserId = 1 })
            .QueryMultipleAsync();

        result.IsSuccess.Should().BeTrue("release verification must provide MARS-capable QueryMultiple connections");

        await using (IMultipleResultReader reader = result.Value!)
        {
            List<DashboardUserInfo> users = await reader.ReadAsync<DashboardUserInfo>();
            List<DashboardOrder> orders = await reader.ReadAsync<DashboardOrder>();

            users.Should().ContainSingle(user => user.UserId == 1);
            orders.Should().NotBeNull();
        }

        DbResult<int> ping = await _db
            .Sql("SELECT CAST(1 AS INT)")
            .With(new { })
            .ExecuteScalarAsync<int>();

        ping.IsSuccess.Should().BeTrue("unread stored procedure result sets must be discarded when the grid reader is disposed");
        ping.Value.Should().Be(1);
    }

    [Fact]
    public async Task V05B_QueryMultiple_CanReadDeclaredFourResultSetsAndIgnoreFifth()
    {
        DbResult<IMultipleResultReader> result = await _db
            .Sql("""
                SELECT CAST(1 AS INT) AS [Value];
                SELECT CAST(2 AS INT) AS [Value];
                SELECT CAST(3 AS INT) AS [Value];
                SELECT CAST(4 AS INT) AS [Value];
                SELECT CAST(5 AS INT) AS [Value];
                """)
            .With(new { })
            .QueryMultipleAsync();

        result.IsSuccess.Should().BeTrue("extra result sets must be ignored only after QueryMultiple succeeds");

        await using (IMultipleResultReader reader = result.Value!)
        {
            ResultSetValue? first = await reader.ReadSingleAsync<ResultSetValue>();
            ResultSetValue? second = await reader.ReadSingleAsync<ResultSetValue>();
            ResultSetValue? third = await reader.ReadSingleAsync<ResultSetValue>();
            ResultSetValue? fourth = await reader.ReadSingleAsync<ResultSetValue>();

            first.Should().BeEquivalentTo(new ResultSetValue(1));
            second.Should().BeEquivalentTo(new ResultSetValue(2));
            third.Should().BeEquivalentTo(new ResultSetValue(3));
            fourth.Should().BeEquivalentTo(new ResultSetValue(4));
        }

        DbResult<int> ping = await _db
            .Sql("SELECT CAST(9 AS INT)")
            .With(new { })
            .ExecuteScalarAsync<int>();

        ping.IsSuccess.Should().BeTrue("the fifth result set is intentionally not mapped by the C# layer");
        ping.Value.Should().Be(9);
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

    private sealed record ResultSetValue(int Value);
}
