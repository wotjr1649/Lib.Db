param(
    [ValidateSet('NarrowWideOnly', 'FullMatrix')]
    [string] $SetupMode = 'FullMatrix',
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $Job = 'Short',
    [string] $Filter = '*TvpBenchmarks*',
    [switch] $SkipSetup,
    [switch] $SkipRun,
    [switch] $SkipSecretScan,
    [switch] $AllowPartial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$canonicalScript = Join-Path $repoRoot 'Verification\scripts\Invoke-Benchmarks.ps1'

Write-Warning 'Tools\benchmark\Invoke-LibDbBenchmarks.ps1 is a compatibility shim. Use Verification\scripts\Invoke-Benchmarks.ps1.'
& pwsh -NoProfile -File $canonicalScript @PSBoundParameters
exit $LASTEXITCODE
