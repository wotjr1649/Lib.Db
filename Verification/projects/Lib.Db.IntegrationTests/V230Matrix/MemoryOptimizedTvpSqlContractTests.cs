// ============================================================================
// 파일: V230Matrix/MemoryOptimizedTvpSqlContractTests.cs
// 설명: v2.3.0 memory-optimized TVP SQL 파일 경계 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.IntegrationTests.Infrastructure;

namespace Lib.Db.IntegrationTests.V230Matrix;

public sealed class MemoryOptimizedTvpSqlContractTests
{
    [Fact]
    public void DefaultBenchmarkVerifyScript_ShouldNotRequireMemoryOptimizedTvpObjects()
    {
        string scriptPath = SqlScriptRunner.ResolveScriptPath("verify-libdb-bench-test.sql");
        string script = File.ReadAllText(scriptPath);

        script.Should().NotContain("libdb_bench_MemoryOptimizedOrderItem");
        script.Should().NotContain("libdb_bench_MemoryOptimizedOrderItems");
        script.Should().NotContain("libdb_bench_InsertMemoryOptimizedOrderItems");
        script.Should().NotContain("MEMORY_OPTIMIZED_DATA");
        script.Should().NotContain("is_memory_optimized");
    }

    [Fact]
    public void OptInBenchmarkVerifyScript_ShouldOwnMemoryOptimizedTvpObjects()
    {
        string scriptPath = SqlScriptRunner.ResolveScriptPath("verify-libdb-bench-memory-optimized-tvp-optin.sql");
        string script = File.ReadAllText(scriptPath);

        script.Should().Contain("libdb_bench_MemoryOptimizedOrderItem");
        script.Should().Contain("libdb_bench_MemoryOptimizedOrderItems");
        script.Should().Contain("libdb_bench_InsertMemoryOptimizedOrderItems");
        script.Should().Contain("MEMORY_OPTIMIZED_DATA");
        script.Should().Contain("is_memory_optimized");
    }
}
