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

$forwardArgs = @('-Db', 'Chaos')

if ($Setup) {
  $forwardArgs += '-Setup'
}

if ($Verify) {
  $forwardArgs += '-Verify'
}

if ($ServerChaosSetup) {
  $forwardArgs += '-ServerChaosSetup'
}

if ($ServerChaosVerify) {
  $forwardArgs += '-ServerChaosVerify'
}

if ($ServerChaosTeardown) {
  $forwardArgs += '-ServerChaosTeardown'
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
