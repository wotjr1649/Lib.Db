param(
    [string] $Configuration = 'Debug',
    [string] $ResultsDirectory = 'Verification\artifacts\coverage\raw',
    [string] $ReportDirectory = 'Verification\artifacts\coverage\report',
    [switch] $SkipReport,
    [switch] $SkipGate,
    [switch] $UseLocalEnvironment,
    [switch] $RestoreTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$integrationProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
$coverageSettings = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\mtp-codecoverage.config.xml'
$assertScript = Join-Path $PSScriptRoot 'Assert-LibDbCoverage.ps1'
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
        throw "coverage.cobertura.xml was not produced under $(Format-RepoRelativePath -Path $Root)."
    }

    return $coverage.FullName
}

function Set-ProcessEnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [AllowNull()] [string] $Value
    )

    $path = "Env:$Name"
    if ($null -eq $Value) {
        Remove-Item -Path $path -ErrorAction SilentlyContinue
        return
    }

    Set-Item -Path $path -Value $Value
}

function Invoke-BuildServerCleanup {
    try {
        & dotnet build-server shutdown
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "dotnet build-server shutdown failed with exit code $LASTEXITCODE."
        }
    }
    catch {
        Write-Warning "dotnet build-server shutdown failed: $($_.Exception.Message)"
    }
}

function Get-ProjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)] [xml] $Project,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    foreach ($propertyGroup in @($Project.Project.PropertyGroup)) {
        foreach ($childNode in @($propertyGroup.ChildNodes)) {
            if ($childNode.Name -eq $Name -and -not [string]::IsNullOrWhiteSpace($childNode.InnerText)) {
                return $childNode.InnerText
            }
        }
    }

    return $null
}

function Get-IntegrationTestApplicationPath {
    [xml] $project = Get-Content -LiteralPath $integrationProject
    $targetFramework = Get-ProjectPropertyValue -Project $project -Name 'TargetFramework'
    if ([string]::IsNullOrWhiteSpace($targetFramework)) {
        $targetFrameworks = Get-ProjectPropertyValue -Project $project -Name 'TargetFrameworks'
        $targetFramework = $targetFrameworks.Split(';')[0]
    }

    if ([string]::IsNullOrWhiteSpace($targetFramework)) {
        throw "Unable to determine TargetFramework from $(Format-RepoRelativePath -Path $integrationProject)."
    }

    $assemblyName = Get-ProjectPropertyValue -Project $project -Name 'AssemblyName'
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($integrationProject)
    }

    $extension = if ($IsWindows) { '.exe' } else { '' }
    $projectDirectory = Split-Path -Parent $integrationProject
    return Join-Path $projectDirectory "bin\$Configuration\$targetFramework\$assemblyName$extension"
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

$savedDisableNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE')
$savedTestingPlatformTelemetryOptOut = [Environment]::GetEnvironmentVariable('TESTINGPLATFORM_TELEMETRY_OPTOUT')
$savedDotnetCliTelemetryOptOut = [Environment]::GetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT')
Set-ProcessEnvironmentVariable -Name 'MSBUILDDISABLENODEREUSE' -Value '1'
Set-ProcessEnvironmentVariable -Name 'TESTINGPLATFORM_TELEMETRY_OPTOUT' -Value '1'
Set-ProcessEnvironmentVariable -Name 'DOTNET_CLI_TELEMETRY_OPTOUT' -Value '1'
try {
    Invoke-Checked 'dotnet' @(
        'build',
        $integrationProject,
        '-c', $Configuration,
        '--no-restore',
        '-v:minimal',
        '-p:UseSharedCompilation=false'
    )

    $testApplication = Get-IntegrationTestApplicationPath
    if (-not (Test-Path -LiteralPath $testApplication)) {
        throw "Test application not found: $(Format-RepoRelativePath -Path $testApplication)."
    }

    Invoke-Checked $testApplication @(
        '--results-directory', $resultsPath,
        '--coverage',
        '--coverage-output-format', 'cobertura',
        '--coverage-output', $coverageOutput,
        '--coverage-settings', $coverageSettings,
        '--output', 'Normal',
        '--no-progress'
    )

    $coveragePath = Get-LatestCoverageFile -Root $resultsPath
    Write-Host "Cobertura=$(Format-RepoRelativePath -Path $coveragePath)"

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

        Write-Host "CoverageReport=$(Format-RepoRelativePath -Path $reportPath)"
    }

    if (-not $SkipGate) {
        & pwsh -NoProfile -File $assertScript -CoberturaPath $coveragePath
        if ($LASTEXITCODE -ne 0) {
            throw "Coverage gate failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Set-ProcessEnvironmentVariable -Name 'MSBUILDDISABLENODEREUSE' -Value $savedDisableNodeReuse
    Set-ProcessEnvironmentVariable -Name 'TESTINGPLATFORM_TELEMETRY_OPTOUT' -Value $savedTestingPlatformTelemetryOptOut
    Set-ProcessEnvironmentVariable -Name 'DOTNET_CLI_TELEMETRY_OPTOUT' -Value $savedDotnetCliTelemetryOptOut
    Invoke-BuildServerCleanup
}

Write-Host 'Lib.Db coverage run completed.'
