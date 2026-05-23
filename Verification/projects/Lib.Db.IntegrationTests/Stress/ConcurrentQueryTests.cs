// ============================================================================
// 파일: Stress/ConcurrentQueryTests.cs
// 설명: 과부하 + 트랜잭션 스트레스 + 연결 풀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.Stress;

[Collection("MultiDb")]
public sealed class ConcurrentQueryTests(MultiDbFixture fixture)
{
    private readonly IProcedureStage _verification = fixture.Verification;
    private readonly IProcedureStage _sorter = fixture.Sorter;
    private readonly IDbSession _session = fixture.Session;

    [Fact]
    public async Task P01_Concurrent50Queries_AllShouldSucceed()
    {
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(_verification.Sql("SELECT 1").ExecuteScalarAsync<int>(TestContext.Current.CancellationToken));
        }

        DbResult<int>[] results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task P02_Concurrent10Writes_NoDeadlock()
    {
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 10; i++)
        {
            int idx = i;
            tasks.Add(_sorter
                .Procedure("IF_SP_CHUTE_BTN_LOG")
                .With(new { V_CHUTE_NO = $"{idx:D3}", V_STATUS = "TEST" })
                .ExecuteAsync(TestContext.Current.CancellationToken));
        }

        DbResult<int>[] results = await Task.WhenAll(tasks);
        int successCount = results.Count(r => r.IsSuccess);
        successCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task P03_CrossDb_Parallel50_AllSucceed()
    {
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 25; i++)
        {
            tasks.Add(_verification.Sql("SELECT 1").ExecuteScalarAsync<int>(TestContext.Current.CancellationToken));
            tasks.Add(_sorter.Sql("SELECT 1").ExecuteScalarAsync<int>(TestContext.Current.CancellationToken));
        }

        DbResult<int>[] results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task P04_SequentialTransactions_DataIntegrity()
    {
        // Arrange — 5개 고유 이메일 저장
        string[] emails = new string[5];

        for (int i = 0; i < 5; i++)
        {
            emails[i] = $"stress_{Guid.NewGuid():N}@test.com";
            string userName = $"StressUser_{i}";
            string email = emails[i];
            await using IDbTransactionScope tx = await _session.BeginTransactionAsync("Verification", TestContext.Current.CancellationToken);

            // FormattableString SQL 사용 (SP 대신, 트랜잭션 내 직접 INSERT)
            DbResult<int> insertResult = await tx
                .Sql((FormattableString)$"INSERT INTO core.Users (UserName, Email, Age) VALUES ({userName}, {email}, {20 + i})")
                .ExecuteAsync(TestContext.Current.CancellationToken);
            insertResult.IsSuccess.Should().BeTrue();

            if (i % 2 == 0)
                await tx.CommitAsync(TestContext.Current.CancellationToken);
            else
                await tx.RollbackAsync(TestContext.Current.CancellationToken);
        }

        // Assert — 커밋된 이메일(짝수: 0,2,4)만 존재, 롤백된 이메일(홀수: 1,3)은 미존재
        for (int i = 0; i < 5; i++)
        {
            DbResult<int> countResult = await _verification
                .Sql((FormattableString)$"SELECT COUNT(*) FROM core.Users WHERE Email = {emails[i]}")
                .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
            countResult.IsSuccess.Should().BeTrue();

            if (i % 2 == 0)
                countResult.Value.Should().Be(1, $"인덱스 {i}는 커밋됨 — 데이터가 존재해야 합니다.");
            else
                countResult.Value.Should().Be(0, $"인덱스 {i}는 롤백됨 — 데이터가 없어야 합니다.");
        }
    }

    [Fact]
    public async Task M01_TwoDb_Parallel_BothSucceed()
    {
        Task<DbResult<int>> vTask = _verification
            .Sql("SELECT COUNT(*) FROM core.Users")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        Task<DbResult<int>> sTask = _sorter
            .Sql("SELECT COUNT(*) FROM IF_CHUTE_INFO")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);

        DbResult<int>[] results = await Task.WhenAll(vTask, sTask);

        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeTrue();
        results[0].Value.Should().BeGreaterThan(0);
        results[1].Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task M02_DefaultInstance_ShouldBeVerification()
    {
        DbResult<int> result = await _session.Default
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>(TestContext.Current.CancellationToken);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }
}
