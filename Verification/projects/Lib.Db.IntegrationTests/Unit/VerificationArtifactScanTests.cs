// ============================================================================
// 파일: Unit/VerificationArtifactScanTests.cs
// 설명: 검증 artifact secret scan 스크립트 회귀 테스트
// 대상: .NET 10
// ============================================================================

using System.Diagnostics;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class VerificationArtifactScanTests
{
    [Fact]
    public async Task Scanner_ShouldRejectConcretePasswordlessConnectionString()
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, "report.json");
        await File.WriteAllTextAsync(
            file,
            """
            {
              "ConnectionString": "Server=prod-sql.internal;Database=CustomerLedger;Encrypt=True;TrustServerCertificate=False"
            }
            """,
            TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("Potential verification artifact secret pattern paths");
        result.Output.Should().Contain("report.json");
    }

    [Theory]
    [InlineData(
        """
        {
          "ConnectionStrings": {
            "Verification": "Server=prod-sql.internal;Database=CustomerLedger;Encrypt=True;TrustServerCertificate=False"
          }
        }
        """)]
    [InlineData("LIBDB_BENCHMARK_CONNECTION=Server=prod-sql.internal;Database=CustomerLedger;Encrypt=True;TrustServerCertificate=False")]
    [InlineData("ConnectionStrings__Verification=Server=prod-sql.internal;Database=CustomerLedger;Encrypt=True;TrustServerCertificate=False")]
    public async Task Scanner_ShouldRejectNestedAndEnvironmentStyleConnectionStrings(string content)
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, "report.txt");
        await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("report.txt");
    }

    [Fact]
    public async Task Scanner_ShouldRejectMixedNestedLocalAndRemoteConnectionStrings()
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, "report.json");
        await File.WriteAllTextAsync(
            file,
            """
            {
              "ConnectionStrings": {
                "Local": "Server=localhost;Database=TEST;Integrated Security=True;Encrypt=True",
                "Remote": "Server=prod-sql.internal;Database=CustomerLedger;Encrypt=True;TrustServerCertificate=False"
              }
            }
            """,
            TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("report.json");
    }

    [Theory]
    [InlineData("""{ "ConnectionString": "redacted" }""")]
    [InlineData("""{ "ConnectionStrings": "placeholder" }""")]
    [InlineData("""connection string: Server=localhost;Database=TEST;Integrated Security=True;Encrypt=True""")]
    [InlineData("""{ "ConnectionStrings": { "Verification": "Server=localhost;Database=TEST;Integrated Security=True;Encrypt=True" } }""")]
    [InlineData("LIBDB_BENCHMARK_CONNECTION=Server=localhost;Database=TEST;Integrated Security=True;Encrypt=True")]
    [InlineData("""{ "ConnectionString": "Server=127.0.0.1;Database=TEST;Integrated Security=True;Encrypt=True" }""")]
    [InlineData("""{ "ConnectionString": "Server=.;Database=TEST;Integrated Security=True;Encrypt=True" }""")]
    [InlineData("""{ "ConnectionString": "Server=(local);Database=TEST;Integrated Security=True;Encrypt=True" }""")]
    [InlineData("""{ "ConnectionString": "Server=(localdb);Database=TEST;Integrated Security=True;Encrypt=True" }""")]
    [InlineData("""{ "ConnectionString": "Server=(localdb)\MSSQLLocalDB;Database=TEST;Integrated Security=True;Encrypt=True" }""")]
    public async Task Scanner_ShouldAllowRedactedPlaceholderAndLocalOnlyConnectionStrings(string content)
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, "report.json");
        await File.WriteAllTextAsync(file, content, TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("No verification artifact secret pattern paths found.");
    }

    private static async Task<ProcessResult> RunScannerAsync(string artifactPath)
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot.FullName, "Verification", "scripts", "Scan-VerificationArtifacts.ps1");

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("-Paths");
        process.StartInfo.ArgumentList.Add(artifactPath);

        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return new ProcessResult(process.ExitCode, output + error);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lib.Db.slnx")))
                return current;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Lib.Db repository root could not be found.");
    }

    private sealed class TemporaryArtifactRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LibDbArtifactScanTests",
            Guid.NewGuid().ToString("N"));

        public TemporaryArtifactRoot()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private readonly record struct ProcessResult(int ExitCode, string Output);
}
