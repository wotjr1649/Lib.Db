param(
    [switch] $SkipCoverage,
    [switch] $SkipBenchmark,
    [switch] $SkipMatrixDbTests,
    [switch] $SkipAot,
    [switch] $SkipReleasePackage,
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $BenchmarkJob = 'Short',
    [switch] $UseLocalEnvironment,
    [switch] $AllowPartial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$integrationProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
$benchmarkProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj'
$testScript = Join-Path $PSScriptRoot 'Invoke-Tests.ps1'
$coverageScript = Join-Path $PSScriptRoot 'Invoke-Coverage.ps1'
$benchmarkScript = Join-Path $PSScriptRoot 'Invoke-Benchmarks.ps1'
$aotScript = Join-Path $PSScriptRoot 'Invoke-Aot.ps1'
$releasePackageScript = Join-Path $PSScriptRoot 'Invoke-ReleasePackage.ps1'
$artifactScanner = Join-Path $PSScriptRoot 'Scan-VerificationArtifacts.ps1'
$artifactTrackingGate = Join-Path $PSScriptRoot 'Assert-GeneratedArtifactsUntracked.ps1'
$localEnvironmentScript = Join-Path $PSScriptRoot 'Set-LibDbVerificationEnvironment.local.ps1'
$matrixResultsDirectory = Join-Path $repoRoot 'Verification\artifacts\test-results\matrix'
$coverageResultsDirectory = 'Verification\artifacts\coverage\raw'
$coverageReportDirectory = 'Verification\artifacts\coverage\report'
$aotArtifactsDirectory = 'Verification\artifacts\aot'
$releasePackageArtifactsDirectory = 'Verification\artifacts\release-package'
$benchmarkArtifactsDirectory = Join-Path $repoRoot 'Verification\artifacts\benchmarks\BenchmarkDotNet.Artifacts'

$skippedGates = [System.Collections.Generic.List[string]]::new()

function Format-RepoRelativePath {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPath = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    if ($fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($rootPath.Length)
    }

    return [System.IO.Path]::GetFileName($fullPath)
}

if ($UseLocalEnvironment) {
    if (-not (Test-Path -LiteralPath $localEnvironmentScript)) {
        throw 'Local verification environment script was requested but not found.'
    }

    . $localEnvironmentScript -NoBenchmarkReset
    Write-Host "Loaded local verification environment script: $(Format-RepoRelativePath -Path $localEnvironmentScript)"
}
else {
    Write-Host 'Local verification environment script not loaded; pass -UseLocalEnvironment to opt in, or use existing process environment.'
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [string[]] $Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Write-SecretSafeEnvironmentSummary {
    $names = @(
        'LIBDB_TEST_CONNECTION_VERIFICATION',
        'LIBDB_TEST_CONNECTION_SORTER',
        'LIBDB_TEST_CONNECTION_STRESS',
        'LIBDB_TEST_CONNECTION_CHAOS',
        'LIBDB_TEST_CONNECTION_BENCHMARK',
        'LIBDB_TEST_SQL_PASSWORD',
        'LIBDB_BENCHMARK_CONNECTION'
    )

    foreach ($name in $names) {
        $present = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))
        Write-Host "$name present: $present"
    }
}

Write-Host 'Lib.Db verification started.'
Write-SecretSafeEnvironmentSummary

Invoke-Checked 'dotnet' @(
    'build',
    $integrationProject,
    '--no-restore',
    '-v:minimal',
    '-p:UseSharedCompilation=false'
)
Invoke-Checked 'dotnet' @(
    'build',
    $benchmarkProject,
    '--no-restore',
    '-v:minimal',
    '-p:UseSharedCompilation=false'
)

if (-not $SkipMatrixDbTests) {
    if (Test-Path -LiteralPath $matrixResultsDirectory) {
        Remove-Item -LiteralPath $matrixResultsDirectory -Recurse -Force
    }

    Invoke-Checked 'pwsh' @(
        '-NoProfile',
        '-File', $testScript,
        '-Target', 'IntegrationTests',
        '-NoRestore',
        '-NoBuild',
        '-FilterClass', '*V230TvpMatrixTests*',
        '-ReportTrx',
        '-TrxFileName', 'v230-matrix.trx',
        '-ResultsDirectory', $matrixResultsDirectory,
        '-Verbosity', 'minimal',
        '-KeepBuildServers'
    )

    $matrixTrx = Get-ChildItem -LiteralPath $matrixResultsDirectory -Recurse -Filter 'v230-matrix.trx' -File |
        Select-Object -First 1
    if ($null -eq $matrixTrx) {
        throw 'MTP matrix test gate did not produce v230-matrix.trx.'
    }

    Write-Host "MatrixTrx=$(Format-RepoRelativePath -Path $matrixTrx.FullName)"
}
else {
    $skippedGates.Add('matrix-db-tests')
}

if (-not $SkipCoverage) {
    & pwsh -NoProfile -File $coverageScript `
        -ResultsDirectory $coverageResultsDirectory `
        -ReportDirectory $coverageReportDirectory `
        -RestoreTools
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage verification failed with exit code $LASTEXITCODE."
    }
}
else {
    $skippedGates.Add('coverage')
}

if (-not $SkipAot) {
    & pwsh -NoProfile -File $aotScript -ArtifactsDirectory $aotArtifactsDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "AOT verification failed with exit code $LASTEXITCODE."
    }
}
else {
    $skippedGates.Add('aot')
}

if (-not $SkipReleasePackage) {
    & pwsh -NoProfile -File $releasePackageScript -ArtifactsDirectory $releasePackageArtifactsDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Release package verification failed with exit code $LASTEXITCODE."
    }
}
else {
    $skippedGates.Add('release-package')
}

if (-not $SkipBenchmark) {
    & pwsh -NoProfile -File $benchmarkScript -Job $BenchmarkJob -Filter '*TvpBenchmarks*'
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark verification failed with exit code $LASTEXITCODE."
    }
}
else {
    $skippedGates.Add('benchmark')
}

& pwsh -NoProfile -File $artifactScanner -SelfTest
if ($LASTEXITCODE -ne 0) {
    throw "Verification artifact secret scan self-test failed with exit code $LASTEXITCODE."
}

$currentArtifactScanPaths = @(
    $matrixResultsDirectory,
    (Join-Path $repoRoot $coverageResultsDirectory),
    (Join-Path $repoRoot $coverageReportDirectory),
    (Join-Path $repoRoot $aotArtifactsDirectory),
    (Join-Path $repoRoot $releasePackageArtifactsDirectory),
    $benchmarkArtifactsDirectory
) | Where-Object { Test-Path -LiteralPath $_ }

if ($currentArtifactScanPaths.Count -gt 0) {
    & pwsh -NoProfile -File $artifactScanner -Paths $currentArtifactScanPaths
    if ($LASTEXITCODE -ne 0) {
        throw "Verification artifact secret scan failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Warning 'No current-run verification artifact paths exist; skipping final artifact secret scan for this partial run.'
}

& pwsh -NoProfile -File $artifactTrackingGate
if ($LASTEXITCODE -ne 0) {
    throw "Generated artifact tracking gate failed with exit code $LASTEXITCODE."
}

if ($skippedGates.Count -gt 0) {
    Write-Warning "Lib.Db verification completed as a PARTIAL run. Skipped gates: $($skippedGates -join ', '). This is not release-grade evidence."
    if (-not $AllowPartial) {
        throw "Partial verification runs require -AllowPartial so CI cannot mistake them for release-grade evidence."
    }
}
else {
    Write-Host 'Lib.Db release-grade verification completed.'
}
