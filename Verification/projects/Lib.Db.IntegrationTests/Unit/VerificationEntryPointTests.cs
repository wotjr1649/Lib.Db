// ============================================================================
// 파일: Unit/VerificationEntryPointTests.cs
// 설명: 검증 테스트 공식 진입점 회귀 테스트
// 대상: .NET 10
// ============================================================================

namespace Lib.Db.IntegrationTests.Unit;

public sealed class VerificationEntryPointTests
{
    [Fact]
    public void VerificationScripts_ShouldLoadLocalEnvironmentBeforeDotnetTest()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string[] scriptNames =
        [
            "Invoke-Tests.ps1",
            "Invoke-Coverage.ps1",
            "Invoke-Benchmarks.ps1",
            "Invoke-Verification.ps1"
        ];

        foreach (string scriptName in scriptNames)
        {
            string scriptPath = Path.Combine(repoRoot.FullName, "Verification", "scripts", scriptName);
            File.Exists(scriptPath).Should().BeTrue("database-backed commands need wrappers that load local environment values");

            string script = File.ReadAllText(scriptPath);
            script.Should().Contain("Set-LibDbVerificationEnvironment.local.ps1");
            script.Should().Contain(". $localEnvironmentScript");
        }

        string testScript = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Invoke-Tests.ps1"));
        testScript.Should().Contain("'dotnet'");
        testScript.Should().Contain("'test'");
        testScript.Should().Contain("Write-SecretSafeEnvironmentSummary");
    }

    [Fact]
    public void PublicVerificationDocs_ShouldHideInternalCommandsWhileInternalReadmeKeepsWrappers()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string verificationReadme = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "README.md"));
        string verificationRunbook = File.ReadAllText(Path.Combine(repoRoot.FullName, "docs", "verification.md"));
        string readme = File.ReadAllText(Path.Combine(repoRoot.FullName, "README.md"));
        string operations = File.ReadAllText(Path.Combine(repoRoot.FullName, "docs", "04_operations.md"));

        verificationReadme.Should().Contain("Invoke-Tests.ps1");
        verificationReadme.Should().Contain("Invoke-Verification.ps1");

        string publicDocs = string.Join(Environment.NewLine, readme, operations, verificationRunbook);
        publicDocs.Should().Contain("not part of the consumer API");
        publicDocs.Should().NotContain("Verification/scripts/");
        publicDocs.Should().NotContain("Invoke-Tests.ps1");
        publicDocs.Should().NotContain("Invoke-Coverage.ps1");
        publicDocs.Should().NotContain("Invoke-Benchmarks.ps1");
        publicDocs.Should().NotContain("Invoke-Verification.ps1");
        publicDocs.Should().NotContain("BenchmarkJob");
    }

    [Fact]
    public void TestProject_ShouldFailFastWhenVerificationEnvironmentIsMissing()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string project = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "projects",
            "Lib.Db.IntegrationTests",
            "Lib.Db.IntegrationTests.csproj"));

        project.Should().Contain("GuardLibDbVerificationTestEnvironment");
        project.Should().Contain("LIBDB_TEST_SQL_PASSWORD");
        project.Should().Contain("Invoke-Tests.ps1");
        project.Should().Contain("LIBDB_SKIP_TEST_ENV_GUARD");
    }

    [Fact]
    public void VerificationManifest_ShouldRegisterTestWrapper()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string manifest = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "manifest.json"));

        manifest.Should().Contain("\"tests\"");
        manifest.Should().Contain("scripts/Invoke-Tests.ps1");
    }

    [Fact]
    public void PublishWorkflow_ShouldUseVerificationWrapperInsteadOfRawDotnetTest()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string workflowRoot = Path.Combine(repoRoot.FullName, ".github", "workflows");
        string[] workflows = Directory.Exists(workflowRoot)
            ? Directory.GetFiles(workflowRoot, "*.yml", SearchOption.TopDirectoryOnly)
            : [];

        workflows.Should().NotBeEmpty();

        string combined = string.Join(Environment.NewLine, workflows.Select(File.ReadAllText));
        combined.Should().Contain("Invoke-Verification.ps1");
        combined.Should().Contain("LIBDB_TEST_SQL_PASSWORD");
        combined.Should().NotContain("dotnet test");
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
}
