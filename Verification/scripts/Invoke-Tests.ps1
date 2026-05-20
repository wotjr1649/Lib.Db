param(
    [ValidateSet('Solution', 'IntegrationTests')]
    [string] $Target = 'Solution',
    [string] $Configuration = 'Debug',
    [string] $Filter,
    [string] $Logger,
    [string] $ResultsDirectory,
    [switch] $NoRestore,
    [switch] $NoBuild,
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

$testTarget = if ($Target -eq 'IntegrationTests') { $integrationProject } else { $solution }
$dotnetArguments = [System.Collections.Generic.List[string]]::new()
$dotnetArguments.Add('test')
$dotnetArguments.Add($testTarget)
$dotnetArguments.Add('-c')
$dotnetArguments.Add($Configuration)

if ($NoRestore) {
    $dotnetArguments.Add('--no-restore')
}

if ($NoBuild) {
    $dotnetArguments.Add('--no-build')
}

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $dotnetArguments.Add('--filter')
    $dotnetArguments.Add($Filter)
}

if (-not [string]::IsNullOrWhiteSpace($Logger)) {
    $dotnetArguments.Add('--logger')
    $dotnetArguments.Add($Logger)
}

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
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    Write-Host "Filter=$Filter"
}

Write-SecretSafeEnvironmentSummary
Invoke-Checked 'dotnet' $dotnetArguments.ToArray()
Write-Host 'Lib.Db test run completed.'
