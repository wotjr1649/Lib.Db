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
        testScript.Should().Contain("SkipTestEnvGuard");
        testScript.Should().Contain("[Environment]::SetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD', 'true')");
    }

    [Fact]
    public void ReleaseVerification_ShouldRunArtifactSecretScanAndTrackingGate()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string verificationScript = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Invoke-Verification.ps1"));
        string benchmarkScript = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Invoke-Benchmarks.ps1"));
        string manifest = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "manifest.json"));

        verificationScript.Should().Contain("Scan-VerificationArtifacts.ps1");
        verificationScript.Should().Contain("Assert-GeneratedArtifactsUntracked.ps1");
        benchmarkScript.Should().Contain("Scan-VerificationArtifacts.ps1");

        string scannerScript = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Scan-VerificationArtifacts.ps1"));
        scannerScript.Should().Contain("'.html'");
        scannerScript.Should().Contain("'.xml'");
        scannerScript.Should().Contain("'.nuspec'");
        scannerScript.Should().Contain("'.psmdcp'");
        scannerScript.Should().Contain("'.rels'");
        scannerScript.Should().Contain("'.csproj'");
        scannerScript.Should().Contain("placeholder");
        scannerScript.Should().Contain("redacted");

        benchmarkScript.Should().Contain("Get-BenchmarkFiltersToRun");
        benchmarkScript.Should().Contain("releaseRequiredBenchmarkTypes");
        benchmarkScript.Should().Contain("benchmark-type:");
        benchmarkScript.Should().Contain("ExpectedBenchmarkTypes");
        benchmarkScript.Should().Contain("TvpBenchmarks");
        benchmarkScript.Should().Contain("WideTvpBenchmarks");
        benchmarkScript.Should().Contain("'*Lib.Db.Benchmarks.WideTvpBenchmarks*'");
        manifest.Should().Contain("artifactSecretScan");
        manifest.Should().Contain("artifactTrackingGate");
    }

    [Fact]
    public void ReleaseVerification_ShouldRunReleasePackageGate()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string verificationScript = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Invoke-Verification.ps1"));
        string manifest = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "manifest.json"));

        verificationScript.Should().Contain("Invoke-ReleasePackage.ps1");
        manifest.Should().Contain("releasePackage");
        manifest.Should().Contain("scripts/Invoke-ReleasePackage.ps1");
    }

    [Fact]
    public void AotVerification_ShouldUseWarningBaseline()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string baselinePath = Path.Combine(repoRoot.FullName, "Verification", "baselines", "aot-warnings.json");
        string script = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Invoke-Aot.ps1"));
        string verificationPolicy = File.ReadAllText(Path.Combine(repoRoot.FullName, "docs", "verification.md"));
        string manifest = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "manifest.json"));
        string baselineJson = File.ReadAllText(baselinePath);
        System.Text.Json.Nodes.JsonNode? baseline = System.Text.Json.Nodes.JsonNode.Parse(baselineJson);
        System.Text.Json.Nodes.JsonArray? allowedWarnings = baseline?["allowedWarnings"]?.AsArray();

        File.Exists(baselinePath).Should().BeTrue("AOT provider warning allowances must be versioned as a release gate input");
        allowedWarnings.Should().NotBeNull();
        foreach (System.Text.Json.Nodes.JsonNode? warning in allowedWarnings!)
        {
            warning.Should().NotBeNull();
            string? id = warning!["id"]?.GetValue<string>();
            string? assembly = warning["assembly"]?.GetValue<string>();
            string? sourcePackage = warning["sourcePackage"]?.GetValue<string>();
            string? packageVersion = warning["packageVersion"]?.GetValue<string>();

            id.Should().NotBeNullOrWhiteSpace();
            assembly.Should().NotBeNullOrWhiteSpace();
            sourcePackage.Should().NotBeNullOrWhiteSpace();
            packageVersion.Should().NotBeNullOrWhiteSpace();
        }

        script.Should().Contain("aot-warnings.json");
        script.Should().Contain("Get-AotPackageVersions");
        script.Should().Contain("Assert-AotWarningsMatchBaseline");
        script.Should().Contain("Assert-AotWarningsMatchBaseline -Warnings $aotWarnings -BaselinePath $warningBaselinePath -PackageVersions $packageVersions -RequirePackageVersions");
        script.Should().Contain("Lib.Db");
        script.Should().Contain("Trim analysis warning");
        script.Should().Contain("AOT analysis warning");
        script.Should().Contain("-p:GeneratePackageOnBuild=false");
        script.Should().Contain("-p:WarningsAsErrors=");
        script.Should().Contain("-RequirePackageVersions");
        verificationPolicy.Should().Contain("AOT warning baseline");
        verificationPolicy.Should().Contain("Verification/baselines/aot-warnings.json");
        verificationPolicy.Should().Contain("source package");
        verificationPolicy.Should().Contain("package version");
        manifest.Should().Contain("aotWarnings");
        manifest.Should().Contain("baselines/aot-warnings.json");
    }

    [Fact]
    public async Task AotVerificationParserSelfTest_ShouldValidateWarningParsingAndBaselineRejection()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot.FullName, "Verification", "scripts", "Invoke-Aot.ps1");
        File.Exists(scriptPath).Should().BeTrue("AOT parser self-test must stay executable");

        using System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
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
        process.StartInfo.ArgumentList.Add("-ParserSelfTest");

        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        string combined = output + error;

        process.ExitCode.Should().Be(0, combined);
        combined.Should().Contain("ParsedWarning=IL2104|Provider.One");
        combined.Should().Contain("ParsedWarning=IL3053|Provider.Two");
        combined.Should().Contain("ParsedWarning=IL2026|Provider.Three");
        combined.Should().Contain("ParsedWarning=IL3050|Provider.Four");
        combined.Should().Contain("RejectedLibDbOwnedWarning=True");
        combined.Should().Contain("RejectedUnbaselinedWarning=True");
        combined.Should().Contain("RejectedUnobservedBaselineWarning=True");
        combined.Should().Contain("RejectedPackageVersionDrift=True");
    }

    [Fact]
    public void ReleasePackageScript_ShouldValidatePackageMetadataAndUnsignedPolicy()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string script = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "Verification",
            "scripts",
            "Invoke-ReleasePackage.ps1"));

        script.Should().Contain("dotnet");
        script.Should().Contain("pack");
        script.Should().Contain("RepositoryCommit");
        script.Should().Contain("dotnet nuget verify");
        script.Should().Contain("NU3004");
        script.Should().Contain("AllowUnsigned");
        script.Should().Contain("Test-OnlyAcceptedUnsignedNuGetFailure");
        script.Should().Contain("Package repository commit does not match HEAD");
        script.Should().Contain("Assert-RepositoryStatusClean");
        script.Should().Contain("status");
        script.Should().Contain("--porcelain=v1");
        script.Should().Contain("must resolve under Verification artifacts");
        script.Should().Contain("Scan-VerificationArtifacts.ps1");
        script.Should().Contain("finally");
        script.Should().Contain("Remove-Item -LiteralPath $expandedDirectory -Recurse -Force");
    }

    [Fact]
    public async Task ReleasePackagePrivateSelfTest_ShouldRejectInvalidUnsignedAndRepositoryCommitCases()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot.FullName, "Verification", "scripts", "Invoke-ReleasePackage.ps1");
        File.Exists(scriptPath).Should().BeTrue("release package helper self-tests must stay executable");

        using System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
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
        string combined = output + error;

        process.ExitCode.Should().Be(0, combined);
        combined.Should().Contain("AcceptsLocalizedUnsignedNu3004WithSignatureFailureSummary");
        combined.Should().Contain("RejectsMixedNu3004AndAnotherNuGetCode");
        combined.Should().Contain("RejectsNu3004WithUnrelatedFatalText");
        combined.Should().Contain("RejectsShortRepositoryCommit");
        combined.Should().Contain("RejectsDifferentRepositoryCommit");
        combined.Should().Contain("AcceptsCleanRepositoryStatus");
        combined.Should().Contain("RejectsDirtyRepositoryStatus");
        combined.Should().Contain("RejectsArtifactDirectoryOutsideVerificationArtifacts");
        combined.Should().Contain("RejectsVerificationArtifactsRootAsArtifactDirectory");
    }

    [Fact]
    public async Task BenchmarkWrapper_ShouldExpandCustomNarrowTvpFilterToWide()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot.FullName, "Verification", "scripts", "Invoke-Benchmarks.ps1");

        using System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
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
        process.StartInfo.ArgumentList.Add("-Job");
        process.StartInfo.ArgumentList.Add("Dry");
        process.StartInfo.ArgumentList.Add("-Filter");
        process.StartInfo.ArgumentList.Add("*Lib.Db.Benchmarks.TvpBenchmarks*");
        process.StartInfo.ArgumentList.Add("-SkipSetup");
        process.StartInfo.ArgumentList.Add("-SkipRun");
        process.StartInfo.ArgumentList.Add("-SkipSecretScan");
        process.StartInfo.ArgumentList.Add("-AllowPartial");

        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        string combined = output + error;

        process.ExitCode.Should().Be(0, combined);
        combined.Should().Contain("ResolvedFilters=*Lib.Db.Benchmarks.TvpBenchmarks*, *Lib.Db.Benchmarks.WideTvpBenchmarks*");
        combined.Should().Contain("ExpectedBenchmarkTypes=TvpBenchmarks, WideTvpBenchmarks");
        combined.Should().NotContain("benchmark-type:");
    }

    [Fact]
    public async Task BenchmarkWrapper_DefaultTvpFilter_ShouldUseNonOverlappingClassFilters()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string scriptPath = Path.Combine(repoRoot.FullName, "Verification", "scripts", "Invoke-Benchmarks.ps1");

        using System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
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
        process.StartInfo.ArgumentList.Add("-Job");
        process.StartInfo.ArgumentList.Add("Dry");
        process.StartInfo.ArgumentList.Add("-SkipSetup");
        process.StartInfo.ArgumentList.Add("-SkipRun");
        process.StartInfo.ArgumentList.Add("-SkipSecretScan");
        process.StartInfo.ArgumentList.Add("-AllowPartial");

        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        string combined = output + error;

        process.ExitCode.Should().Be(0, combined);
        combined.Should().Contain(
            "ResolvedFilters=*Lib.Db.Benchmarks.TvpBenchmarks*, *Lib.Db.Benchmarks.WideTvpBenchmarks*");
        combined.Should().Contain("ExpectedBenchmarkTypes=TvpBenchmarks, WideTvpBenchmarks");
    }

    [Fact]
    public void PublicVerificationDocs_ShouldHideInternalCommandsWhileInternalReadmeKeepsWrappers()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string verificationReadme = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "README.md"));

        verificationReadme.Should().Contain("Invoke-Tests.ps1");
        verificationReadme.Should().Contain("Invoke-Verification.ps1");

        string publicDocs = string.Join(
            Environment.NewLine,
            EnumeratePublicDocumentationFiles(repoRoot).Select(File.ReadAllText));
        publicDocs.Should().NotContain("Verification/scripts/");
        publicDocs.Should().NotContain("Invoke-Tests.ps1");
        publicDocs.Should().NotContain("Invoke-Coverage.ps1");
        publicDocs.Should().NotContain("Invoke-Benchmarks.ps1");
        publicDocs.Should().NotContain("Invoke-Verification.ps1");
        publicDocs.Should().NotContain("BenchmarkJob");
        publicDocs.Should().NotContain("BenchmarkDotNet");
        publicDocs.Should().NotContain("coverage gate");
        publicDocs.Should().NotContain("coverage gates");
        publicDocs.Should().NotContain("Verification/");
        publicDocs.Should().NotContain("Verification\\artifacts");

        string chaosPolicy = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "docs",
            "security",
            "libdb-server-chaos-harness.md"));
        chaosPolicy.Should().Contain("internal maintainer runbook");
        chaosPolicy.Should().NotContain("dotnet run");
        chaosPolicy.Should().NotContain("Verification/");
        chaosPolicy.Should().NotContain("LIBDB_CHAOS_TEST");

        string riskLedger = File.ReadAllText(Path.Combine(
            repoRoot.FullName,
            "docs",
            "security",
            "aot-tvp-risk-ledger.md"));
        riskLedger.Should().Contain("internal maintainer risk ledger");
        riskLedger.Should().Contain("not consumer API documentation");
    }

    [Fact]
    public void InternalDevelopmentDocs_ShouldBeExplicitlyMarkedAndExcludedFromConsumerDocs()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string docsRoot = Path.Combine(repoRoot.FullName, "docs");

        string reviewsReadme = File.ReadAllText(Path.Combine(docsRoot, "reviews", "README.md"));
        string superpowersReadme = File.ReadAllText(Path.Combine(docsRoot, "superpowers", "README.md"));
        string verificationPolicy = File.ReadAllText(Path.Combine(docsRoot, "verification.md"));

        reviewsReadme.Should().Contain("Internal Review Records");
        reviewsReadme.Should().Contain("not consumer documentation");
        superpowersReadme.Should().Contain("Internal Development Records");
        superpowersReadme.Should().Contain("not consumer documentation");
        superpowersReadme.Should().Contain("internal verification, benchmark, coverage");
        verificationPolicy.Should().Contain("internal maintainer policy");
        verificationPolicy.Should().Contain("not consumer API documentation");

        string[] consumerDocs = EnumeratePublicDocumentationFiles(repoRoot).ToArray();
        consumerDocs.Should().NotContain(path =>
            path.Contains($"{Path.DirectorySeparatorChar}reviews{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        consumerDocs.Should().NotContain(path =>
            path.Contains($"{Path.DirectorySeparatorChar}superpowers{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        consumerDocs.Should().NotContain(path =>
            path.EndsWith($"{Path.DirectorySeparatorChar}verification.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ArchivedReviewDocs_ShouldBeMarkedAsHistoricalAndExcludedFromConsumerGuidance()
    {
        DirectoryInfo repoRoot = FindRepoRoot();
        string archiveRoot = Path.Combine(repoRoot.FullName, "docs", "reviews", "archive");

        Directory.Exists(archiveRoot).Should().BeTrue();

        string[] archiveFiles = Directory.GetFiles(archiveRoot, "*.md", SearchOption.TopDirectoryOnly);
        archiveFiles.Should().NotBeEmpty();

        foreach (string file in archiveFiles)
        {
            string content = File.ReadAllText(file);
            content.Should().Contain("Historical internal review");
            content.Should().Contain("Not consumer documentation");
            content.Should().Contain("Not current skill guidance");
        }

        string publicDocs = string.Join(
            Environment.NewLine,
            EnumeratePublicDocumentationFiles(repoRoot).Select(File.ReadAllText));
        publicDocs.Should().NotContain("tvpgen-guide.md");
        publicDocs.Should().NotContain("runtime-api.md");
        publicDocs.Should().NotContain("security-guardrails.md");

        string activeSkillGuidance = string.Join(
            Environment.NewLine,
            EnumerateActiveSkillFiles(repoRoot).Select(File.ReadAllText));
        activeSkillGuidance.Should().NotContain("tvpgen-guide.md");
        activeSkillGuidance.Should().NotContain("runtime-api.md");
        activeSkillGuidance.Should().NotContain("security-guardrails.md");
        activeSkillGuidance.Should().NotContain("BenchmarkDotNet");
        activeSkillGuidance.Should().NotContain("Invoke-Verification.ps1");
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
        project.Should().Contain("-SkipTestEnvGuard");
        project.Should().NotContain("-p:LIBDB_SKIP_TEST_ENV_GUARD=true");
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
        combined.Should().Contain("NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}");
        combined.Should().Contain("--api-key \"$NUGET_API_KEY\"");
        combined.Should().NotContain("id-token: write");
        combined.Should().NotContain("NuGet/login@v1");
        combined.Should().NotContain("secrets.NUGET_USER");
        combined.Should().NotContain("--api-key ${{ secrets.NUGET_API_KEY }}");
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

    private static IEnumerable<string> EnumerateActiveSkillFiles(DirectoryInfo repoRoot)
    {
        foreach (string skillRootName in new[] { ".agents", ".claude" })
        {
            string skillRoot = Path.Combine(repoRoot.FullName, skillRootName, "skills", "lib-db");
            Directory.Exists(skillRoot).Should().BeTrue($"active Lib.Db skill guidance must exist under {skillRootName}");

            foreach (string file in Directory.EnumerateFiles(skillRoot, "*.md", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumeratePublicDocumentationFiles(DirectoryInfo repoRoot)
    {
        yield return Path.Combine(repoRoot.FullName, "README.md");

        string docsRoot = Path.Combine(repoRoot.FullName, "docs");
        foreach (string file in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly)
                     .Where(static file => !Path.GetFileName(file).Equals("verification.md", StringComparison.OrdinalIgnoreCase)))
        {
            yield return file;
        }

        string securityRoot = Path.Combine(docsRoot, "security");
        if (!Directory.Exists(securityRoot))
            yield break;

        foreach (string file in Directory.EnumerateFiles(securityRoot, "*.md", SearchOption.TopDirectoryOnly)
                     .Where(static file => !IsInternalSecurityDocument(file)))
        {
            yield return file;
        }
    }

    private static bool IsInternalSecurityDocument(string file)
    {
        string name = Path.GetFileName(file);
        return name.Equals("libdb-server-chaos-harness.md", StringComparison.OrdinalIgnoreCase)
            || name.Equals("aot-tvp-risk-ledger.md", StringComparison.OrdinalIgnoreCase);
    }
}
