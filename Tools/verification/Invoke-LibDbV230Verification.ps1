param(
    [switch] $SkipCoverage,
    [switch] $SkipBenchmark,
    [switch] $SkipMatrixDbTests,
    [switch] $SkipAot,
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $BenchmarkJob = 'Dry',
    [switch] $AllowPartial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$integrationProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
$benchmarkProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj'
$aotProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj'
$coverageScript = Join-Path $repoRoot 'Tools\coverage\Invoke-LibDbCoverage.ps1'
$benchmarkScript = Join-Path $repoRoot 'Tools\benchmark\Invoke-LibDbBenchmarks.ps1'

$skippedGates = [System.Collections.Generic.List[string]]::new()

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

Write-Host 'Lib.Db v2.3.0 verification started.'
Write-SecretSafeEnvironmentSummary

Invoke-Checked 'dotnet' @('build', $integrationProject, '--no-restore', '-v:minimal')
Invoke-Checked 'dotnet' @('build', $benchmarkProject, '--no-restore', '-v:minimal')

if (-not $SkipMatrixDbTests) {
    Invoke-Checked 'dotnet' @(
        'test',
        $integrationProject,
        '--no-build',
        '--filter', 'FullyQualifiedName~Lib.Db.IntegrationTests.V230Matrix.V230TvpMatrixTests',
        '-v:minimal'
    )
}
else {
    $skippedGates.Add('matrix-db-tests')
}

if (-not $SkipCoverage) {
    & pwsh -NoProfile -File $coverageScript
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage verification failed with exit code $LASTEXITCODE."
    }
}
else {
    $skippedGates.Add('coverage')
}

if (-not $SkipAot) {
    if (-not (Test-Path -LiteralPath $aotProject)) {
        throw "AOT verification project was not found: $aotProject"
    }

    Invoke-Checked 'dotnet' @(
        'publish',
        $aotProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishAot=true',
        '-p:TreatWarningsAsErrors=true',
        '-v:minimal'
    )

    $aotPublishDirectory = Join-Path (Split-Path -Parent $aotProject) 'bin\Release\net10.0\win-x64\publish'
    $aotExecutable = Join-Path $aotPublishDirectory 'Lib.Db.AotVerification.exe'
    if (-not (Test-Path -LiteralPath $aotExecutable)) {
        throw "AOT verification executable was not produced: $aotExecutable"
    }

    Invoke-Checked $aotExecutable @()
}
else {
    $skippedGates.Add('aot')
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

if ($skippedGates.Count -gt 0) {
    Write-Warning "Lib.Db v2.3.0 verification completed as a PARTIAL run. Skipped gates: $($skippedGates -join ', '). This is not release-grade evidence."
    if (-not $AllowPartial) {
        throw "Partial verification runs require -AllowPartial so CI cannot mistake them for release-grade evidence."
    }
}
else {
    Write-Host 'Lib.Db v2.3.0 release-grade verification completed.'
}
