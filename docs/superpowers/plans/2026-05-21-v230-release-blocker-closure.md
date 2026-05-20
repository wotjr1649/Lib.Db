# Lib.Db v2.3.0 Release Blocker Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close all remaining v2.3.0 release blockers and make the release gate prove package, AOT, observability, docs, and artifact safety.

**Architecture:** Keep the Runtime TVP/fluent API unchanged. Harden the existing library and verification surfaces with small, reviewable changes: metric gating in the connection factory, package/AOT gate scripts under `Verification/scripts`, baselines under `Verification/baselines`, and docs/tests that keep consumer guidance separate from internal release workflow.

**Tech Stack:** .NET 10, C# 14 syntax currently used by the repo, xUnit v3, FluentAssertions, Microsoft.Data.SqlClient, BenchmarkDotNet, PowerShell 7, NuGet CLI verification, native AOT publish.

---

## Reviewed Spec

Spec: `docs/superpowers/specs/2026-05-21-v230-release-blocker-closure-design.md`

The spec is valid after review against current repository state and official docs:

- Microsoft TVP guidance matches Lib.Db Runtime TVP direction: TVPs are strongly typed input parameters and `SqlParameter.SqlDbType = Structured` plus `TypeName` are the supported ADO.NET pattern.
- .NET metrics guidance confirms `Meter` measurements can be collected by `MeterListener`, OpenTelemetry, or tools, so `EnableObservability=false` must gate every direct measurement path.
- `dotnet nuget verify` is signature-focused, so unsigned v2.3.0 packages need an explicit accepted policy instead of treating `NU3004` as an unexplained failure.
- Native AOT guidance treats warnings as compatibility evidence. Provider-owned warnings can be accepted only when they are baselined and reviewed.

Official references checked on 2026-05-21:

- <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-verify>
- <https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu3004>
- <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3053>
- <https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-collection>
- <https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql/table-valued-parameters>

## Locked Release Policy

For this implementation, v2.3.0 packages are allowed to be unsigned because no signing
certificate or signing service is configured in the workspace.

The release-package gate must still run `dotnet nuget verify --all`. If the only failure
is `NU3004` for an unsigned package, the script treats it as an explicit accepted policy
result and prints that unsigned status was accepted. Any other verification failure must
fail the gate.

## File Structure

Modify:

- `Lib.Db/Infrastructure/Infrastructure.cs`  
  Gate all direct connection metrics on `DbMetrics.IsEnabled`.

- `Lib.Db/Configuration/LibDbOptionsValidator.cs`  
  Route all connection-name text used in validation messages through a single safe-name helper.

- `Lib.Db/Execution/Executors/SqlGridReader.cs`  
  Fix the stale `EmptyGridReader` inline comment.

- `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/PoolMetricsTests.cs`  
  Add DB-backed meter tests for connection metrics enabled/disabled behavior.

- `Verification/projects/Lib.Db.IntegrationTests/Diagnostics/DbDiagnosticsTests.cs`  
  Restore global `DbMetrics` state after diagnostics tests.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/OptionsValidationTests.cs`  
  Add redaction regression tests for `ConnectionStringNames` error paths.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs`  
  Add release-package gate, AOT baseline, docs archive, and benchmark filter assertions.

- `Verification/scripts/Invoke-Aot.ps1`  
  Capture publish output, parse IL warnings, compare them with a baseline, and fail on drift.

- `Verification/scripts/Invoke-Benchmarks.ps1`  
  Replace overlapping benchmark filters with exact class filters.

- `Verification/scripts/Invoke-Verification.ps1`  
  Add the release-package gate to non-partial release verification.

- `Verification/manifest.json`  
  Register release-package and AOT warning baseline gates.

- `docs/verification.md`  
  Document the AOT baseline and unsigned package verification policy.

- `docs/reviews/README.md`  
  Document archive policy.

Create:

- `Verification/scripts/Invoke-ReleasePackage.ps1`  
  Build and validate the Release nupkg/snupkg from HEAD.

- `Verification/baselines/aot-warnings.json`  
  Baseline provider-owned AOT/trim warnings.

- `docs/reviews/archive/`  
  Historical internal review records.

Move:

- `docs/reviews/lib-db-skill-api-coverage-validation-2026-05-20.md`
- `docs/reviews/lib-db-skill-evaluation-2026-05-20.md`

Remove from working tree if present and ignored:

- `artifacts/package-consumer-check/Lib.Db.2.3.0.nupkg`
- `artifacts/package-consumer-check/Lib.Db.2.3.0.snupkg`

## Task 1: Gate Connection Metrics On Observability

**Files:**

- Modify: `Lib.Db/Infrastructure/Infrastructure.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/PoolMetricsTests.cs`

- [ ] **Step 1: Write the failing meter test**

Add `using Lib.Db.Diagnostics;` to `PoolMetricsTests.cs`.

Add this test to the class:

```csharp
[Theory]
[InlineData(false)]
[InlineData(true)]
public async Task PM03_ConnectionMetrics_ShouldHonorDbMetricsGate(bool enabled)
{
    bool previous = DbMetrics.IsEnabled;
    DbMetrics.IsEnabled = enabled;

    try
    {
        using TelemetryTestHarness harness = new("Lib.Db");

        DbResult<int> result = await _db
            .Sql("SELECT 1")
            .ExecuteScalarAsync<int>();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        IReadOnlyList<CapturedMeasurement<double>> measurements =
            harness.GetDoubles("libdb.connection.acquire_duration_ms");

        if (enabled)
        {
            measurements.Should().NotBeEmpty();
            measurements.Should().OnlyContain(m =>
                m.Tags.Any(t => t.Key == "instance" && t.Value is string));
        }
        else
        {
            measurements.Should().BeEmpty();
        }
    }
    finally
    {
        DbMetrics.IsEnabled = previous;
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "PoolMetricsTests"
```

Expected before implementation: `PM03_ConnectionMetrics_ShouldHonorDbMetricsGate(false)` fails because `libdb.connection.acquire_duration_ms` is recorded even when metrics are disabled.

- [ ] **Step 3: Gate successful connection metrics**

In `DbConnectionFactory.CreateConnectionAsync`, wrap the successful connection metrics:

```csharp
if (DbMetrics.IsEnabled)
{
    LibDbTelemetry.ConnectionAcquireDuration.Record(
        sw.Elapsed.TotalMilliseconds,
        new KeyValuePair<string, object?>("instance", diagnosticInstance));

    if (sw.ElapsedMilliseconds > PoolWaitThresholdMs)
    {
        LibDbTelemetry.ConnectionPoolWaits.Add(1,
            new KeyValuePair<string, object?>("instance", diagnosticInstance));
    }
}
```

- [ ] **Step 4: Gate failed connection metrics**

In the `catch` block, wrap timeout/error metrics:

```csharp
if (DbMetrics.IsEnabled)
{
    string reason = ex is Microsoft.Data.SqlClient.SqlException sqlEx
        ? (sqlEx.Number == -2 ? "timeout" : "sql_error")
        : "other";

    LibDbTelemetry.ConnectionPoolTimeouts.Add(1,
        new KeyValuePair<string, object?>("instance", diagnosticInstance),
        new KeyValuePair<string, object?>("reason", reason));
}
```

- [ ] **Step 5: Run focused verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "PoolMetricsTests|RuntimeUtilityCoverageTests"
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add Lib.Db/Infrastructure/Infrastructure.cs Verification/projects/Lib.Db.IntegrationTests/VerificationDb/PoolMetricsTests.cs
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "fix: gate connection metrics on observability"
```

## Task 2: Harden Connection Name Redaction And Static Test State

**Files:**

- Modify: `Lib.Db/Configuration/LibDbOptionsValidator.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/OptionsValidationTests.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Diagnostics/DbDiagnosticsTests.cs`
- Modify: `Lib.Db/Execution/Executors/SqlGridReader.cs`

- [ ] **Step 1: Add redaction regression tests**

In `OptionsValidationTests.cs`, add these tests near the existing `ConnectionStringNames` tests:

```csharp
[Theory]
[InlineData("Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
[InlineData("Raw:Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True")]
public void ConnectionStringNames_SensitiveMissingName_ShouldNotLeakRawName(string sensitiveName)
{
    LibDbOptions options = TestOptionsFactory.CreateMinimal();
    options.ConnectionStrings.Clear();
    options.ConnectionStringNames = [sensitiveName];

    LibDbOptionsValidator validator = new();
    ValidateOptionsResult result = validator.Validate(null, options);

    result.Failed.Should().BeTrue();
    string message = string.Join(";", result.Failures);
    message.Should().Contain("[redacted]");
    message.Should().NotContain("Password=placeholder");
    message.Should().NotContain("User Id=app_user");
    message.Should().NotContain("Database=TEST");
}

[Fact]
public void ConnectionStringNames_SensitiveDuplicateNames_ShouldNotLeakRawName()
{
    const string sensitiveName =
        "Server=localhost;Database=TEST;User Id=app_user;Password=placeholder;Encrypt=True;TrustServerCertificate=True";
    LibDbOptions options = TestOptionsFactory.CreateMinimal();
    options.ConnectionStringNames = [sensitiveName, sensitiveName];
    options.ConnectionStrings.Clear();

    LibDbOptionsValidator validator = new();
    ValidateOptionsResult result = validator.Validate(null, options);

    result.Failed.Should().BeTrue();
    string message = string.Join(";", result.Failures);
    message.Should().Contain("[redacted]");
    message.Should().NotContain("Password=placeholder");
    message.Should().NotContain("User Id=app_user");
    message.Should().NotContain("Database=TEST");
}

[Fact]
public void ConnectionStringNames_SensitiveProductionProfileName_ShouldStopBeforeProductionMessage()
{
    const string sensitiveName =
        "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True";
    LibDbOptions options = TestOptionsFactory.CreateMinimal();
    options.ConnectionSecurityProfile = ConnectionSecurityProfile.Production;
    options.ConnectionStringNames = [sensitiveName];
    options.ConnectionStrings[sensitiveName] =
        "Server=localhost;Database=TEST;User Id=sa;Password=placeholder;Encrypt=False;TrustServerCertificate=True";

    LibDbOptionsValidator validator = new();
    ValidateOptionsResult result = validator.Validate(null, options);

    result.Failed.Should().BeTrue();
    string message = string.Join(";", result.Failures);
    message.Should().Contain("[redacted]");
    message.Should().NotContain("Password=placeholder");
    message.Should().NotContain("User Id=sa");
    message.Should().NotContain("Database=TEST");
}
```

Add this using if missing:

```csharp
using Microsoft.Extensions.Options;
```

- [ ] **Step 2: Run the tests before implementation**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "OptionsValidationTests"
```

Expected: tests should already mostly pass because sensitive names are rejected early. If any fail, the next implementation step must fix the leaking path.

- [ ] **Step 3: Add a safe connection-name helper**

In `LibDbOptionsValidator`, add:

```csharp
private static string SafeConnectionName(string connectionName)
    => DbDiagnosticRedactor.RedactInstanceId(connectionName) ?? connectionName;
```

Change `ValidateConnectionStringSecurityProfile`:

```csharp
internal static void ValidateConnectionStringSecurityProfile(
    LibDbOptions options,
    string connectionName,
    string connectionString,
    List<string> errors)
{
    string safeConnectionName = SafeConnectionName(connectionName);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        errors.Add($"'{safeConnectionName}'의 연결 문자열이 비어있습니다.");
        return;
    }

    try
    {
        SqlConnectionStringBuilder builder = new(connectionString);
        ValidateConnectionSecurityProfile(options, safeConnectionName, builder, errors);
    }
    catch (ArgumentException)
    {
        errors.Add($"'{safeConnectionName}'의 연결 문자열 형식이 잘못되었습니다.");
    }
}
```

This makes later validation paths safe even if a future caller forgets to reject a
connection-string-shaped name before validation.

- [ ] **Step 4: Restore global metric state in diagnostics tests**

Make `DbDiagnosticsTests` implement `IDisposable`:

```csharp
public sealed class DbDiagnosticsTests : IDisposable
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly bool _previousMetricsEnabled;

    public DbDiagnosticsTests()
    {
        _mockLogger = new Mock<ILogger>();
        _previousMetricsEnabled = DbMetrics.IsEnabled;
        DbMetrics.ResetForTesting();
        DbMetrics.IsEnabled = true;
    }

    public void Dispose()
    {
        DbMetrics.ResetForTesting();
        DbMetrics.IsEnabled = _previousMetricsEnabled;
    }
}
```

Keep the existing test methods inside the class.

- [ ] **Step 5: Fix the stale SqlGridReader comment**

In `SqlGridReader.EmptyGridReader.ReadAsync`, replace the stale inline comment with:

```csharp
// Return an empty list through the async contract used by real grid readers.
```

- [ ] **Step 6: Run focused verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "OptionsValidationTests|DbDiagnosticsTests|SqlGridReader"
```

Expected: all selected tests pass.

- [ ] **Step 7: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add Lib.Db/Configuration/LibDbOptionsValidator.cs Lib.Db/Execution/Executors/SqlGridReader.cs Verification/projects/Lib.Db.IntegrationTests/Unit/OptionsValidationTests.cs Verification/projects/Lib.Db.IntegrationTests/Diagnostics/DbDiagnosticsTests.cs
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "test: harden validation redaction coverage"
```

## Task 3: Archive Stale Internal Review Documents

**Files:**

- Move: `docs/reviews/lib-db-skill-api-coverage-validation-2026-05-20.md`
- Move: `docs/reviews/lib-db-skill-evaluation-2026-05-20.md`
- Modify: `docs/reviews/README.md`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs`

- [ ] **Step 1: Move stale review files**

Run:

```powershell
New-Item -ItemType Directory -Path C:\Users\js\Documents\Codex\Lib.Db\docs\reviews\archive -Force
git -C C:\Users\js\Documents\Codex\Lib.Db mv docs/reviews/lib-db-skill-api-coverage-validation-2026-05-20.md docs/reviews/archive/lib-db-skill-api-coverage-validation-2026-05-20.md
git -C C:\Users\js\Documents\Codex\Lib.Db mv docs/reviews/lib-db-skill-evaluation-2026-05-20.md docs/reviews/archive/lib-db-skill-evaluation-2026-05-20.md
```

- [ ] **Step 2: Add archive banners**

At the top of both moved files, add:

```markdown
> Historical internal review. Not consumer documentation. Not current skill guidance.
> Do not use this file as v2.3.0 API or skill instruction source.
```

- [ ] **Step 3: Update docs/reviews README**

Set `docs/reviews/README.md` to:

```markdown
# Internal Review Records

This directory is for maintainer review records only. It is not consumer
documentation and must not be indexed as current Lib.Db usage guidance.

Current consumer guidance lives in `README.md`, `docs/*.md`, `.agent/skills/lib-db`,
and `.claude/skills/lib-db`.

`archive/` contains historical internal reviews. Archived files may mention old
skill filenames, old verification commands, or rejected findings. They are retained
for audit history only.
```

- [ ] **Step 4: Add docs archive regression tests**

In `VerificationEntryPointTests.cs`, add:

```csharp
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
}
```

Ensure `EnumeratePublicDocumentationFiles` continues to exclude `docs/reviews`.

- [ ] **Step 5: Run focused verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests|ConsumerSkillTests"
```

Expected: docs and skill tests pass.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add docs/reviews Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "docs: archive stale internal review records"
```

## Task 4: Add Release Package Verification Gate

**Files:**

- Create: `Verification/scripts/Invoke-ReleasePackage.ps1`
- Modify: `Verification/scripts/Invoke-Verification.ps1`
- Modify: `Verification/manifest.json`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs`
- Remove ignored stale artifacts under `artifacts/package-consumer-check/` if present.

- [ ] **Step 1: Write script presence tests**

In `VerificationEntryPointTests.cs`, add:

```csharp
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
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests"
```

Expected: failure because `Invoke-ReleasePackage.ps1` is not present and manifest/wrapper do not reference it.

- [ ] **Step 3: Create Invoke-ReleasePackage.ps1**

Create `Verification/scripts/Invoke-ReleasePackage.ps1` with this structure:

```powershell
param(
    [string] $ArtifactsDirectory = 'Verification\artifacts\packages',
    [bool] $AllowUnsigned = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'Lib.Db\Lib.Db.csproj'

function Resolve-RepoChildPath {
    param(
        [Parameter(Mandatory = $true)] [string] $PathValue,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    if ([System.IO.Path]::IsPathFullyQualified($PathValue)) {
        throw "$Name must be a relative path under the repository root."
    }

    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
    $relative = [System.IO.Path]::GetRelativePath($root, $fullPath)
    if ($relative.StartsWith('..') -or [System.IO.Path]::IsPathFullyQualified($relative)) {
        throw "$Name resolved outside the repository root."
    }

    return $fullPath
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [Parameter(Mandatory = $true)] [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Get-ProjectProperty {
    param([xml] $ProjectXml, [string] $Name)
    $nodes = $ProjectXml.Project.PropertyGroup.$Name | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($nodes.Count -eq 0) {
        return $null
    }

    return [string] $nodes[0]
}

function Get-ProjectPackageReferences {
    param([xml] $ProjectXml)

    return @($ProjectXml.Project.ItemGroup.PackageReference | ForEach-Object {
        [pscustomobject]@{
            Include = [string] $_.Include
            Version = [string] $_.Version
        }
    } | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Include) -and
        -not [string]::IsNullOrWhiteSpace($_.Version)
    })
}

function Assert-PackageDependenciesMatchProject {
    param(
        [Parameter(Mandatory = $true)] [xml] $ProjectXml,
        [Parameter(Mandatory = $true)] [xml] $NuspecXml
    )

    $projectReferences = @(Get-ProjectPackageReferences -ProjectXml $ProjectXml)
    $nuspecDependencies = @($NuspecXml.package.metadata.dependencies.group.dependency | ForEach-Object {
        [pscustomobject]@{
            Id = [string] $_.id
            Version = [string] $_.version
        }
    })

    foreach ($reference in $projectReferences) {
        $match = $nuspecDependencies | Where-Object {
            $_.Id -eq $reference.Include -and $_.Version -eq $reference.Version
        } | Select-Object -First 1

        if ($null -eq $match) {
            throw "Package dependency mismatch: $($reference.Include) $($reference.Version)"
        }
    }
}

function Test-OnlyAcceptedUnsignedNuGetFailure {
    param([Parameter(Mandatory = $true)] [string] $VerifyText)

    $nuCodes = [regex]::Matches($VerifyText, 'NU\d{4}') |
        ForEach-Object { $_.Value } |
        Sort-Object -Unique

    return @($nuCodes).Count -eq 1 -and $nuCodes[0] -eq 'NU3004'
}

function Expand-Nupkg {
    param([string] $PackagePath, [string] $Destination)
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $Destination -Force
}

Write-Host 'Lib.Db v2.3.0 release package verification started.'

$artifactRoot = Resolve-RepoChildPath -PathValue $ArtifactsDirectory -Name 'ArtifactsDirectory'
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot | Out-Null

[xml] $projectXml = Get-Content -LiteralPath $project
$version = Get-ProjectProperty -ProjectXml $projectXml -Name 'Version'
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Lib.Db.csproj Version is missing.'
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
    throw 'Failed to resolve HEAD commit for package verification.'
}

Invoke-Checked 'dotnet' @(
    'pack',
    $project,
    '-c', 'Release',
    '--no-restore',
    '-o', $artifactRoot,
    "-p:RepositoryCommit=$head",
    "-p:SourceRevisionId=$head",
    '-v:minimal'
)

$nupkg = Join-Path $artifactRoot "Lib.Db.$version.nupkg"
$snupkg = Join-Path $artifactRoot "Lib.Db.$version.snupkg"
if (-not (Test-Path -LiteralPath $nupkg)) { throw "Expected nupkg was not produced: $nupkg" }
if (-not (Test-Path -LiteralPath $snupkg)) { throw "Expected snupkg was not produced: $snupkg" }

$expanded = Join-Path $artifactRoot 'expanded'
Expand-Nupkg -PackagePath $nupkg -Destination $expanded
$nuspecPath = Get-ChildItem -LiteralPath $expanded -Filter '*.nuspec' -File | Select-Object -First 1
if ($null -eq $nuspecPath) { throw 'Package nuspec was not found.' }

[xml] $nuspec = Get-Content -LiteralPath $nuspecPath.FullName
$metadata = $nuspec.package.metadata
if ($metadata.id -ne 'Lib.Db') { throw "Unexpected package id: $($metadata.id)" }
if ($metadata.version -ne $version) { throw "Unexpected package version: $($metadata.version)" }
if ([string]::IsNullOrWhiteSpace($metadata.license.expression)) { throw 'Package license expression is missing.' }
if ([string]::IsNullOrWhiteSpace($metadata.readme)) { throw 'Package readme metadata is missing.' }
if ([string]::IsNullOrWhiteSpace($metadata.repository.url)) { throw 'Package repository URL is missing.' }
if (-not [string]::IsNullOrWhiteSpace($metadata.repository.commit) -and
    -not $head.StartsWith($metadata.repository.commit, [System.StringComparison]::OrdinalIgnoreCase) -and
    -not $metadata.repository.commit.StartsWith($head, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Package repository commit does not match HEAD."
}
Assert-PackageDependenciesMatchProject -ProjectXml $projectXml -NuspecXml $nuspec

$verifyOutput = & dotnet nuget verify $nupkg --all 2>&1
$verifyExitCode = $LASTEXITCODE
$verifyText = $verifyOutput -join [Environment]::NewLine
if ($verifyExitCode -ne 0) {
    if ($AllowUnsigned -and (Test-OnlyAcceptedUnsignedNuGetFailure -VerifyText $verifyText)) {
        Write-Warning 'Package is unsigned. NU3004 accepted by explicit v2.3.0 unsigned-package policy.'
    }
    else {
        Write-Host $verifyText
        throw "dotnet nuget verify failed with exit code $verifyExitCode."
    }
}

Write-Host "Package=$nupkg"
Write-Host "SymbolsPackage=$snupkg"
Write-Host 'Lib.Db v2.3.0 release package verification completed.'
```

- [ ] **Step 4: Wire release package gate into full verification**

In `Invoke-Verification.ps1`, add:

```powershell
$releasePackageScript = Join-Path $PSScriptRoot 'Invoke-ReleasePackage.ps1'
```

Run it after AOT and before benchmarks:

```powershell
& pwsh -NoProfile -File $releasePackageScript
if ($LASTEXITCODE -ne 0) {
    throw "Release package verification failed with exit code $LASTEXITCODE."
}
```

- [ ] **Step 5: Register the gate in manifest**

Add to `Verification/manifest.json` scripts:

```json
"releasePackage": "scripts/Invoke-ReleasePackage.ps1"
```

- [ ] **Step 6: Remove stale package-consumer-check artifacts**

Run:

```powershell
$repoRoot = 'C:\Users\js\Documents\Codex\Lib.Db'
$artifactPath = Join-Path $repoRoot 'artifacts\package-consumer-check'
if (Test-Path -LiteralPath $artifactPath) {
    git -C $repoRoot check-ignore -q artifacts/package-consumer-check
    if ($LASTEXITCODE -ne 0) {
        throw 'Refusing to remove package-consumer-check because it is not ignored.'
    }
    Remove-Item -LiteralPath $artifactPath -Recurse -Force
}
```

This removal is allowed only after the path is resolved under the repository root and
confirmed ignored by git.

- [ ] **Step 7: Run focused verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests"
pwsh -NoProfile -File Verification/scripts/Invoke-ReleasePackage.ps1
pwsh -NoProfile -File Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1
```

Expected: tests pass; release package script completes; unsigned package warning is accepted only through explicit policy; generated artifacts remain untracked.

- [ ] **Step 8: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add Verification/scripts/Invoke-ReleasePackage.ps1 Verification/scripts/Invoke-Verification.ps1 Verification/manifest.json Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "build: add release package verification gate"
```

## Task 5: Add AOT Warning Baseline Gate

**Files:**

- Create: `Verification/baselines/aot-warnings.json`
- Modify: `Verification/scripts/Invoke-Aot.ps1`
- Modify: `docs/verification.md`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs`

- [ ] **Step 1: Add script/baseline tests**

In `VerificationEntryPointTests.cs`, add:

```csharp
[Fact]
public void AotVerification_ShouldUseWarningBaseline()
{
    DirectoryInfo repoRoot = FindRepoRoot();
    string baselinePath = Path.Combine(repoRoot.FullName, "Verification", "baselines", "aot-warnings.json");
    string script = File.ReadAllText(Path.Combine(repoRoot.FullName, "Verification", "scripts", "Invoke-Aot.ps1"));
    string verificationDoc = File.ReadAllText(Path.Combine(repoRoot.FullName, "docs", "verification.md"));

    File.Exists(baselinePath).Should().BeTrue();
    script.Should().Contain("aot-warnings.json");
    script.Should().Contain("Assert-AotWarningsMatchBaseline");
    script.Should().Contain("Lib.Db");
    verificationDoc.Should().Contain("AOT warning baseline");
    verificationDoc.Should().Contain("Verification/baselines/aot-warnings.json");
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests"
```

Expected: failure because baseline and parser are not present.

- [ ] **Step 3: Create the warning baseline**

Create `Verification/baselines/aot-warnings.json`:

```json
{
  "version": 1,
  "policy": "Lib.Db-owned IL warnings are release blockers. Provider-owned warnings are allowed only when the id and assembly match this baseline.",
  "allowedWarnings": [
    {
      "id": "IL2104",
      "assembly": "Microsoft.Data.SqlClient",
      "owner": "provider",
      "rationale": "Provider-owned trim warning from Microsoft.Data.SqlClient 7.0.1 during Native AOT publish."
    },
    {
      "id": "IL3053",
      "assembly": "Microsoft.Data.SqlClient",
      "owner": "provider",
      "rationale": "Provider-owned AOT analysis warning from Microsoft.Data.SqlClient 7.0.1 during Native AOT publish."
    },
    {
      "id": "IL2104",
      "assembly": "Microsoft.Data.SqlClient.Internal.Logging",
      "owner": "provider",
      "rationale": "Transitive provider-owned trim warning emitted by SqlClient internal logging package."
    },
    {
      "id": "IL2104",
      "assembly": "System.Configuration.ConfigurationManager",
      "owner": "provider",
      "rationale": "Transitive framework package trim warning emitted through SqlClient dependency chain."
    }
  ]
}
```

- [ ] **Step 4: Capture publish output in Invoke-Aot.ps1**

Replace the current `Invoke-Checked 'dotnet'` publish call with output capture:

```powershell
$publishArguments = @(
    'publish',
    $aotProject,
    '-c', 'Release',
    '-r', $aotRid,
    '--self-contained', 'true',
    '-p:PublishAot=true',
    '-p:TreatWarningsAsErrors=true',
    '-p:TrimmerSingleWarn=false',
    '-o', $publishDirectory,
    '-v:minimal'
)

$publishOutput = & dotnet @publishArguments 2>&1
$publishExitCode = $LASTEXITCODE
$publishOutput | ForEach-Object { Write-Host $_ }
if ($publishExitCode -ne 0) {
    throw "dotnet publish failed with exit code $publishExitCode."
}
```

- [ ] **Step 5: Add warning parser and baseline assertion**

Add these functions to `Invoke-Aot.ps1`:

```powershell
function Get-AotWarnings {
    param([Parameter(Mandatory = $true)] [object[]] $PublishOutput)

    $warnings = [System.Collections.Generic.List[object]]::new()
    foreach ($lineObject in $PublishOutput) {
        $line = [string] $lineObject
        if ($line -notmatch ':\s*warning\s+(IL\d+):') {
            continue
        }

        $id = $Matches[1]
        $pathPart = ($line -split '\s*:\s*warning\s+IL\d+:', 2)[0]
        $assembly = [System.IO.Path]::GetFileNameWithoutExtension($pathPart)
        if ([string]::IsNullOrWhiteSpace($assembly)) {
            $assembly = 'unknown'
        }

        $warnings.Add([pscustomobject]@{
            Id = $id
            Assembly = $assembly
            Line = $line
        })
    }

    return $warnings.ToArray()
}

function Assert-AotWarningsMatchBaseline {
    param([Parameter(Mandatory = $true)] [object[]] $Warnings)

    $baselinePath = Join-Path $repoRoot 'Verification\baselines\aot-warnings.json'
    if (-not (Test-Path -LiteralPath $baselinePath)) {
        throw "AOT warning baseline not found: $baselinePath"
    }

    $baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    $allowed = @($baseline.allowedWarnings)

    foreach ($warning in $Warnings) {
        if ($warning.Assembly.Equals('Lib.Db', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Lib.Db-owned AOT warning is not allowed: $($warning.Id) $($warning.Line)"
        }

        $match = $allowed | Where-Object {
            $_.id -eq $warning.Id -and
            $_.assembly -eq $warning.Assembly
        } | Select-Object -First 1

        if ($null -eq $match) {
            throw "AOT warning is not in baseline: $($warning.Id) assembly=$($warning.Assembly)"
        }
    }

    foreach ($entry in $allowed) {
        $seen = $Warnings | Where-Object {
            $_.Id -eq $entry.id -and
            $_.Assembly -eq $entry.assembly
        } | Select-Object -First 1

        if ($null -eq $seen) {
            throw "AOT warning baseline entry was not observed. Update baseline intentionally if removed: $($entry.id) assembly=$($entry.assembly)"
        }
    }
}
```

After publish output capture, call:

```powershell
$warnings = @(Get-AotWarnings -PublishOutput $publishOutput)
Assert-AotWarningsMatchBaseline -Warnings $warnings
Write-Host "AotWarnings=$($warnings.Count)"
```

- [ ] **Step 6: Document AOT warning policy**

In `docs/verification.md`, add a short section:

```markdown
## AOT warning baseline

Native AOT publish must have zero Lib.Db-owned IL warnings. Provider-owned warnings are
accepted only when the warning id and assembly match
`Verification/baselines/aot-warnings.json`.

When Microsoft.Data.SqlClient or transitive packages are upgraded, rerun
`Verification/scripts/Invoke-Aot.ps1` and update the baseline only after reviewing the
new warning owner and impact.
```

- [ ] **Step 7: Run focused verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests"
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: tests pass; AOT publish runs; current provider warnings match the baseline.

- [ ] **Step 8: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add Verification/baselines/aot-warnings.json Verification/scripts/Invoke-Aot.ps1 docs/verification.md Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "build: baseline native aot warnings"
```

## Task 6: Fix Benchmark Filter Overlap

**Files:**

- Modify: `Verification/scripts/Invoke-Benchmarks.ps1`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs`

- [ ] **Step 1: Update benchmark wrapper test expectation**

In `BenchmarkWrapper_ShouldExpandCustomNarrowTvpFilterToWide`, change the expected output to exact class filters:

```csharp
combined.Should().Contain(
    "ResolvedFilters=*Lib.Db.Benchmarks.TvpBenchmarks*, *Lib.Db.Benchmarks.WideTvpBenchmarks*");
```

Add a second test:

```csharp
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
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests"
```

Expected: default filter assertion fails until the wrapper emits exact class filters.

- [ ] **Step 3: Update Get-BenchmarkFiltersToRun**

In `Invoke-Benchmarks.ps1`, change the exact base TVP default branch:

```powershell
if ($normalized.Equals('*TvpBenchmarks*', [System.StringComparison]::OrdinalIgnoreCase) -or
    $normalized.Equals('TvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase) -or
    $normalized.Equals('Lib.Db.Benchmarks.TvpBenchmarks*', [System.StringComparison]::OrdinalIgnoreCase)) {
    return @('*Lib.Db.Benchmarks.TvpBenchmarks*', '*Lib.Db.Benchmarks.WideTvpBenchmarks*')
}
```

Keep the custom-filter branch that replaces `TvpBenchmarks` with `WideTvpBenchmarks`.

- [ ] **Step 4: Run focused verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "VerificationEntryPointTests"
pwsh -NoProfile -File Verification/scripts/Invoke-Benchmarks.ps1 -Job Dry -SkipSetup -SkipRun -SkipSecretScan -AllowPartial
```

Expected: exact non-overlapping filters are printed and the required benchmark-type checks remain active.

- [ ] **Step 5: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add Verification/scripts/Invoke-Benchmarks.ps1 Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationEntryPointTests.cs
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "build: avoid overlapping tvp benchmark filters"
```

## Task 7: Final Documentation And Full Release Verification

**Files:**

- Modify: `docs/history.md`
- Modify: `docs/04_operations.md`
- Modify: `README.md` only if release policy needs a consumer-visible note.

- [ ] **Step 1: Add release policy notes**

In `docs/history.md`, add a v2.3.0 release-readiness bullet:

```markdown
- Release verification now includes package provenance checks, explicit unsigned package
  policy handling, AOT warning baseline checks, and generated artifact tracking.
```

In `docs/04_operations.md`, add a short observability note:

```markdown
`EnableObservability` is a process-wide Lib.Db switch in v2.3.0. Configure it once at
startup through `AddLibDb`/`AddHighPerformanceDb`, or use
`LibDbRuntime.ConfigureMetrics(bool)` as an explicit process-wide override.
```

- [ ] **Step 2: Run full focused tests before the expensive gate**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Filter "OptionsValidationTests|PoolMetricsTests|DbDiagnosticsTests|VerificationEntryPointTests|ConsumerSkillTests|RuntimeUtilityCoverageTests"
```

Expected: all selected tests pass.

- [ ] **Step 3: Run script gates individually**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-ReleasePackage.ps1
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
pwsh -NoProfile -File Verification/scripts/Scan-VerificationArtifacts.ps1
pwsh -NoProfile -File Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1
```

Expected:

- release package gate passes and accepts unsigned status only by policy,
- AOT warnings match baseline,
- artifact scanner finds no secret patterns,
- generated artifacts remain ignored/untracked.

- [ ] **Step 4: Run full release verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1
```

Expected:

- Matrix DB tests pass.
- Coverage tests pass and all coverage gates pass.
- AOT publish passes and warning baseline matches.
- Release package gate passes.
- Tvp and WideTvp benchmarks both produce reports.
- Artifact scan and tracking gate pass.
- Final output includes `Lib.Db v2.3.0 release-grade verification completed.`

- [ ] **Step 5: Run diff checks**

Run:

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db diff --check
git -C C:\Users\js\Documents\Codex\Lib.Db status --short
```

Expected: no whitespace errors; only intended source/doc/script changes before final commit.

- [ ] **Step 6: Commit**

```powershell
git -C C:\Users\js\Documents\Codex\Lib.Db add README.md docs/history.md docs/04_operations.md Verification/scripts Verification/baselines Verification/manifest.json Verification/projects/Lib.Db.IntegrationTests Lib.Db
git -C C:\Users\js\Documents\Codex\Lib.Db commit -m "chore: close v2.3.0 release blockers"
```

## Task 8: Required Final Reviews

**Files:**

- No code changes expected.

- [ ] **Step 1: Request security review**

Ask a reviewer to check the final diff against the six blockers:

```text
Review HEAD against the previous release-blocker design. Verify connection-string
redaction, observability default-off, package provenance, AOT baseline, stale docs
archive, generated artifact tracking, and release package policy. Report P1/High/P2
findings and release verdict.
```

- [ ] **Step 2: Request docs/skills review**

Ask a reviewer to check:

```text
Verify README.md, docs/, .agent/skills/lib-db, and .claude/skills/lib-db.
Confirm Runtime TVP guidance is current, archived review files cannot be mistaken for
current skill guidance, and internal verification/benchmark/coverage workflow does not
appear in consumer docs or skills.
```

- [ ] **Step 3: Request release/process review**

Ask a reviewer to check:

```text
Verify the v2.3.0 Release package is generated from HEAD, dependency metadata matches
Lib.Db.csproj, unsigned policy is explicit, AOT warning baseline is enforced, and
Invoke-Verification.ps1 includes every release gate.
```

- [ ] **Step 4: Close or fix review findings**

If any reviewer reports P1/High or release-blocking P2, do not release. Fix the issue,
rerun the focused verification for that area, rerun full `Invoke-Verification.ps1`, and
commit the fix.

## Final Success Criteria

The release blockers are closed only when all of these are true:

- `Invoke-Verification.ps1` passes without skip flags.
- `Invoke-ReleasePackage.ps1` proves the package is generated from HEAD.
- `Invoke-Aot.ps1` proves warnings match `Verification/baselines/aot-warnings.json`.
- Security review reports no P1/High release blockers.
- Docs/skills review reports no stale guidance release blocker.
- Release/process review reports the package gate is sufficient for the explicit
  unsigned v2.3.0 policy.
- `git status --short` is clean after the final commit.
