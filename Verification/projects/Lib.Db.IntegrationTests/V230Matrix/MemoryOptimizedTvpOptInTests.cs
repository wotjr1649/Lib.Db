// ============================================================================
// 파일: V230Matrix/MemoryOptimizedTvpOptInTests.cs
// 설명: v2.3.0 memory-optimized TVP 별도 opt-in 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;
namespace Lib.Db.IntegrationTests.V230Matrix;

[Collection("MultiDb")]
public sealed class MemoryOptimizedTvpOptInTests(MultiDbFixture fixture)
{
    [Fact]
    public void SqlcmdOptInScript_ShouldComposeSetupAndVerifyFiles()
    {
        _ = fixture.GetConnectionString(TestConnectionStrings.Benchmark);

        string scriptPath = SqlScriptRunner.ResolveScriptPath("run-libdb-bench-memory-optimized-tvp-optin.sql");
        string script = File.ReadAllText(scriptPath);

        script.Contains(":ON ERROR EXIT", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        script.Contains(":r .\\setup-libdb-bench-memory-optimized-tvp-optin.sql", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        script.Contains(":r .\\verify-libdb-bench-memory-optimized-tvp-optin.sql", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void DefaultAllVerificationScript_ShouldNotRunMemoryOptimizedTvpSetup()
    {
        string scriptPath = SqlScriptRunner.ResolveScriptPath("verify-libdb-all.migration-reference.sql");
        string script = File.ReadAllText(scriptPath);

        script.Contains("verify-libdb-bench-test.sql", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        script.Contains("setup-libdb-bench-memory-optimized-tvp-optin.sql", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        script.Contains("run-libdb-bench-memory-optimized-tvp-optin.sql", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        script.Contains("MEMORY_OPTIMIZED_DATA", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }
}
