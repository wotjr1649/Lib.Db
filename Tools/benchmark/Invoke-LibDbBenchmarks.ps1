param(
    [ValidateSet('NarrowWideOnly', 'FullMatrix')]
    [string] $SetupMode = 'FullMatrix',
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $Job = 'Short',
    [string] $Filter = '*TvpBenchmarks*',
    [switch] $SkipSetup,
    [switch] $SkipRun,
    [switch] $SkipSecretScan,
    [switch] $AllowPartial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj'
$artifactRoot = Join-Path $repoRoot 'BenchmarkDotNet.Artifacts'
$scanner = Join-Path $repoRoot 'Verification\projects\Lib.Db.Benchmarks\ScanBenchmarkArtifacts.ps1'

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Assert-BenchmarkConnectionConfigured {
    $present = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('LIBDB_BENCHMARK_CONNECTION'))
    Write-Host "LIBDB_BENCHMARK_CONNECTION present: $present"
    if (-not $present) {
        Write-Host 'If using appsettings.json instead, BenchmarkDatabase will validate it inside the benchmark process.'
    }
}

function Assert-BenchmarkReportHasMeasurements {
    param([Parameter(Mandatory = $true)] [DateTime] $RunStartedUtc)

    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        throw "Benchmark artifact path was not created: $artifactRoot"
    }

    $report = Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'results') -File -Filter '*-report-github.md' -ErrorAction Stop |
        Where-Object { $_.LastWriteTimeUtc -ge $RunStartedUtc } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $report) {
        throw 'BenchmarkDotNet GitHub markdown report was not produced.'
    }

    $content = [System.IO.File]::ReadAllText($report.FullName)
    if ($content.Contains('There are not any results runs', [System.StringComparison]::OrdinalIgnoreCase) -or
        $content.Contains('Build Error', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "BenchmarkDotNet report contains no valid measurements: $($report.FullName)"
    }

    $measurementLine = $content -split "`n" |
        Where-Object {
            $columns = $_ -split '\|'
            if ($columns.Length -lt 4) {
                return $false
            }

            $method = ($columns[1] -replace '[*`]', '').Trim()
            if ($method -notin @('GeneratedAccessorBaseline', 'RuntimeObjectStreaming', 'RuntimeRegisteredFastPath')) {
                return $false
            }

            $mean = ($columns[3] -replace '[*`]', '').Trim()
            return $mean.Length -gt 0 -and $mean -ne 'NA'
        } |
        Select-Object -First 1

    if ($null -eq $measurementLine) {
        throw "BenchmarkDotNet report did not contain a measured baseline/runtime row: $($report.FullName)"
    }
}

Write-Host 'Lib.Db v2.3.0 benchmark run started.'
Assert-BenchmarkConnectionConfigured
Write-Host "SetupMode=$SetupMode"
Write-Host "BenchmarkJob=$Job"
Write-Host "Filter=$Filter"

$runStartedUtc = [DateTime]::UtcNow.AddSeconds(-5)
$skippedGates = [System.Collections.Generic.List[string]]::new()

if (-not $SkipSetup -or -not $SkipRun) {
    $env:LIBDB_BENCHMARK_ALLOW_RESET = 'true'
}

if (-not $SkipSetup) {
    $setupArgument = if ($SetupMode -eq 'FullMatrix') { '--setup-full-matrix' } else { '--setup-only' }
    Invoke-Checked 'dotnet' @('run', '--no-restore', '--project', $project, '--', $setupArgument)
}
else {
    $skippedGates.Add('setup')
}

if (-not $SkipRun) {
    $env:LIBDB_BENCHMARK_JOB = $Job
    Invoke-Checked 'dotnet' @(
        'run',
        '-c', 'Release',
        '--no-restore',
        '--project', $project,
        '--',
        '--filter', $Filter
    )

    Assert-BenchmarkReportHasMeasurements -RunStartedUtc $runStartedUtc
}
else {
    $skippedGates.Add('run')
}

if (-not $SkipSecretScan) {
    & pwsh -NoProfile -File $scanner -Paths $artifactRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark artifact secret scan failed with exit code $LASTEXITCODE."
    }
}
else {
    $skippedGates.Add('secret-scan')
}

if (Test-Path -LiteralPath $artifactRoot) {
    $reports = Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'results') -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'report' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 12

    Write-Host "BenchmarkArtifacts=$artifactRoot"
    foreach ($report in $reports) {
        Write-Host "Report=$($report.FullName)"
    }
}

if ($skippedGates.Count -gt 0) {
    Write-Warning "Lib.Db v2.3.0 benchmark completed as a PARTIAL run. Skipped gates: $($skippedGates -join ', '). This is not release-grade benchmark evidence."
    if (-not $AllowPartial) {
        throw "Partial benchmark runs require -AllowPartial so CI cannot mistake them for release-grade evidence."
    }
}
else {
    Write-Host 'Lib.Db v2.3.0 benchmark run completed.'
}
