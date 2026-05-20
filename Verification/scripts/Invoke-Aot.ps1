param(
    [string] $ArtifactsDirectory = 'Verification\artifacts\aot'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$aotProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj'

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

if (-not (Test-Path -LiteralPath $aotProject)) {
    throw "AOT verification project was not found: $aotProject"
}

$aotRid = Get-AotRuntimeIdentifier
$artifactRoot = Resolve-RepoChildPath -PathValue $ArtifactsDirectory -Name 'ArtifactsDirectory'
$publishDirectory = Join-Path $artifactRoot (Join-Path 'publish' $aotRid)

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

Write-Host 'Lib.Db v2.3.0 AOT verification started.'
Write-Host "AotArtifacts=$artifactRoot"

Invoke-Checked 'dotnet' @(
    'publish',
    $aotProject,
    '-c', 'Release',
    '-r', $aotRid,
    '--self-contained', 'true',
    '-p:PublishAot=true',
    '-p:TreatWarningsAsErrors=true',
    '-o', $publishDirectory,
    '-v:minimal'
)

$aotExecutable = Join-Path $publishDirectory (Get-AotExecutableName)
if (-not (Test-Path -LiteralPath $aotExecutable)) {
    throw "AOT verification executable was not produced: $aotExecutable"
}

Invoke-Checked $aotExecutable @()

Write-Host 'Lib.Db v2.3.0 AOT verification completed.'
