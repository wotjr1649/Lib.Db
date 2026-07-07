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
        script.Should().Contain("PackageVersion override must match project Version");
        script.Should().Contain("/p:PackageVersion=$effectivePackageVersion");
        script.Should().Contain("/p:Version=$effectivePackageVersion");
        script.Should().Contain("'--no-restore'");
        script.Should().Contain("RejectsMismatchedPackageVersionOverride");
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

    [Fact]
    public async Task VerificationGate_ShouldRunNuGetAuditPolicyBeforeReleasePackage()
    {
        string verificationScript = await ReadRepoFileAsync("Verification", "scripts", "Invoke-Verification.ps1");
        string auditScript = await ReadRepoFileAsync("Verification", "scripts", "Invoke-NuGetAudit.ps1");
        string manifest = await ReadRepoFileAsync("Verification", "manifest.json");

        verificationScript.Should().Contain("Invoke-NuGetAudit.ps1");
        verificationScript.Should().Contain("$nugetAuditScript");
        verificationScript.Should().Contain("SkipNuGetAudit");
        auditScript.Should().Contain("NuGetAudit=true");
        auditScript.Should().Contain("NuGetAuditMode=all");
        auditScript.Should().Contain("WarningsAsErrors=NU1900;NU1903;NU1904");
        auditScript.Should().Contain("WarningsAsErrors=NU1900%3BNU1903%3BNU1904");
        auditScript.Should().Contain("WarningsNotAsErrors=NU1901%3BNU1902");
        auditScript.Should().Contain("AcceptLowModerateAuditWarnings");
        auditScript.Should().Contain("Review the advisory");
        auditScript.Should().Contain("'-m:1'");
        auditScript.Should().Contain("'-nr:false'");
        auditScript.Should().Contain("NU1901/NU1902 are documented-accept warnings");
        auditScript.Should().Contain("audit source failure");
        auditScript.Should().NotContain("WarningsAsErrors=NU1900;NU1901;NU1902;NU1903;NU1904");
        manifest.Should().Contain("nugetAudit");
        manifest.Should().Contain("scripts/Invoke-NuGetAudit.ps1");

        int auditGateIndex = verificationScript.IndexOf("if (-not $SkipNuGetAudit)", StringComparison.Ordinal);
        int auditInvokeIndex = verificationScript.IndexOf("'-File', $nugetAuditScript", auditGateIndex, StringComparison.Ordinal);
        int releasePackageGateIndex = verificationScript.IndexOf("if (-not $SkipReleasePackage)", StringComparison.Ordinal);
        int releasePackageInvokeIndex = verificationScript.IndexOf("-File $releasePackageScript", releasePackageGateIndex, StringComparison.Ordinal);
        auditGateIndex.Should().BeGreaterThanOrEqualTo(0);
        auditInvokeIndex.Should().BeGreaterThan(auditGateIndex);
        releasePackageGateIndex.Should().BeGreaterThan(auditInvokeIndex);
        releasePackageInvokeIndex.Should().BeGreaterThan(releasePackageGateIndex);
    }

    [Fact]
    public async Task GitHubWorkflowActions_ShouldBePinnedToFullCommitShaAndDocumentSourceVersion()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string workflowRoot = Path.Combine(repoRoot.FullName, ".github", "workflows");
        string[] workflows = Directory.GetFiles(workflowRoot, "*.yml", SearchOption.TopDirectoryOnly);

        workflows.Should().NotBeEmpty();
        Dictionary<string, string> expectedPins = new(StringComparer.Ordinal)
        {
            ["actions/checkout@v6"] = "df4cb1c069e1874edd31b4311f1884172cec0e10",
            ["actions/setup-dotnet@v5"] = "26b0ec14cb23fa6904739307f278c14f94c95bf1",
            ["actions/upload-artifact@v6"] = "b7c566a772e6b6bfb58ed0dc250532a479d7789f"
        };

        foreach (string workflow in workflows)
        {
            string[] lines = await File.ReadAllLinesAsync(workflow, TestContext.Current.CancellationToken);
            for (int index = 0; index < lines.Length; index++)
            {
                string trimmed = lines[index].Trim();
                string? usesValue = null;
                if (trimmed.StartsWith("- uses:", StringComparison.Ordinal))
                    usesValue = trimmed["- uses:".Length..].Trim();
                else if (trimmed.StartsWith("uses:", StringComparison.Ordinal))
                    usesValue = trimmed["uses:".Length..].Trim();

                if (string.IsNullOrWhiteSpace(usesValue) || usesValue.StartsWith("./", StringComparison.Ordinal) || usesValue.StartsWith("docker://", StringComparison.Ordinal))
                    continue;

                int commentIndex = usesValue.IndexOf('#');
                string actionRef = (commentIndex < 0 ? usesValue : usesValue[..commentIndex]).Trim();
                string comment = commentIndex < 0 ? string.Empty : usesValue[(commentIndex + 1)..].Trim();
                int atIndex = actionRef.LastIndexOf('@');
                atIndex.Should().BeGreaterThan(0, $"{workflow}:{index + 1} must use owner/repo@ref syntax");

                string action = actionRef[..atIndex];
                string reference = actionRef[(atIndex + 1)..];
                action.Should().MatchRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", $"{workflow}:{index + 1} must be a GitHub action ref");
                reference.Should().MatchRegex("^[0-9a-fA-F]{40}$", $"{workflow}:{index + 1} must pin {action} to a full commit SHA");
                comment.Should().StartWith("action-version: ", $"{workflow}:{index + 1} must document the source action version next to the SHA pin");
                string actionVersion = comment["action-version: ".Length..].Trim();
                int documentedAtIndex = actionVersion.LastIndexOf('@');
                documentedAtIndex.Should().BeGreaterThan(0, $"{workflow}:{index + 1} action-version comment must use owner/repo@version syntax");
                expectedPins.Should().ContainKey(actionVersion, $"{workflow}:{index + 1} must document a reviewed source action version");
                string documentedAction = actionVersion[..documentedAtIndex];
                documentedAction.Should().Be(action, $"{workflow}:{index + 1} action-version comment must match the pinned action");
                reference.Should().Be(expectedPins[actionVersion], $"{workflow}:{index + 1} SHA must match the reviewed source action version");
            }
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
