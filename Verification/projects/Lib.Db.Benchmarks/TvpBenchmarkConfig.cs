// ============================================================================
// 파일: Benchmarks/Lib.Db.Benchmarks/TvpBenchmarkConfig.cs
// 설명: BenchmarkDotNet 리포트/진단 설정
// 대상: .NET 10 / C# 14
// ============================================================================

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace Lib.Db.Benchmarks;

public static class TvpBenchmarkConfig
{
    private const string JobEnvironmentVariableName = "LIBDB_BENCHMARK_JOB";
    private const string ArtifactsEnvironmentVariableName = "LIBDB_BENCHMARK_ARTIFACTS_PATH";

    public static IConfig Create()
    {
        ManualConfig config = ManualConfig.CreateEmpty();
        config.ArtifactsPath = GetArtifactsPath();

        return config
            .AddJob(CreateJob())
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(CsvExporter.Default)
            .AddLogger(ConsoleLogger.Default);
    }

    private static Job CreateJob()
    {
        string? jobName = Environment.GetEnvironmentVariable(JobEnvironmentVariableName);
        if (string.Equals(jobName, "Dry", StringComparison.OrdinalIgnoreCase))
            return Job.Dry.WithId("Dry");

        if (string.Equals(jobName, "Short", StringComparison.OrdinalIgnoreCase))
            return Job.ShortRun.WithId("ShortRealSqlServer");

        return Job.Default.WithId("RealSqlServer");
    }

    private static string GetArtifactsPath()
    {
        string? configuredPath = Environment.GetEnvironmentVariable(ArtifactsEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(configuredPath);

        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lib.Db.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "Verification")))
            {
                return Path.Combine(
                    current.FullName,
                    "Verification",
                    "artifacts",
                    "benchmarks",
                    "BenchmarkDotNet.Artifacts");
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "Verification",
            "artifacts",
            "benchmarks",
            "BenchmarkDotNet.Artifacts"));
    }
}
