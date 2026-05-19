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

$forwardArgs = @('-Db', 'Bench')

if ($Setup) {
  $forwardArgs += '-Setup'
}

if ($Verify) {
  $forwardArgs += '-Verify'
}

if ($VerifyFinal) {
  $forwardArgs += '-VerifyFinal'
}

if ($MemoryOptimizedTvpOptIn) {
  $forwardArgs += '-MemoryOptimizedTvpOptIn'
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
