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
$canonicalScript = Join-Path $repoRoot 'Verification\scripts\Invoke-Coverage.ps1'

Write-Warning 'Tools\coverage\Invoke-LibDbCoverage.ps1 is a compatibility shim. Use Verification\scripts\Invoke-Coverage.ps1.'
& pwsh -NoProfile -File $canonicalScript @PSBoundParameters
exit $LASTEXITCODE
