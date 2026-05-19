// ============================================================================
// 파일: V230Matrix/V230TvpMatrixTests.cs
// 설명: v2.3.0 검증 DB의 TVP 저장 프로시저 전체 실행 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.V230Matrix;

[Collection("MultiDb")]
public sealed class V230TvpMatrixTests(MultiDbFixture fixture)
{
    [Fact]
    public async Task StressDatabase_ShouldExecuteEveryTvpProcedure()
    {
        TvpMatrixRunSummary summary = await TvpMatrixProcedureHarness.ExecuteAllAsync(
            fixture.Stress,
            fixture.GetConnectionString(TestConnectionStrings.Stress),
            TestContext.Current.CancellationToken);

        summary.DiscoveredProcedures.Should().BeGreaterThan(0);
        summary.UnexpectedFailures.Should().BeEmpty();
        summary.ExecutedProcedures.Should().Be(summary.DiscoveredProcedures);
    }

    [Fact]
    public async Task ChaosDatabase_ShouldExecuteEveryTvpProcedure()
    {
        TvpMatrixRunSummary summary = await TvpMatrixProcedureHarness.ExecuteAllAsync(
            fixture.Chaos,
            fixture.GetConnectionString(TestConnectionStrings.Chaos),
            TestContext.Current.CancellationToken);

        summary.DiscoveredProcedures.Should().BeGreaterThan(0);
        summary.UnexpectedFailures.Should().BeEmpty();
        summary.ExecutedProcedures.Should().Be(summary.DiscoveredProcedures);
    }

    [Fact]
    public async Task BenchmarkDatabase_ShouldExecuteEveryTvpProcedure()
    {
        TvpMatrixRunSummary summary = await TvpMatrixProcedureHarness.ExecuteAllAsync(
            fixture.Benchmark,
            fixture.GetConnectionString(TestConnectionStrings.Benchmark),
            TestContext.Current.CancellationToken);

        summary.DiscoveredProcedures.Should().BeGreaterThan(0);
        summary.UnexpectedFailures.Should().BeEmpty();
        summary.ExecutedProcedures.Should().Be(summary.DiscoveredProcedures);
    }

    [Fact]
    public void DefaultAllVerificationScript_ShouldNotRunServerLevelChaos()
    {
        string scriptPath = SqlScriptRunner.ResolveScriptPath("verify-libdb-all.sql");
        string script = File.ReadAllText(scriptPath);

        script.Contains("chaos-server-optin", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        script.Contains("KILL ", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        script.Contains("ALTER SERVER", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }
}
