param(
    [Parameter(Mandatory = $true)]
    [string] $CoberturaPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$canonicalScript = Join-Path $PSScriptRoot 'Assert-Coverage.ps1'
& pwsh -NoProfile -File $canonicalScript -CoberturaPath $CoberturaPath
exit $LASTEXITCODE
