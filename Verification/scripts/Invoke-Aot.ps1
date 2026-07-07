param(
    [string] $ArtifactsDirectory = 'Verification\artifacts\aot',
    [switch] $ParserSelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$aotProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj'
$warningBaselinePath = Join-Path $repoRoot 'Verification\baselines\aot-warnings.json'

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

function Get-AotRuntimeIdentifier {
    if ($IsWindows) {
        return 'win-x64'
    }

    if ($IsLinux) {
        return 'linux-x64'
    }

    if ($IsMacOS) {
        return 'osx-x64'
    }

    throw 'Unsupported operating system for AOT verification.'
}

function Get-AotExecutableName {
    if ($IsWindows) {
        return 'Lib.Db.AotVerification.exe'
    }

    return 'Lib.Db.AotVerification'
}

function Get-AotWarnings {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Lines
    )

    foreach ($line in $Lines) {
        if ($null -eq $line) {
            continue
        }

        $text = $line.ToString()
        if ($text -notmatch '(?i)\bwarning\b') {
            continue
        }

        $idMatch = [regex]::Match($text, '\bIL\d{4}\b')
        if (-not $idMatch.Success) {
            continue
        }

        $assembly = 'unknown'
        $assemblyMatch = [regex]::Match($text, "Assembly\s+'([^']+)'")
        if ($assemblyMatch.Success) {
            $assembly = $assemblyMatch.Groups[1].Value
        }
        else {
            $pathAssemblyMatch = [regex]::Match($text, '(?i)(?:^|\s|[\\/])([^\\/:\s]+)\.dll\s*:\s*(?:warning|error)\s+IL\d{4}')
            if ($pathAssemblyMatch.Success) {
                $assembly = $pathAssemblyMatch.Groups[1].Value
            }
            else {
                $memberMatch = [regex]::Match($text, '(?i)\bIL\d{4}:\s*([A-Za-z_][A-Za-z0-9_.`]+)')
                if ($memberMatch.Success) {
                    $memberName = $memberMatch.Groups[1].Value
                    if ($memberName.StartsWith('Microsoft.Data.SqlClient.', [System.StringComparison]::Ordinal) -or
                        $memberName.StartsWith('Microsoft.Data.Sql.', [System.StringComparison]::Ordinal) -or
                        $memberName.StartsWith('Microsoft.Data.SqlTypes.', [System.StringComparison]::Ordinal)) {
                        $assembly = 'Microsoft.Data.SqlClient'
                    }
                    elseif ($memberName.StartsWith('System.Configuration.', [System.StringComparison]::Ordinal)) {
                        $assembly = 'System.Configuration.ConfigurationManager'
                    }
                    elseif ($memberName.StartsWith('Microsoft.Extensions.Caching.Hybrid.', [System.StringComparison]::Ordinal)) {
                        $assembly = 'Microsoft.Extensions.Caching.Hybrid'
                    }
                    elseif ($memberName.StartsWith('Lib.Db.', [System.StringComparison]::Ordinal)) {
                        $assembly = 'Lib.Db'
                    }
                }
            }
        }

        [pscustomobject]@{
            Id = $idMatch.Value
            Assembly = $assembly
            Text = $text
        }
    }
}

function Get-AotPackageVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string] $AssetsPath
    )

    if (-not (Test-Path -LiteralPath $AssetsPath)) {
        throw "AOT restore assets file was not found: $AssetsPath"
    }

    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $versions = @{}
    foreach ($library in $assets.libraries.PSObject.Properties.Name) {
        $separatorIndex = $library.LastIndexOf('/')
        if ($separatorIndex -le 0 -or $separatorIndex -ge ($library.Length - 1)) {
            continue
        }

        $packageId = $library.Substring(0, $separatorIndex)
        $packageVersion = $library.Substring($separatorIndex + 1)
        $versions[$packageId] = $packageVersion
    }

    return $versions
}

function Get-JsonStringProperty {
    param(
        [Parameter(Mandatory = $true)] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return ''
    }

    return [string] $property.Value
}

function Assert-AotWarningsMatchBaseline {
    param(
        [Parameter(Mandatory = $true)] [object[]] $Warnings,
        [Parameter(Mandatory = $true)] [string] $BaselinePath,
        [hashtable] $PackageVersions = @{},
        [switch] $RequirePackageVersions
    )

    if (-not (Test-Path -LiteralPath $BaselinePath)) {
        throw "AOT warning baseline was not found: $BaselinePath"
    }

    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
    $allowedProperty = $baseline.PSObject.Properties['allowedWarnings']
    $allowedWarnings = @()
    if ($null -ne $allowedProperty -and $null -ne $allowedProperty.Value) {
        $allowedWarnings = @($allowedProperty.Value)
    }

    $allowed = @{}
    $blockers = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $allowedWarnings) {
        $owner = Get-JsonStringProperty -InputObject $entry -Name 'owner'
        if ($owner -cne 'provider') {
            continue
        }

        $id = Get-JsonStringProperty -InputObject $entry -Name 'id'
        $assembly = Get-JsonStringProperty -InputObject $entry -Name 'assembly'
        if ($id.Length -eq 0 -or $assembly.Length -eq 0) {
            continue
        }

        $sourcePackage = Get-JsonStringProperty -InputObject $entry -Name 'sourcePackage'
        $packageVersion = Get-JsonStringProperty -InputObject $entry -Name 'packageVersion'
        if ($RequirePackageVersions) {
            if ($sourcePackage.Length -eq 0 -or $packageVersion.Length -eq 0) {
                $blockers.Add("AOT warning baseline entry $id in ${assembly} must include sourcePackage and packageVersion.")
                continue
            }

            if (-not $PackageVersions.ContainsKey($sourcePackage)) {
                $blockers.Add("AOT warning baseline package was not restored: $sourcePackage for $id in ${assembly}.")
                continue
            }

            $actualPackageVersion = [string] $PackageVersions[$sourcePackage]
            if ($actualPackageVersion -cne $packageVersion) {
                $blockers.Add("AOT warning baseline package version drift for ${sourcePackage}: expected $packageVersion but restored $actualPackageVersion.")
                continue
            }
        }

        $allowed["$id|$assembly"] = $true
    }

    $observed = @{}
    foreach ($warning in $Warnings) {
        $id = Get-JsonStringProperty -InputObject $warning -Name 'Id'
        $assembly = Get-JsonStringProperty -InputObject $warning -Name 'Assembly'
        if ($assembly.Length -eq 0) {
            $assembly = 'unknown'
        }

        if ($assembly.StartsWith('Lib.Db', [System.StringComparison]::Ordinal)) {
            $blockers.Add("Lib.Db-owned warning $id in ${assembly}: $($warning.Text)")
            continue
        }

        if (-not $allowed.ContainsKey("$id|$assembly")) {
            $blockers.Add("Unbaselined provider warning $id in ${assembly}: $($warning.Text)")
            continue
        }

        $observed["$id|$assembly"] = $true
    }

    foreach ($entry in $allowedWarnings) {
        $owner = Get-JsonStringProperty -InputObject $entry -Name 'owner'
        if ($owner -cne 'provider') {
            continue
        }

        $id = Get-JsonStringProperty -InputObject $entry -Name 'id'
        $assembly = Get-JsonStringProperty -InputObject $entry -Name 'assembly'
        if ($id.Length -eq 0 -or $assembly.Length -eq 0) {
            continue
        }

        if (-not $observed.ContainsKey("$id|$assembly")) {
            $blockers.Add("AOT warning baseline entry was not observed. Update baseline intentionally if removed: $id in ${assembly}")
        }
    }

    if ($blockers.Count -gt 0) {
        throw "AOT warning baseline failed:`n - $($blockers -join "`n - ")"
    }
}

function Invoke-AotParserSelfTest {
    $samples = @(
        "C:\packages\Provider.One.dll : warning IL2104: Assembly 'Provider.One' produced trim warnings.",
        "warning IL3053: Assembly 'Provider.Two' produced AOT analysis warnings.",
        "Trim analysis warning IL2026: Assembly 'Provider.Three' uses reflection.",
        "AOT analysis warning IL3050: Assembly 'Provider.Four' requires dynamic code.",
        "ILC : Trim analysis warning IL2113: Microsoft.Data.SqlClient.SqlConnectionStringBuilder: provider warning.",
        "ILC : Trim analysis warning IL2026: Microsoft.Data.Sql.SqlDataSourceEnumeratorNativeHelper.ParseServerEnumString(String): provider warning.",
        "ILC : AOT analysis warning IL3050: Microsoft.Data.SqlTypes.SqlVector`1.GetString(): provider warning.",
        "ILC : Trim analysis warning IL2070: System.Configuration.TypeUtil.GetConstructor(Type): provider warning.",
        "ILC : AOT analysis warning IL3050: Microsoft.Data.SqlClient.HostGuardianServiceEnclaveProvider.MakeRequest(String): provider warning.",
        "ILC : Trim analysis warning IL2026: Lib.Db.Execution.SomePath.Execute(): Lib.Db-owned warning."
    )
    $expectedWarnings = @(
        [pscustomobject]@{ Id = 'IL2104'; Assembly = 'Provider.One' },
        [pscustomobject]@{ Id = 'IL3053'; Assembly = 'Provider.Two' },
        [pscustomobject]@{ Id = 'IL2026'; Assembly = 'Provider.Three' },
        [pscustomobject]@{ Id = 'IL3050'; Assembly = 'Provider.Four' },
        [pscustomobject]@{ Id = 'IL2113'; Assembly = 'Microsoft.Data.SqlClient' },
        [pscustomobject]@{ Id = 'IL2026'; Assembly = 'Microsoft.Data.SqlClient' },
        [pscustomobject]@{ Id = 'IL3050'; Assembly = 'Microsoft.Data.SqlClient' },
        [pscustomobject]@{ Id = 'IL2070'; Assembly = 'System.Configuration.ConfigurationManager' },
        [pscustomobject]@{ Id = 'IL3050'; Assembly = 'Microsoft.Data.SqlClient' },
        [pscustomobject]@{ Id = 'IL2026'; Assembly = 'Lib.Db' }
    )

    $warnings = @(Get-AotWarnings -Lines $samples)
    if ($warnings.Count -ne $expectedWarnings.Count) {
        throw "Expected $($expectedWarnings.Count) parser self-test warnings but found $($warnings.Count)."
    }

    for ($i = 0; $i -lt $expectedWarnings.Count; $i++) {
        if ($warnings[$i].Id -cne $expectedWarnings[$i].Id -or
            $warnings[$i].Assembly -cne $expectedWarnings[$i].Assembly) {
            throw "Parser self-test mismatch at index $i."
        }

        Write-Host "ParsedWarning=$($warnings[$i].Id)|$($warnings[$i].Assembly)"
    }

    $baselineFile = New-TemporaryFile
    try {
        $baseline = [ordered]@{
            version = 1
            policy = 'AOT parser self-test baseline.'
            allowedWarnings = @($expectedWarnings | ForEach-Object {
                $sourcePackage = $_.Assembly
                $packageVersion = '1.0.0'
                [ordered]@{
                    id = $_.Id
                    assembly = $_.Assembly
                    sourcePackage = $sourcePackage
                    packageVersion = $packageVersion
                    owner = if ($_.Assembly.StartsWith('Lib.Db', [System.StringComparison]::Ordinal)) { 'lib-db' } else { 'provider' }
                    rationale = 'Parser self-test sample.'
                }
            })
        }
        $baseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $baselineFile.FullName -Encoding utf8NoBOM

        $providerWarnings = @($warnings | Where-Object { -not $_.Assembly.StartsWith('Lib.Db', [System.StringComparison]::Ordinal) })
        $libDbWarnings = @($warnings | Where-Object { $_.Assembly.StartsWith('Lib.Db', [System.StringComparison]::Ordinal) })
        $testPackageVersions = @{}
        foreach ($providerWarning in $providerWarnings) {
            $testPackageVersions[$providerWarning.Assembly] = '1.0.0'
        }

        Assert-AotWarningsMatchBaseline -Warnings $providerWarnings -BaselinePath $baselineFile.FullName -PackageVersions $testPackageVersions -RequirePackageVersions

        $libDbRejected = $false
        try {
            Assert-AotWarningsMatchBaseline -Warnings $libDbWarnings -BaselinePath $baselineFile.FullName -PackageVersions $testPackageVersions -RequirePackageVersions
        }
        catch {
            if ($_.Exception.Message.Contains('Lib.Db-owned warning', [System.StringComparison]::Ordinal)) {
                $libDbRejected = $true
            }
            else {
                throw
            }
        }

        if (-not $libDbRejected) {
            throw 'Parser self-test expected a Lib.Db-owned warning to be rejected.'
        }

        Write-Host 'RejectedLibDbOwnedWarning=True'

        $negativeWarnings = @(Get-AotWarnings -Lines @(
            "warning IL3050: Assembly 'Provider.Five' requires dynamic code."
        ))
        $rejected = $false
        try {
            Assert-AotWarningsMatchBaseline -Warnings $negativeWarnings -BaselinePath $baselineFile.FullName
        }
        catch {
            if ($_.Exception.Message.Contains('Unbaselined provider warning', [System.StringComparison]::Ordinal)) {
                $rejected = $true
            }
            else {
                throw
            }
        }

        if (-not $rejected) {
            throw 'Parser self-test expected an unbaselined provider warning to be rejected.'
        }

        Write-Host 'RejectedUnbaselinedWarning=True'

        $driftBaseline = [ordered]@{
            version = 1
            policy = 'AOT parser self-test drift baseline.'
            allowedWarnings = @(
                @($expectedWarnings | Where-Object { -not $_.Assembly.StartsWith('Lib.Db', [System.StringComparison]::Ordinal) } | ForEach-Object {
                    [ordered]@{
                        id = $_.Id
                        assembly = $_.Assembly
                        sourcePackage = $_.Assembly
                        packageVersion = '1.0.0'
                        owner = 'provider'
                        rationale = 'Parser self-test sample.'
                    }
                }) +
                [ordered]@{
                    id = 'IL3050'
                    assembly = 'Provider.Six'
                    sourcePackage = 'Provider.Six'
                    packageVersion = '1.0.0'
                    owner = 'provider'
                    rationale = 'Parser self-test stale baseline sample.'
                }
            )
        }
        $driftBaseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $baselineFile.FullName -Encoding utf8NoBOM

        $driftRejected = $false
        try {
            $driftPackageVersions = $testPackageVersions.Clone()
            $driftPackageVersions['Provider.Six'] = '1.0.0'
            Assert-AotWarningsMatchBaseline -Warnings $providerWarnings -BaselinePath $baselineFile.FullName -PackageVersions $driftPackageVersions -RequirePackageVersions
        }
        catch {
            if ($_.Exception.Message.Contains('baseline entry was not observed', [System.StringComparison]::Ordinal)) {
                $driftRejected = $true
            }
            else {
                throw
            }
        }

        if (-not $driftRejected) {
            throw 'Parser self-test expected an unobserved baseline entry to be rejected.'
        }

        Write-Host 'RejectedUnobservedBaselineWarning=True'

        $versionDriftBaseline = [ordered]@{
            version = 1
            policy = 'AOT parser self-test package drift baseline.'
            allowedWarnings = @($providerWarnings | ForEach-Object {
                [ordered]@{
                    id = $_.Id
                    assembly = $_.Assembly
                    sourcePackage = $_.Assembly
                    packageVersion = '9.9.9'
                    owner = 'provider'
                    rationale = 'Parser self-test package version drift sample.'
                }
            })
        }
        $versionDriftBaseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $baselineFile.FullName -Encoding utf8NoBOM

        $versionDriftRejected = $false
        try {
            Assert-AotWarningsMatchBaseline -Warnings $providerWarnings -BaselinePath $baselineFile.FullName -PackageVersions $testPackageVersions -RequirePackageVersions
        }
        catch {
            if ($_.Exception.Message.Contains('package version drift', [System.StringComparison]::Ordinal)) {
                $versionDriftRejected = $true
            }
            else {
                throw
            }
        }

        if (-not $versionDriftRejected) {
            throw 'Parser self-test expected package version drift to be rejected.'
        }

        Write-Host 'RejectedPackageVersionDrift=True'
    }
    finally {
        Remove-Item -LiteralPath $baselineFile.FullName -Force -ErrorAction SilentlyContinue
    }
}

if ($ParserSelfTest) {
    Invoke-AotParserSelfTest
    return
}

if (-not (Test-Path -LiteralPath $aotProject)) {
    throw "AOT verification project was not found: $aotProject"
}

$aotRid = Get-AotRuntimeIdentifier
$artifactRoot = Resolve-RepoChildPath -PathValue $ArtifactsDirectory -Name 'ArtifactsDirectory'
$publishDirectory = Join-Path $artifactRoot (Join-Path 'publish' $aotRid)

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

Write-Host 'Lib.Db AOT verification started.'
Write-Host "AotArtifacts=$artifactRoot"

$publishArguments = @(
    'publish',
    $aotProject,
    '-c', 'Release',
    '-r', $aotRid,
    '--self-contained', 'true',
    '--no-restore',
    '-p:PublishAot=true',
    '-p:GeneratePackageOnBuild=false',
    '-p:TreatWarningsAsErrors=false',
    '-p:WarningsAsErrors=',
    '-p:TrimmerSingleWarn=false',
    '-p:UseSharedCompilation=false',
    '-o', $publishDirectory,
    '-v:minimal'
)

$publishOutput = & dotnet @publishArguments 2>&1
$publishExitCode = $LASTEXITCODE
$publishLines = @($publishOutput | ForEach-Object { $_.ToString() })
foreach ($line in $publishLines) {
    Write-Output $line
}

if ($publishExitCode -ne 0) {
    throw "dotnet publish failed with exit code $publishExitCode."
}

$aotWarnings = @(Get-AotWarnings -Lines $publishLines)
$assetsPath = Join-Path (Split-Path -Parent $aotProject) 'obj\project.assets.json'
$packageVersions = Get-AotPackageVersions -AssetsPath $assetsPath
Assert-AotWarningsMatchBaseline -Warnings $aotWarnings -BaselinePath $warningBaselinePath -PackageVersions $packageVersions -RequirePackageVersions
Write-Host "AotWarningCount=$($aotWarnings.Count)"

$aotExecutable = Join-Path $publishDirectory (Get-AotExecutableName)
if (-not (Test-Path -LiteralPath $aotExecutable)) {
    throw "AOT verification executable was not produced: $aotExecutable"
}

Invoke-Checked $aotExecutable @()

Write-Host 'Lib.Db AOT verification completed.'
