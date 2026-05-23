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
        result.Output.Should().Contain("Potential verification artifact secret markers");
        result.Output.Should().Contain("report.json");
        result.Output.Should().Contain("Marker: ConnectionString");
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
    [InlineData("Lib.Db.nuspec")]
    [InlineData("metadata.psmdcp")]
    [InlineData(".rels")]
    public async Task Scanner_ShouldRejectNuGetMetadataTextFiles(string fileName)
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, fileName);
        await File.WriteAllTextAsync(
            file,
            """
            <metadata>
              <repository url="https://github.com/example/lib-db" />
              <connectionString>Server=prod-sql.internal;Database=CustomerLedger;Encrypt=True;TrustServerCertificate=False</connectionString>
            </metadata>
            """,
            TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain(fileName);
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
        result.Output.Should().Contain("No verification artifact secret markers found.");
    }

    [Fact]
    public async Task Scanner_ShouldRejectBroadenedSensitiveMarkersWithoutEchoingValues()
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, "report.log");
        string[] values = Enumerable.Range(0, 14)
            .Select(index => $"fixture-{index}-{Guid.NewGuid():N}")
            .ToArray();

        await File.WriteAllLinesAsync(
            file,
            [
                $"Password={values[0]}",
                $"AccessToken={values[1]}",
                $"ApiKey={values[2]}",
                $"ClientSecret={values[3]}",
                $"Bearer {values[4]}",
                $"SharedAccessSignature={values[5]}",
                $"SignedUrl=https://account.blob.core.windows.net/container/blob.txt?sv=2025-01-01&sig={values[6]}",
                $"SignedUrlReversed=https://account.blob.core.windows.net/container/blob.txt?sig={values[7]}&sv=2025-01-01",
                $"SqlParameterValue={values[8]}",
                $"RowValue={values[9]}",
                $"CachePayload={values[10]}",
                $"TenantId={values[11]}",
                $"UserId={values[12]}",
                $"EmailAddress={values[13]}"
            ],
            TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("Marker: Password");
        result.Output.Should().Contain("Marker: Token");
        result.Output.Should().Contain("Marker: ApiKey");
        result.Output.Should().Contain("Marker: ClientSecret");
        result.Output.Should().Contain("Marker: Bearer");
        result.Output.Should().Contain("Marker: Sas");
        result.Output.Should().Contain("Marker: SqlParameterValue");
        result.Output.Should().Contain("Marker: RowValue");
        result.Output.Should().Contain("Marker: CachePayload");
        result.Output.Should().Contain("Marker: TenantUserIdentifier");
        foreach (string value in values)
        {
            result.Output.Should().NotContain(value);
        }
    }

    [Fact]
    public async Task Scanner_ShouldFlagShortNumericTenantAndUserIdentifiersWithoutEchoingValues()
    {
        using TemporaryArtifactRoot root = new();
        string file = Path.Combine(root.Path, "numeric-identifiers.log");
        string tenantId = "42";
        string userId = "123";

        await File.WriteAllLinesAsync(
            file,
            [
                $"TenantId={tenantId}",
                $"UserId = {userId}",
                $"\"TenantId\": \"{tenantId}\"",
                "EmailAddress: user@example.invalid"
            ],
            TestContext.Current.CancellationToken);

        ProcessResult result = await RunScannerAsync(root.Path);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("Marker: TenantUserIdentifier");
        result.Output.Should().NotContain(tenantId);
        result.Output.Should().NotContain(userId);
        result.Output.Should().NotContain("user@example.invalid");
    }

    [Fact]
    public async Task ScannerSelfTest_ShouldRunWithoutEchoingFixtureValues()
    {
        ProcessResult result = await RunScannerSelfTestAsync();

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Scanner self-test passed.");
        result.Output.Should().NotContain("fixture-");
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

    private static async Task<ProcessResult> RunScannerSelfTestAsync()
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
        process.StartInfo.ArgumentList.Add("-SelfTest");

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
