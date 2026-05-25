// ============================================================================
// 파일: Unit/ReleasePackageGuardTests.cs
// 설명: NuGet release guard 스크립트/워크플로우 회귀 테스트
// 대상: .NET 10
// ============================================================================

namespace Lib.Db.IntegrationTests.Unit;

public sealed class ReleasePackageGuardTests
{
    [Fact]
    public async Task ReleasePackageScript_ShouldStayDryRunOnlyAndNeverPublishOrTag()
    {
        string script = await ReadRepoFileAsync("Verification", "scripts", "Invoke-ReleasePackage.ps1");

        script.Should().Contain("dotnet nuget verify");
        script.Should().NotContain("dotnet nuget push");
        script.Should().NotContain("nuget push");
        script.Should().NotContain("gh release");
        script.Should().NotContain("git tag");
    }

    [Fact]
    public async Task ReleasePackageScript_ShouldSupportExplicitPackageVersionForTagBuilds()
    {
        string script = await ReadRepoFileAsync("Verification", "scripts", "Invoke-ReleasePackage.ps1");

        script.Should().Contain("[string] $ArtifactsDirectory = 'Verification\\artifacts\\release-package'");
        script.Should().Contain("[string] $PackageVersion");
        script.Should().Contain("$effectivePackageVersion");
        script.Should().Contain("/p:PackageVersion=$effectivePackageVersion");
        script.Should().Contain("/p:Version=$effectivePackageVersion");
    }

    [Fact]
    public async Task ReleasePackageScript_ShouldEnforceLibDbPackageIdAllowlist()
    {
        string script = await ReadRepoFileAsync("Verification", "scripts", "Invoke-ReleasePackage.ps1");

        script.Should().Contain("if ($id -ne 'Lib.Db')");
        script.Should().Contain("Unexpected package id");
    }

    [Fact]
    public async Task PublishWorkflow_ShouldUseReleasePackageGuardBeforeNuGetPush()
    {
        string workflow = await ReadRepoFileAsync(".github", "workflows", "publish.yml");

        workflow.Should().Contain("Invoke-ReleasePackage.ps1");
        workflow.Should().Contain("-PackageVersion");
        workflow.Should().Contain("Verification/artifacts/release-package");
        workflow.Should().Contain("Lib.Db.${{ steps.version.outputs.version }}.nupkg");
        workflow.Should().NotContain("./nupkgs/*.nupkg");

        int guardIndex = workflow.IndexOf("Invoke-ReleasePackage.ps1", StringComparison.Ordinal);
        int pushIndex = workflow.IndexOf("dotnet nuget push", StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThanOrEqualTo(0);
        pushIndex.Should().BeGreaterThanOrEqualTo(0);
        guardIndex.Should().BeLessThan(pushIndex);
    }

    [Fact]
    public async Task PublishWorkflow_ShouldNotUploadArtifactsWhenRedactionGateFails()
    {
        string workflow = await ReadRepoFileAsync(".github", "workflows", "publish.yml");
        string uploadStep = GetWorkflowStepBlock(workflow, "- name: Upload verification artifacts");

        uploadStep.Should().NotContain("if: always()");
        uploadStep.Should().Contain("Verification/artifacts/**");
        uploadStep.Should().Contain("!Verification/artifacts/release-package/**");
        uploadStep.Should().Contain("!Verification/artifacts/**/*.nupkg");
        uploadStep.Should().Contain("!Verification/artifacts/**/*.snupkg");

        int releasePackageGuardIndex = workflow.IndexOf("Invoke-ReleasePackage.ps1", StringComparison.Ordinal);
        int uploadIndex = workflow.IndexOf("- name: Upload verification artifacts", StringComparison.Ordinal);
        releasePackageGuardIndex.Should().BeLessThan(uploadIndex);
    }

    [Fact]
    public async Task ReleaseVerificationWorkflow_ShouldNotUploadArtifactsWhenRedactionGateFails()
    {
        string workflow = await ReadRepoFileAsync(".github", "workflows", "release-verification.yml");
        string uploadStep = GetWorkflowStepBlock(workflow, "- name: Upload verification artifacts");

        workflow.Should().Contain("Run release verification gate");
        uploadStep.Should().NotContain("if: always()");
        uploadStep.Should().Contain("Verification/artifacts/**");
        uploadStep.Should().Contain("!Verification/artifacts/release-package/**");
        uploadStep.Should().Contain("!Verification/artifacts/**/*.nupkg");
        uploadStep.Should().Contain("!Verification/artifacts/**/*.snupkg");
    }

    [Fact]
    public async Task VerificationGate_ShouldWriteDryRunPackageUnderExcludedReleasePackageArtifacts()
    {
        string script = await ReadRepoFileAsync("Verification", "scripts", "Invoke-Verification.ps1");

        script.Should().Contain("Verification\\artifacts\\release-package");
        script.Should().Contain("-ArtifactsDirectory");
        script.Should().NotContain("Verification\\artifacts\\packages");
    }

    [Fact]
    public async Task ReleasePackageScript_ShouldRedactSecretLikeDirtyStatusPaths()
    {
        string script = await ReadRepoFileAsync("Verification", "scripts", "Invoke-ReleasePackage.ps1");

        script.Should().Contain("ConvertTo-SafeRepositoryStatusLine");
        script.Should().Contain("RedactsSecretLikeDirtyStatusPath");
    }

    [Fact]
    public async Task NativeAotWorkflow_ShouldScanArtifactsBeforeUploadAndNotUseAlwaysUpload()
    {
        string workflow = await ReadRepoFileAsync(".github", "workflows", "native-aot.yml");
        string uploadStep = GetWorkflowStepBlock(workflow, "- name: Upload Native AOT artifacts");

        workflow.Should().Contain("Scan Native AOT artifacts");
        uploadStep.Should().NotContain("if: always()");

        int scanIndex = workflow.IndexOf("Scan Native AOT artifacts", StringComparison.Ordinal);
        int uploadIndex = workflow.IndexOf("- name: Upload Native AOT artifacts", StringComparison.Ordinal);
        scanIndex.Should().BeGreaterThanOrEqualTo(0);
        uploadIndex.Should().BeGreaterThanOrEqualTo(0);
        scanIndex.Should().BeLessThan(uploadIndex);
    }

    [Fact]
    public async Task LibDbToolsProject_ShouldRemainNonPackableUntilToolPackageBoundaryIsApproved()
    {
        string project = await ReadRepoFileAsync("Lib.Db.Tools", "Lib.Db.Tools.csproj");

        project.Should().Contain("<PackageId>Lib.Db.Tools</PackageId>");
        project.Should().Contain("<IsPackable>false</IsPackable>");
    }

    [Fact]
    public async Task VerificationScripts_ShouldUseVersionNeutralGateNames()
    {
        string[] scriptPaths =
        [
            "Verification/scripts/Assert-Coverage.ps1",
            "Verification/scripts/Invoke-Aot.ps1",
            "Verification/scripts/Invoke-Benchmarks.ps1",
            "Verification/scripts/Invoke-Coverage.ps1",
            "Verification/scripts/Invoke-Verification.ps1"
        ];

        foreach (string scriptPath in scriptPaths)
        {
            string script = await ReadRepoFileAsync(scriptPath.Split('/'));
            script.Should().NotContain("Lib.Db v2.4.0", scriptPath);
        }
    }

    private static async Task<string> ReadRepoFileAsync(params string[] pathParts)
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string path = Path.Combine(new[] { repoRoot.FullName }.Concat(pathParts).ToArray());
        return await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
    }

    private static string GetWorkflowStepBlock(string workflow, string stepName)
    {
        int start = workflow.IndexOf(stepName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        int nextStep = workflow.IndexOf("\n    - name:", start + stepName.Length, StringComparison.Ordinal);
        return nextStep < 0 ? workflow[start..] : workflow[start..nextStep];
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
