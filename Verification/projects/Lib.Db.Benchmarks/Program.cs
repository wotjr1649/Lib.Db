// ============================================================================
// 파일: Benchmarks/Lib.Db.Benchmarks/Program.cs
// 설명: Lib.Db v2.3 런타임 TVP BenchmarkDotNet 진입점
// 대상: .NET 10 / C# 14
// ============================================================================

using BenchmarkDotNet.Running;
using Lib.Db.Benchmarks.Database;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(static arg => string.Equals(arg, "--list-db-objects", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string connectionString = BenchmarkDatabase.GetConnectionString();
                IReadOnlyList<BenchmarkDatabase.DatabaseObjectInfo> objects =
                    await BenchmarkDatabase.ListUserObjectsAsync(connectionString, CancellationToken.None).ConfigureAwait(false);

                foreach (BenchmarkDatabase.DatabaseObjectInfo item in objects)
                    Console.WriteLine($"{item.ObjectType}\t{item.SchemaName}.{item.ObjectName}");

                Console.WriteLine($"COUNT={objects.Count}");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (SqlException ex)
            {
                Console.Error.WriteLine($"SQL Server object listing failed. ErrorNumber={ex.Number}, State={ex.State}, Class={ex.Class}. {ex.Message}");
                return 3;
            }
        }

        if (args.Any(static arg => string.Equals(arg, "--setup-only", StringComparison.OrdinalIgnoreCase)) ||
            args.Any(static arg => string.Equals(arg, "--setup-full-matrix", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string connectionString = BenchmarkDatabase.GetConnectionString();
                BenchmarkDatabaseSetupMode setupMode = args.Any(static arg => string.Equals(arg, "--setup-full-matrix", StringComparison.OrdinalIgnoreCase))
                    ? BenchmarkDatabaseSetupMode.FullMatrix
                    : BenchmarkDatabaseSetupMode.NarrowWideOnly;

                await BenchmarkDatabase.ResetAsync(connectionString, CancellationToken.None, setupMode).ConfigureAwait(false);
                Console.WriteLine($"Benchmark database objects are ready. SetupMode={setupMode}.");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (SqlException ex)
            {
                Console.Error.WriteLine($"SQL Server benchmark setup failed. ErrorNumber={ex.Number}, State={ex.State}, Class={ex.Class}. {ex.Message}");
                return 3;
            }
        }

        if (RequiresBenchmarkDatabase(args))
        {
            try
            {
                _ = BenchmarkDatabase.GetConnectionString();
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }

        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, TvpBenchmarkConfig.Create());

        return 0;
    }

    private static bool RequiresBenchmarkDatabase(string[] args)
    {
        if (args.Length == 0)
            return true;

        return !args.Any(static arg =>
            string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--info", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase));
    }
}
