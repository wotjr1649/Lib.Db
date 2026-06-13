param(
    [ValidateSet('Solution', 'IntegrationTests')]
    [string] $Target = 'IntegrationTests',
    [string] $Configuration = 'Debug',
    [string] $Filter,
    [Alias('filter-namespace')]
    [string] $FilterNamespace,
    [Alias('filter-class')]
    [string] $FilterClass,
    [Alias('filter-method')]
    [string] $FilterMethod,
    [Alias('filter-trait')]
    [string] $FilterTrait,
    [Alias('filter-query')]
    [string] $FilterQuery,
    [string] $Logger,
    [switch] $ReportTrx,
    [string] $TrxFileName,
    [string] $ResultsDirectory,
    [switch] $Coverage,
    [ValidateSet('coverage', 'xml', 'cobertura')]
    [string] $CoverageOutputFormat,
    [string] $CoverageOutput,
    [string] $CoverageSettings,
    [switch] $NoRestore,
    [switch] $NoBuild,
    [switch] $KeepBuildServers,
    [switch] $UseLocalEnvironment,
    [switch] $SkipLocalEnvironment,
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

if ($UseLocalEnvironment -and $SkipLocalEnvironment) {
    throw 'UseLocalEnvironment and SkipLocalEnvironment cannot be specified together.'
}

if ($UseLocalEnvironment) {
    if (-not (Test-Path -LiteralPath $localEnvironmentScript)) {
        throw 'Local verification environment script was requested but not found.'
    }

    . $localEnvironmentScript -NoBenchmarkReset
    Write-Host "Loaded local verification environment script: $(Format-RepoRelativePath -Path $localEnvironmentScript)"
}
elseif ($SkipLocalEnvironment) {
    Write-Host 'Local verification environment script skipped.'
}
else {
    Write-Host 'Local verification environment script not loaded; pass -UseLocalEnvironment to opt in, or use existing process environment.'
}

if ($Coverage -or
    -not [string]::IsNullOrWhiteSpace($CoverageOutputFormat) -or
    -not [string]::IsNullOrWhiteSpace($CoverageOutput) -or
    -not [string]::IsNullOrWhiteSpace($CoverageSettings)) {
    throw 'Invoke-Tests.ps1 does not run coverage directly. Use Invoke-Coverage.ps1 so Windows MTP coverage runs from the dedicated apphost path instead of the test wrapper command line.'
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [AllowEmptyCollection()]
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

function Test-AllEnvironmentVariablesPresent {
    param([Parameter(Mandatory = $true)] [string[]] $Names)

    foreach ($name in $Names) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            return $false
        }
    }

    return $true
}

function Assert-VerificationEnvironmentConfigured {
    $skipGuard = [Environment]::GetEnvironmentVariable('LIBDB_SKIP_TEST_ENV_GUARD')
    if ('true'.Equals($skipGuard, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('LIBDB_TEST_SQL_PASSWORD'))) {
        return
    }

    if (Test-AllEnvironmentVariablesPresent -Names @(
            'LIBDB_TEST_CONNECTION_VERIFICATION',
            'LIBDB_TEST_CONNECTION_SORTER',
            'LIBDB_TEST_CONNECTION_STRESS',
            'LIBDB_TEST_CONNECTION_CHAOS',
            'LIBDB_TEST_CONNECTION_BENCHMARK')) {
        return
    }

    if (Test-AllEnvironmentVariablesPresent -Names @(
            'ConnectionStrings__Verification',
            'ConnectionStrings__Sorter',
            'ConnectionStrings__Stress',
            'ConnectionStrings__Chaos',
            'ConnectionStrings__Benchmark')) {
        return
    }

    throw 'Lib.Db integration tests require the verification environment before the test executable starts. Pass -UseLocalEnvironment to opt into Set-LibDbVerificationEnvironment.local.ps1, set LIBDB_TEST_SQL_PASSWORD / all LIBDB_TEST_CONNECTION_* / all ConnectionStrings__* values, or pass -SkipTestEnvGuard for non-database-only local runs.'
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

function Add-MinimumExpectedTests {
    param([AllowEmptyCollection()] [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments)

    if (-not $Arguments.Contains('--minimum-expected-tests')) {
        $Arguments.Add('--minimum-expected-tests')
        $Arguments.Add('1')
    }
}

function Remove-AutoMinimumExpectedTests {
    param([AllowEmptyCollection()] [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments)

    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        if ($Arguments[$i] -eq '--minimum-expected-tests') {
            $Arguments.RemoveAt($i)
            if ($i -lt $Arguments.Count) {
                $Arguments.RemoveAt($i)
            }

            return
        }
    }
}

function Add-MtpFilterArguments {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments,
        [string] $Filter,
        [string] $FilterNamespace,
        [string] $FilterClass,
        [string] $FilterMethod,
        [string] $FilterTrait,
        [string] $FilterQuery
    )

    $hasNativeFilter = -not [string]::IsNullOrWhiteSpace($FilterNamespace) -or
        -not [string]::IsNullOrWhiteSpace($FilterClass) -or
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

        throw 'MTP does not support arbitrary VSTest --filter syntax for xUnit v3. Use -FilterNamespace, -FilterClass, -FilterMethod, -FilterTrait, or -FilterQuery.'
    }

    if (-not [string]::IsNullOrWhiteSpace($FilterNamespace)) {
        $Arguments.Add('--filter-namespace')
        $Arguments.Add($FilterNamespace)
        Add-MinimumExpectedTests -Arguments $Arguments
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
        [AllowEmptyCollection()]
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

function Resolve-MtpPathArgumentValue {
    param([Parameter(Mandatory = $true)] [string] $PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $PathValue
    }

    if ([System.IO.Path]::IsPathFullyQualified($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

function Ensure-MtpOutputPath {
    param(
        [Parameter(Mandatory = $true)] [string] $OptionName,
        [Parameter(Mandatory = $true)] [string] $PathValue
    )

    if ($OptionName.Equals('--results-directory', [System.StringComparison]::OrdinalIgnoreCase)) {
        New-Item -ItemType Directory -Path $PathValue -Force | Out-Null
    }
}

function Add-MtpAdditionalArguments {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments,
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)] [string[]] $AdditionalArguments
    )

    $pathOptions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $pathOptions.Add('--results-directory') | Out-Null
    $coverageOptions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $coverageOptions.Add('--coverage') | Out-Null
    $coverageOptions.Add('--coverage-output') | Out-Null
    $coverageOptions.Add('--coverage-output-format') | Out-Null
    $coverageOptions.Add('--coverage-settings') | Out-Null

    for ($i = 0; $i -lt $AdditionalArguments.Count; $i++) {
        $argument = $AdditionalArguments[$i]
        if ($coverageOptions.Contains($argument)) {
            throw 'Invoke-Tests.ps1 does not accept raw MTP coverage arguments. Use Invoke-Coverage.ps1 so Windows MTP coverage runs from the dedicated apphost path instead of the test wrapper command line.'
        }

        if ($argument -eq '--minimum-expected-tests' -and $Arguments.Contains('--minimum-expected-tests')) {
            Remove-AutoMinimumExpectedTests -Arguments $Arguments
        }

        $Arguments.Add($argument)
        if ($pathOptions.Contains($argument) -and ($i + 1) -lt $AdditionalArguments.Count) {
            $i++
            $pathArgument = Resolve-MtpPathArgumentValue -PathValue $AdditionalArguments[$i]
            Ensure-MtpOutputPath -OptionName $argument -PathValue $pathArgument
            $Arguments.Add($pathArgument)
        }
    }
}

function ConvertTo-MtpStringArguments {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments
    )

    $normalized = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $Arguments) {
        $normalized.Add([string] $argument)
    }

    return ,$normalized
}

function Add-MtpVerbosityArguments {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]] $Arguments,
        [Parameter(Mandatory = $true)] [string] $Verbosity
    )

    if (-not $Arguments.Contains('--output')) {
        $outputLevel = if ($Verbosity -eq 'detailed' -or $Verbosity -eq 'diagnostic') {
            'Detailed'
        }
        else {
            'Normal'
        }

        $Arguments.Add('--output')
        $Arguments.Add($outputLevel)
    }

    if (($Verbosity -eq 'quiet' -or $Verbosity -eq 'minimal') -and -not $Arguments.Contains('--no-progress')) {
        $Arguments.Add('--no-progress')
    }
}

function Get-ProjectXml {
    param([Parameter(Mandatory = $true)] [string] $ProjectPath)

    [xml] $project = Get-Content -LiteralPath $ProjectPath
    return $project
}

function Get-ProjectPropertyValues {
    param(
        [Parameter(Mandatory = $true)] [xml] $Project,
        [Parameter(Mandatory = $true)] [string] $PropertyName
    )

    $values = [System.Collections.Generic.List[string]]::new()
    foreach ($propertyGroup in @($Project.Project.PropertyGroup)) {
        if ($null -eq $propertyGroup) {
            continue
        }

        foreach ($childNode in @($propertyGroup.ChildNodes)) {
            if ($childNode.Name -eq $PropertyName -and -not [string]::IsNullOrWhiteSpace($childNode.InnerText)) {
                $values.Add($childNode.InnerText)
            }
        }
    }

    return $values.ToArray()
}

function Get-ProjectTargetFramework {
    param([Parameter(Mandatory = $true)] [string] $ProjectPath)

    $project = Get-ProjectXml -ProjectPath $ProjectPath
    $targetFramework = Get-ProjectPropertyValues -Project $project -PropertyName 'TargetFramework' |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($targetFramework)) {
        $targetFramework = Get-ProjectPropertyValues -Project $project -PropertyName 'TargetFrameworks' |
            ForEach-Object { $_.Split(';')[0] } |
            Select-Object -First 1
    }

    if ([string]::IsNullOrWhiteSpace($targetFramework)) {
        throw "Unable to determine TargetFramework from $(Format-RepoRelativePath -Path $ProjectPath)."
    }

    return $targetFramework
}

function Get-ProjectAssemblyName {
    param([Parameter(Mandatory = $true)] [string] $ProjectPath)

    $project = Get-ProjectXml -ProjectPath $ProjectPath
    $assemblyName = Get-ProjectPropertyValues -Project $project -PropertyName 'AssemblyName' |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    }

    return $assemblyName
}

function Test-IsMtpTestProject {
    param([Parameter(Mandatory = $true)] [string] $ProjectPath)

    $project = Get-ProjectXml -ProjectPath $ProjectPath
    $isTestingPlatformApplication = Get-ProjectPropertyValues -Project $project -PropertyName 'IsTestingPlatformApplication' |
        Select-Object -First 1

    if ('true'.Equals($isTestingPlatformApplication, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    foreach ($itemGroup in @($project.Project.ChildNodes | Where-Object { $_.Name -eq 'ItemGroup' })) {
        foreach ($childNode in @($itemGroup.ChildNodes)) {
            if ($childNode.Name -eq 'PackageReference') {
                $include = [string] $childNode.Include
                if ($include -eq 'Microsoft.Testing.Platform' -or $include.StartsWith('xunit.v3.', [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $true
                }
            }
        }
    }

    return $false
}

function Resolve-SolutionProjectPath {
    param([Parameter(Mandatory = $true)] [string] $ProjectPath)

    $normalizedProjectPath = $ProjectPath -replace '/', [System.IO.Path]::DirectorySeparatorChar
    if ([System.IO.Path]::IsPathFullyQualified($normalizedProjectPath)) {
        return $normalizedProjectPath
    }

    return Join-Path $repoRoot $normalizedProjectPath
}

function Get-SolutionProjectPaths {
    [xml] $solutionXml = Get-Content -LiteralPath $solution
    $projectPaths = [System.Collections.Generic.List[string]]::new()

    foreach ($projectNode in @($solutionXml.Solution.Project)) {
        if ($null -ne $projectNode -and -not [string]::IsNullOrWhiteSpace($projectNode.Path)) {
            $projectPaths.Add((Resolve-SolutionProjectPath -ProjectPath $projectNode.Path))
        }
    }

    foreach ($folderNode in @($solutionXml.Solution.Folder)) {
        foreach ($projectNode in @($folderNode.Project)) {
            if ($null -ne $projectNode -and -not [string]::IsNullOrWhiteSpace($projectNode.Path)) {
                $projectPaths.Add((Resolve-SolutionProjectPath -ProjectPath $projectNode.Path))
            }
        }
    }

    return $projectPaths.ToArray()
}

function Get-SolutionMtpTestProjects {
    $testProjects = [System.Collections.Generic.List[string]]::new()

    foreach ($projectPath in Get-SolutionProjectPaths) {
        if ((Test-Path -LiteralPath $projectPath) -and (Test-IsMtpTestProject -ProjectPath $projectPath)) {
            $testProjects.Add($projectPath)
        }
    }

    if ($testProjects.Count -eq 0) {
        throw "No Microsoft Testing Platform test projects were found in $(Format-RepoRelativePath -Path $solution)."
    }

    return $testProjects.ToArray()
}

function Get-TestAssemblyPath {
    param(
        [Parameter(Mandatory = $true)] [string] $ProjectPath,
        [Parameter(Mandatory = $true)] [string] $Configuration
    )

    $targetFramework = Get-ProjectTargetFramework -ProjectPath $ProjectPath
    $assemblyName = Get-ProjectAssemblyName -ProjectPath $ProjectPath
    $projectDirectory = Split-Path -Parent $ProjectPath

    return Join-Path $projectDirectory "bin\$Configuration\$targetFramework\$assemblyName.dll"
}

function Get-TestApplicationPath {
    param(
        [Parameter(Mandatory = $true)] [string] $ProjectPath,
        [Parameter(Mandatory = $true)] [string] $Configuration
    )

    $assemblyPath = Get-TestAssemblyPath -ProjectPath $ProjectPath -Configuration $Configuration
    $assemblyDirectory = Split-Path -Parent $assemblyPath
    $applicationName = [System.IO.Path]::GetFileNameWithoutExtension($assemblyPath)
    $appHostName = if ($IsWindows) { "$applicationName.exe" } else { $applicationName }
    $appHostPath = Join-Path $assemblyDirectory $appHostName

    if (Test-Path -LiteralPath $appHostPath) {
        return $appHostPath
    }

    return $assemblyPath
}

function Invoke-DirectMtpTestRun {
    param(
        [Parameter(Mandatory = $true)] [string] $Configuration,
        [Parameter(Mandatory = $true)] [bool] $NoRestore,
        [Parameter(Mandatory = $true)] [bool] $NoBuild,
        [Parameter(Mandatory = $true)] [string] $Verbosity,
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)] [string[]] $TestArguments
    )

    if (-not $NoBuild) {
        $buildTarget = if ($Target -eq 'Solution') { $solution } else { $integrationProject }
        $buildArguments = [System.Collections.Generic.List[string]]::new()
        $buildArguments.Add('build')
        $buildArguments.Add($buildTarget)
        $buildArguments.Add('-c')
        $buildArguments.Add($Configuration)
        $buildArguments.Add(('-v:' + $Verbosity))
        if ($NoRestore) {
            $buildArguments.Add('--no-restore')
        }

        $buildArgumentArray = $buildArguments.ToArray()
        Invoke-Checked 'dotnet' $buildArgumentArray
    }

    $testProjects = if ($Target -eq 'Solution') { Get-SolutionMtpTestProjects } else { @($integrationProject) }

    foreach ($testProject in $testProjects) {
        $testAssembly = Get-TestAssemblyPath -ProjectPath $testProject -Configuration $Configuration
        $testApplication = Get-TestApplicationPath -ProjectPath $testProject -Configuration $Configuration
        $displayAssembly = Format-RepoRelativePath -Path $testAssembly
        if (-not (Test-Path -LiteralPath $testAssembly)) {
            throw "Test assembly not found: $displayAssembly. Build once or omit -NoBuild."
        }

        if (-not (Test-Path -LiteralPath $testApplication)) {
            throw "Test application not found: $(Format-RepoRelativePath -Path $testApplication). Build once or omit -NoBuild."
        }

        $testArgumentList = [System.Collections.Generic.List[string]]::new()
        foreach ($argument in $TestArguments) {
            $testArgumentList.Add($argument)
        }

        Write-Host "Executing MTP test application: $displayAssembly"
        if ([string]::Equals($testApplication, $testAssembly, [System.StringComparison]::OrdinalIgnoreCase)) {
            $execArguments = [System.Collections.Generic.List[string]]::new()
            $execArguments.Add('exec')
            $execArguments.Add($testAssembly)
            foreach ($argument in $TestArguments) {
                $execArguments.Add($argument)
            }

            $execArgumentArray = $execArguments.ToArray()
            Invoke-Checked 'dotnet' $execArgumentArray
            continue
        }

        $testArgumentArray = $testArgumentList.ToArray()
        Invoke-Checked $testApplication $testArgumentArray
    }
}

$mtpArguments = [System.Collections.Generic.List[string]]::new()

Add-MtpFilterArguments `
    -Arguments $mtpArguments `
    -Filter $Filter `
    -FilterNamespace $FilterNamespace `
    -FilterClass $FilterClass `
    -FilterMethod $FilterMethod `
    -FilterTrait $FilterTrait `
    -FilterQuery $FilterQuery

Add-MtpReportArguments `
    -Arguments $mtpArguments `
    -Logger $Logger `
    -ReportTrx $ReportTrx.IsPresent `
    -TrxFileName $TrxFileName

if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $resolvedResultsDirectory = Resolve-MtpPathArgumentValue -PathValue $ResultsDirectory
    Ensure-MtpOutputPath -OptionName '--results-directory' -PathValue $resolvedResultsDirectory
    $mtpArguments.Add('--results-directory')
    $mtpArguments.Add($resolvedResultsDirectory)
}

Add-MtpAdditionalArguments -Arguments $mtpArguments -AdditionalArguments $AdditionalArguments

Add-MtpVerbosityArguments -Arguments $mtpArguments -Verbosity $Verbosity
$mtpArguments = ConvertTo-MtpStringArguments -Arguments $mtpArguments

Write-Host 'Lib.Db test run started.'
Write-Host "Target=$Target"
Write-Host "Configuration=$Configuration"
Write-Host "Verbosity=$Verbosity"
Write-Host "KeepBuildServers=$($KeepBuildServers.IsPresent)"
Write-Host "UseLocalEnvironment=$($UseLocalEnvironment.IsPresent)"
Write-Host "SkipLocalEnvironment=$($SkipLocalEnvironment.IsPresent)"
Write-Host "SkipTestEnvGuard=$($SkipTestEnvGuard.IsPresent)"
Write-Host 'MtpExecution=DirectAppHostPreferred'
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    Write-Host "Filter=$Filter"
}
if (-not [string]::IsNullOrWhiteSpace($FilterNamespace)) {
    Write-Host "FilterNamespace=$FilterNamespace"
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
$savedTestingPlatformTelemetryOptOut = [Environment]::GetEnvironmentVariable('TESTINGPLATFORM_TELEMETRY_OPTOUT')
$savedDotnetCliTelemetryOptOut = [Environment]::GetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT')
if ($SkipTestEnvGuard) {
    Set-ProcessEnvironmentVariable -Name 'LIBDB_SKIP_TEST_ENV_GUARD' -Value 'true'
}
Set-ProcessEnvironmentVariable -Name 'MSBUILDDISABLENODEREUSE' -Value '1'
Set-ProcessEnvironmentVariable -Name 'TESTINGPLATFORM_TELEMETRY_OPTOUT' -Value '1'
Set-ProcessEnvironmentVariable -Name 'DOTNET_CLI_TELEMETRY_OPTOUT' -Value '1'

try {
    Write-SecretSafeEnvironmentSummary
    Assert-VerificationEnvironmentConfigured
    $testArgumentArray = $mtpArguments.ToArray()
    Invoke-DirectMtpTestRun `
        -Configuration $Configuration `
        -NoRestore:$NoRestore.IsPresent `
        -NoBuild:$NoBuild.IsPresent `
        -Verbosity $Verbosity `
        -TestArguments $testArgumentArray
}
finally {
    if ($SkipTestEnvGuard) {
        Set-ProcessEnvironmentVariable -Name 'LIBDB_SKIP_TEST_ENV_GUARD' -Value $savedSkipGuard
    }

    Set-ProcessEnvironmentVariable -Name 'MSBUILDDISABLENODEREUSE' -Value $savedDisableNodeReuse
    Set-ProcessEnvironmentVariable -Name 'TESTINGPLATFORM_TELEMETRY_OPTOUT' -Value $savedTestingPlatformTelemetryOptOut
    Set-ProcessEnvironmentVariable -Name 'DOTNET_CLI_TELEMETRY_OPTOUT' -Value $savedDotnetCliTelemetryOptOut
    Invoke-BuildServerCleanup -KeepBuildServers:$KeepBuildServers.IsPresent
}
Write-Host 'Lib.Db test run completed.'
