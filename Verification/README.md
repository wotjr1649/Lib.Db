# Lib.Db Verification

This directory is the canonical root for Lib.Db v2.4.0 verification assets.

## Environment Variable Names

- LIBDB_TEST_CONNECTION_VERIFICATION
- LIBDB_TEST_CONNECTION_SORTER
- LIBDB_TEST_CONNECTION_STRESS
- LIBDB_TEST_CONNECTION_CHAOS
- LIBDB_TEST_CONNECTION_BENCHMARK
- LIBDB_TEST_SQL_PASSWORD
- LIBDB_BENCHMARK_CONNECTION
- SQLCMDPASSWORD

Scripts print only whether each key is present and do not print values. `Invoke-Tests.ps1`, `Invoke-Coverage.ps1`, `Invoke-Benchmarks.ps1`, and `Invoke-Verification.ps1` load `Verification/scripts/Set-LibDbVerificationEnvironment.local.ps1` automatically when it exists. The example bootstrap reads `LIBDB_TEST_SQL_PASSWORD` and sets process-scoped `SQLCMDPASSWORD` so `Invoke-VerificationDb.ps1` can call `sqlcmd -U` without putting the password on the command line.

Do not run database-backed tests through raw `dotnet test`. The integration-test project has an MSBuild guard that fails before VSTest when the verification environment is missing. Use `Invoke-Tests.ps1` for focused tests and `Invoke-Verification.ps1` for release gates.

## Database Allowlist

- LIBDB_VERIFICATION_TEST
- LIBDB_STRESS_TEST
- LIBDB_CHAOS_TEST
- LIBDB_BENCH_TEST

Direct SQL execution is restricted to allowlisted files under `Verification/databases/<DB>/`.

## Commands

```powershell
.\Verification\scripts\Invoke-Tests.ps1 -NoRestore
.\Verification\scripts\Invoke-Tests.ps1 -Target IntegrationTests -NoRestore -NoBuild -Filter "FullyQualifiedName~Lib.Db.IntegrationTests.V230Matrix.V230TvpMatrixTests"
.\Verification\scripts\Invoke-Verification.ps1
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

Generated artifacts are not source and must not be committed.
