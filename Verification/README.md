# Lib.Db Verification

This directory is the canonical root for Lib.Db v2.3.0 verification assets.

## Environment Variable Names

- LIBDB_TEST_CONNECTION_VERIFICATION
- LIBDB_TEST_CONNECTION_SORTER
- LIBDB_TEST_CONNECTION_STRESS
- LIBDB_TEST_CONNECTION_CHAOS
- LIBDB_TEST_CONNECTION_BENCHMARK
- LIBDB_BENCHMARK_CONNECTION
- SQLCMDPASSWORD

Scripts print only whether each key is present and do not print values.

## Database Allowlist

- LIBDB_VERIFICATION_TEST
- LIBDB_STRESS_TEST
- LIBDB_CHAOS_TEST
- LIBDB_BENCH_TEST

Direct SQL execution is restricted to allowlisted files under `Verification/databases/<DB>/`.

## Commands

```powershell
.\Verification\scripts\Invoke-Verification.ps1 -Mode Full
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Verification -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Stress -Setup -Verify -Matrix
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Chaos -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench -MemoryOptimizedTvpOptIn -VerifyFinal
```

## Artifacts

- Verification/artifacts/test-results
- Verification/artifacts/coverage
- Verification/artifacts/benchmarks
- Verification/artifacts/aot

Generated artifacts are not source and must not be committed.
