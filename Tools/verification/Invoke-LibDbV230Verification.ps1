param(
    [switch] $SkipCoverage,
    [switch] $SkipBenchmark,
    [switch] $SkipMatrixDbTests,
    [switch] $SkipAot,
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $BenchmarkJob = 'Short',
    [switch] $AllowPartial
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$canonicalScript = Join-Path $repoRoot 'Verification\scripts\Invoke-Verification.ps1'
$canonicalArguments = [System.Collections.Generic.List[string]]::new()

if ($SkipCoverage) {
    $canonicalArguments.Add('-SkipCoverage')
}

if ($SkipBenchmark) {
    $canonicalArguments.Add('-SkipBenchmark')
}

if ($SkipMatrixDbTests) {
    $canonicalArguments.Add('-SkipMatrixDbTests')
}

if ($SkipAot) {
    $canonicalArguments.Add('-SkipAot')
}

$canonicalArguments.Add('-BenchmarkJob')
$canonicalArguments.Add($BenchmarkJob)

if ($AllowPartial) {
    $canonicalArguments.Add('-AllowPartial')
}

Write-Warning 'Tools\verification\Invoke-LibDbV230Verification.ps1 is a compatibility shim. Use Verification\scripts\Invoke-Verification.ps1.'
& pwsh -NoProfile -File $canonicalScript @($canonicalArguments.ToArray())
exit $LASTEXITCODE
