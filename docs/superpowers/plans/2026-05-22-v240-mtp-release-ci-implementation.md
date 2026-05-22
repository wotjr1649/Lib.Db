# v2.4.0 MTP Release Scripts and CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the official v2.4.0 release verification scripts and publish CI from VSTest-style arguments to xUnit v3 Microsoft.Testing.Platform arguments.

**Architecture:** Keep the MTP runner opt-in from the spike branch. Move release scripts to explicit MTP flags, preserve existing coverage/report/artifact gates, and fail fast on zero-test or unsupported VSTest filter syntax.

**Tech Stack:** PowerShell 7, .NET 10 SDK, xUnit v3 MTP v2, Microsoft.Testing.Extensions.TrxReport, Microsoft.Testing.Extensions.CodeCoverage, GitHub Actions.

---

### Task 1: Convert Release Matrix Test Gate

**Files:**
- Modify: `Verification/scripts/Invoke-Verification.ps1`

- [ ] **Step 1: Replace VSTest matrix arguments**

Change the matrix `dotnet test` call to:

```powershell
Invoke-Checked 'dotnet' @(
    'test',
    '--project', $integrationProject,
    '--no-build',
    '--filter-class', '*V230TvpMatrixTests*',
    '--minimum-expected-tests', '1',
    '--report-trx',
    '--report-trx-filename', 'v230-matrix.trx',
    '--results-directory', $matrixResultsDirectory,
    '-v:minimal'
)
```

- [ ] **Step 2: Verify TRX file creation**

After the test run, add a check:

```powershell
$matrixTrx = Get-ChildItem -LiteralPath $matrixResultsDirectory -Recurse -Filter 'v230-matrix.trx' -File |
    Select-Object -First 1
if ($null -eq $matrixTrx) {
    throw 'MTP matrix test gate did not produce v230-matrix.trx.'
}
Write-Host "MatrixTrx=$($matrixTrx.FullName)"
```

- [ ] **Step 3: Run targeted verification**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Verification.ps1 -SkipCoverage -SkipBenchmark -SkipAot -SkipReleasePackage
```

Expected: matrix DB tests run with MTP and `v230-matrix.trx` is produced.

### Task 2: Convert Test Wrapper CLI

**Files:**
- Modify: `Verification/scripts/Invoke-Tests.ps1`

- [ ] **Step 1: Add MTP parameters**

Update the param block with:

```powershell
[string] $FilterClass,
[string] $FilterMethod,
[string] $FilterTrait,
[string] $FilterQuery,
[switch] $ReportTrx,
[string] $TrxFileName,
```

- [ ] **Step 2: Add filter translator**

Add:

```powershell
function Add-MtpFilterArguments {
    param(
        [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments,
        [string] $Filter,
        [string] $FilterClass,
        [string] $FilterMethod,
        [string] $FilterTrait,
        [string] $FilterQuery
    )

    $hasNativeFilter = -not [string]::IsNullOrWhiteSpace($FilterClass) -or
        -not [string]::IsNullOrWhiteSpace($FilterMethod) -or
        -not [string]::IsNullOrWhiteSpace($FilterTrait) -or
        -not [string]::IsNullOrWhiteSpace($FilterQuery)

    if ($hasNativeFilter -and -not [string]::IsNullOrWhiteSpace($Filter)) {
        throw 'Use either -Filter for a simple VSTest FullyQualifiedName~ClassName translation or native MTP filter parameters, not both.'
    }

    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        if ($Filter -match '^FullyQualifiedName~(?<class>[A-Za-z0-9_.+]+)$') {
            $Arguments.Add('--filter-class')
            $Arguments.Add("*$($Matches['class'].Split('.')[-1])*")
            $Arguments.Add('--minimum-expected-tests')
            $Arguments.Add('1')
            return
        }

        throw 'MTP does not support arbitrary VSTest --filter syntax for xUnit v3. Use -FilterClass, -FilterMethod, -FilterTrait, or -FilterQuery.'
    }

    if (-not [string]::IsNullOrWhiteSpace($FilterClass)) {
        $Arguments.Add('--filter-class')
        $Arguments.Add($FilterClass)
        $Arguments.Add('--minimum-expected-tests')
        $Arguments.Add('1')
    }
    if (-not [string]::IsNullOrWhiteSpace($FilterMethod)) {
        $Arguments.Add('--filter-method')
        $Arguments.Add($FilterMethod)
        $Arguments.Add('--minimum-expected-tests')
        $Arguments.Add('1')
    }
    if (-not [string]::IsNullOrWhiteSpace($FilterTrait)) {
        $Arguments.Add('--filter-trait')
        $Arguments.Add($FilterTrait)
        $Arguments.Add('--minimum-expected-tests')
        $Arguments.Add('1')
    }
    if (-not [string]::IsNullOrWhiteSpace($FilterQuery)) {
        $Arguments.Add('--filter-query')
        $Arguments.Add($FilterQuery)
        $Arguments.Add('--minimum-expected-tests')
        $Arguments.Add('1')
    }
}
```

- [ ] **Step 3: Translate TRX logger**

Replace `--logger` handling with MTP reporting:

```powershell
if ($ReportTrx -or $Logger -like 'trx*') {
    $dotnetArguments.Add('--report-trx')
    if (-not [string]::IsNullOrWhiteSpace($TrxFileName)) {
        $dotnetArguments.Add('--report-trx-filename')
        $dotnetArguments.Add($TrxFileName)
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($Logger)) {
    throw 'MTP only supports -Logger trx compatibility in Invoke-Tests.ps1. Use MTP report options for other formats.'
}
```

- [ ] **Step 4: Run wrapper smoke tests**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -Target IntegrationTests -NoRestore -FilterClass '*CacheHostingCoverageTests*'
```

Expected: 32 tests pass.

### Task 3: Convert Coverage Script

**Files:**
- Modify: `Verification/scripts/Assert-Coverage.ps1`
- Modify: `Verification/scripts/Invoke-Coverage.ps1`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/TvpCoreCoverageTests.cs`

- [ ] **Step 1: Use MTP coverage settings**

Replace `$runSettings` with:

```powershell
$coverageSettings = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\mtp-codecoverage.config.xml'
```

- [ ] **Step 2: Use fixed Cobertura output**

Before `dotnet test`, add:

```powershell
$coverageOutput = Join-Path $resultsPath 'coverage.cobertura.xml'
```

Replace the VSTest collector invocation with:

```powershell
Invoke-Checked 'dotnet' @(
    'test',
    '--project', $testProject,
    '-c', $Configuration,
    '--no-restore',
    '--coverage',
    '--coverage-output-format', 'cobertura',
    '--coverage-output', $coverageOutput,
    '--coverage-settings', $coverageSettings,
    '--results-directory', $resultsPath,
    '-v:minimal'
)
```

- [ ] **Step 3: Run coverage gate**

Add coverage-gate compatibility before running the full gate:

```powershell
function Convert-CoverageClassName {
    param([Parameter(Mandatory = $true)] [string] $Name)

    return [System.Text.RegularExpressions.Regex]::Replace(
        $Name,
        '<[^>]+>',
        {
            param($Match)
            $genericArguments = $Match.Value.Trim('<', '>').Split(',').Count
            return "$([char] 0x60)$genericArguments"
        })
}
```

Add targeted tests for Microsoft Code Coverage branch semantics:

```csharp
RuntimeTvpDataReader.NormalizeValue((Half)1.5f, typeof(double)).Should().Be((Half)1.5f);
foreach (SqlDbType dbType in Enum.GetValues<SqlDbType>())
{
    TvpColumnShape.FromSql<object>($"All{dbType}Value", dbType, false, 0, 0, 0).DbType.Should().Be(dbType);
}
```

Then run:

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Coverage.ps1 -RestoreTools
```

Expected: Cobertura file is produced, reportgenerator runs, coverage gate passes.

### Task 4: Update Publish Workflow Artifact Handling

**Files:**
- Modify: `.github/workflows/publish.yml`

- [ ] **Step 1: Add verification artifact upload**

After the release gate step, add:

```yaml
    - name: Upload verification artifacts
      if: always()
      uses: actions/upload-artifact@v4
      with:
        name: verification-artifacts-${{ github.run_id }}
        path: Verification/artifacts/**
        if-no-files-found: warn
        retention-days: 7
```

- [ ] **Step 2: Keep publish credentials unchanged**

Do not change `NUGET_API_KEY`, `LIBDB_TEST_SQL_PASSWORD`, SQL Server service configuration, or NuGet push command.

### Task 5: Full Verification and Review

**Files:**
- Review all changed files.

- [ ] **Step 1: Run spike regression**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-MtpSpike.ps1 -NoRestore
```

Expected: all spike scenarios pass.

- [ ] **Step 2: Run official release gate**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Verification.ps1 -BenchmarkJob Short
```

Expected: build, matrix DB, coverage, AOT, package, benchmark, artifact scan, and tracking gates pass.

- [ ] **Step 3: Run static checks**

Run:

```powershell
rg -n -e "xUnit1051|NoWarn" Verification/projects/Lib.Db.IntegrationTests
git diff --check
```

Expected: `rg` finds no matches, `git diff --check` exits 0.

- [ ] **Step 4: Commit and PR**

Commit:

```powershell
git add --force docs/superpowers/specs/2026-05-22-v240-mtp-release-ci-design.md docs/superpowers/plans/2026-05-22-v240-mtp-release-ci-implementation.md
git add Verification/scripts/Invoke-Verification.ps1 Verification/scripts/Invoke-Tests.ps1 Verification/scripts/Invoke-Coverage.ps1 .github/workflows/publish.yml
git commit -m "ci: migrate release verification to mtp"
```

Open a stacked PR:

```powershell
gh pr create --base v2.4.0-mtp-migration-spike --head v2.4.0-mtp-release-ci --title "ci: migrate release verification to MTP" --draft
```
