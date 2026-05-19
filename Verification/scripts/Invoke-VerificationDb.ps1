param(
    [ValidateSet('Verification', 'Stress', 'Chaos', 'Bench')]
    [string] $Db = $(throw "Missing mandatory parameter: Db."),
    [switch] $Setup,
    [switch] $Verify,
    [switch] $VerifyDefault,
    [switch] $VerifyFinal,
    [switch] $Matrix,
    [switch] $MemoryOptimizedTvpOptIn,
    [switch] $ServerChaosSetup,
    [switch] $ServerChaosVerify,
    [switch] $ServerChaosTeardown,
    [string] $Server = '127.0.0.1',
    [string] $User = 'SA',
    [ValidateSet('optional', 'mandatory', 'strict')]
    [string] $Encrypt = 'optional',
    [switch] $TrustServerCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DbAllowlist = @{
    Verification = @{
        Name = 'LIBDB_VERIFICATION_TEST'
        Root = 'Verification\databases\LIBDB_VERIFICATION_TEST'
        DefaultSetup = @('sql\setup-libdb-verification-test.sql')
        DefaultVerify = @('sql\verify-libdb-verification-test.sql')
        OptionalVerify = @(
            'sql\verify-libdb-sqlserver2025-syntax.sql',
            'sql\feature-gap-verification.sql',
            'sql\upgrade-coverage-100.sql'
        )
    }
    Stress = @{
        Name = 'LIBDB_STRESS_TEST'
        Root = 'Verification\databases\LIBDB_STRESS_TEST'
        DefaultSetup = @('sql\setup-libdb-stress-test.sql')
        DefaultVerify = @('sql\verify-libdb-stress-test.sql')
    }
    Chaos = @{
        Name = 'LIBDB_CHAOS_TEST'
        Root = 'Verification\databases\LIBDB_CHAOS_TEST'
        DefaultSetup = @('sql\setup-libdb-chaos-test.sql')
        DefaultVerify = @('sql\verify-libdb-chaos-test.sql')
        ServerSetup = @('server-optin\setup-libdb-chaos-server-optin.sql')
        ServerVerify = @('server-optin\verify-libdb-chaos-server-optin.sql')
        ServerTeardown = @('server-optin\teardown-libdb-chaos-server-optin.sql')
    }
    Bench = @{
        Name = 'LIBDB_BENCH_TEST'
        Root = 'Verification\databases\LIBDB_BENCH_TEST'
        DefaultSetup = @('sql\setup-libdb-bench-test.sql')
        DefaultVerify = @('sql\verify-libdb-bench-default.sql')
        FinalVerify = @('sql\verify-libdb-bench-test.sql')
        MemoryOptimizedTvpOptIn = @(
            'sql\setup-libdb-bench-memory-optimized-tvp-optin.sql',
            'sql\run-libdb-bench-memory-optimized-tvp-optin.sql',
            'sql\verify-libdb-bench-memory-optimized-tvp-optin.sql'
        )
    }
}

function Assert-NoReparsePointPath {
    param(
        [Parameter(Mandatory = $true)] [string] $StopDirectory,
        [Parameter(Mandatory = $true)] [string] $TargetPath
    )

    $stopPath = (Resolve-Path -LiteralPath $StopDirectory -ErrorAction Stop).Path
    $targetFullPath = (Resolve-Path -LiteralPath $TargetPath -ErrorAction Stop).Path
    $normalizedStopPath = $stopPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedTargetPath = $targetFullPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $relativeToStop = [System.IO.Path]::GetRelativePath($normalizedStopPath, $normalizedTargetPath)

    if ($relativeToStop.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relativeToStop)) {
        throw "Path resolved outside its allowed root."
    }

    $currentPath = $normalizedTargetPath

    while (-not [string]::IsNullOrWhiteSpace($currentPath)) {
        $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction Stop
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Path uses a reparse point: $currentPath"
        }

        if ([string]::Equals($currentPath, $normalizedStopPath, [StringComparison]::OrdinalIgnoreCase)) {
            return $normalizedTargetPath
        }

        $parentPath = Split-Path -Parent $currentPath
        if ([string]::Equals($parentPath, $currentPath, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $currentPath = $parentPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    }

    throw "Path walk did not reach its allowed root."
}

function Resolve-AllowlistedSqlFile {
    param(
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [hashtable] $Entry,
        [Parameter(Mandatory = $true)] [string] $RelativeSqlPath
    )

    if ([System.IO.Path]::IsPathRooted($RelativeSqlPath)) {
        throw "Absolute SQL paths are not allowed."
    }

    if ($RelativeSqlPath -split '[\\/]' | Where-Object { $_ -eq '..' }) {
        throw "Path traversal is not allowed in SQL paths."
    }

    $repoRootPath = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).Path
    $dbRoot = Assert-NoReparsePointPath -StopDirectory $repoRootPath -TargetPath (Join-Path $repoRootPath $Entry.Root)
    $candidatePath = Join-Path $dbRoot $RelativeSqlPath
    $resolved = (Resolve-Path -LiteralPath $candidatePath -ErrorAction Stop).Path
    $relativeToRoot = [System.IO.Path]::GetRelativePath($dbRoot, $resolved)

    if ($relativeToRoot.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relativeToRoot)) {
        throw "SQL file resolved outside its database root."
    }

    $resolved = Assert-NoReparsePointPath -StopDirectory $dbRoot -TargetPath $resolved

    return $resolved
}

function Assert-SqlcmdIncludesAllowed {
    param(
        [Parameter(Mandatory = $true)] [string] $SqlFile,
        [Parameter(Mandatory = $true)] [string[]] $AllowedFullPaths
    )

    $allowed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $AllowedFullPaths) {
        [void] $allowed.Add((Resolve-Path -LiteralPath $path -ErrorAction Stop).Path)
    }

    $baseDirectory = Split-Path -Parent $SqlFile
    foreach ($line in [System.IO.File]::ReadLines($SqlFile)) {
        if ($line -notmatch '^\s*:r\s+(.+?)\s*$') {
            continue
        }

        $include = $Matches[1].Trim().Trim('"')
        if ([System.IO.Path]::IsPathRooted($include)) {
            throw "Absolute SQLCMD include is not allowed in $SqlFile."
        }

        if ($include -split '[\\/]' | Where-Object { $_ -eq '..' }) {
            throw "Path traversal is not allowed in SQLCMD includes in $SqlFile."
        }

        $includePath = (Resolve-Path -LiteralPath (Join-Path $baseDirectory $include) -ErrorAction Stop).Path
        if (-not $allowed.Contains($includePath)) {
            throw "SQLCMD include is not allowlisted: $include"
        }
    }
}

function Invoke-AllowlistedSqlFile {
    param(
        [Parameter(Mandatory = $true)] [string] $SqlFile,
        [Parameter(Mandatory = $true)] [string] $Server,
        [Parameter(Mandatory = $true)] [string] $User,
        [Parameter(Mandatory = $true)] [string] $Encrypt,
        [switch] $TrustServerCertificate,
        [switch] $EnableServerChaos
    )

    $passwordPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('SQLCMDPASSWORD'))
    Write-Host "SQLCMDPASSWORD present: $passwordPresent"
    Write-Host "SqlFile=$SqlFile"
    $sqlDirectory = Split-Path -Parent $SqlFile
    $locationPushed = $false
    $sqlcmdExitCode = 0
    $sqlcmdEnvironment = [Environment]::GetEnvironmentVariables('Process')
    $sqlcmdIniPresent = $sqlcmdEnvironment.Contains('SQLCMDINI')
    $sqlcmdIni = if ($sqlcmdIniPresent) { [Environment]::GetEnvironmentVariable('SQLCMDINI', 'Process') } else { $null }
    Write-Host "SQLCMDINI present: $sqlcmdIniPresent"

    $encryptValue = switch ($Encrypt) {
        'optional' { 'o' }
        'mandatory' { 'm' }
        'strict' { 's' }
    }

    try {
        if ($sqlcmdIniPresent) {
            [Environment]::SetEnvironmentVariable('SQLCMDINI', $null, 'Process')
        }

        $args = @('-X', '-S', $Server, '-U', $User, '-N', $encryptValue, '-i', $SqlFile, '-f', '65001', '-b')
        if ($TrustServerCertificate) {
            $args += '-C'
        }
        if ($EnableServerChaos) {
            $args += @('-v', 'EnableServerChaos=1')
        }

        Push-Location -LiteralPath $sqlDirectory
        $locationPushed = $true
        $PSNativeCommandUseErrorActionPreference = $false
        $global:LASTEXITCODE = 0
        & sqlcmd @args
        $sqlcmdExitCode = $LASTEXITCODE
    }
    finally {
        try {
            if ($locationPushed) {
                Pop-Location
            }
        }
        finally {
            if ($sqlcmdIniPresent) {
                [Environment]::SetEnvironmentVariable('SQLCMDINI', $sqlcmdIni, 'Process')
            }
        }
    }

    if ($sqlcmdExitCode -ne 0) {
        exit $sqlcmdExitCode
    }
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..') -ErrorAction Stop).Path
$repoRoot = Assert-NoReparsePointPath -StopDirectory $repoRoot -TargetPath $repoRoot
$entry = $DbAllowlist[$Db]

if ($null -eq $entry) {
    throw "Unknown verification database."
}

if ($Matrix) {
    Write-Host "Matrix switch is accepted for compatibility; DB SQL setup/verify selection is unchanged."
}

$selected = [System.Collections.Generic.List[object]]::new()
function Add-SelectedSqlFiles {
    param(
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [System.Collections.Generic.List[object]] $Selected,
        [Parameter(Mandatory = $true)] [string[]] $RelativeSqlPaths,
        [switch] $EnableServerChaos
    )

    foreach ($relativeSqlPath in $RelativeSqlPaths) {
        [void] $Selected.Add([pscustomobject]@{
            RelativeSqlPath = $relativeSqlPath
            EnableServerChaos = [bool] $EnableServerChaos
        })
    }
}

if ($Setup) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.DefaultSetup }
if ($Verify -or $VerifyDefault) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.DefaultVerify }
if ($Db -eq 'Bench' -and $MemoryOptimizedTvpOptIn) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.MemoryOptimizedTvpOptIn }
if ($Db -eq 'Bench' -and $VerifyFinal) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.FinalVerify }
if ($Db -eq 'Chaos' -and $ServerChaosSetup) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.ServerSetup -EnableServerChaos }
if ($Db -eq 'Chaos' -and $ServerChaosVerify) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.ServerVerify -EnableServerChaos }
if ($Db -eq 'Chaos' -and $ServerChaosTeardown) { Add-SelectedSqlFiles -Selected $selected -RelativeSqlPaths $entry.ServerTeardown -EnableServerChaos }

if ($Db -ne 'Bench' -and ($MemoryOptimizedTvpOptIn -or $VerifyFinal)) {
    throw "Memory-optimized BENCH switches are valid only for -Db Bench."
}

if ($Db -ne 'Chaos' -and ($ServerChaosSetup -or $ServerChaosVerify -or $ServerChaosTeardown)) {
    throw "Server chaos switches are valid only for -Db Chaos."
}

if ($selected.Count -eq 0) {
    throw "No allowlisted SQL action was selected."
}

$resolvedSqlActions = [System.Collections.Generic.List[object]]::new()
$allowedFullPaths = [System.Collections.Generic.List[string]]::new()
foreach ($selectedSqlFile in $selected) {
    $resolvedSqlFile = Resolve-AllowlistedSqlFile -RepoRoot $repoRoot -Entry $entry -RelativeSqlPath $selectedSqlFile.RelativeSqlPath
    [void] $allowedFullPaths.Add($resolvedSqlFile)
    [void] $resolvedSqlActions.Add([pscustomobject]@{
        SqlFile = $resolvedSqlFile
        EnableServerChaos = [bool] $selectedSqlFile.EnableServerChaos
    })
}

$allowedFullPathArray = $allowedFullPaths.ToArray()
foreach ($sqlFile in $allowedFullPathArray) {
    Assert-SqlcmdIncludesAllowed -SqlFile $sqlFile -AllowedFullPaths $allowedFullPathArray
}

foreach ($sqlAction in $resolvedSqlActions) {
    Invoke-AllowlistedSqlFile `
        -SqlFile $sqlAction.SqlFile `
        -Server $Server `
        -User $User `
        -Encrypt $Encrypt `
        -TrustServerCertificate:$TrustServerCertificate `
        -EnableServerChaos:$sqlAction.EnableServerChaos
}
