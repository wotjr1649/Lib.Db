param(
  [switch] $Setup,
  [switch] $Verify,
  [switch] $VerifyFinal,
  [switch] $MemoryOptimizedTvpOptIn
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$verificationRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$scriptPath = Join-Path $verificationRoot 'scripts\Invoke-VerificationDb.ps1'

$forwardArgs = @{ Db = 'Bench' }

if ($Setup) {
  $forwardArgs.Setup = $true
}

if ($Verify) {
  $forwardArgs.Verify = $true
}

if ($VerifyFinal) {
  $forwardArgs.VerifyFinal = $true
}

if ($MemoryOptimizedTvpOptIn) {
  $forwardArgs.MemoryOptimizedTvpOptIn = $true
}

$global:LASTEXITCODE = 0
& $scriptPath @forwardArgs
$childSucceeded = $?
$childExitCode = $LASTEXITCODE

if (-not $childSucceeded) {
  if ($null -ne $childExitCode -and $childExitCode -ne 0) {
    exit $childExitCode
  }

  exit 1
}

if ($null -ne $childExitCode -and $childExitCode -ne 0) {
  exit $childExitCode
}
