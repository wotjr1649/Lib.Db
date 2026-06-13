param(
    [ValidateSet('All', 'Guard', 'Runner', 'Filter', 'Matrix', 'Trx', 'Coverage', 'ArtifactScan')]
    [string[]] $Scenario = @('All'),
    [string] $Configuration = 'Debug',
    [switch] $NoRestore,
    [switch] $UseLocalEnvironment,
    [switch] $KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$testProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
$testScript = Join-Path $repoRoot 'Verification\scripts\Invoke-Tests.ps1'
$coverageSettings = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\mtp-codecoverage.config.xml'
$artifactRoot = Join-Path $repoRoot 'Verification\artifacts\mtp-spike'
$trxDirectory = Join-Path $artifactRoot 'trx'
$coverageDirectory = Join-Path $artifactRoot 'coverage'
$localEnvironmentScript = Join-Path $PSScriptRoot 'Set-LibDbVerificationEnvironment.local.ps1'
$artifactScanner = Join-Path $PSScriptRoot 'Scan-VerificationArtifacts.ps1'
$artifactTrackingGate = Join-Path $PSScriptRoot 'Assert-GeneratedArtifactsUntracked.ps1'

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

$verificationEnvironmentNames = @(
    'LIBDB_TEST_CONNECTION_VERIFICATION',
    'LIBDB_TEST_CONNECTION_SORTER',
    'LIBDB_TEST_CONNECTION_STRESS',
    'LIBDB_TEST_CONNECTION_CHAOS',
    'LIBDB_TEST_CONNECTION_BENCHMARK',
    'LIBDB_TEST_SQL_PASSWORD',
    'LIBDB_BENCHMARK_CONNECTION',
    'ConnectionStrings__Verification',
    'ConnectionStrings__Sorter',
    'ConnectionStrings__Stress',
    'ConnectionStrings__Chaos',
    'ConnectionStrings__Benchmark',
    'LIBDB_SKIP_TEST_ENV_GUARD'
)

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

function Invoke-Captured {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [Parameter(Mandatory = $true)] [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit(300000)) {
        try {
            $process.Kill($true)
        }
        catch {
            $process.Kill()
        }

        throw "$FilePath timed out after 300 seconds."
    }

    [System.Threading.Tasks.Task]::WaitAll($stdoutTask, $stderrTask)
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()

    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = "$stdout`n$stderr"
    }
}

function Write-SecretSafeEnvironmentSummary {
    foreach ($name in $verificationEnvironmentNames) {
        if ($name.StartsWith('ConnectionStrings__')) {
            continue
        }

        $present = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))
        Write-Host "$name present: $present"
    }
}

function Invoke-WithClearedVerificationEnvironment {
    param([Parameter(Mandatory = $true)] [scriptblock] $Script)

    $saved = @{}
    foreach ($name in $verificationEnvironmentNames) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }

    try {
        & $Script
    }
    finally {
        foreach ($name in $verificationEnvironmentNames) {
            if ($null -eq $saved[$name]) {
                [Environment]::SetEnvironmentVariable($name, $null)
            }
            else {
                [Environment]::SetEnvironmentVariable($name, [string] $saved[$name])
            }
        }
    }
}

function Invoke-WithSkippedTestEnvironmentGuard {
    param([Parameter(Mandatory = $true)] [scriptblock] $Script)

    $saved = [Environment]::GetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD')
    [Environment]::SetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD', 'true')

    try {
        & $Script
    }
    finally {
        [Environment]::SetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD', $saved)
    }
}

function New-TestWrapperArguments {
    param(
        [Parameter(Mandatory = $true)] [string] $FilterClass,
        [switch] $SkipLocalEnvironment,
        [switch] $SkipTestEnvGuard,
        [string[]] $AdditionalArguments = @()
    )

    $arguments = @(
        '-NoProfile',
        '-File', $testScript,
        '-Target', 'IntegrationTests',
        '-Configuration', $Configuration,
        '-FilterClass', $FilterClass
    )

    if ($NoRestore) {
        $arguments += '-NoRestore'
    }

    if ($UseLocalEnvironment -and -not $SkipLocalEnvironment) {
        $arguments += '-UseLocalEnvironment'
    }

    if ($SkipLocalEnvironment) {
        $arguments += '-SkipLocalEnvironment'
    }

    if ($SkipTestEnvGuard) {
        $arguments += '-SkipTestEnvGuard'
    }

    return $arguments + $AdditionalArguments
}

function Get-Scenarios {
    if ($Scenario -contains 'All') {
        return @('Guard', 'Runner', 'Filter', 'Matrix', 'Trx', 'Coverage', 'ArtifactScan')
    }

    return $Scenario
}

function New-ArtifactDirectory {
    param([Parameter(Mandatory = $true)] [string] $Path)

    if ((Test-Path -LiteralPath $Path) -and -not $KeepArtifacts) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Invoke-GuardScenario {
    Write-Host 'MTP spike guard scenario started.'
    Invoke-WithClearedVerificationEnvironment {
        $result = Invoke-Captured 'pwsh' (New-TestWrapperArguments -FilterClass '*CacheHostingCoverageTests*' -SkipLocalEnvironment)

        if ($result.ExitCode -eq 0) {
            throw 'MTP guard scenario unexpectedly succeeded without verification environment variables.'
        }

        if (-not $result.Output.Contains('Lib.Db integration tests require the verification environment')) {
            Write-Host $result.Output
            throw 'MTP guard scenario failed, but not with the expected Lib.Db verification guard message.'
        }
    }

    Write-Host 'MTP spike guard scenario passed.'
}

function Invoke-RunnerScenario {
    Write-Host 'MTP spike runner scenario started.'
    $result = Invoke-Captured 'dotnet' @(
        'run',
        '--project', $testProject,
        '-c', $Configuration,
        '--no-restore',
        '--',
        '-?'
    )

    if ($result.ExitCode -ne 0) {
        throw "MTP runner help failed with exit code $($result.ExitCode)."
    }

    foreach ($expected in @('Microsoft.Testing.Platform', '--filter-class', '--report-trx', '--coverage')) {
        if (-not $result.Output.Contains($expected)) {
            throw "MTP runner help output did not include expected option '$expected'."
        }
    }

    Write-Host 'MtpRunnerOptionsPresent=Microsoft.Testing.Platform,--filter-class,--report-trx,--coverage'
    Write-Host 'MTP spike runner scenario passed.'
}

function Invoke-FilterScenario {
    Write-Host 'MTP spike filter scenario started.'

    Invoke-Checked 'pwsh' (New-TestWrapperArguments -FilterClass '*CacheHostingCoverageTests*' -SkipTestEnvGuard)
    Write-Host 'MTP spike filter scenario passed.'
}

function Invoke-MatrixScenario {
    Write-Host 'MTP spike matrix scenario started.'

    Invoke-Checked 'pwsh' (New-TestWrapperArguments -FilterClass '*V230TvpMatrixTests*')
    Write-Host 'MTP spike matrix scenario passed.'
}

function Invoke-TrxScenario {
    Write-Host 'MTP spike TRX scenario started.'
    New-ArtifactDirectory -Path $trxDirectory
    $arguments = New-TestWrapperArguments -FilterClass '*V230TvpMatrixTests*' -AdditionalArguments @(
        '--report-trx',
        '--report-trx-filename', 'mtp-matrix.trx',
        '--results-directory', $trxDirectory
    )

    Invoke-Checked 'pwsh' $arguments

    $trx = Get-ChildItem -LiteralPath $trxDirectory -Recurse -Filter 'mtp-matrix.trx' -File |
        Select-Object -First 1
    if ($null -eq $trx) {
        throw 'MTP TRX scenario did not produce mtp-matrix.trx.'
    }

    Write-Host "MtpTrx=$(Format-RepoRelativePath -Path $trx.FullName)"
    Write-Host 'MTP spike TRX scenario passed.'
}

function Invoke-CoverageScenario {
    Write-Host 'MTP spike coverage guard scenario started.'
    New-ArtifactDirectory -Path $coverageDirectory
    $coverageOutput = Join-Path $coverageDirectory 'coverage.cobertura.xml'
    $arguments = New-TestWrapperArguments -FilterClass '*CacheHostingCoverageTests*' -SkipTestEnvGuard -AdditionalArguments @(
        '-Coverage',
        '-CoverageOutputFormat', 'cobertura',
        '-CoverageOutput', $coverageOutput,
        '-CoverageSettings', $coverageSettings,
        '-ResultsDirectory', $coverageDirectory
    )

    $result = Invoke-Captured 'pwsh' $arguments

    if ($result.ExitCode -eq 0) {
        throw 'MTP coverage guard scenario unexpectedly allowed Invoke-Tests.ps1 to run coverage directly.'
    }

    if (-not $result.Output.Contains('Invoke-Tests.ps1 does not run coverage directly')) {
        Write-Host $result.Output
        throw 'MTP coverage guard scenario failed, but not with the expected coverage routing message.'
    }

    if (Test-Path -LiteralPath $coverageOutput) {
        throw 'MTP coverage guard scenario unexpectedly produced coverage.cobertura.xml.'
    }

    Write-Host 'MTP spike coverage guard scenario passed.'
}

function Invoke-ArtifactScanScenario {
    Write-Host 'MTP spike artifact scan scenario started.'
    Invoke-Checked 'pwsh' @('-NoProfile', '-File', $artifactScanner, '-Paths', $artifactRoot)
    Invoke-Checked 'pwsh' @('-NoProfile', '-File', $artifactTrackingGate)
    Write-Host 'MTP spike artifact scan scenario passed.'
}

Write-Host 'Lib.Db MTP migration spike started.'
Write-Host "Configuration=$Configuration"

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

Write-SecretSafeEnvironmentSummary

if (-not $NoRestore) {
    Invoke-Checked 'dotnet' @('restore', $testProject)
}

New-ArtifactDirectory -Path $artifactRoot

foreach ($item in Get-Scenarios) {
    switch ($item) {
        'Guard' { Invoke-GuardScenario }
        'Runner' { Invoke-RunnerScenario }
        'Filter' { Invoke-FilterScenario }
        'Matrix' { Invoke-MatrixScenario }
        'Trx' { Invoke-TrxScenario }
        'Coverage' { Invoke-CoverageScenario }
        'ArtifactScan' { Invoke-ArtifactScanScenario }
    }
}

Write-Host 'Lib.Db MTP migration spike completed.'
