// ============================================================================
// 파일: Stress/ConnectionPoolTests.cs
// 설명: 연결 풀 동시 접근 + Max Pool Size 제한 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.Stress;

/// <summary>
/// 연결 풀의 동시 접근 내구성과 Max Pool Size 제한 시 대기/성공 동작을 검증하는 테스트.
/// <para><b>[설계 의도]</b> 대량 동시 쿼리/INSERT와 제한된 풀 크기에서의
/// 안정적 동작을 검증하여 프로덕션 환경의 연결 풀 안정성을 보장한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class ConnectionPoolTests(MultiDbFixture fixture)
{
    #region 필드 선언 (C# 14)

    private readonly IProcedureStage _db = fixture.Verification;
    private readonly IDbSession _session = fixture.Session;
    private readonly string _limitedPoolConnectionString =
        TestConnectionStrings.WithMaxPoolSize(fixture.GetConnectionString(TestConnectionStrings.Verification), 5);

    #endregion

    #region CP01: 100개 동시 쿼리 — 전부 성공

    /// <summary>
    /// 100개의 동시 SELECT 1 쿼리가 모두 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task CP01_100ConcurrentQueries_AllShouldSucceed()
    {
        // Arrange
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(_db.Sql("SELECT 1 AS Val").ExecuteScalarAsync<int>());
        }

        // Act
        DbResult<int>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Assert — 전부 성공, Value == 1
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().Be(1);
        });
    }

    #endregion

    #region CP02: 20개 동시 INSERT — 전부 성공

    /// <summary>
    /// 20개의 동시 usp_Core_Insert_User 호출이 모두 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task CP02_20ConcurrentInserts_AllShouldSucceed()
    {
        // Arrange
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 20; i++)
        {
            int idx = i;
            tasks.Add(_db
                .Procedure("core.usp_Core_Insert_User")
                .With(new { UserName = $"Pool_{idx}", Email = $"pool_{idx}_{Guid.NewGuid():N}@test.com" })
                .ExecuteAsync());
        }

        // Act
        DbResult<int>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Assert — 전부 성공
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    #endregion

    #region CP03: Max Pool Size=5 제한 — 대기 후 성공

    /// <summary>
    /// Max Pool Size=5인 별도 연결 문자열로 10개 동시 쿼리(각 1초 지연)를 실행하여
    /// 5개 연결로 10개를 처리(~2초 소요)하고 전부 성공하는지 검증한다.
    /// </summary>
    [Fact]
    public async Task CP03_ExceedMaxPoolSize_ShouldWaitAndSucceed()
    {
        // Arrange
        List<Task<DbResult<int>>> tasks = [];
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_session.UseConnectionString(_limitedPoolConnectionString)
                .Sql("WAITFOR DELAY '00:00:01'; SELECT 1 AS Val")
                .ExecuteScalarAsync<int>());
        }

        // Act
        DbResult<int>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Assert — 전부 성공 (5개 연결이 10개 쿼리를 순차 처리)
        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().Be(1);
        });
    }

    #endregion
}
