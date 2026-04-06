// ============================================================================
// 파일: VerificationDb/PerformanceTests.cs
// 설명: 고동시성 부하 테스트 (TestSuite 25_PerformanceTests 이관)
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
using System.Diagnostics;

namespace Lib.Db.IntegrationTests.VerificationDb;

/// <summary>
/// 고동시성 환경에서의 안정성 및 처리량을 검증하는 성능 테스트.
/// <para><b>[설계 의도]</b> TestSuite의 25_PerformanceTests를 IntegrationTests로 이관하고,
/// MultiDbFixture의 DI 컨테이너를 통한 스코프 격리 동시성 테스트를 수행한다.</para>
/// </summary>
[Collection("MultiDb")]
public sealed class PerformanceTests(MultiDbFixture fixture, ITestOutputHelper output)
{
    #region 필드 선언 (C# 14)

    private readonly IServiceProvider _services = fixture.Services;
    private readonly ITestOutputHelper _output = output;

    #endregion

    #region 고동시성 테스트

    /// <summary>
    /// 50개 동시 쿼리를 실행하여 고동시성 환경에서의 안정성을 검증한다.
    /// </summary>
    [Fact]
    public async Task HighConcurrency_50Queries_ShouldHandleLoad()
    {
        // Arrange
        const int concurrentQueries = 50;
        _output.WriteLine($"=== 고동시성 테스트 시작: {concurrentQueries}개 동시 쿼리 ===");

        // Act
        Stopwatch sw = Stopwatch.StartNew();
        List<Task> tasks = new(concurrentQueries);

        for (int i = 0; i < concurrentQueries; i++)
        {
            int taskId = i;
            tasks.Add(Task.Run(async () =>
            {
                using IServiceScope scope = _services.CreateScope();
                IDbSession db = scope.ServiceProvider.GetRequiredService<IDbSession>();

                DbResult<int> result = await db.Use("Verification")
                    .Sql("SELECT @TaskId AS TaskId")
                    .With(new { TaskId = taskId })
                    .ExecuteScalarAsync<int>();

                result.IsSuccess.Should().BeTrue($"TaskId={taskId} 쿼리가 성공해야 합니다.");
                result.Value.Should().Be(taskId);
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        // Output
        double qps = concurrentQueries / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"완료: {concurrentQueries}개 쿼리, {sw.Elapsed.TotalSeconds:F2}초");
        _output.WriteLine($"QPS: {qps:F0} queries/sec");
        _output.WriteLine("=== 고동시성 테스트 완료 ===");
    }

    #endregion
}
