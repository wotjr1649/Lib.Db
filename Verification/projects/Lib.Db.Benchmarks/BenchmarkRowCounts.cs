// ============================================================================
// File: Benchmarks/Lib.Db.Benchmarks/BenchmarkRowCounts.cs
// Description: Shared BenchmarkDotNet row-count matrix.
// Target: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.Benchmarks;

internal static class BenchmarkRowCounts
{
    public static IEnumerable<int> Values
    {
        get
        {
            yield return 100;
            yield return 1_000;
            yield return 10_000;

            if (string.Equals(
                Environment.GetEnvironmentVariable("LIBDB_BENCHMARK_SCALE"),
                "Full",
                StringComparison.OrdinalIgnoreCase))
            {
                yield return 100_000;
            }
        }
    }
}
