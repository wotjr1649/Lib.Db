---
name: lib-db
description: Use when using the Lib.Db NuGet package in application code, especially for dependency injection, SQL Server connection security, stored procedure execution, raw SQL policy, result mapping, DbResult handling, TVP rows, source-generated mappers, or production-safe examples.
allowed-tools:
  - Read
  - Grep
  - Glob
---

# Lib.Db Skill

## Purpose

Use this skill for application code that consumes the Lib.Db NuGet package.

Lib.Db is a SQL Server focused data access library with fluent execution APIs, `DbResult<T>`, TVP/source generation, result mapping, diagnostics, and security-oriented execution guardrails.

This skill is for Lib.Db package consumption, not package-source maintenance. Assume the user has the Lib.Db package, application code, and this skill package. Do not require access to Lib.Db source repository internals.

Keep this file as the entry point. Load only the reference file needed for the current task.

## First Steps

1. Identify the application task: dependency injection, connection security, stored procedures, raw SQL policy, result mapping, TVP/source generation, examples, or documentation.
2. Read the relevant reference file from this skill before proposing or editing code.
3. Follow user and repository instructions for secrets, SQL execution, encoding, and local checks.
4. Keep changes scoped to application usage of Lib.Db public APIs.

## Reference Map

- For connection security, raw SQL policy, secret handling, and production-safe defaults, read [references/security-guardrails.md](references/security-guardrails.md).
- For dependency injection, fluent execution, options, transactions, observability, and caching, read [references/runtime-api.md](references/runtime-api.md).
- For result mapping, parameter binding, `DateOnly`/`TimeOnly`, and generated result mapper compatibility, read [references/mapping-contracts.md](references/mapping-contracts.md).
- For `[TvpRow]`, `[DbResult]`, TVP rows, and source-generated mapping, read [references/tvpgen-guide.md](references/tvpgen-guide.md).
- For safe application examples and small templates, read [references/examples.md](references/examples.md).

## Non-Negotiable Rules

- Do not print secret values, tokens, passwords, or full connection strings. Report only key names and whether a value exists.
- Do not recommend high-privilege SQL logins, certificate validation bypasses, or inline passwords as defaults.
- Prefer stored procedures for write operations, administrative operations, tenant-sensitive data access, and SQL Server permission boundaries.
- Treat raw SQL policy as a guardrail, not as a SQL parser or complete security boundary.
- For production-oriented examples, use production security defaults or explicit production-safe options.
- Do not treat tool allowlists, examples, or application guardrails as security boundaries by themselves.
- Use observability APIs intended for current public use; mention older naming only when maintaining existing application code.
- Preserve public XML documentation quality when generating public application APIs or shared wrappers.

## Stable Public Contracts

- Default result mapping resolves exact case-insensitive names first, then underscore-insensitive normalized names such as `CELL_NO` to `CellNo`.
- Normalized-name collisions must not silently bind ambiguous properties.
- Generated `[DbResult]` mappers should operate through `DbDataReader`; concrete SQL reader types are compatibility details.
- Diagnostic reader wrappers must be treated as normal `DbDataReader` implementations.
- Raw `DateOnly` parameters bind as SQL `date`; raw `TimeOnly` parameters bind as SQL `time`.
- SQL Server setup that depends on computed-column indexes must use `SET QUOTED_IDENTIFIER ON`.

## Consumer Workflows

### Runtime Usage

1. Read [references/runtime-api.md](references/runtime-api.md) and [references/security-guardrails.md](references/security-guardrails.md).
2. Prefer application configuration and dependency injection over inline setup.
3. Keep SQL command shape explicit: stored procedure or intentional policy-covered text SQL.
4. Handle `DbResult<T>` success, failure, missing-row, and cancellation cases deliberately.

### Mapping or Binding Usage

1. Read [references/mapping-contracts.md](references/mapping-contracts.md).
2. Design DTOs with clear property names and nullable annotations.
3. Use SQL aliases when database column names do not clearly map to DTO names.
4. Avoid ambiguous normalized column names.

### TVP or Source Generator Usage

1. Read [references/tvpgen-guide.md](references/tvpgen-guide.md).
2. Keep CLR row types aligned with SQL Server user-defined table types.
3. Keep generated mapper contracts based on `DbDataReader`.
4. Treat schema mismatch, unsupported CLR types, and nullability mismatch as consumer integration issues to fix explicitly.

### Documentation or Example Usage

1. Keep examples production-safe by default.
2. Avoid full connection string values.
3. Avoid repository-internal paths, package-source maintenance commands, release checks, or package-source project commands.
4. Cross-link to the relevant reference file instead of duplicating long guidance.

## Completion Criteria

- The relevant reference files were consulted.
- Generated code or examples use Lib.Db public APIs from the consumer application perspective.
- Security-sensitive examples avoid high-privilege logins, certificate bypass defaults, and inline secrets.
- Raw SQL is either avoided or explicitly intentional and covered by policy guidance.
- Result mapping and TVP usage preserve the public contracts described above.
- Any proof gap is stated plainly without inventing repository-internal verification steps.
