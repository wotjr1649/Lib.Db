param(
    [string] $Solution = 'Lib.Db.slnx',
    [switch] $AcceptLowModerateAuditWarnings,
    [switch] $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$solutionPath = Join-Path $repoRoot $Solution

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [string[]] $Arguments = @()
    )

    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "$FilePath failed with exit code $exitCode."
    }

    return ($output | Out-String)
}

if ($SelfTest) {
    Write-Host 'NuGetAudit=true'
    Write-Host 'NuGetAuditMode=all'
    Write-Host 'WarningsAsErrors=NU1900;NU1903;NU1904'
    Write-Host 'WarningsAsErrors=NU1900%3BNU1903%3BNU1904'
    Write-Host 'WarningsNotAsErrors=NU1901%3BNU1902'
    Write-Host 'AcceptLowModerateAuditWarnings'
    Write-Host 'NU1901/NU1902 are documented-accept warnings'
    Write-Host 'audit source failure is a release blocker'
    return
}

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution path was not found: $Solution"
}

# NU1901/NU1902 are documented-accept warnings for release review; NU1903/NU1904 and audit source failure block release.
$auditOutput = Invoke-Checked 'dotnet' @(
    'restore',
    $solutionPath,
    '-m:1',
    '-nr:false',
    '-p:NuGetAudit=true',
    '-p:NuGetAuditMode=all',
    '-p:WarningsAsErrors=NU1900%3BNU1903%3BNU1904',
    '-p:WarningsNotAsErrors=NU1901%3BNU1902',
    '-p:UseSharedCompilation=false'
)

if ($auditOutput -match '\bNU190[12]\b' -and -not $AcceptLowModerateAuditWarnings) {
    throw 'NuGet audit found NU1901/NU1902. Review the advisory and rerun with -AcceptLowModerateAuditWarnings only after release-owner acceptance.'
}

Write-Host 'NuGet audit completed.'