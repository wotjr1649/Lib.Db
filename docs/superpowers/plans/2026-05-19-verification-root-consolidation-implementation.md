# Verification Root Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate Lib.Db v2.3.0 verification SQL, integration tests, AOT checks, benchmarks, harnesses, scripts, and generated artifacts under the canonical `Verification/` root.

**Architecture:** Keep Lib.Db runtime code untouched. Move verification-only assets into `Verification/`, introduce a guarded direct SQL runner with a hard-coded DB/file allowlist, split BENCH default verification from memory-optimized TVP final verification, and leave existing `Tools/*` commands as thin compatibility shims.

**Tech Stack:** .NET 10, C# preview, xUnit v3, Microsoft.Data.SqlClient, PowerShell 7, sqlcmd, Coverlet collector, ReportGenerator, BenchmarkDotNet.

---

## Source Spec

- Spec: `docs/superpowers/specs/2026-05-19-verification-root-consolidation-design.md`
- Design commit: `dc4b1fa docs: add verification root consolidation design`

## Guardrails

- Do not change Lib.Db runtime behavior in this plan.
- Do not run direct `sqlcmd` DDL/DML/EXEC unless the user explicitly approves it for the four verification DBs.
- Do not print connection strings, passwords, tokens, or expanded secret values.
- Do not pass secrets as command-line values such as `-P` or `--connection-string`.
- Use environment variable key names or secure prompt flow only.
- Commit one task at a time. Stage only files listed in that task.
- Generated artifacts are not source and must not be committed.

## File Structure Map

### Create

- `Verification/README.md`  
  Human entrypoint with env var names, command examples, DB allowlist summary, artifacts, and opt-in notes.
- `Verification/manifest.json`  
  Advisory metadata for docs/help/reporting only.
- `Verification/databases/LIBDB_VERIFICATION_TEST/sql/*`  
  Verification DB SQL files moved from `Tests/Lib.Db.IntegrationTests/sql`.
- `Verification/databases/LIBDB_STRESS_TEST/sql/*`  
  Stress DB SQL files moved from `Tests/Lib.Db.IntegrationTests/sql`.
- `Verification/databases/LIBDB_CHAOS_TEST/sql/*`  
  Default chaos SQL files moved from `Tests/Lib.Db.IntegrationTests/sql`.
- `Verification/databases/LIBDB_CHAOS_TEST/server-optin/*`  
  Server-level chaos setup/verify/teardown SQL files moved from `Tests/Lib.Db.IntegrationTests/sql`.
- `Verification/databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-default.sql`  
  New default BENCH verifier that excludes memory-optimized objects.
- `Verification/databases/LIBDB_BENCH_TEST/sql/*memory-optimized*`  
  BENCH memory-optimized TVP opt-in SQL files moved from `Tests/Lib.Db.IntegrationTests/sql`.
- `Verification/databases/*/run.ps1`  
  Per-DB wrappers that call `Verification/scripts/Invoke-VerificationDb.ps1`.
- `Verification/scripts/Invoke-Verification.ps1`  
  Full orchestrator.
- `Verification/scripts/Invoke-VerificationDb.ps1`  
  Direct SQL allowlist runner.
- `Verification/scripts/Invoke-Coverage.ps1`  
  Coverage runner writing to `Verification/artifacts/coverage`.
- `Verification/scripts/Assert-LibDbCoverage.ps1`  
  Coverage gate moved from `Tools/coverage`.
- `Verification/scripts/Invoke-Benchmarks.ps1`  
  Benchmark runner writing to `Verification/artifacts/benchmarks`.
- `Verification/scripts/Scan-VerificationArtifacts.ps1`  
  Secret-pattern scan for benchmark, AOT, coverage, and test-result text artifacts.
- `Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1`  
  Tracked-file gate for generated verification artifacts.
- `Verification/scripts/Invoke-Aot.ps1`  
  AOT publish wrapper writing to `Verification/artifacts/aot`.
- `Verification/projects/Lib.Db.IntegrationTests/**`  
  Moved integration test project.
- `Verification/projects/Lib.Db.AotVerification/**`  
  Moved Native AOT verification project.
- `Verification/projects/Lib.Db.Benchmarks/**`  
  Moved BenchmarkDotNet project.
- `Verification/projects/Lib.Db.AotSmoke/**`  
  Moved DB-backed AOT smoke project.
- `Verification/projects/Lib.Db.ChaosHarness/**`  
  Moved server-level chaos harness.

### Modify

- `Lib.Db.slnx`  
  Reference verification projects from `Verification/projects/*`.
- `Verification/projects/*/*.csproj`  
  Update project references to `../../../Lib.Db/Lib.Db.csproj` after the move.
- `Verification/projects/Lib.Db.IntegrationTests/Infrastructure/SqlScriptRunner.cs`  
  Resolve SQL scripts from copied output `sql/`, new `Verification/databases/*`, and the project output.
- `Verification/projects/Lib.Db.IntegrationTests/Infrastructure/MultiDbFixture.cs`  
  Use default BENCH verifier, not final memory-optimized verifier, during default fixture initialization.
- `Verification/projects/Lib.Db.IntegrationTests/V230Matrix/*.cs`  
  Align tests with new SQL paths and BENCH default/final split.
- `Tools/verification/Invoke-LibDbV230Verification.ps1`  
  Replace with thin shim.
- `Tools/coverage/Invoke-LibDbCoverage.ps1`  
  Replace with thin shim.
- `Tools/coverage/Assert-LibDbCoverage.ps1`  
  Replace with thin shim or remove after confirming compatibility command coverage.
- `Tools/benchmark/Invoke-LibDbBenchmarks.ps1`  
  Replace with thin shim.
- `Benchmarks/Lib.Db.Benchmarks/ScanBenchmarkArtifacts.ps1`  
  Replace with thin shim if the old path remains for compatibility.
- `.gitignore`  
  Ignore `Verification/artifacts/`, `BenchmarkDotNet.Artifacts/`, `TestResults/`, and transient AOT/benchmark output.
- `README.md`, `docs/v2.3.0-verification.md`, `docs/04_operations.md`  
  Update active docs to start from `Verification/`.

---

## Task 1: Create Verification Skeleton, README, And Manifest

**Files:**
- Create: `Verification/README.md`
- Create: `Verification/manifest.json`
- Create: `Verification/databases/LIBDB_VERIFICATION_TEST/run.ps1`
- Create: `Verification/databases/LIBDB_STRESS_TEST/run.ps1`
- Create: `Verification/databases/LIBDB_CHAOS_TEST/run.ps1`
- Create: `Verification/databases/LIBDB_BENCH_TEST/run.ps1`
- Create directories under `Verification/artifacts/`

- [ ] **Step 1: Create the directory skeleton**

Run:

```powershell
New-Item -ItemType Directory -Force `
  Verification, `
  Verification\databases\LIBDB_VERIFICATION_TEST\sql, `
  Verification\databases\LIBDB_STRESS_TEST\sql, `
  Verification\databases\LIBDB_CHAOS_TEST\sql, `
  Verification\databases\LIBDB_CHAOS_TEST\server-optin, `
  Verification\databases\LIBDB_BENCH_TEST\sql, `
  Verification\projects, `
  Verification\scripts, `
  Verification\artifacts\test-results, `
  Verification\artifacts\coverage, `
  Verification\artifacts\benchmarks, `
  Verification\artifacts\aot
```

Expected: all directories exist, and no `Verification/v2.3.0` directory exists.

- [ ] **Step 2: Create `Verification/manifest.json`**

Write this exact JSON shape. Keep paths relative and do not add credentials:

```json
{
  "version": "v2.3.0",
  "databases": {
    "Verification": {
      "name": "LIBDB_VERIFICATION_TEST",
      "defaultSetup": "databases/LIBDB_VERIFICATION_TEST/sql/setup-libdb-verification-test.sql",
      "defaultVerify": "databases/LIBDB_VERIFICATION_TEST/sql/verify-libdb-verification-test.sql",
      "optional": [
        "databases/LIBDB_VERIFICATION_TEST/sql/verify-libdb-sqlserver2025-syntax.sql",
        "databases/LIBDB_VERIFICATION_TEST/sql/feature-gap-verification.sql",
        "databases/LIBDB_VERIFICATION_TEST/sql/upgrade-coverage-100.sql"
      ],
      "testFilters": [
        "FullyQualifiedName~Lib.Db.IntegrationTests.VerificationDb"
      ],
      "artifactSubdirectory": "test-results"
    },
    "Stress": {
      "name": "LIBDB_STRESS_TEST",
      "defaultSetup": "databases/LIBDB_STRESS_TEST/sql/setup-libdb-stress-test.sql",
      "defaultVerify": "databases/LIBDB_STRESS_TEST/sql/verify-libdb-stress-test.sql",
      "optional": [],
      "testFilters": [
        "FullyQualifiedName~Lib.Db.IntegrationTests.V230Matrix"
      ],
      "artifactSubdirectory": "test-results"
    },
    "Chaos": {
      "name": "LIBDB_CHAOS_TEST",
      "defaultSetup": "databases/LIBDB_CHAOS_TEST/sql/setup-libdb-chaos-test.sql",
      "defaultVerify": "databases/LIBDB_CHAOS_TEST/sql/verify-libdb-chaos-test.sql",
      "optional": [
        "databases/LIBDB_CHAOS_TEST/server-optin/setup-libdb-chaos-server-optin.sql",
        "databases/LIBDB_CHAOS_TEST/server-optin/verify-libdb-chaos-server-optin.sql",
        "databases/LIBDB_CHAOS_TEST/server-optin/teardown-libdb-chaos-server-optin.sql"
      ],
      "testFilters": [
        "FullyQualifiedName~Lib.Db.IntegrationTests.V230Matrix"
      ],
      "artifactSubdirectory": "test-results"
    },
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
      "benchmarkFilters": [
        "*TvpBenchmarks*"
      ],
      "artifactSubdirectory": "benchmarks"
    }
  },
  "scripts": {
    "full": "scripts/Invoke-Verification.ps1",
    "db": "scripts/Invoke-VerificationDb.ps1",
    "coverage": "scripts/Invoke-Coverage.ps1",
    "benchmarks": "scripts/Invoke-Benchmarks.ps1",
    "aot": "scripts/Invoke-Aot.ps1"
  }
}
```

- [ ] **Step 3: Create `Verification/README.md`**

Include these sections:

```markdown
# Lib.Db Verification

This directory is the canonical root for Lib.Db v2.3.0 verification assets.

## Environment Variable Names

- `LIBDB_TEST_CONNECTION_VERIFICATION`
- `LIBDB_TEST_CONNECTION_SORTER`
- `LIBDB_TEST_CONNECTION_STRESS`
- `LIBDB_TEST_CONNECTION_CHAOS`
- `LIBDB_TEST_CONNECTION_BENCHMARK`
- `LIBDB_BENCHMARK_CONNECTION`
- `SQLCMDPASSWORD`

The scripts print only whether each key is present. They do not print values.

## Database Allowlist

- `LIBDB_VERIFICATION_TEST`
- `LIBDB_STRESS_TEST`
- `LIBDB_CHAOS_TEST`
- `LIBDB_BENCH_TEST`

Direct SQL execution is restricted to the allowlisted files under `Verification/databases/<DB>/`.

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

- `Verification/artifacts/test-results`
- `Verification/artifacts/coverage`
- `Verification/artifacts/benchmarks`
- `Verification/artifacts/aot`

Generated artifacts are not source and must not be committed.
```

- [ ] **Step 4: Create per-DB `run.ps1` wrappers**

Each DB wrapper forwards to the canonical runner. Example for `Verification/databases/LIBDB_BENCH_TEST/run.ps1`:

```powershell
param(
    [switch] $Setup,
    [switch] $Verify,
    [switch] $VerifyFinal,
    [switch] $MemoryOptimizedTvpOptIn
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runner = Join-Path $repoRoot 'Verification\scripts\Invoke-VerificationDb.ps1'
& pwsh -NoProfile -File $runner -Db Bench -Setup:$Setup -Verify:$Verify -VerifyFinal:$VerifyFinal -MemoryOptimizedTvpOptIn:$MemoryOptimizedTvpOptIn
exit $LASTEXITCODE
```

Use the same pattern for the other DBs with `-Db Verification`, `-Db Stress`, or `-Db Chaos`. The chaos wrapper also exposes `-ServerChaosSetup`, `-ServerChaosVerify`, and `-ServerChaosTeardown`.

- [ ] **Step 5: Verify skeleton and commit**

Run:

```powershell
Test-Path .\Verification\README.md
Test-Path .\Verification\manifest.json
Test-Path .\Verification\v2.3.0
```

Expected:

```text
True
True
False
```

Commit:

```powershell
git add Verification\README.md Verification\manifest.json Verification\databases
git commit -m "chore: create verification root skeleton"
```

---

## Task 2: Move SQL Files And Split BENCH Default Verification

**Files:**
- Move: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-verification-test.sql` -> `Verification/databases/LIBDB_VERIFICATION_TEST/sql/setup-libdb-verification-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-verification-test.sql` -> `Verification/databases/LIBDB_VERIFICATION_TEST/sql/verify-libdb-verification-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-sqlserver2025-syntax.sql` -> `Verification/databases/LIBDB_VERIFICATION_TEST/sql/verify-libdb-sqlserver2025-syntax.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/feature-gap-verification.sql` -> `Verification/databases/LIBDB_VERIFICATION_TEST/sql/feature-gap-verification.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/upgrade-coverage-100.sql` -> `Verification/databases/LIBDB_VERIFICATION_TEST/sql/upgrade-coverage-100.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-stress-test.sql` -> `Verification/databases/LIBDB_STRESS_TEST/sql/setup-libdb-stress-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-stress-test.sql` -> `Verification/databases/LIBDB_STRESS_TEST/sql/verify-libdb-stress-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-chaos-test.sql` -> `Verification/databases/LIBDB_CHAOS_TEST/sql/setup-libdb-chaos-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-chaos-test.sql` -> `Verification/databases/LIBDB_CHAOS_TEST/sql/verify-libdb-chaos-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-chaos-server-optin.sql` -> `Verification/databases/LIBDB_CHAOS_TEST/server-optin/setup-libdb-chaos-server-optin.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-chaos-server-optin.sql` -> `Verification/databases/LIBDB_CHAOS_TEST/server-optin/verify-libdb-chaos-server-optin.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/teardown-libdb-chaos-server-optin.sql` -> `Verification/databases/LIBDB_CHAOS_TEST/server-optin/teardown-libdb-chaos-server-optin.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-bench-test.sql` -> `Verification/databases/LIBDB_BENCH_TEST/sql/setup-libdb-bench-test.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-bench-memory-optimized-tvp-optin.sql` -> `Verification/databases/LIBDB_BENCH_TEST/sql/setup-libdb-bench-memory-optimized-tvp-optin.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-bench-memory-optimized-tvp-optin.sql` -> `Verification/databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-memory-optimized-tvp-optin.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/run-libdb-bench-memory-optimized-tvp-optin.sql` -> `Verification/databases/LIBDB_BENCH_TEST/sql/run-libdb-bench-memory-optimized-tvp-optin.sql`
- Move: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-bench-test.sql` -> `Verification/databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-test.sql`
- Move or delete after migration: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-all.sql`
- Create: `Verification/databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-default.sql`

- [ ] **Step 1: Move SQL files with `git mv`**

Run these moves from repo root:

```powershell
git mv Tests\Lib.Db.IntegrationTests\sql\setup-libdb-verification-test.sql Verification\databases\LIBDB_VERIFICATION_TEST\sql\setup-libdb-verification-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-verification-test.sql Verification\databases\LIBDB_VERIFICATION_TEST\sql\verify-libdb-verification-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-sqlserver2025-syntax.sql Verification\databases\LIBDB_VERIFICATION_TEST\sql\verify-libdb-sqlserver2025-syntax.sql
git mv Tests\Lib.Db.IntegrationTests\sql\feature-gap-verification.sql Verification\databases\LIBDB_VERIFICATION_TEST\sql\feature-gap-verification.sql
git mv Tests\Lib.Db.IntegrationTests\sql\upgrade-coverage-100.sql Verification\databases\LIBDB_VERIFICATION_TEST\sql\upgrade-coverage-100.sql
git mv Tests\Lib.Db.IntegrationTests\sql\setup-libdb-stress-test.sql Verification\databases\LIBDB_STRESS_TEST\sql\setup-libdb-stress-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-stress-test.sql Verification\databases\LIBDB_STRESS_TEST\sql\verify-libdb-stress-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\setup-libdb-chaos-test.sql Verification\databases\LIBDB_CHAOS_TEST\sql\setup-libdb-chaos-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-chaos-test.sql Verification\databases\LIBDB_CHAOS_TEST\sql\verify-libdb-chaos-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\setup-libdb-chaos-server-optin.sql Verification\databases\LIBDB_CHAOS_TEST\server-optin\setup-libdb-chaos-server-optin.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-chaos-server-optin.sql Verification\databases\LIBDB_CHAOS_TEST\server-optin\verify-libdb-chaos-server-optin.sql
git mv Tests\Lib.Db.IntegrationTests\sql\teardown-libdb-chaos-server-optin.sql Verification\databases\LIBDB_CHAOS_TEST\server-optin\teardown-libdb-chaos-server-optin.sql
git mv Tests\Lib.Db.IntegrationTests\sql\setup-libdb-bench-test.sql Verification\databases\LIBDB_BENCH_TEST\sql\setup-libdb-bench-test.sql
git mv Tests\Lib.Db.IntegrationTests\sql\setup-libdb-bench-memory-optimized-tvp-optin.sql Verification\databases\LIBDB_BENCH_TEST\sql\setup-libdb-bench-memory-optimized-tvp-optin.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-bench-memory-optimized-tvp-optin.sql Verification\databases\LIBDB_BENCH_TEST\sql\verify-libdb-bench-memory-optimized-tvp-optin.sql
git mv Tests\Lib.Db.IntegrationTests\sql\run-libdb-bench-memory-optimized-tvp-optin.sql Verification\databases\LIBDB_BENCH_TEST\sql\run-libdb-bench-memory-optimized-tvp-optin.sql
git mv Tests\Lib.Db.IntegrationTests\sql\verify-libdb-bench-test.sql Verification\databases\LIBDB_BENCH_TEST\sql\verify-libdb-bench-test.sql
```

Expected: the old SQL directory no longer contains the moved files.

- [ ] **Step 2: Create default BENCH verifier**

Create `Verification/databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-default.sql` by copying final `verify-libdb-bench-test.sql` and applying these exact removals:

Remove expected table:

```sql
(N'dbo.libdb_bench_MemoryOptimizedOrderItems')
```

Remove expected TVP type:

```sql
(N'dbo.libdb_bench_MemoryOptimizedOrderItem')
```

Remove expected procedure:

```sql
(N'dbo.libdb_bench_InsertMemoryOptimizedOrderItems')
```

Remove these final-only checks:

```sql
IF NOT EXISTS (SELECT 1 FROM sys.filegroups WHERE [type] = N'FX')
    INSERT INTO @Failures VALUES (N'memory-optimized-filegroup', N'LIBDB_BENCH_TEST must include a MEMORY_OPTIMIZED_DATA filegroup.');
```

```sql
IF NOT EXISTS
(
    SELECT 1
    FROM sys.table_types AS table_types
    WHERE SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND table_types.[is_memory_optimized] = 1
)
    INSERT INTO @Failures VALUES (N'memory-optimized-type', N'dbo.libdb_bench_MemoryOptimizedOrderItem must be memory optimized.');
```

```sql
IF NOT EXISTS
(
    SELECT 1
    FROM sys.table_types AS table_types
    INNER JOIN sys.hash_indexes AS hash_indexes
        ON hash_indexes.[object_id] = table_types.[type_table_object_id]
    WHERE SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND hash_indexes.[bucket_count] = 1024
)
    INSERT INTO @Failures VALUES (N'memory-optimized-hash-index', N'dbo.libdb_bench_MemoryOptimizedOrderItem must expose the expected hash index.');
```

```sql
IF NOT EXISTS
(
    SELECT 1
    FROM sys.parameters AS parameters
    INNER JOIN sys.table_types AS table_types
        ON table_types.[user_type_id] = parameters.[user_type_id]
    WHERE parameters.[object_id] = OBJECT_ID(N'[dbo].[libdb_bench_InsertMemoryOptimizedOrderItems]', N'P')
      AND parameters.[name] = N'@Rows'
      AND parameters.[is_readonly] = 1
      AND SCHEMA_NAME(table_types.[schema_id]) = N'dbo'
      AND table_types.[name] = N'libdb_bench_MemoryOptimizedOrderItem'
      AND table_types.[is_memory_optimized] = 1
)
    INSERT INTO @Failures VALUES (N'memory-optimized-tvp-param', N'dbo.libdb_bench_InsertMemoryOptimizedOrderItems @Rows must be READONLY dbo.libdb_bench_MemoryOptimizedOrderItem.');
```

Remove the memory-optimized TVP smoke block that declares `@MemoryOptimizedRows` and executes `libdb_bench_InsertMemoryOptimizedOrderItems`.

Change the final note to:

```sql
SELECT N'LIBDB_BENCH_TEST default verification passed.' AS [Result],
       (SELECT COUNT(*) FROM @ExpectedTables) AS [ExpectedTables],
       (SELECT COUNT(*) FROM @ExpectedTypes) AS [ExpectedTypes],
       (SELECT COUNT(*) FROM @ExpectedProcedures) AS [ExpectedProcedures],
       N'SqlBulkCopy and BenchmarkDotNet timing require .NET benchmark harness. Memory-optimized TVP final sync requires explicit opt-in.' AS [BenchmarkNote];
```

- [ ] **Step 3: Update `run-libdb-bench-memory-optimized-tvp-optin.sql` includes**

The moved file must include local files from the same `sql` folder:

```sql
:ON ERROR EXIT
:r .\setup-libdb-bench-memory-optimized-tvp-optin.sql
:r .\verify-libdb-bench-memory-optimized-tvp-optin.sql
```

- [ ] **Step 4: Decide `verify-libdb-all.sql` migration**

Create `Verification/databases/verify-libdb-all.migration-reference.sql` only if a human-readable migration reference is needed. Otherwise delete the old `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-all.sql` after all tests stop depending on it.

The new direct runner must not execute this file.

- [ ] **Step 5: Verify SQL inventory**

Run:

```powershell
$expected = @(
  'Verification\databases\LIBDB_VERIFICATION_TEST\sql\setup-libdb-verification-test.sql',
  'Verification\databases\LIBDB_VERIFICATION_TEST\sql\verify-libdb-verification-test.sql',
  'Verification\databases\LIBDB_VERIFICATION_TEST\sql\verify-libdb-sqlserver2025-syntax.sql',
  'Verification\databases\LIBDB_VERIFICATION_TEST\sql\feature-gap-verification.sql',
  'Verification\databases\LIBDB_VERIFICATION_TEST\sql\upgrade-coverage-100.sql',
  'Verification\databases\LIBDB_STRESS_TEST\sql\setup-libdb-stress-test.sql',
  'Verification\databases\LIBDB_STRESS_TEST\sql\verify-libdb-stress-test.sql',
  'Verification\databases\LIBDB_CHAOS_TEST\sql\setup-libdb-chaos-test.sql',
  'Verification\databases\LIBDB_CHAOS_TEST\sql\verify-libdb-chaos-test.sql',
  'Verification\databases\LIBDB_CHAOS_TEST\server-optin\setup-libdb-chaos-server-optin.sql',
  'Verification\databases\LIBDB_CHAOS_TEST\server-optin\verify-libdb-chaos-server-optin.sql',
  'Verification\databases\LIBDB_CHAOS_TEST\server-optin\teardown-libdb-chaos-server-optin.sql',
  'Verification\databases\LIBDB_BENCH_TEST\sql\setup-libdb-bench-test.sql',
  'Verification\databases\LIBDB_BENCH_TEST\sql\verify-libdb-bench-default.sql',
  'Verification\databases\LIBDB_BENCH_TEST\sql\verify-libdb-bench-test.sql',
  'Verification\databases\LIBDB_BENCH_TEST\sql\run-libdb-bench-memory-optimized-tvp-optin.sql',
  'Verification\databases\LIBDB_BENCH_TEST\sql\setup-libdb-bench-memory-optimized-tvp-optin.sql',
  'Verification\databases\LIBDB_BENCH_TEST\sql\verify-libdb-bench-memory-optimized-tvp-optin.sql'
)
$missing = $expected | Where-Object { -not (Test-Path $_) }
if ($missing) { $missing; exit 1 }
```

Expected: no output and exit code 0.

- [ ] **Step 6: Commit**

```powershell
git add Verification\databases Tests\Lib.Db.IntegrationTests\sql
git commit -m "chore: move verification sql under verification root"
```

---

## Task 3: Implement Guarded Direct SQL Runner

**Files:**
- Create: `Verification/scripts/Invoke-VerificationDb.ps1`
- Modify: `Verification/databases/*/run.ps1`
- Test manually with path validation commands only unless SQL execution is approved.

- [ ] **Step 1: Implement hard-coded allowlist**

`Invoke-VerificationDb.ps1` must define this allowlist in code, not in `manifest.json`:

```powershell
$DbAllowlist = @{
    Verification = @{
        Name = 'LIBDB_VERIFICATION_TEST'
        Root = 'Verification\databases\LIBDB_VERIFICATION_TEST'
        DefaultSetup = @('sql\setup-libdb-verification-test.sql')
        DefaultVerify = @('sql\verify-libdb-verification-test.sql')
        OptionalVerify = @('sql\verify-libdb-sqlserver2025-syntax.sql')
    }
    Stress = @{
        Name = 'LIBDB_STRESS_TEST'
        Root = 'Verification\databases\LIBDB_STRESS_TEST'
        DefaultSetup = @('sql\setup-libdb-stress-test.sql')
        DefaultVerify = @('sql\verify-libdb-stress-test.sql')
    }
    Chaos = @{
        Name = 'LIBDB_CHAOS_TEST'
        Root = 'Verification\databases\LIBDB_CHAOS_TEST'
        DefaultSetup = @('sql\setup-libdb-chaos-test.sql')
        DefaultVerify = @('sql\verify-libdb-chaos-test.sql')
        ServerSetup = @('server-optin\setup-libdb-chaos-server-optin.sql')
        ServerVerify = @('server-optin\verify-libdb-chaos-server-optin.sql')
        ServerTeardown = @('server-optin\teardown-libdb-chaos-server-optin.sql')
    }
    Bench = @{
        Name = 'LIBDB_BENCH_TEST'
        Root = 'Verification\databases\LIBDB_BENCH_TEST'
        DefaultSetup = @('sql\setup-libdb-bench-test.sql')
        DefaultVerify = @('sql\verify-libdb-bench-default.sql')
        FinalVerify = @('sql\verify-libdb-bench-test.sql')
        MemoryOptimizedTvpOptIn = @(
            'sql\setup-libdb-bench-memory-optimized-tvp-optin.sql',
            'sql\verify-libdb-bench-memory-optimized-tvp-optin.sql'
        )
    }
}
```

- [ ] **Step 2: Implement parameter surface**

Use this parameter block:

```powershell
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Verification', 'Stress', 'Chaos', 'Bench')]
    [string] $Db,
    [switch] $Setup,
    [switch] $Verify,
    [switch] $VerifyDefault,
    [switch] $VerifyFinal,
    [switch] $Matrix,
    [switch] $MemoryOptimizedTvpOptIn,
    [switch] $ServerChaosSetup,
    [switch] $ServerChaosVerify,
    [switch] $ServerChaosTeardown,
    [string] $Server = '127.0.0.1',
    [string] $User = 'SA',
    [ValidateSet('optional', 'mandatory', 'strict')]
    [string] $Encrypt = 'optional',
    [switch] $TrustServerCertificate
)
```

Do not add parameters for SQL file paths or connection strings.

- [ ] **Step 3: Implement path normalization**

Use this function in the script:

```powershell
function Resolve-AllowlistedSqlFile {
    param(
        [Parameter(Mandatory = $true)] [string] $RepoRoot,
        [Parameter(Mandatory = $true)] [hashtable] $Entry,
        [Parameter(Mandatory = $true)] [string] $RelativeSqlPath
    )

    if ([System.IO.Path]::IsPathRooted($RelativeSqlPath)) {
        throw "Absolute SQL paths are not allowed."
    }

    if ($RelativeSqlPath -split '[\\/]' | Where-Object { $_ -eq '..' }) {
        throw "Path traversal is not allowed in SQL paths."
    }

    $dbRoot = (Resolve-Path -LiteralPath (Join-Path $RepoRoot $Entry.Root)).Path
    $candidatePath = Join-Path $dbRoot $RelativeSqlPath
    $resolved = (Resolve-Path -LiteralPath $candidatePath -ErrorAction Stop).Path
    $relativeToRoot = [System.IO.Path]::GetRelativePath($dbRoot, $resolved)

    if ($relativeToRoot.StartsWith('..') -or [System.IO.Path]::IsPathRooted($relativeToRoot)) {
        throw "SQL file resolved outside its database root."
    }

    return $resolved
}
```

- [ ] **Step 4: Implement SQLCMD include validation**

Before execution, scan `:r` includes and require them to resolve inside the same DB root and be part of the selected file list:

```powershell
function Assert-SqlcmdIncludesAllowed {
    param(
        [Parameter(Mandatory = $true)] [string] $SqlFile,
        [Parameter(Mandatory = $true)] [string[]] $AllowedFullPaths
    )

    $allowed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $AllowedFullPaths) {
        [void] $allowed.Add((Resolve-Path -LiteralPath $path).Path)
    }

    $baseDirectory = Split-Path -Parent $SqlFile
    foreach ($line in [System.IO.File]::ReadLines($SqlFile)) {
        if ($line -notmatch '^\s*:r\s+(.+?)\s*$') {
            continue
        }

        $include = $Matches[1].Trim().Trim('"')
        if ([System.IO.Path]::IsPathRooted($include)) {
            throw "Absolute SQLCMD include is not allowed in $SqlFile."
        }

        $includePath = (Resolve-Path -LiteralPath (Join-Path $baseDirectory $include) -ErrorAction Stop).Path
        if (-not $allowed.Contains($includePath)) {
            throw "SQLCMD include is not allowlisted: $include"
        }
    }
}
```

- [ ] **Step 5: Implement secret-safe sqlcmd invocation**

Use `SQLCMDPASSWORD` or prompt. Do not use `-P`.

```powershell
function Invoke-AllowlistedSqlFile {
    param(
        [Parameter(Mandatory = $true)] [string] $SqlFile,
        [Parameter(Mandatory = $true)] [string] $Server,
        [Parameter(Mandatory = $true)] [string] $User,
        [Parameter(Mandatory = $true)] [string] $Encrypt,
        [switch] $TrustServerCertificate
    )

    $passwordPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('SQLCMDPASSWORD'))
    Write-Host "SQLCMDPASSWORD present: $passwordPresent"
    Write-Host "SqlFile=$SqlFile"

    $encryptValue = switch ($Encrypt) {
        'optional' { 'o' }
        'mandatory' { 'm' }
        'strict' { 's' }
    }

    $args = @('-S', $Server, '-U', $User, '-N', $encryptValue, '-i', $SqlFile, '-f', '65001', '-b')
    if ($TrustServerCertificate) {
        $args += '-C'
    }

    & sqlcmd @args
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed for allowlisted SQL file."
    }
}
```

- [ ] **Step 6: Add mode selection**

Selection rules:

```powershell
$selected = [System.Collections.Generic.List[string]]::new()
if ($Setup) { $entry.DefaultSetup | ForEach-Object { $selected.Add($_) } }
if ($Verify -or $VerifyDefault) { $entry.DefaultVerify | ForEach-Object { $selected.Add($_) } }
if ($Db -eq 'Bench' -and $MemoryOptimizedTvpOptIn) { $entry.MemoryOptimizedTvpOptIn | ForEach-Object { $selected.Add($_) } }
if ($Db -eq 'Bench' -and $VerifyFinal) { $entry.FinalVerify | ForEach-Object { $selected.Add($_) } }
if ($Db -eq 'Chaos' -and $ServerChaosSetup) { $entry.ServerSetup | ForEach-Object { $selected.Add($_) } }
if ($Db -eq 'Chaos' -and $ServerChaosVerify) { $entry.ServerVerify | ForEach-Object { $selected.Add($_) } }
if ($Db -eq 'Chaos' -and $ServerChaosTeardown) { $entry.ServerTeardown | ForEach-Object { $selected.Add($_) } }
```

Reject these combinations:

```powershell
if ($Db -ne 'Bench' -and ($MemoryOptimizedTvpOptIn -or $VerifyFinal)) {
    throw "Memory-optimized BENCH switches are valid only for -Db Bench."
}
if ($Db -ne 'Chaos' -and ($ServerChaosSetup -or $ServerChaosVerify -or $ServerChaosTeardown)) {
    throw "Server chaos switches are valid only for -Db Chaos."
}
if ($selected.Count -eq 0) {
    throw "No allowlisted SQL action was selected."
}
```

- [ ] **Step 7: Add validation-only tests through PowerShell**

Run without executing SQL:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-VerificationDb.ps1 -Db Bench
```

Expected: fails with `No allowlisted SQL action was selected.`

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-VerificationDb.ps1 -Db Stress -VerifyFinal
```

Expected: fails with `Memory-optimized BENCH switches are valid only for -Db Bench.`

- [ ] **Step 8: Commit**

```powershell
git add Verification\scripts\Invoke-VerificationDb.ps1 Verification\databases\*\run.ps1
git commit -m "feat: add guarded verification database runner"
```

---

## Task 4: Move Verification Projects And Update Solution References

**Files:**
- Move: `Tests/Lib.Db.IntegrationTests/**` -> `Verification/projects/Lib.Db.IntegrationTests/**`
- Move: `Tests/Lib.Db.AotVerification/**` -> `Verification/projects/Lib.Db.AotVerification/**`
- Move: `Benchmarks/Lib.Db.Benchmarks/**` -> `Verification/projects/Lib.Db.Benchmarks/**`
- Move: `Tools/Lib.Db.AotSmoke/**` -> `Verification/projects/Lib.Db.AotSmoke/**`
- Move: `Tools/Lib.Db.ChaosHarness/**` -> `Verification/projects/Lib.Db.ChaosHarness/**`
- Modify: `Lib.Db.slnx`
- Modify: moved `*.csproj`

- [ ] **Step 1: Move project directories**

Run:

```powershell
git mv Tests\Lib.Db.IntegrationTests Verification\projects\Lib.Db.IntegrationTests
git mv Tests\Lib.Db.AotVerification Verification\projects\Lib.Db.AotVerification
git mv Benchmarks\Lib.Db.Benchmarks Verification\projects\Lib.Db.Benchmarks
git mv Tools\Lib.Db.AotSmoke Verification\projects\Lib.Db.AotSmoke
git mv Tools\Lib.Db.ChaosHarness Verification\projects\Lib.Db.ChaosHarness
```

- [ ] **Step 2: Remove stale moved SQL folder from integration project**

After Task 2, the moved integration project may still contain an empty `sql` directory. Keep no canonical SQL under `Verification/projects/Lib.Db.IntegrationTests/sql`.

Run:

```powershell
Get-ChildItem Verification\projects\Lib.Db.IntegrationTests\sql -ErrorAction SilentlyContinue
```

Expected: no source SQL files remain there. Delete the empty folder if it exists.

- [ ] **Step 3: Update project references**

Update these references:

```xml
<ProjectReference Include="..\..\Lib.Db\Lib.Db.csproj" />
```

to:

```xml
<ProjectReference Include="..\..\..\Lib.Db\Lib.Db.csproj" />
```

Affected files:

- `Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj`
- `Verification/projects/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj`
- `Verification/projects/Lib.Db.Benchmarks/Lib.Db.Benchmarks.csproj`
- `Verification/projects/Lib.Db.AotSmoke/Lib.Db.AotSmoke.csproj`

- [ ] **Step 4: Update SQL includes in integration test project**

Replace the old `sql\*.sql` copy item:

```xml
<None Include="sql\*.sql">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

with:

```xml
<None Include="..\..\databases\**\*.sql" Link="sql\%(RecursiveDir)%(Filename)%(Extension)">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 5: Update benchmark SQL link**

In `Verification/projects/Lib.Db.Benchmarks/Lib.Db.Benchmarks.csproj`, replace:

```xml
<None Include="..\..\Tests\Lib.Db.IntegrationTests\sql\setup-libdb-bench-test.sql" Link="sql\setup-libdb-bench-test.sql" CopyToOutputDirectory="PreserveNewest" />
```

with:

```xml
<None Include="..\..\databases\LIBDB_BENCH_TEST\sql\setup-libdb-bench-test.sql" Link="sql\setup-libdb-bench-test.sql" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 6: Update `Lib.Db.slnx`**

Replace the solution body with:

```xml
<Solution>
  <Folder Name="/Verification/">
    <Project Path="Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj" />
    <Project Path="Verification/projects/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj" />
    <Project Path="Verification/projects/Lib.Db.Benchmarks/Lib.Db.Benchmarks.csproj" />
    <Project Path="Verification/projects/Lib.Db.AotSmoke/Lib.Db.AotSmoke.csproj" />
    <Project Path="Verification/projects/Lib.Db.ChaosHarness/Lib.Db.ChaosHarness.csproj" />
  </Folder>
  <Project Path="Lib.Db/Lib.Db.csproj" />
</Solution>
```

- [ ] **Step 7: Verify build paths**

Run:

```powershell
dotnet build .\Lib.Db.slnx --no-restore -v:minimal
```

Expected: build exits 0.

- [ ] **Step 8: Commit**

```powershell
git add Lib.Db.slnx Verification\projects Tests Benchmarks Tools
git commit -m "chore: move verification projects under verification root"
```

---

## Task 5: Update Test SQL Resolution And BENCH Default/Final Behavior

**Files:**
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Infrastructure/SqlScriptRunner.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Infrastructure/MultiDbFixture.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/V230Matrix/V230TvpMatrixTests.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/V230Matrix/MemoryOptimizedTvpOptInTests.cs`
- Modify or remove: any tests that still require `verify-libdb-all.sql`

- [ ] **Step 1: Update `SqlScriptRunner.ResolveScriptPath`**

Replace the old `Tests/Lib.Db.IntegrationTests/sql` search with DB-root search:

```csharp
public static string ResolveScriptPath(string scriptFileName)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(scriptFileName);

    string direct = Path.Combine(AppContext.BaseDirectory, "sql", scriptFileName);
    if (File.Exists(direct))
        return direct;

    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
        string verificationRoot = Path.Combine(current.FullName, "Verification", "databases");
        if (Directory.Exists(verificationRoot))
        {
            string? match = Directory
                .EnumerateFiles(verificationRoot, scriptFileName, SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (match is not null)
                return match;
        }

        current = current.Parent;
    }

    throw new FileNotFoundException($"SQL script '{scriptFileName}' was not found.", scriptFileName);
}
```

- [ ] **Step 2: Update `MultiDbFixture` default BENCH verifier**

Change:

```csharp
new(TestConnectionStrings.Benchmark, "setup-libdb-bench-test.sql", "verify-libdb-bench-test.sql")
```

to:

```csharp
new(TestConnectionStrings.Benchmark, "setup-libdb-bench-test.sql", "verify-libdb-bench-default.sql")
```

- [ ] **Step 3: Replace `verify-libdb-all.sql` dependency in tests**

In `V230TvpMatrixTests.DefaultAllVerificationScript_ShouldNotRunServerLevelChaos`, replace the `verify-libdb-all.sql` read with reads of default SQL files:

```csharp
string[] defaultScripts =
[
    "verify-libdb-verification-test.sql",
    "verify-libdb-stress-test.sql",
    "verify-libdb-chaos-test.sql",
    "verify-libdb-bench-default.sql",
    "verify-libdb-sqlserver2025-syntax.sql"
];

foreach (string scriptFileName in defaultScripts)
{
    string scriptPath = SqlScriptRunner.ResolveScriptPath(scriptFileName);
    string script = File.ReadAllText(scriptPath);

    script.Contains("chaos-server-optin", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    script.Contains("KILL ", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    script.Contains("ALTER SERVER", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
}
```

- [ ] **Step 4: Update memory-optimized opt-in test**

In `MemoryOptimizedTvpOptInTests.DefaultAllVerificationScript_ShouldNotRunMemoryOptimizedTvpSetup`, replace the `verify-libdb-all.sql` read with `verify-libdb-bench-default.sql`:

```csharp
string scriptPath = SqlScriptRunner.ResolveScriptPath("verify-libdb-bench-default.sql");
string script = File.ReadAllText(scriptPath);

script.Contains("verify-libdb-bench-test.sql", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
script.Contains("setup-libdb-bench-memory-optimized-tvp-optin.sql", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
script.Contains("run-libdb-bench-memory-optimized-tvp-optin.sql", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
script.Contains("MEMORY_OPTIMIZED_DATA", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
script.Contains("libdb_bench_MemoryOptimizedOrderItem", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
```

- [ ] **Step 5: Run focused tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~V230Matrix|FullyQualifiedName~VerificationSqlScriptTests" -v:minimal
```

Expected: test exits 0 when local verification DB env vars are configured. If env vars are absent, report the missing key names only.

- [ ] **Step 6: Commit**

```powershell
git add Verification\projects\Lib.Db.IntegrationTests
git commit -m "test: align verification sql resolution with new root"
```

---

## Task 6: Move Coverage, Benchmark, AOT, And Full Verification Scripts

**Files:**
- Create: `Verification/scripts/Invoke-Verification.ps1`
- Create: `Verification/scripts/Invoke-Coverage.ps1`
- Create: `Verification/scripts/Assert-LibDbCoverage.ps1`
- Create: `Verification/scripts/Invoke-Benchmarks.ps1`
- Create: `Verification/scripts/Invoke-Aot.ps1`
- Modify: moved benchmark scanner path usage

- [ ] **Step 1: Create `Verification/scripts/Invoke-Coverage.ps1`**

Base it on old `Tools/coverage/Invoke-LibDbCoverage.ps1`, but set these defaults:

```powershell
[string] $ResultsDirectory = 'Verification\artifacts\coverage\raw'
[string] $ReportDirectory = 'Verification\artifacts\coverage\report'
```

Set paths:

```powershell
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$testProject = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj'
$runSettings = Join-Path $repoRoot 'Verification\projects\Lib.Db.IntegrationTests\coverlet.runsettings'
$assertScript = Join-Path $repoRoot 'Verification\scripts\Assert-LibDbCoverage.ps1'
```

- [ ] **Step 2: Move coverage gate**

Move old `Tools/coverage/Assert-LibDbCoverage.ps1` to `Verification/scripts/Assert-LibDbCoverage.ps1` without changing coverage target semantics.

- [ ] **Step 3: Create `Verification/scripts/Invoke-Benchmarks.ps1`**

Base it on old `Tools/benchmark/Invoke-LibDbBenchmarks.ps1`, but set:

```powershell
$project = Join-Path $repoRoot 'Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj'
$artifactRoot = Join-Path $repoRoot 'Verification\artifacts\benchmarks'
$scanner = Join-Path $repoRoot 'Verification\scripts\Scan-VerificationArtifacts.ps1'
```

Keep:

```powershell
Write-Host "LIBDB_BENCHMARK_CONNECTION present: $present"
```

Do not print the value.

- [ ] **Step 4: Create `Verification/scripts/Invoke-Aot.ps1`**

Use this publish command:

```powershell
dotnet publish .\Verification\projects\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishAot=true `
  -p:TreatWarningsAsErrors=true `
  -v:minimal
```

Capture output to `Verification/artifacts/aot/aot-publish.log` through a redaction helper before preserving it.

- [ ] **Step 5: Create `Verification/scripts/Invoke-Verification.ps1`**

The full orchestrator should run:

```powershell
dotnet build .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore -v:minimal
dotnet build .\Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj --no-restore -v:minimal
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Lib.Db.IntegrationTests.V230Matrix.V230TvpMatrixTests" -v:minimal --results-directory .\Verification\artifacts\test-results
pwsh -NoProfile -File .\Verification\scripts\Invoke-Coverage.ps1
pwsh -NoProfile -File .\Verification\scripts\Invoke-Aot.ps1
pwsh -NoProfile -File .\Verification\scripts\Invoke-Benchmarks.ps1 -Job Dry -Filter "*TvpBenchmarks*"
pwsh -NoProfile -File .\Verification\scripts\Scan-VerificationArtifacts.ps1 -Paths .\Verification\artifacts
pwsh -NoProfile -File .\Verification\scripts\Assert-GeneratedArtifactsUntracked.ps1
```

Expose switches:

```powershell
param(
    [ValidateSet('Full', 'Fast')]
    [string] $Mode = 'Full',
    [switch] $SkipCoverage,
    [switch] $SkipBenchmark,
    [switch] $SkipMatrixDbTests,
    [switch] $SkipAot,
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $BenchmarkJob = 'Dry'
)
```

- [ ] **Step 6: Commit**

```powershell
git add Verification\scripts
git commit -m "feat: add verification orchestration scripts"
```

---

## Task 7: Add Artifact Secret Scan And Generated Artifact Gate

**Files:**
- Create: `Verification/scripts/Scan-VerificationArtifacts.ps1`
- Create: `Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1`
- Modify: `.gitignore`

- [ ] **Step 1: Create redacting artifact scanner**

Use this pattern table and output only path plus rule id:

```powershell
$rules = @(
    @{ Id = 'connection-string-key'; Pattern = '(?i)\b(connection\s*string|connectionstrings)\b\s*[:=]' },
    @{ Id = 'sql-credential-fragment'; Pattern = '(?i)\b(server|data\s+source|initial\s+catalog|database|user\s+id|uid|password|pwd)\s*=' },
    @{ Id = 'token-key'; Pattern = '(?i)\b(access[_-]?token|refresh[_-]?token|api[_-]?key|secret)\b\s*[:=]' }
)
$textExtensions = @('.txt', '.md', '.csv', '.json', '.xml', '.html', '.log', '.out', '.err', '.config', '.props', '.targets', '.trx')
```

When a match is found, print:

```powershell
Write-Output "SecretPatternHit Path=$file Rule=$($rule.Id)"
```

Do not print the matched line or value.

- [ ] **Step 2: Create tracked-file gate**

`Assert-GeneratedArtifactsUntracked.ps1` should run:

```powershell
$tracked = git ls-files -- `
  'Verification/artifacts/**' `
  'TestResults/**' `
  'BenchmarkDotNet.Artifacts/**'
if ($tracked) {
    $tracked
    throw 'Generated verification artifacts are tracked by Git.'
}
```

- [ ] **Step 3: Update `.gitignore`**

Add:

```gitignore
## Verification artifacts
Verification/artifacts/
BenchmarkDotNet.Artifacts/
TestResults/
```

Keep existing ignores for `bin/`, `obj/`, and package artifacts.

- [ ] **Step 4: Verify gates**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Scan-VerificationArtifacts.ps1 -Paths .\Verification\artifacts
pwsh -NoProfile -File .\Verification\scripts\Assert-GeneratedArtifactsUntracked.ps1
```

Expected: scanner prints no secret-pattern hits; tracked-file gate exits 0.

- [ ] **Step 5: Commit**

```powershell
git add Verification\scripts\Scan-VerificationArtifacts.ps1 Verification\scripts\Assert-GeneratedArtifactsUntracked.ps1 .gitignore
git commit -m "chore: guard generated verification artifacts"
```

---

## Task 8: Convert Legacy Tools To Thin Shims

**Files:**
- Modify: `Tools/verification/Invoke-LibDbV230Verification.ps1`
- Modify: `Tools/coverage/Invoke-LibDbCoverage.ps1`
- Modify: `Tools/coverage/Assert-LibDbCoverage.ps1`
- Modify: `Tools/benchmark/Invoke-LibDbBenchmarks.ps1`
- Modify: `Verification/projects/Lib.Db.Benchmarks/ScanBenchmarkArtifacts.ps1` if retained at old linked path

- [ ] **Step 1: Replace verification wrapper**

`Tools/verification/Invoke-LibDbV230Verification.ps1` should be:

```powershell
param(
    [switch] $SkipCoverage,
    [switch] $SkipBenchmark,
    [switch] $SkipMatrixDbTests,
    [switch] $SkipAot,
    [ValidateSet('Dry', 'Short', 'Default')]
    [string] $BenchmarkJob = 'Dry'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$target = Join-Path $repoRoot 'Verification\scripts\Invoke-Verification.ps1'
Write-Host 'Compatibility shim: use Verification\scripts\Invoke-Verification.ps1.'
& pwsh -NoProfile -File $target -SkipCoverage:$SkipCoverage -SkipBenchmark:$SkipBenchmark -SkipMatrixDbTests:$SkipMatrixDbTests -SkipAot:$SkipAot -BenchmarkJob $BenchmarkJob
exit $LASTEXITCODE
```

- [ ] **Step 2: Replace coverage wrapper**

`Tools/coverage/Invoke-LibDbCoverage.ps1` should only forward to `Verification/scripts/Invoke-Coverage.ps1` with the same public switches.

- [ ] **Step 3: Replace coverage assertion wrapper**

`Tools/coverage/Assert-LibDbCoverage.ps1` should only forward `-CoberturaPath` to `Verification/scripts/Assert-LibDbCoverage.ps1`.

- [ ] **Step 4: Replace benchmark wrapper**

`Tools/benchmark/Invoke-LibDbBenchmarks.ps1` should only forward to `Verification/scripts/Invoke-Benchmarks.ps1` with `-SetupMode`, `-Job`, `-Filter`, `-SkipSetup`, `-SkipRun`, and `-SkipSecretScan`.

- [ ] **Step 5: Verify shim content contains no forbidden logic**

Run:

```powershell
rg -n "sqlcmd|LIBDB_TEST_CONNECTION|LIBDB_BENCHMARK_CONNECTION|BenchmarkDotNet.Artifacts|TestResults|ScanBenchmarkArtifacts" Tools
```

Expected: either no matches or only migration-note text. No shim should contain independent SQL execution, DB selection, credential handling, or artifact scan logic.

- [ ] **Step 6: Commit**

```powershell
git add Tools Verification\projects\Lib.Db.Benchmarks\ScanBenchmarkArtifacts.ps1
git commit -m "chore: replace legacy verification tools with shims"
```

---

## Task 9: Update Active Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/v2.3.0-verification.md`
- Modify: `docs/04_operations.md`

- [ ] **Step 1: Replace active command paths**

Replace active references:

```text
Tests\Lib.Db.IntegrationTests
Tests\Lib.Db.AotVerification
Benchmarks\Lib.Db.Benchmarks
Tools\verification
Tools\coverage
Tools\benchmark
BenchmarkDotNet.Artifacts
TestResults
```

with:

```text
Verification\projects\Lib.Db.IntegrationTests
Verification\projects\Lib.Db.AotVerification
Verification\projects\Lib.Db.Benchmarks
Verification\scripts\Invoke-Verification.ps1
Verification\scripts\Invoke-Coverage.ps1
Verification\scripts\Invoke-Benchmarks.ps1
Verification\artifacts\benchmarks
Verification\artifacts\test-results or Verification\artifacts\coverage
```

- [ ] **Step 2: Document BENCH split**

Add this text to `docs/v2.3.0-verification.md`:

```markdown
`LIBDB_BENCH_TEST` has two verification modes. Default BENCH verification uses `verify-libdb-bench-default.sql` and does not require memory-optimized TVP objects. Final BENCH verification requires explicit memory-optimized TVP opt-in and then runs `verify-libdb-bench-test.sql`.
```

- [ ] **Step 3: Document secret handling**

Add this text:

```markdown
Do not pass SQL passwords or connection strings on the command line. Use environment variable key names documented by `Verification/README.md` or an interactive secure prompt. Logs may show key presence only.
```

- [ ] **Step 4: Verify active docs**

Run:

```powershell
rg -n "Tools\\verification|Tools\\coverage|Tools\\benchmark|Benchmarks\\Lib.Db.Benchmarks|Tests\\Lib.Db.IntegrationTests|BenchmarkDotNet.Artifacts|TestResults" README.md docs\v2.3.0-verification.md docs\04_operations.md
```

Expected: no active command examples point users to old roots. Historical docs under `docs/superpowers/plans` and old specs may retain old paths.

- [ ] **Step 5: Commit**

```powershell
git add README.md docs\v2.3.0-verification.md docs\04_operations.md
git commit -m "docs: point active verification docs to verification root"
```

---

## Task 10: Final Verification Gate

**Files:**
- Read-only verification over the consolidated tree.

- [ ] **Step 1: Verify source inventory**

Run:

```powershell
rg --files Verification
```

Expected includes:

```text
Verification/README.md
Verification/manifest.json
Verification/scripts/Invoke-Verification.ps1
Verification/scripts/Invoke-VerificationDb.ps1
Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj
Verification/projects/Lib.Db.Benchmarks/Lib.Db.Benchmarks.csproj
Verification/databases/LIBDB_BENCH_TEST/sql/verify-libdb-bench-default.sql
```

- [ ] **Step 2: Verify no canonical duplicates**

Run:

```powershell
rg --files Tests Benchmarks Tools | rg "(Lib\.Db\.IntegrationTests\.csproj|Lib\.Db\.Benchmarks\.csproj|Lib\.Db\.AotVerification\.csproj|setup-libdb-|verify-libdb-|run-libdb-)"
```

Expected: no canonical project or SQL files remain outside `Verification/`. Thin shims under `Tools/` may remain.

- [ ] **Step 3: Build solution**

Run:

```powershell
dotnet build .\Lib.Db.slnx --no-restore -v:minimal
```

Expected: build exits 0.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~V230Matrix|FullyQualifiedName~VerificationSqlScriptTests|FullyQualifiedName~MemoryOptimizedTvpOptInTests" -v:minimal --results-directory .\Verification\artifacts\test-results
```

Expected: tests exit 0 when local DB env vars are configured. If DB env vars are absent, report missing key names only.

- [ ] **Step 5: Run coverage gate**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Coverage.ps1 -SkipReport
```

Expected: coverage collection and gate complete, or missing DB env vars are reported by key name only.

- [ ] **Step 6: Run AOT publish**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Aot.ps1
```

Expected: AOT publish exits 0. Lib.Db-owned AOT warnings remain blockers.

- [ ] **Step 7: Run benchmark dry gate**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Benchmarks.ps1 -Job Dry -Filter "*TvpBenchmarks*"
```

Expected: BenchmarkDotNet produces results under `Verification/artifacts/benchmarks` or reports missing benchmark connection key name only.

- [ ] **Step 8: Run artifact gates**

Run:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Scan-VerificationArtifacts.ps1 -Paths .\Verification\artifacts
pwsh -NoProfile -File .\Verification\scripts\Assert-GeneratedArtifactsUntracked.ps1
```

Expected: no secret-pattern hits, no generated artifacts tracked.

- [ ] **Step 9: Commit final doc or verification adjustments**

If verification required small path-only adjustments, commit them:

```powershell
git add Verification Lib.Db.slnx README.md docs .gitignore Tools
git commit -m "chore: finalize verification root consolidation"
```

If no adjustments were needed, do not create an empty commit.

---

## Self-Review Checklist

- Spec coverage:
  - Canonical `Verification/` root: Task 1.
  - No version-named directory: Task 1 verification.
  - SQL grouped by DB: Task 2.
  - Hard-coded direct SQL allowlist: Task 3.
  - BENCH default/final split: Tasks 2, 3, and 5.
  - Project move and solution update: Task 4.
  - Artifact output consolidation: Tasks 6 and 7.
  - Thin compatibility shims: Task 8.
  - Active docs update: Task 9.
  - Final verification gate: Task 10.
- Placeholder scan target:
  - The plan must not contain deferred-work markers or cross-task shorthand.
- Type and path consistency:
  - Every project path uses `Verification/projects/*`.
  - Every SQL path uses `Verification/databases/*`.
  - Every artifact path uses `Verification/artifacts/*`.
- Security consistency:
  - `manifest.json` is advisory only.
  - Direct SQL runner has no caller-supplied SQL path.
  - Secrets are not passed as command-line values.
  - Artifact scanner reports path and rule id only.
