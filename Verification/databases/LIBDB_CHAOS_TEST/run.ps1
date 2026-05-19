param(
  [switch] $Setup,
  [switch] $Verify,
  [switch] $ServerChaosSetup,
  [switch] $ServerChaosVerify,
  [switch] $ServerChaosTeardown
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$verificationRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$scriptPath = Join-Path $verificationRoot 'scripts\Invoke-VerificationDb.ps1'

$forwardArgs = @{ Db = 'Chaos' }

if ($Setup) {
  $forwardArgs.Setup = $true
}

if ($Verify) {
  $forwardArgs.Verify = $true
}

if ($ServerChaosSetup) {
  $forwardArgs.ServerChaosSetup = $true
}

if ($ServerChaosVerify) {
  $forwardArgs.ServerChaosVerify = $true
}

if ($ServerChaosTeardown) {
  $forwardArgs.ServerChaosTeardown = $true
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
