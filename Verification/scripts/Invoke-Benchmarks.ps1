param(
    [ValidateSet('NarrowWideOnly', 'FullMatrix')]
    [string] $SetupMode = 'FullMatrix',
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $Job = 'Short',
    [string] $Filter = '*TvpBenchmarks*',
    [switch] $SkipSetup,
    [switch] $SkipRun,
    [switch] $SkipSecretScan,
    [switch] $UseLocalEnvironment,
    [switch] $AllowPartial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj'
$artifactRoot = Join-Path $repoRoot 'Verification\artifacts\benchmarks\BenchmarkDotNet.Artifacts'
$scanner = Join-Path $PSScriptRoot 'Scan-VerificationArtifacts.ps1'
$localEnvironmentScript = Join-Path $PSScriptRoot 'Set-LibDbVerificationEnvironment.local.ps1'

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

function Add-UniqueString {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]] $Items,
        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    if (-not $Items.Contains($Value)) {
        $Items.Add($Value)
    }
}

function Get-BenchmarkFiltersToRun {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BenchmarkFilter
    )

    if ([string]::IsNullOrWhiteSpace($BenchmarkFilter)) {
        throw 'Benchmark filter must not be empty.'
    }

    $normalized = $BenchmarkFilter.Trim()
    if ($normalized.Equals('*TvpBenchmarks*', [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals('TvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalized.Equals('Lib.Db.Benchmarks.TvpBenchmarks*', [System.StringComparison]::OrdinalIgnoreCase)) {
        return @('*Lib.Db.Benchmarks.TvpBenchmarks*', '*Lib.Db.Benchmarks.WideTvpBenchmarks*')
    }

    if ($normalized.Contains('TvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $normalized.Contains('WideTvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase)) {
        $wideFilter = [System.Text.RegularExpressions.Regex]::Replace(
            $normalized,
            'TvpBenchmarks',
            'WideTvpBenchmarks',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        return @($BenchmarkFilter, $wideFilter)
    }

    return @($BenchmarkFilter)
}

function Get-ExpectedBenchmarkTypes {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $BenchmarkFilters
    )

    $types = [System.Collections.Generic.List[string]]::new()
    foreach ($benchmarkFilter in $BenchmarkFilters) {
        if ([string]::IsNullOrWhiteSpace($benchmarkFilter)) {
            continue
        }

        $normalized = $benchmarkFilter.Trim()
        $isBroadBenchmarkFilter =
            $normalized.Equals('*', [System.StringComparison]::OrdinalIgnoreCase) -or
            $normalized.Equals('*Benchmarks*', [System.StringComparison]::OrdinalIgnoreCase) -or
            $normalized.Equals('Lib.Db.Benchmarks.*', [System.StringComparison]::OrdinalIgnoreCase)

        if ($isBroadBenchmarkFilter -or
            ($normalized.Contains('TvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase) -and
             -not $normalized.Contains('WideTvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase))) {
            Add-UniqueString -Items $types -Value 'TvpBenchmarks'
        }

        if ($isBroadBenchmarkFilter -or
            $normalized.Contains('WideTvpBenchmarks', [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-UniqueString -Items $types -Value 'WideTvpBenchmarks'
        }
    }

    return $types.ToArray()
}

function Assert-BenchmarkReportHasMeasurements {
    param(
        [Parameter(Mandatory = $true)]
        [DateTime] $RunStartedUtc,
        [Parameter(Mandatory = $true)]
        [string[]] $ExpectedBenchmarkTypes
    )

    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        throw "Benchmark artifact path was not created: $(Format-RepoRelativePath -Path $artifactRoot)"
    }

    $reports = @(Get-ChildItem -LiteralPath (Join-Path $artifactRoot 'results') -File -Filter '*-report-github.md' -ErrorAction Stop |
        Where-Object { $_.LastWriteTimeUtc -ge $RunStartedUtc } |
        Sort-Object LastWriteTimeUtc -Descending)

    if ($reports.Count -eq 0) {
        throw 'BenchmarkDotNet GitHub markdown report was not produced.'
    }

    foreach ($benchmarkType in $ExpectedBenchmarkTypes) {
        $matching = @($reports | Where-Object {
            $_.BaseName.Contains(".$benchmarkType-report-github", [System.StringComparison]::OrdinalIgnoreCase)
        })

        if ($matching.Count -eq 0) {
            throw "BenchmarkDotNet report for $benchmarkType was not produced in this run."
        }
    }

    $expectedMethods = @(
        'GeneratedAccessorBaseline',
        'RuntimeObjectStreaming',
        'RuntimeRegisteredFastPath'
    )

    foreach ($report in $reports) {
        $content = [System.IO.File]::ReadAllText($report.FullName)
        if ($content.Contains('There are not any results runs', [System.StringComparison]::OrdinalIgnoreCase) -or
            $content.Contains('Build Error', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "BenchmarkDotNet report contains no valid measurements: $(Format-RepoRelativePath -Path $report.FullName)"
        }

        foreach ($method in $expectedMethods) {
            $methodLine = $content -split "`n" |
                Where-Object {
                    $columns = $_ -split '\|'
                    if ($columns.Length -lt 4) {
                        return $false
                    }

                    $methodName = ($columns[1] -replace '[*`]', '').Trim()
                    if ($methodName -ne $method) {
                        return $false
                    }

                    $mean = ($columns[3] -replace '[*`]', '').Trim()
                    return $mean.Length -gt 0 -and $mean -ne 'NA'
                } |
                Select-Object -First 1

            if ($null -eq $methodLine) {
                throw "BenchmarkDotNet report did not contain measured method ${method}: $(Format-RepoRelativePath -Path $report.FullName)"
            }
        }
    }
}

Write-Host 'Lib.Db benchmark run started.'
Assert-BenchmarkConnectionConfigured
Write-Host "SetupMode=$SetupMode"
Write-Host "BenchmarkJob=$Job"
Write-Host "Filter=$Filter"
Write-Host "BenchmarkArtifacts=$(Format-RepoRelativePath -Path $artifactRoot)"

$env:LIBDB_BENCHMARK_ARTIFACTS_PATH = $artifactRoot
$runStartedUtc = [DateTime]::UtcNow.AddSeconds(-5)
$skippedGates = [System.Collections.Generic.List[string]]::new()
$benchmarkFiltersToRun = @(Get-BenchmarkFiltersToRun -BenchmarkFilter $Filter)
$expectedBenchmarkTypes = @(Get-ExpectedBenchmarkTypes -BenchmarkFilters $benchmarkFiltersToRun)
$releaseRequiredBenchmarkTypes = @('TvpBenchmarks', 'WideTvpBenchmarks')

Write-Host "ResolvedFilters=$($benchmarkFiltersToRun -join ', ')"
if ($expectedBenchmarkTypes.Count -gt 0) {
    Write-Host "ExpectedBenchmarkTypes=$($expectedBenchmarkTypes -join ', ')"
}

foreach ($requiredBenchmarkType in $releaseRequiredBenchmarkTypes) {
    if ($expectedBenchmarkTypes -notcontains $requiredBenchmarkType) {
        $skippedGates.Add("benchmark-type:$requiredBenchmarkType")
    }
}

if (-not $SkipSetup -or -not $SkipRun) {
    $env:LIBDB_BENCHMARK_ALLOW_RESET = 'true'
}

if (-not $SkipSetup) {
    $setupArgument = if ($SetupMode -eq 'FullMatrix') { '--setup-full-matrix' } else { '--setup-only' }
    Invoke-Checked 'dotnet' @(
        'run',
        '--no-restore',
        '--project', $project,
        '--property:UseSharedCompilation=false',
        '--',
        $setupArgument
    )
}
else {
    $skippedGates.Add('setup')
}

if (-not $SkipRun) {
    $env:LIBDB_BENCHMARK_JOB = $Job
    foreach ($benchmarkFilter in $benchmarkFiltersToRun) {
        Invoke-Checked 'dotnet' @(
            'run',
            '-c', 'Release',
            '--no-restore',
            '--project', $project,
            '--property:UseSharedCompilation=false',
            '--',
            '--filter', $benchmarkFilter
        )
    }

    Assert-BenchmarkReportHasMeasurements -RunStartedUtc $runStartedUtc -ExpectedBenchmarkTypes $expectedBenchmarkTypes
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

    foreach ($report in $reports) {
        Write-Host "Report=$(Format-RepoRelativePath -Path $report.FullName)"
    }
}

if ($skippedGates.Count -gt 0) {
    Write-Warning "Lib.Db benchmark completed as a PARTIAL run. Skipped gates: $($skippedGates -join ', '). This is not release-grade benchmark evidence."
    if (-not $AllowPartial) {
        throw "Partial benchmark runs require -AllowPartial so CI cannot mistake them for release-grade evidence."
    }
}
else {
    Write-Host 'Lib.Db benchmark run completed.'
}
