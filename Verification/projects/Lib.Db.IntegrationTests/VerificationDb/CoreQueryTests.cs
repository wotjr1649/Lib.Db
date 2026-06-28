// ============================================================================
// 파일: VerificationDb/CoreQueryTests.cs
// 설명: LIBDB_VERIFICATION_TEST 핵심 CRUD + TVP + 다중 결과셋 + OUTPUT 파라미터 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.IntegrationTests.Infrastructure;
using Lib.Db.Contracts.Execution;
using Lib.Db.Extensions;
using Microsoft.Data.SqlClient;

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
            .QuerySingleAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task V02_SearchUsers_ReturnsStream()
    {
        DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await _db
            .Procedure("core.usp_Core_Search_Users")
            .With(new { SearchTerm = "A" })
            .QueryAsync<Dictionary<string, object?>>(TestContext.Current.CancellationToken);
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
            .ExecuteAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task V05_Dashboard_CanIgnoreUnconsumedStoredProcedureResultSets()
    {
        DbResult<IMultipleResultReader> result = await _db
            .Procedure("core.usp_Core_Get_Dashboard")
            .With(new { UserId = 1 })
            .QueryMultipleAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue("release verification must provide MARS-capable QueryMultiple connections");

        await using (IMultipleResultReader reader = result.Value!)
        {
            List<DashboardUserInfo> users = await reader.ReadAsync<DashboardUserInfo>(TestContext.Current.CancellationToken);
            List<DashboardOrder> orders = await reader.ReadAsync<DashboardOrder>(TestContext.Current.CancellationToken);

            users.Should().ContainSingle(user => user.UserId == 1);
            orders.Should().NotBeNull();
        }

        DbResult<int> ping = await _db
            .Sql("SELECT CAST(1 AS INT)")
            .With(new { })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

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
            .QueryMultipleAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue("extra result sets must be ignored only after QueryMultiple succeeds");

        await using (IMultipleResultReader reader = result.Value!)
        {
            ResultSetValue? first = await reader.ReadSingleAsync<ResultSetValue>(TestContext.Current.CancellationToken);
            ResultSetValue? second = await reader.ReadSingleAsync<ResultSetValue>(TestContext.Current.CancellationToken);
            ResultSetValue? third = await reader.ReadSingleAsync<ResultSetValue>(TestContext.Current.CancellationToken);
            ResultSetValue? fourth = await reader.ReadSingleAsync<ResultSetValue>(TestContext.Current.CancellationToken);

            first.Should().BeEquivalentTo(new ResultSetValue(1));
            second.Should().BeEquivalentTo(new ResultSetValue(2));
            third.Should().BeEquivalentTo(new ResultSetValue(3));
            fourth.Should().BeEquivalentTo(new ResultSetValue(4));
        }

        DbResult<int> ping = await _db
            .Sql("SELECT CAST(9 AS INT)")
            .With(new { })
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        ping.IsSuccess.Should().BeTrue("the fifth result set is intentionally not mapped by the C# layer");
        ping.Value.Should().Be(9);
    }

    [Fact]
    public async Task V05C_QueryMultiple_ProcedureThrowBeforeFirstResultSet_ShouldPreserveSqlErrorCode()
    {
        DbResult<DbMultiple<ResultSetValue, ResultSetValue>> result = await _db
            .Procedure("test.usp_Error_Custom_50001")
            .With(new { OrderId = 99999, Action = "VALIDATE" })
            .QueryMultipleAsync(TestContext.Current.CancellationToken)
            .ReadMultipleAsync<ResultSetValue, ResultSetValue>(TestContext.Current.CancellationToken);

        AssertMultipleReadFailure(result, 50001, DbErrorKind.UserDefined);
    }

    [Fact]
    public async Task V05D_QueryMultiple_ThrowDuringSecondResultSet_ShouldPreserveSqlErrorCode()
    {
        DbResult<DbMultiple<ResultSetValue, ResultSetValue>> result = await _db
            .Sql("""
                SET NOCOUNT ON;
                SELECT CAST(1 AS INT) AS [Value];
                THROW 51740, N'Lib.Db multi-result verification failure.', 1;
                """)
            .With(new { })
            .QueryMultipleAsync(TestContext.Current.CancellationToken)
            .ReadMultipleAsync<ResultSetValue, ResultSetValue>(TestContext.Current.CancellationToken);

        AssertMultipleReadFailure(result, 51740, DbErrorKind.UserDefined);
    }

    [Fact]
    public async Task V05E_QueryMultiple_ConstraintDuringSecondResultSet_ShouldPreserveSqlErrorCode()
    {
        DbResult<DbMultiple<ResultSetValue, ResultSetValue>> result = await _db
            .Sql("""
                SET NOCOUNT ON;
                SELECT CAST(1 AS INT) AS [Value];
                CREATE TABLE #LibDbV261Dup ([Id] INT NOT NULL PRIMARY KEY);
                INSERT INTO #LibDbV261Dup ([Id]) VALUES (1);
                INSERT INTO #LibDbV261Dup ([Id]) VALUES (1);
                SELECT CAST(2 AS INT) AS [Value];
                """)
            .With(new { })
            .QueryMultipleAsync(TestContext.Current.CancellationToken)
            .ReadMultipleAsync<ResultSetValue, ResultSetValue>(TestContext.Current.CancellationToken);

        AssertMultipleReadFailure(result, 2627, DbErrorKind.ConstraintViolation);
    }
    [Fact]
    public async Task V06_OutputParameters_ReturnsValues()
    {
        var outputVal = new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };
        var inOutVal = new SqlParameter("@InOutVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.InputOutput,
            Value = 5
        };

        var parameters = new { InputVal = 10, OutputVal = outputVal, InOutVal = inOutVal };
        DbResult<int> result = await _db
            .Procedure("adv.usp_Adv_OutputParameters")
            .With(parameters)
            .ExecuteAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        Convert.ToInt32(outputVal.Value).Should().Be(20);
        Convert.ToInt32(inOutVal.Value).Should().Be(15);
    }

    private static void AssertMultipleReadFailure<T>(DbResult<T> result, int sqlErrorCode, DbErrorKind expectedKind)
    {
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
        result.Error.Value.SqlErrorCode.Should().Be(sqlErrorCode);
        result.Error.Value.Kind.Should().Be(expectedKind);
        result.Error.Value.InnerException.Should().BeNull();
    }

    private sealed record ResultSetValue(int Value);
}
