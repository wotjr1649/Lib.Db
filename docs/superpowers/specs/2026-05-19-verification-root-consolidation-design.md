# Verification Root Consolidation Design

Date: 2026-05-19

Status: Draft for user review

## Goal

Consolidate the Lib.Db v2.3.0 verification, benchmark, SQL setup/verify, AOT, coverage, and harness assets under a single top-level `Verification/` directory. The version must not appear as a directory name. The consolidated root must let a maintainer run each of the four verification databases independently, or run the full v2.3.0 verification flow from one place.

## Decisions

- The canonical physical root is `Verification/`.
- No `Verification/v2.3.0/` or other version-named subdirectory is created.
- v2.3.0 identity is documented in `Verification/README.md`, `Verification/manifest.json`, script names/output, and release docs.
- `LIBDB_VERIFICATION_TEST`, `LIBDB_STRESS_TEST`, `LIBDB_CHAOS_TEST`, and `LIBDB_BENCH_TEST` remain the only direct SQL verification databases.
- Direct `sqlcmd` DDL/DML/EXEC execution is allowed only through guarded scripts that resolve to a hard-coded DB/file allowlist under `Verification/databases/*`.
- `manifest.json` is advisory metadata only. It must never be the authority for DB selection, SQL execution permission, credential handling, or artifact preservation.
- Connection strings, passwords, and secret values must never be accepted, passed, echoed, printed, or persisted as expanded command-line values. Scripts may print only key presence and target DB names.
- Existing `Tools/` commands remain as compatibility shims for one migration window, but canonical scripts move to `Verification/scripts/`.

## Official References

- Microsoft Learn `sqlcmd` utility: `sqlcmd` supports input files, SQLCMD commands, SQL Server authentication options, and warns that command-line passwords are insecure compared with environment variable or prompt-based input.  
  <https://learn.microsoft.com/en-us/sql/tools/sqlcmd/sqlcmd-utility?view=sql-server-ver17>
- Microsoft Learn table-valued parameters: TVPs are strongly typed user-defined table types passed to routines as `READONLY` parameters.  
  <https://learn.microsoft.com/en-us/sql/relational-databases/tables/use-table-valued-parameters-database-engine?view=sql-server-ver17>
- Microsoft Learn memory-optimized table variables: SQL Server memory-optimized table types require a `MEMORY_OPTIMIZED_DATA` filegroup before use on SQL Server.  
  <https://learn.microsoft.com/en-us/sql/relational-databases/in-memory-oltp/faster-temp-table-and-table-variable-by-using-memory-optimization?view=sql-server-ver17>
- Microsoft Learn `dotnet test`: `dotnet test` builds and runs tests for a solution or project, with runner behavior depending on VSTest or Microsoft Testing Platform.  
  <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test>
- Microsoft Learn `dotnet sln`: solution files can list/add/remove project paths and organize projects into solution folders.  
  <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-sln>
- BenchmarkDotNet console arguments: `BenchmarkSwitcher` supports `--filter`/`-f` for benchmark selection by name/glob.  
  <https://benchmarkdotnet.org/articles/guides/console-args.html>
- BenchmarkDotNet exporters: default reports include markdown/html/csv style outputs under artifacts results.  
  <https://benchmarkdotnet.org/articles/configs/exporters.html>

## Current State

Verification assets are currently spread across multiple top-level folders:

- `Tests/Lib.Db.IntegrationTests`
  - xUnit integration/unit coverage tests
  - `V230Matrix` tests
  - SQL setup/verify files under `sql/`
  - Coverlet runsettings
- `Tests/Lib.Db.AotVerification`
  - Native AOT publish gate executable
- `Benchmarks/Lib.Db.Benchmarks`
  - BenchmarkDotNet console project
  - runtime TVP benchmark cases
  - generated accessor baseline readers
  - benchmark artifact scanner
- `Tools/verification`
  - v2.3.0 full verification orchestrator
- `Tools/coverage`
  - Coverlet collection and coverage gate scripts
- `Tools/benchmark`
  - BenchmarkDotNet wrapper
- `Tools/Lib.Db.AotSmoke`
  - DB-backed AOT smoke tool
- `Tools/Lib.Db.ChaosHarness`
  - server-level chaos harness
- `TestResults`
  - coverage and test result output
- `BenchmarkDotNet.Artifacts`
  - benchmark reports and logs

The runtime behavior is usable, but discovery is fragmented. A maintainer has to know which project, SQL file, wrapper script, and artifact directory belongs to each DB.

## Target Structure

```text
Verification/
  README.md
  manifest.json

  databases/
    LIBDB_VERIFICATION_TEST/
      sql/
        setup-libdb-verification-test.sql
        verify-libdb-verification-test.sql
        verify-libdb-sqlserver2025-syntax.sql
        feature-gap-verification.sql
        upgrade-coverage-100.sql
      run.ps1

    LIBDB_STRESS_TEST/
      sql/
        setup-libdb-stress-test.sql
        verify-libdb-stress-test.sql
      run.ps1

    LIBDB_CHAOS_TEST/
      sql/
        setup-libdb-chaos-test.sql
        verify-libdb-chaos-test.sql
      server-optin/
        setup-libdb-chaos-server-optin.sql
        verify-libdb-chaos-server-optin.sql
        teardown-libdb-chaos-server-optin.sql
      run.ps1

    LIBDB_BENCH_TEST/
      sql/
        setup-libdb-bench-test.sql
        verify-libdb-bench-default.sql
        verify-libdb-bench-test.sql
        run-libdb-bench-memory-optimized-tvp-optin.sql
        setup-libdb-bench-memory-optimized-tvp-optin.sql
        verify-libdb-bench-memory-optimized-tvp-optin.sql
      run.ps1

  projects/
    Lib.Db.IntegrationTests/
    Lib.Db.AotVerification/
    Lib.Db.Benchmarks/
    Lib.Db.AotSmoke/
    Lib.Db.ChaosHarness/

  scripts/
    Invoke-Verification.ps1
    Invoke-VerificationDb.ps1
    Invoke-Coverage.ps1
    Invoke-Benchmarks.ps1
    Invoke-Aot.ps1

  artifacts/
    test-results/
    coverage/
    benchmarks/
    aot/
```

## Direct SQL Allowlist

`Invoke-VerificationDb.ps1` owns a hard-coded allowlist. The allowlist is a tuple of `{logicalDb, exactDatabaseName, databaseRoot, allowedSqlFiles}` and is the only source of truth for direct `sqlcmd` DDL/DML/EXEC execution.

The current authored SQL files, plus the required BENCH default split file, are allowlisted for migration as follows:

| Logical DB | Exact database name | Allowed SQL files | Mode |
| --- | --- | --- | --- |
| `Verification` | `LIBDB_VERIFICATION_TEST` | `setup-libdb-verification-test.sql`, `verify-libdb-verification-test.sql`, `verify-libdb-sqlserver2025-syntax.sql`, `feature-gap-verification.sql`, `upgrade-coverage-100.sql` | default |
| `Stress` | `LIBDB_STRESS_TEST` | `setup-libdb-stress-test.sql`, `verify-libdb-stress-test.sql` | default |
| `Chaos` | `LIBDB_CHAOS_TEST` | `setup-libdb-chaos-test.sql`, `verify-libdb-chaos-test.sql` | default |
| `Chaos` | `LIBDB_CHAOS_TEST` | `server-optin/setup-libdb-chaos-server-optin.sql`, `server-optin/verify-libdb-chaos-server-optin.sql`, `server-optin/teardown-libdb-chaos-server-optin.sql` | explicit server opt-in only |
| `Bench` | `LIBDB_BENCH_TEST` | `setup-libdb-bench-test.sql`, `verify-libdb-bench-default.sql` | default |
| `Bench` | `LIBDB_BENCH_TEST` | `setup-libdb-bench-memory-optimized-tvp-optin.sql`, `verify-libdb-bench-memory-optimized-tvp-optin.sql`, `run-libdb-bench-memory-optimized-tvp-optin.sql`, `verify-libdb-bench-test.sql` | explicit memory-optimized TVP opt-in/final only |

`verify-libdb-bench-default.sql` is created during the consolidation by extracting the non-memory-optimized checks from the existing `verify-libdb-bench-test.sql`. The existing `verify-libdb-bench-test.sql` remains the final BENCH sync verifier after the memory-optimized TVP opt-in setup has been applied.

`verify-libdb-all.sql` is a migration-only orchestration reference. It must not be passed as an arbitrary SQL input to `Invoke-VerificationDb.ps1`; the new full verification path calls per-DB allowlisted files explicitly.

Allowlist enforcement rules:

- Callers cannot pass arbitrary SQL file paths.
- All SQL paths are normalized before execution and must resolve under `Verification/databases/<exactDatabaseName>/`.
- Absolute paths, `..` traversal, symlink escapes, and DB-folder-external includes are rejected.
- SQLCMD `:r` includes are allowed only when every included file is itself in the same tuple's allowlist and resolves under the same DB root.
- `master` may be used only for allowed DB existence checks, allowed DB creation, and allowed DB options required by the same tuple.
- Server-level DDL/EXEC is prohibited unless the selected tuple is `Chaos` server opt-in.

## Directory Responsibilities

### `Verification/README.md`

The human entrypoint. It documents:

- required environment variable names, without values
- SQL Server target assumptions
- DB allowlist
- direct `sqlcmd` command examples
- full verification command
- per-DB command examples
- artifact locations
- notes about memory-optimized TVP and server-level chaos opt-in

### `Verification/manifest.json`

The machine-readable registry of verification assets. It records each database, setup script, verify script, optional scripts, related test filters, benchmark filters, and artifact subdirectories.

The manifest is advisory and must not contain secret values. Scripts may read it to render help, docs, reports, or artifact locations, but scripts must not use it as the security authority for SQL execution. The hard-coded allowlist remains authoritative.

Manifest rules:

- Schema validation is required before use.
- Only relative paths are allowed.
- Absolute paths, path traversal, and paths outside `Verification/` are invalid.
- Manifest entries cannot grant new DBs, new SQL files, new credentials, or new artifact preservation permissions.

Example shape:

```json
{
  "version": "v2.3.0",
  "databases": {
    "Bench": {
      "name": "LIBDB_BENCH_TEST",
      "defaultSetup": "databases/LIBDB_BENCH_TEST/sql/setup-libdb-bench-test.sql",
      "defaultVerify": "databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-default.sql",
      "finalVerify": "databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-test.sql",
      "optional": [
        "databases/LIBDB_BENCH_TEST/sql/run-libdb-bench-memory-optimized-tvp-optin.sql",
        "databases/LIBDB_BENCH_TEST/sql/setup-libdb-bench-memory-optimized-tvp-optin.sql",
        "databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-memory-optimized-tvp-optin.sql"
      ],
      "benchmarkFilters": ["*TvpBenchmarks*"]
    }
  }
}
```

### `Verification/databases/*`

Each verification database owns its SQL files and a small `run.ps1` entrypoint. The DB folder name is the exact database name to make the target unambiguous.

Rules:

- `setup-*` SQL files may contain DDL/DML required to create the test database objects.
- `verify-*` SQL files may contain direct DML/EXEC smoke checks for their own database.
- Every script must `USE` only its allowed DB, or `master` only when creating/checking that same allowed DB.
- Any server-level behavior must live under an explicit opt-in subfolder.
- `LIBDB_CHAOS_TEST/server-optin` stays separate from default chaos verification.
- SQLCMD includes must not cross out of the selected DB folder unless every included file is explicitly present in the same allowlist tuple.
- `LIBDB_BENCH_TEST` has two verification baselines: default BENCH baseline and after-opt-in final BENCH baseline.

### `Verification/projects/*`

The executable/test projects move under the verification root:

- `Lib.Db.IntegrationTests`
- `Lib.Db.AotVerification`
- `Lib.Db.Benchmarks`
- `Lib.Db.AotSmoke`
- `Lib.Db.ChaosHarness`

Project references to `Lib.Db/Lib.Db.csproj` must be updated with the new relative paths.

### `Verification/scripts/*`

These are the canonical orchestrators.

`Invoke-Verification.ps1`:

- runs build
- runs DB matrix tests
- runs coverage
- runs AOT publish
- runs benchmark wrapper
- scans benchmark artifacts

`Invoke-VerificationDb.ps1`:

- accepts `-Db Verification|Stress|Chaos|Bench`
- accepts `-Setup`, `-Verify`, `-VerifyDefault`, `-VerifyFinal`, `-Matrix`, and safe opt-in switches
- maps DB names through an internal hard-coded allowlist tuple of `{logicalDb, exactDatabaseName, databaseRoot, allowedSqlFiles}`
- refuses unknown DB names
- refuses caller-supplied SQL paths
- normalizes all candidate paths and rejects absolute paths, `..`, symlink escape, and DB-folder-external includes
- treats `manifest.json` as advisory only
- never prints or passes connection string/password/token values as command-line arguments

`Invoke-Coverage.ps1`:

- writes raw coverage to `Verification/artifacts/coverage/raw`
- writes report output to `Verification/artifacts/coverage/report`
- invokes the coverage gate

`Invoke-Benchmarks.ps1`:

- writes BenchmarkDotNet artifacts under `Verification/artifacts/benchmarks`
- supports `-Job Dry|Short|Default`
- supports BenchmarkDotNet `--filter`
- runs artifact secret scanning

`Invoke-Aot.ps1`:

- publishes `Lib.Db.AotVerification`
- stores logs/output under `Verification/artifacts/aot`
- keeps Lib.Db-owned warnings as release blockers
- leaves provider-owned AOT warnings visible for tracking

### `Verification/artifacts/*`

All generated verification outputs move under one tree:

- `test-results`
- `coverage`
- `benchmarks`
- `aot`

This avoids repo-root clutter from `TestResults` and `BenchmarkDotNet.Artifacts`. Existing generated artifact folders should not be treated as source.

## DB-Specific Verification Model

### `LIBDB_VERIFICATION_TEST`

Purpose:

- broad functional verification
- representative complex tables/SP/TVP
- runtime TVP execution
- schema provider/cache flush checks

Expected current sync baseline:

- 35 tables
- 13 TVP types
- 56 stored procedures

### `LIBDB_STRESS_TEST`

Purpose:

- connection pool and concurrency tests
- TVP matrix load and shape coverage
- Query Store workload analysis

Expected current sync baseline:

- 18 tables
- 10 TVP types
- 36 stored procedures

### `LIBDB_CHAOS_TEST`

Purpose:

- DB-scoped fault injection
- rollback/constraint/conversion/timeout/retryable error checks
- TVP chaos matrix

Expected current sync baseline:

- 17 tables
- 8 TVP types
- 34 stored procedures

Server-level chaos:

- remains explicitly opt-in
- uses separate setup/verify/teardown SQL
- uses separate harness
- must not be included in default verification

### `LIBDB_BENCH_TEST`

Purpose:

- BenchmarkDotNet target schema
- generated accessor baseline comparison
- runtime TVP fast-path benchmark
- memory-optimized TVP opt-in coverage

Default BENCH baseline:

- runs with `setup-libdb-bench-test.sql`
- verifies with `verify-libdb-bench-default.sql`
- excludes memory-optimized TVP objects and `MEMORY_OPTIMIZED_DATA` filegroup checks
- is the target for default `-Db Bench -Setup -Verify`

Expected default sync baseline:

- 20 tables
- 14 TVP types
- 42 stored procedures
- `AUTO_CLOSE = OFF`

After-opt-in final BENCH baseline:

- runs with `setup-libdb-bench-memory-optimized-tvp-optin.sql`, or `run-libdb-bench-memory-optimized-tvp-optin.sql`
- verifies with `verify-libdb-bench-memory-optimized-tvp-optin.sql` plus final `verify-libdb-bench-test.sql`
- requires an explicit `-MemoryOptimizedTvpOptIn` or `-VerifyFinal` command path

Expected final sync baseline:

- 21 tables
- 15 TVP types
- 43 stored procedures
- 1 memory-optimized TVP type
- 1 memory-optimized TVP parameter
- `MEMORY_OPTIMIZED_DATA` filegroup
- `AUTO_CLOSE = OFF`

The benchmark verifier treats memory-optimized TVP objects as part of the final BENCH sync manifest. The setup that creates the durable memory-optimized filegroup remains explicit because it changes database metadata.

## Compatibility Strategy

Existing paths are widely referenced in scripts, docs, and historical plans. To avoid a brittle big-bang migration:

1. Move canonical assets to `Verification/`.
2. Leave old `Tools/verification`, `Tools/coverage`, and `Tools/benchmark` scripts as compatibility shims.
3. The shims call the new `Verification/scripts/*` files and print a short migration note.
4. Compatibility shims must be thin wrappers only. They must not contain their own `sqlcmd`, DB selection, credential handling, artifact scan, or SQL path resolution logic.
5. Do not keep duplicate SQL files in both old and new locations after migration. Duplication would create drift risk.
6. Historical docs under `docs/superpowers/plans` and old release notes may retain old paths as historical records. Active docs must point to `Verification/`.

## Security And Safety

- Direct `sqlcmd` DDL/DML/EXEC is restricted to the four verification DBs and to the hard-coded allowlisted SQL files.
- Direct SQL scripts must reject unexpected database names.
- Direct SQL runners must reject caller-supplied SQL paths, absolute paths, path traversal, symlink escape, and DB-folder-external includes.
- Scripts must not print connection strings, passwords, tokens, or expanded secret values.
- Scripts must not accept or pass secret values through command-line arguments such as `-P` or `--connection-string`; use environment variable key names or secure prompt input only.
- Logs may include only key presence, target DB names, logical mode, artifact path, and rule ids. Logs must not include secret values.
- Exceptions, command echo, transcripts, BenchmarkDotNet output, AOT logs, test result logs, and coverage artifacts must pass through a redaction wrapper before preservation.
- Server-level chaos must stay out of default verification and is allowed only through `LIBDB_CHAOS_TEST/server-optin`.
- Memory-optimized TVP setup must stay explicit because it creates durable database filegroup/file metadata.
- Default BENCH verification must not create or require memory-optimized TVP objects or `MEMORY_OPTIMIZED_DATA` filegroups.
- Final BENCH verification may require memory-optimized TVP objects only after explicit memory-optimized TVP opt-in.
- Benchmark, AOT, coverage, and test-result artifacts must be scanned for secret-like strings before preservation or release.
- The artifact scanner prints only matching path and rule id, never matched secret-like values.
- `Verification/artifacts/**`, legacy `TestResults/**`, and `BenchmarkDotNet.Artifacts/**` must be protected by `.gitignore` and a tracked-file gate.
- Release/commit precheck must fail when `git ls-files` reports generated verification artifacts as tracked source.
- The `manifest.json` file must contain names and paths only, never credentials, and must not grant execution authority.

## Execution UX

Full gate:

```powershell
.\Verification\scripts\Invoke-Verification.ps1 -Mode Full
```

DB-specific setup and verify:

```powershell
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Verification -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Stress -Setup -Verify -Matrix
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Chaos -Setup -Verify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench -Setup -Verify
```

BENCH memory-optimized TVP opt-in:

```powershell
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench -MemoryOptimizedTvpOptIn -VerifyFinal
```

Server-level chaos opt-in:

```powershell
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Chaos -ServerChaosSetup
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Chaos -ServerChaosVerify
.\Verification\scripts\Invoke-VerificationDb.ps1 -Db Chaos -ServerChaosTeardown
```

Coverage:

```powershell
.\Verification\scripts\Invoke-Coverage.ps1
```

Benchmark:

```powershell
.\Verification\scripts\Invoke-Benchmarks.ps1 -Job Short -Filter "*TvpBenchmarks*"
```

AOT:

```powershell
.\Verification\scripts\Invoke-Aot.ps1
```

## Migration Plan Summary

Detailed implementation plan will be written separately after this design is approved.

High-level migration order:

1. Create `Verification/` skeleton and manifest.
2. Move SQL files into DB-specific folders.
3. Move verification projects under `Verification/projects/`.
4. Update project references and SQL copy/link includes.
5. Move canonical scripts under `Verification/scripts/`.
6. Convert old `Tools/` scripts into compatibility shims.
7. Move default artifact outputs under `Verification/artifacts/`.
8. Update active docs and README.
9. Run DB sync checks, focused tests, full matrix, AOT publish, coverage gate, benchmark dry/short, and artifact scan.

## Acceptance Criteria

- `Verification/` is the only canonical root for v2.3.0 verification assets.
- There is no version-named directory under `Verification/`.
- `Lib.Db.slnx` references verification projects from `Verification/projects/*`.
- The old `Tests/`, `Benchmarks/`, and `Tools/` canonical verification assets are either moved or replaced with compatibility shims.
- SQL setup/verify files are grouped by DB under `Verification/databases/<DB>/`.
- Running per-DB verification confirms 100% object sync for all four DBs.
- Default `LIBDB_BENCH_TEST` verification confirms 20 tables, 14 TVP types, and 42 stored procedures with zero missing/extra objects and does not require memory-optimized TVP objects.
- Final `LIBDB_BENCH_TEST` verification, after explicit memory-optimized TVP opt-in, confirms 21 tables, 15 TVP types, and 43 stored procedures with zero missing/extra objects.
- Final `LIBDB_BENCH_TEST` verification confirms the memory-optimized TVP type, hash bucket count, SP parameter, filegroup, and `AUTO_CLOSE = OFF`.
- Server-level chaos remains opt-in and separate from the default gate.
- Coverage artifacts are written under `Verification/artifacts/coverage`.
- Benchmark artifacts are written under `Verification/artifacts/benchmarks`.
- AOT artifacts are written under `Verification/artifacts/aot`.
- Existing compatibility commands continue to work through shims.
- Active documentation no longer tells users to start from scattered `Tests/`, `Benchmarks/`, or `Tools/` roots.
- `manifest.json` is schema-validated, relative-path-only, and not used as the authority for SQL execution.
- `Tools/*` compatibility shims contain no independent DB selection, `sqlcmd`, credential handling, or artifact scan logic.
- `Verification/artifacts/**`, `TestResults/**`, and `BenchmarkDotNet.Artifacts/**` are gitignored or rejected by a tracked-file gate.
- Full gate secret scanning covers benchmark, AOT, coverage, and test-result text artifacts and reports only path plus rule id.
- Release/commit precheck fails if generated verification artifacts are tracked by Git.

## Out Of Scope

- Changing Lib.Db runtime behavior.
- Changing benchmark methodology or benchmark scenario definitions.
- Adding new database scenarios beyond the already approved four verification DBs.
- Changing SQL Server credentials or environment variable names.
- Removing historical docs that intentionally describe old work.
- Committing generated artifacts.

## Open Questions

No design-blocking questions remain. Implementation may still choose exact shim deprecation wording and whether to add additional solution filters after the physical move.
