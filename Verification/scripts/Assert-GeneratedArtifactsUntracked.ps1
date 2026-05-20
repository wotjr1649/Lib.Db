Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

$generatedRoots = @(
    'Verification/artifacts',
    'TestResults',
    'BenchmarkDotNet.Artifacts',
    'artifacts'
)

$tracked = @(& git -C $repoRoot ls-files -- $generatedRoots)
if ($LASTEXITCODE -ne 0) {
    throw 'git ls-files failed while checking generated artifact tracking state.'
}

if ($tracked.Count -gt 0) {
    Write-Output 'Generated artifact paths are tracked by git:'
    $tracked | Sort-Object | ForEach-Object { Write-Output $_ }
    exit 1
}

$unignored = @(& git -C $repoRoot status --porcelain -- $generatedRoots)
if ($LASTEXITCODE -ne 0) {
    throw 'git status failed while checking generated artifact tracking state.'
}

if ($unignored.Count -gt 0) {
    Write-Output 'Generated artifact paths are unignored or modified:'
    $unignored | Sort-Object | ForEach-Object { Write-Output $_ }
    exit 1
}

Write-Output 'Generated artifact paths are ignored/untracked as expected.'
