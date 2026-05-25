param(
    [string] $Configuration = 'Debug',
    [string] $ResultsDirectory = 'Verification\artifacts\coverage\raw',
    [string] $ReportDirectory = 'Verification\artifacts\coverage\report',
    [switch] $SkipReport,
    [switch] $SkipGate,
    [switch] $RestoreTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$testProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
$coverageSettings = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\mtp-codecoverage.config.xml'
$assertScript = Join-Path $PSScriptRoot 'Assert-LibDbCoverage.ps1'
$localEnvironmentScript = Join-Path $PSScriptRoot 'Set-LibDbVerificationEnvironment.local.ps1'

if (Test-Path -LiteralPath $localEnvironmentScript) {
    . $localEnvironmentScript -NoBenchmarkReset
    Write-Host "Loaded local verification environment script: $localEnvironmentScript"
}
else {
    Write-Host 'Local verification environment script not found; using existing process environment.'
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

function Get-LatestCoverageFile {
    param([Parameter(Mandatory = $true)] [string] $Root)

    $coverage = Get-ChildItem -LiteralPath $Root -Recurse -Filter 'coverage.cobertura.xml' -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $coverage) {
        throw "coverage.cobertura.xml was not produced under $Root."
    }

    return $coverage.FullName
}

$resultsPath = Resolve-RepoChildPath -PathValue $ResultsDirectory -Name 'ResultsDirectory'
$reportPath = Resolve-RepoChildPath -PathValue $ReportDirectory -Name 'ReportDirectory'
$coverageOutput = Join-Path $resultsPath 'coverage.cobertura.xml'

Write-Host 'Lib.Db coverage run started.'
Write-Host "ResultsDirectory=$ResultsDirectory"
Write-Host "ReportDirectory=$ReportDirectory"

if (Test-Path -LiteralPath $resultsPath) {
    Remove-Item -LiteralPath $resultsPath -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

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

$coveragePath = Get-LatestCoverageFile -Root $resultsPath
Write-Host "Cobertura=$coveragePath"

if (-not $SkipReport) {
    if ($RestoreTools) {
        Invoke-Checked 'dotnet' @('tool', 'restore')
    }

    if (Test-Path -LiteralPath $reportPath) {
        Remove-Item -LiteralPath $reportPath -Recurse -Force
    }

    Invoke-Checked 'dotnet' @(
        'tool', 'run', 'reportgenerator',
        "-reports:$coveragePath",
        "-targetdir:$reportPath",
        '-reporttypes:Html;TextSummary;Cobertura',
        '-assemblyfilters:+Lib.Db;-Lib.Db.IntegrationTests;-Lib.Db.Benchmarks;-Lib.Db.AotVerification'
    )

    Write-Host "CoverageReport=$reportPath"
}

if (-not $SkipGate) {
    & pwsh -NoProfile -File $assertScript -CoberturaPath $coveragePath
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage gate failed with exit code $LASTEXITCODE."
    }
}

Write-Host 'Lib.Db coverage run completed.'
