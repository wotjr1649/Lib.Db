param(
    [ValidateSet('Solution', 'IntegrationTests')]
    [string] $Target = 'Solution',
    [string] $Configuration = 'Debug',
    [string] $Filter,
    [string] $FilterClass,
    [string] $FilterMethod,
    [string] $FilterTrait,
    [string] $FilterQuery,
    [string] $Logger,
    [switch] $ReportTrx,
    [string] $TrxFileName,
    [string] $ResultsDirectory,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $KeepBuildServers,
    [switch] $SkipTestEnvGuard,
    [ValidateSet('quiet', 'minimal', 'normal', 'detailed', 'diagnostic')]
    [string] $Verbosity = 'minimal',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $AdditionalArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$solution = Join-Path $repoRoot 'Lib.Db.slnx'
$integrationProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
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

function Invoke-BuildServerCleanup {
    param([bool] $KeepBuildServers)

    if ($KeepBuildServers) {
        return
    }

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

function Add-MinimumExpectedTests {
    param([Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments)

    if (-not $Arguments.Contains('--minimum-expected-tests')) {
        $Arguments.Add('--minimum-expected-tests')
        $Arguments.Add('1')
    }
}

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
        throw 'Use either -Filter for simple FullyQualifiedName~ClassName compatibility or native MTP filter parameters, not both.'
    }

    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        if ($Filter -match '^FullyQualifiedName~(?<class>[A-Za-z0-9_.+]+)$') {
            $className = $Matches['class'].Split('.')[-1]
            $Arguments.Add('--filter-class')
            $Arguments.Add("*$className*")
            Add-MinimumExpectedTests -Arguments $Arguments
            return
        }

        throw 'MTP does not support arbitrary VSTest --filter syntax for xUnit v3. Use -FilterClass, -FilterMethod, -FilterTrait, or -FilterQuery.'
    }

    if (-not [string]::IsNullOrWhiteSpace($FilterClass)) {
        $Arguments.Add('--filter-class')
        $Arguments.Add($FilterClass)
        Add-MinimumExpectedTests -Arguments $Arguments
    }

    if (-not [string]::IsNullOrWhiteSpace($FilterMethod)) {
        $Arguments.Add('--filter-method')
        $Arguments.Add($FilterMethod)
        Add-MinimumExpectedTests -Arguments $Arguments
    }

    if (-not [string]::IsNullOrWhiteSpace($FilterTrait)) {
        $Arguments.Add('--filter-trait')
        $Arguments.Add($FilterTrait)
        Add-MinimumExpectedTests -Arguments $Arguments
    }

    if (-not [string]::IsNullOrWhiteSpace($FilterQuery)) {
        $Arguments.Add('--filter-query')
        $Arguments.Add($FilterQuery)
        Add-MinimumExpectedTests -Arguments $Arguments
    }
}

function Add-MtpReportArguments {
    param(
        [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments,
        [string] $Logger,
        [bool] $ReportTrx,
        [string] $TrxFileName
    )

    $shouldReportTrx = $ReportTrx
    $effectiveTrxFileName = $TrxFileName

    if (-not [string]::IsNullOrWhiteSpace($Logger)) {
        if ($Logger -notlike 'trx*') {
            throw 'MTP only supports -Logger trx compatibility in Invoke-Tests.ps1. Use native MTP report options for other formats.'
        }

        $shouldReportTrx = $true
        if ([string]::IsNullOrWhiteSpace($effectiveTrxFileName) -and $Logger -match '(?i)(^|;)LogFileName=(?<name>[^;]+)') {
            $effectiveTrxFileName = $Matches['name']
        }
    }

    if ($shouldReportTrx) {
        $Arguments.Add('--report-trx')
        if (-not [string]::IsNullOrWhiteSpace($effectiveTrxFileName)) {
            $Arguments.Add('--report-trx-filename')
            $Arguments.Add($effectiveTrxFileName)
        }
    }
}

$testTarget = if ($Target -eq 'IntegrationTests') { $integrationProject } else { $solution }
$dotnetArguments = [System.Collections.Generic.List[string]]::new()
$dotnetArguments.Add('test')
if ($Target -eq 'IntegrationTests') {
    $dotnetArguments.Add('--project')
}
$dotnetArguments.Add($testTarget)
$dotnetArguments.Add('-c')
$dotnetArguments.Add($Configuration)

if ($NoRestore) {
    $dotnetArguments.Add('--no-restore')
}

if ($NoBuild) {
    $dotnetArguments.Add('--no-build')
}

Add-MtpFilterArguments `
    -Arguments $dotnetArguments `
    -Filter $Filter `
    -FilterClass $FilterClass `
    -FilterMethod $FilterMethod `
    -FilterTrait $FilterTrait `
    -FilterQuery $FilterQuery

Add-MtpReportArguments `
    -Arguments $dotnetArguments `
    -Logger $Logger `
    -ReportTrx $ReportTrx.IsPresent `
    -TrxFileName $TrxFileName

if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $dotnetArguments.Add('--results-directory')
    $dotnetArguments.Add($ResultsDirectory)
}

$dotnetArguments.Add("-v:$Verbosity")

foreach ($argument in $AdditionalArguments) {
    $dotnetArguments.Add($argument)
}

Write-Host 'Lib.Db test run started.'
Write-Host "Target=$Target"
Write-Host "Configuration=$Configuration"
Write-Host "KeepBuildServers=$($KeepBuildServers.IsPresent)"
Write-Host "SkipTestEnvGuard=$($SkipTestEnvGuard.IsPresent)"
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    Write-Host "Filter=$Filter"
}
if (-not [string]::IsNullOrWhiteSpace($FilterClass)) {
    Write-Host "FilterClass=$FilterClass"
}
if (-not [string]::IsNullOrWhiteSpace($FilterMethod)) {
    Write-Host "FilterMethod=$FilterMethod"
}
if (-not [string]::IsNullOrWhiteSpace($FilterTrait)) {
    Write-Host "FilterTrait=$FilterTrait"
}
if (-not [string]::IsNullOrWhiteSpace($FilterQuery)) {
    Write-Host "FilterQuery=$FilterQuery"
}

$savedSkipGuard = [Environment]::GetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD')
$savedDisableNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE')
if ($SkipTestEnvGuard) {
    [Environment]::SetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD', 'true')
}
[Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', '1')

try {
    Write-SecretSafeEnvironmentSummary
    Invoke-Checked 'dotnet' $dotnetArguments.ToArray()
}
finally {
    if ($SkipTestEnvGuard) {
        [Environment]::SetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD', $savedSkipGuard)
    }

    [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', $savedDisableNodeReuse)
    Invoke-BuildServerCleanup -KeepBuildServers:$KeepBuildServers.IsPresent
}
Write-Host 'Lib.Db test run completed.'
