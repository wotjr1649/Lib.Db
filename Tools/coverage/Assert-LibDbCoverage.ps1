param(
    [Parameter(Mandatory = $true)]
    [string] $CoberturaPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$canonicalScript = Join-Path $repoRoot 'Verification\scripts\Assert-LibDbCoverage.ps1'

Write-Warning 'Tools\coverage\Assert-LibDbCoverage.ps1 is a compatibility shim. Use Verification\scripts\Assert-LibDbCoverage.ps1.'
& pwsh -NoProfile -File $canonicalScript -CoberturaPath $CoberturaPath
exit $LASTEXITCODE
