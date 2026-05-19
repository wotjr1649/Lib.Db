# Lib.Db v2.3.0 Full TVP DB Matrix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand `LIBDB_STRESS_TEST`, `LIBDB_CHAOS_TEST`, and `LIBDB_BENCH_TEST` SQL setup/verify files into a full TVP coverage matrix for v2.3.0 runtime, fast-path, chaos, and benchmark validation.

**Architecture:** Keep the four-DB split. Add focused schemas, TVP types, target tables, procedures, and verify expected-object checks to the three auxiliary DB scripts only. Preserve direct SQL safety by writing files only; execution of DDL/DML scripts remains a separate approved action.

**Tech Stack:** SQL Server T-SQL, SQL Server TVP/user-defined table types, Microsoft.Data.SqlClient runtime TVP paths, Query Store, Extended Events, BenchmarkDotNet SQL fixtures.

---

### Task 1: Expand Stress DB Matrix

**Files:**
- Modify: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-stress-test.sql`
- Modify: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-stress-test.sql`

- [ ] Add 13 stress tables so the file creates 18 tables total.
- [ ] Add 9 TVP types so the file creates 10 TVP types total.
- [ ] Add 28 procedures so the file creates 36 procedures total.
- [ ] Update verify expected tables/types/procedures to match setup exactly.
- [ ] Keep smoke checks for seed/read/write/TVP insert and Query Store.

### Task 2: Expand Chaos DB Matrix

**Files:**
- Modify: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-chaos-test.sql`
- Modify: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-chaos-test.sql`

- [ ] Add 12 chaos tables so the file creates 17 tables total.
- [ ] Add 8 TVP types so the file creates 8 TVP types total.
- [ ] Add 25 procedures so the file creates 34 procedures total.
- [ ] Update verify expected tables/types/procedures to match setup exactly.
- [ ] Keep smoke checks for expected error, rollback, app lock, deadlock seed, and Query Store.

### Task 3: Expand Benchmark DB Matrix

**Files:**
- Modify: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-bench-test.sql`
- Modify: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-bench-test.sql`

- [ ] Add 17 benchmark tables so the file creates 20 tables total.
- [ ] Add 12 TVP types so the file creates 14 TVP types total.
- [ ] Add 40 procedures so the file creates 42 procedures total.
- [ ] Update verify expected tables/types/procedures to match setup exactly.
- [ ] Keep smoke checks for narrow and wide benchmark insert and Query Store.

### Task 4: Static Verification

**Files:**
- Verify: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-stress-test.sql`
- Verify: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-chaos-test.sql`
- Verify: `Tests/Lib.Db.IntegrationTests/sql/setup-libdb-bench-test.sql`
- Verify: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-stress-test.sql`
- Verify: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-chaos-test.sql`
- Verify: `Tests/Lib.Db.IntegrationTests/sql/verify-libdb-bench-test.sql`

- [ ] Run static object-count verification for the six target SQL files.
- [ ] Run setup-vs-verify expected object coverage checks.
- [ ] Run UTF-8/no-BOM/no-CRLF/no-secret-literal checks.
- [ ] Do not execute DDL/DML SQL against SQL Server in this task.
