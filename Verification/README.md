# Lib.Db Verification

This directory is the canonical root for Lib.Db verification assets.

## Environment Variable Names

- LIBDB_TEST_CONNECTION_VERIFICATION
- LIBDB_TEST_CONNECTION_SORTER
- LIBDB_TEST_CONNECTION_STRESS
- LIBDB_TEST_CONNECTION_CHAOS
- LIBDB_TEST_CONNECTION_BENCHMARK
- LIBDB_TEST_SQL_PASSWORD
- LIBDB_BENCHMARK_CONNECTION
- SQLCMDPASSWORD

Scripts print only whether each key is present and do not print values. `Invoke-Tests.ps1`, `Invoke-Coverage.ps1`, `Invoke-Benchmarks.ps1`, and `Invoke-Verification.ps1` load `Verification/scripts/Set-LibDbVerificationEnvironment.local.ps1` only when `-UseLocalEnvironment` is specified. The example bootstrap reads `LIBDB_TEST_SQL_PASSWORD` and sets process-scoped `SQLCMDPASSWORD` so `Invoke-VerificationDb.ps1` can call `sqlcmd -U` without putting the password on the command line.

Do not run database-backed tests through raw `dotnet test`. The integration-test project has an MSBuild guard that fails before VSTest when the verification environment is missing. Use `Invoke-Tests.ps1` for focused tests and `Invoke-Verification.ps1` for release gates. `Invoke-Tests.ps1 -Target IntegrationTests` and the matrix gate in `Invoke-Verification.ps1` run the Microsoft.Testing.Platform executable directly by default. `Invoke-Coverage.ps1` runs the built test apphost directly instead of routing coverage through the test wrapper, avoiding Windows MTP coverage process-monitor IPC failures and desktop crash dialogs while preserving the same verification-environment guard in the test assembly.

Run `Invoke-Coverage.ps1` and full release verification from a normal user PowerShell session. Restricted sandbox shells can deny the MTP coverage process-monitor named pipe and surface as a `0xe0434352` Windows crash dialog instead of a normal test failure.

## Database Allowlist

- LIBDB_VERIFICATION_TEST
- LIBDB_STRESS_TEST
- LIBDB_CHAOS_TEST
- LIBDB_BENCH_TEST

Direct SQL execution is restricted to allowlisted files under `Verification/databases/<DB>/`.

## Commands

```powershell
.\Verification\scripts\Invoke-Tests.ps1 -UseLocalEnvironment -NoRestore -NoBuild
.\Verification\scripts\Invoke-Tests.ps1 -UseLocalEnvironment -Target IntegrationTests -NoRestore -NoBuild -FilterClass "*V230TvpMatrixTests*"
.\Verification\scripts\Invoke-Verification.ps1 -UseLocalEnvironment
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Verification -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Stress -Setup -Verify -Matrix
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Chaos -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench -MemoryOptimizedTvpOptIn -VerifyFinal
```

## Artifacts

- Verification/artifacts/test-results
- Verification/artifacts/coverage/raw
- Verification/artifacts/coverage/report
- Verification/artifacts/benchmarks/BenchmarkDotNet.Artifacts
- Verification/artifacts/aot
- Verification/artifacts/release-package

Release package artifacts are dry-run verification outputs. Workflows exclude `Verification/artifacts/release-package/**`, `*.nupkg`, and `*.snupkg` from uploaded verification artifacts. Do not preserve or share package artifacts unless the release package scanner has passed.

`Invoke-Verification.ps1` scans the artifact roots produced by the current release gate run. Run `Scan-VerificationArtifacts.ps1` directly for a broader historical artifact sweep.

Generated artifacts are not source and must not be committed.
