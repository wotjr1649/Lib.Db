---
name: lib-db
description: Use when modifying, reviewing, documenting, or testing Lib.Db v2.2.1 runtime code, Lib.Db.TvpGen source generator code, SQL Server binding, result mapping, TVP, raw SQL policy, connection security, or verification DB workflows in this repository.
allowed-tools:
  - Read
  - Grep
  - Glob
paths:
  - "Lib.Db/**/*.cs"
  - "Lib.Db/**/*.csproj"
  - "Lib.Db/**/*.md"
  - "Tests/**/*.cs"
  - "docs/**/*.md"
---

# Lib.Db v2.2.1 Skill

## Purpose

Use this skill for project-specific Lib.Db work. Lib.Db is a SQL Server focused data access library with fluent execution APIs, `DbResult<T>`, TVP/source generation, result mapping, diagnostics, and security-oriented execution guardrails.

Keep this file as the entry point. Load only the reference file needed for the current task.

## First Steps

1. Identify the touched area: runtime, source generator, tests, docs, or operational guidance.
2. Read the relevant reference file from this skill before editing.
3. Follow repository-level instructions for Git, SQL execution, secrets, encoding, and verification.
4. Keep changes scoped to the current task and avoid unrelated refactors.

## Reference Map

- For connection security, raw SQL policy, SQL execution limits, and secret handling, read [references/security-guardrails.md](references/security-guardrails.md).
- For runtime DI, fluent API, options, transactions, resilience, observability, and caching, read [references/runtime-api.md](references/runtime-api.md).
- For result mapping, parameter binding, `DateOnly`/`TimeOnly`, and `[DbResult]` reader compatibility, read [references/mapping-contracts.md](references/mapping-contracts.md).
- For `Lib.Db.TvpGen`, `[TvpRow]`, `[DbResult]`, and generated code expectations, read [references/tvpgen-guide.md](references/tvpgen-guide.md).
- For safe code patterns and small templates, read [references/examples.md](references/examples.md).
- For builds, tests, verification DB, and documentation checks, read [references/verification.md](references/verification.md).

## Non-Negotiable Rules

- Do not print secret values, tokens, passwords, or full connection strings. Report only key names and whether a value exists.
- Do not run direct SQL CLI DDL/DML automatically. Stored procedure or DDL setup through application/test code is allowed only when the user or repository workflow permits it.
- Prefer stored procedures for write operations and security boundaries.
- Treat `RawSqlPolicy.DenyWriteText` as a guardrail, not as a SQL parser or complete security boundary.
- For production-oriented examples, use `ConnectionSecurityProfile.Production` or `UseProductionSecurityDefaults()`.
- Do not recommend high-privilege SQL logins, certificate validation bypasses, or inline passwords as defaults.
- Do not treat `allowed-tools` as a security boundary; repository instructions and runtime permissions still govern side effects.
- Use `EnableObservability`; do not introduce new `EnableOpenTelemetry` usage except when documenting backward compatibility.
- Preserve public XML documentation quality for public APIs.

## v2.2.1 Invariants

- Default result mapping resolves exact case-insensitive names first, then underscore-insensitive normalized names such as `CELL_NO` to `CellNo`.
- Normalized-name collisions must not silently bind ambiguous properties.
- Generated `[DbResult]` mappers must expose `Map(DbDataReader)`; `Map(SqlDataReader)` is a compatibility shim.
- Runtime generated result mapping must work with `MonitoredSqlDataReader : DbDataReader`.
- Raw `DateOnly` parameters bind as SQL `date`; raw `TimeOnly` parameters bind as SQL `time`.
- SQL Server computed-column index setup that needs it must run with `SET QUOTED_IDENTIFIER ON`.

## Workflows

### Runtime Change

1. Read [references/runtime-api.md](references/runtime-api.md) and [references/security-guardrails.md](references/security-guardrails.md).
2. Locate existing tests for the touched behavior.
3. Add or update focused tests when behavior changes.
4. Run targeted tests first, then broader build/test checks when feasible.

### Mapping or Binding Change

1. Read [references/mapping-contracts.md](references/mapping-contracts.md).
2. Cover both mock reader and real SQL Server paths when the behavior depends on provider behavior.
3. Include collision, null, and wrapper-reader cases where relevant.

### Source Generator Change

1. Read [references/tvpgen-guide.md](references/tvpgen-guide.md).
2. Verify generated source text and runtime interoperability.
3. Keep generated contracts backward compatible unless a breaking change is intentional and documented.

### Documentation Change

1. Keep examples security-safe and v2.2.1-current.
2. Avoid full connection string values.
3. Cross-link to the appropriate reference file instead of duplicating long guidance.
4. Run the static checks in [references/verification.md](references/verification.md).

## Completion Criteria

- The relevant reference files were consulted.
- Security-sensitive examples avoid high-privilege logins, certificate bypass defaults, and inline secrets.
- New or changed behavior is covered by focused verification, or the proof gap is explicitly stated.
- `SKILL.md` remains concise; detailed API or example material belongs in `references/`.
- For skill package validation, read [tests/README.md](tests/README.md).
