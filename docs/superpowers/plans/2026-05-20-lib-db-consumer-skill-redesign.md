# Lib.Db Consumer Skill Redesign Implementation Plan

## Spec

Approved spec: `docs/superpowers/specs/2026-05-20-lib-db-consumer-skill-redesign.md`

## Objective

Rewrite `.claude/skills/lib-db` as a version-neutral, NuGet-consumer-facing skill for using Lib.Db in application code.

The final skill package must contain only:

```text
.claude/skills/lib-db/
  SKILL.md
  references/
    examples.md
    mapping-contracts.md
    runtime-api.md
    security-guardrails.md
    tvpgen-guide.md
```

The final skill package must not contain:

- `references/verification.md`
- `tests/`
- release version strings matching `v?\d+\.\d+\.\d+`
- repository-internal verification, release, benchmark, coverage, chaos, or AOT workflow guidance
- repository project files, manifests, test projects, or README files as required sources of truth

## File Responsibilities

- `.claude/skills/lib-db/SKILL.md`: short entrypoint and router for Lib.Db NuGet consumer usage.
- `.claude/skills/lib-db/references/security-guardrails.md`: consumer-safe security, connection, raw SQL, logging, and secret-handling rules.
- `.claude/skills/lib-db/references/runtime-api.md`: public runtime usage: DI, options, fluent calls, results, transactions, observability, caching.
- `.claude/skills/lib-db/references/mapping-contracts.md`: consumer DTO mapping and parameter binding behavior.
- `.claude/skills/lib-db/references/tvpgen-guide.md`: consumer use of `[TvpRow]`, `[DbResult]`, and generated mapper contracts.
- `.claude/skills/lib-db/references/examples.md`: production-safe consumer examples only.

## Task 1: Remove Non-Consumer Skill Artifacts

**Files:**

- Delete: `.claude/skills/lib-db/references/verification.md`
- Delete: `.claude/skills/lib-db/tests/README.md`
- Delete: `.claude/skills/lib-db/tests/scenarios.md`
- Delete: `.claude/skills/lib-db/tests/validate-skill.ps1`

- [ ] **Step 1: Delete the out-of-scope files**

Use `apply_patch` with delete hunks for the four files above.

- [ ] **Step 2: Confirm the `tests/` directory is gone**

Run:

```powershell
Test-Path .claude/skills/lib-db/tests
```

Expected:

```text
False
```

- [ ] **Step 3: Confirm `verification.md` is gone**

Run:

```powershell
Test-Path .claude/skills/lib-db/references/verification.md
```

Expected:

```text
False
```

## Task 2: Rewrite `SKILL.md` As A Consumer-Facing Router

**Files:**

- Modify: `.claude/skills/lib-db/SKILL.md`

- [ ] **Step 1: Replace `SKILL.md` with version-neutral consumer skill content**

Use this content:

```markdown
---
name: lib-db
description: Use when using the Lib.Db NuGet package in application code, especially for dependency injection, SQL Server connection security, stored procedure execution, raw SQL policy, result mapping, DbResult handling, TVP rows, source-generated mappers, or production-safe examples.
allowed-tools:
  - Read
  - Grep
  - Glob
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.md"
  - "**/*.json"
  - "**/*.config"
---

# Lib.Db Skill

## Purpose

Use this skill for application code that consumes the Lib.Db NuGet package.

Lib.Db is a SQL Server focused data access library with fluent execution APIs, `DbResult<T>`, TVP/source generation, result mapping, diagnostics, and security-oriented execution guardrails.

This skill is not a Lib.Db repository development, release, verification, or test workflow. Assume the user has the Lib.Db package, application code, and this skill package. Do not require access to Lib.Db source repository internals.

Keep this file as the entry point. Load only the reference file needed for the current task.

## First Steps

1. Identify the application task: dependency injection, connection security, stored procedures, raw SQL policy, result mapping, TVP/source generation, examples, or documentation.
2. Read the relevant reference file from this skill before proposing or editing code.
3. Follow user and repository instructions for secrets, SQL execution, encoding, and verification.
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
3. Avoid repository-internal paths, verification commands, release gates, or test project commands.
4. Cross-link to the relevant reference file instead of duplicating long guidance.

## Completion Criteria

- The relevant reference files were consulted.
- Generated code or examples use Lib.Db public APIs from the consumer application perspective.
- Security-sensitive examples avoid high-privilege logins, certificate bypass defaults, and inline secrets.
- Raw SQL is either avoided or explicitly intentional and covered by policy guidance.
- Result mapping and TVP usage preserve the public contracts described above.
- Any proof gap is stated plainly without inventing repository-internal verification steps.
```

- [ ] **Step 2: Check that `SKILL.md` no longer references deleted files**

Run:

```powershell
Select-String -Path .claude/skills/lib-db/SKILL.md -Pattern 'verification|tests/'
```

Expected: no matches.

## Task 3: Rewrite Security Guardrails For Consumers

**Files:**

- Modify: `.claude/skills/lib-db/references/security-guardrails.md`

- [ ] **Step 1: Replace repository-specific security text with consumer safety guidance**

Use this content:

```markdown
# Security Guardrails

Use this file when Lib.Db application code touches SQL Server connection security, raw SQL policy, credentials, logging, or production examples.

## Threat Model

This skill influences agent-generated application code. The main risk is unsafe code generation:

- high-privilege database login examples copied into applications
- certificate validation bypasses copied into production settings
- raw SQL used where stored procedures or least-privilege permissions are expected
- secret values or full connection strings printed into chat, logs, docs, commits, or tests
- local development shortcuts treated as production security guarantees

Tool allowlists and application guardrails are not security boundaries by themselves. User instructions, repository permissions, runtime configuration, SQL Server permissions, code review, and deployment controls still matter.

## Secret and Connection String Handling

- Do not print secret values, passwords, tokens, or full connection strings.
- It is acceptable to name configuration keys and say whether a value exists.
- Prefer configuration providers, user secrets, environment variables, managed secret stores, or CI/CD secret injection over inline values.
- Redact logs and exception messages that could expose credentials or sensitive parameter values.

Safe reporting shape:

```text
Connection setting checked:
- Key: ConnectionStrings:Default
- Value present: yes
- Value printed: no
```

## Production Connection Security

For production-oriented code or docs:

- use production security defaults when configuring Lib.Db options
- use least-privilege SQL Server principals
- prefer integrated identity or managed credentials where available
- avoid high-privilege SQL logins in examples
- avoid certificate validation bypasses as defaults
- keep development-only shortcuts labeled as development-only
- route write and administrative operations through stored procedures when possible

Development-only shortcuts must not be shown as the default path.

## Raw SQL Policy

Prefer `.Procedure(...)` for:

- mutations
- administrative operations
- tenant-sensitive data access
- permission boundaries
- any operation governed through SQL Server permissions

Use `.Sql(...)` or `.SqlInterpolated(...)` only when text SQL is intentional and covered by policy.

Policy guidance:

- `RawSqlPolicy.Allow`: compatibility mode; not an operational safety posture
- `RawSqlPolicy.DenyWriteText`: transition guardrail for mutating or administrative text commands
- `RawSqlPolicy.DenyAllText`: strongest application-level guardrail when raw text SQL should not execute

Do not describe raw SQL policy as a complete SQL parser or standalone security boundary. Pair it with stored procedures, least-privilege SQL permissions, review, and focused tests in the consuming application.

## Direct SQL Execution

If the user asks to run SQL directly through command-line tools, follow the active user and repository rules for SQL execution. Do not run DDL, DML, backup/restore, or stored procedure execution through direct SQL CLI tools unless the user explicitly approves that exact activity.

Application code may call stored procedures or execute configured Lib.Db commands when the user task and local project rules permit it.

## Logging and Diagnostics

- Do not log full SQL parameter values when they may contain sensitive data.
- Prefer structured metadata: command type, instance name, elapsed time, row count, and error classification.
- Treat diagnostic wrappers as normal `DbDataReader` implementations.
- Keep exception logs useful without including credentials or full SQL connection details.

## Review Checklist

- Are secrets and full connection strings absent from generated code, logs, and docs?
- Are production examples free of high-privilege login defaults and certificate bypass defaults?
- Are writes and permission-boundary operations routed through stored procedures where practical?
- Is raw SQL intentional and covered by an explicit raw SQL policy?
- Does the text avoid claiming that application policy alone is a complete security boundary?
```

## Task 4: Rewrite Runtime API Reference For Public Package Usage

**Files:**

- Modify: `.claude/skills/lib-db/references/runtime-api.md`

- [ ] **Step 1: Replace repository-development framing with consumer runtime guidance**

Use this content:

```markdown
# Runtime API Reference

Use this file when application code consumes Lib.Db runtime APIs: dependency injection, fluent execution, options, transactions, resilience, observability, or caching.

## Package Boundary

Treat Lib.Db as a NuGet dependency. Use public APIs from application code and avoid relying on internal repository structure, internal test projects, or release tooling.

## Dependency Injection

Prefer registering Lib.Db through the application host and configuration system.

Typical shape:

```csharp
builder.Services.AddLibDb(builder.Configuration);
```

When custom options are needed, keep them explicit and production-safe:

```csharp
builder.Services.AddLibDb(builder.Configuration, options =>
{
    options.UseProductionSecurityDefaults();
    options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;
    options.EnableObservability = true;
});
```

Do not place full connection strings or passwords directly in source code examples.

## Fluent Execution Shape

Core stored procedure flow:

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

Stored procedure write flow:

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerCd })
    .ExecuteAsync(ct);
```

Intentional parameterized text SQL read:

```csharp
DbResult<long> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

Use raw SQL only when it is intentional and allowed by application policy.

## `DbResult<T>` Handling

Handle success and failure explicitly:

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);

if (!result.Success)
{
    logger.LogWarning("User lookup failed: {ErrorCode}", result.ErrorCode);
    return null;
}

return result.Value;
```

Do not assume every command returns a non-null value. Distinguish missing rows, null database values, failed commands, and cancellation according to the consuming application's behavior.

## Streaming Rows

Use streaming APIs when result sets can be large and the consuming code can process rows incrementally:

```csharp
await foreach (DbResult<OrderDto> row in db.Default
    .Procedure("dbo.usp_StreamOrders")
    .With(new { Status = status })
    .QueryStreamAsync<OrderDto>(ct))
{
    if (!row.Success)
    {
        logger.LogWarning("Order stream row failed: {ErrorCode}", row.ErrorCode);
        continue;
    }

    await processor.HandleAsync(row.Value, ct);
}
```

Keep cancellation tokens flowing through the entire call chain.

## Transactions

Use Lib.Db transaction APIs when multiple commands must share one SQL transaction. Keep transaction scope small and avoid long-running work inside the transaction.

Example shape:

```csharp
await db.Default.InTransactionAsync(async tx =>
{
    await tx.Procedure("dbo.usp_InsertOrder")
        .With(new { request.OrderNo, request.CustomerCd })
        .ExecuteAsync(ct);

    await tx.Procedure("dbo.usp_InsertOrderAudit")
        .With(new { request.OrderNo, Action = "Created" })
        .ExecuteAsync(ct);
}, ct);
```

## Observability

Enable observability through public options and application logging infrastructure. Prefer structured metadata over raw SQL text or parameter values.

Useful metadata:

- command type
- configured instance name
- elapsed time
- row count
- success or error classification

## Caching

Use caching only for reads where staleness is acceptable. Do not cache tenant-sensitive or permission-sensitive results unless the cache key includes the relevant tenant, user, permission, and query dimensions.

## Consumer Checklist

- Is Lib.Db configured through application DI/configuration?
- Are secrets externalized?
- Are writes routed through stored procedures?
- Is raw SQL policy explicit?
- Are `DbResult<T>` failures handled?
- Are cancellation tokens passed through?
- Are logs structured and redacted?
```

## Task 5: Rewrite Mapping And TVP References For Consumer Contracts

**Files:**

- Modify: `.claude/skills/lib-db/references/mapping-contracts.md`
- Modify: `.claude/skills/lib-db/references/tvpgen-guide.md`

- [ ] **Step 1: Replace `mapping-contracts.md` with consumer mapping guidance**

Use this content:

```markdown
# Mapping and Binding Contracts

Use this file when application code maps SQL Server result sets to DTOs, binds parameters, or uses generated result mappers.

## Result Column Name Resolution

Lib.Db result mapping should be treated as a public consumer contract:

1. Exact case-insensitive column/property names are preferred.
2. If no exact match exists, underscore-insensitive normalized names may match, such as `CELL_NO` to `CellNo`.
3. Normalized-name collisions must not silently bind ambiguous properties.

Prefer SQL aliases when database names are unclear:

```sql
SELECT
    CELL_NO AS CellNo,
    CUSTOMER_CD AS CustomerCd
FROM dbo.Customer;
```

DTO shape:

```csharp
public sealed class CustomerDto
{
    public string CellNo { get; init; } = "";
    public string CustomerCd { get; init; } = "";
}
```

## Generated Result Mapper Contract

Generated `[DbResult]` mappers should operate through `DbDataReader`.

Consumer guidance:

- Do not cast diagnostic or wrapped readers to concrete SQL reader types.
- Treat `DbDataReader` as the compatibility boundary.
- Keep generated and reflection-based mapping behavior aligned from the consumer perspective.

## DateOnly and TimeOnly Binding

Raw `DateOnly` values bind as SQL `date`.

Raw `TimeOnly` values bind as SQL `time`.

Example:

```csharp
await db.Default
    .Procedure("dbo.usp_SearchSchedule")
    .With(new
    {
        WorkDate = DateOnly.FromDateTime(dateTime),
        StartAt = TimeOnly.FromDateTime(dateTime)
    })
    .QueryAsync<ScheduleDto>(ct);
```

Use explicit SQL aliases and matching DTO property types when provider behavior matters.

## DTO Design Guidance

- Prefer DTOs with clear property names.
- Use nullable reference types to express database nullability.
- Avoid ambiguous names that differ only by underscores or casing.
- Prefer SQL aliases when mapping legacy database columns.
- Keep DTO constructors and init-only properties compatible with the mapper behavior used by the application.

## Consumer Checklist

- Do SQL result columns clearly map to DTO properties?
- Are ambiguous normalized names avoided?
- Are nullability expectations explicit?
- Are `DateOnly` and `TimeOnly` parameters intentional?
- Do generated mappers use `DbDataReader` as the compatibility boundary?
```

- [ ] **Step 2: Replace `tvpgen-guide.md` with consumer source-generator guidance**

Use this content:

```markdown
# Lib.Db.TvpGen Guide

Use this file when application code uses Lib.Db source generation for TVP rows or generated result mappers.

## Consumer Responsibilities

Application code owns:

- CLR row types used for TVP input
- DTO types used for result mapping
- alignment with SQL Server user-defined table types and stored procedure contracts
- nullability choices
- keeping generated code warnings visible during application builds

Lib.Db.TvpGen owns compile-time generation for supported patterns.

## TVP Rules

Use `[TvpRow]` for CLR types that represent SQL Server table-valued parameter rows.

Consumer guidance:

- Keep property names and order aligned with the SQL Server table type expected by stored procedures.
- Use supported CLR types only.
- Keep nullable CLR properties aligned with SQL Server nullability.
- Prefer immutable or init-only DTO-like row types when practical.

Example shape:

```csharp
[TvpRow("dbo.OrderLineTvp")]
public sealed class OrderLineRow
{
    public int LineNo { get; init; }
    public string ItemCode { get; init; } = "";
    public decimal Quantity { get; init; }
}
```

Stored procedure call shape:

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_SaveOrderLines")
    .With(new
    {
        OrderNo = orderNo,
        Lines = orderLines
    })
    .ExecuteAsync(ct);
```

## DbResult Rules

Use `[DbResult]` for DTOs that should have generated result mapping.

Consumer guidance:

- Generated result mappers should operate through `DbDataReader`.
- Concrete SQL reader types are compatibility details, not the primary consumer boundary.
- Keep SQL aliases aligned with DTO property names.
- Avoid ambiguous normalized names.

Example shape:

```csharp
[DbResult]
public sealed class OrderSummaryDto
{
    public string OrderNo { get; init; } = "";
    public DateOnly OrderDate { get; init; }
    public decimal TotalAmount { get; init; }
}
```

## Troubleshooting

If generated code fails or mapping is unexpected:

- check unsupported CLR property types
- check SQL Server table type and CLR row shape mismatch
- check nullable property mismatch
- check ambiguous result column names
- check whether SQL aliases should be added
- check whether the application build is suppressing generator diagnostics

## Consumer Checklist

- Are TVP row types aligned with SQL Server table types?
- Are `[DbResult]` DTOs aligned with result column names?
- Are unsupported types avoided?
- Are nullable values intentional?
- Are generated mapper diagnostics visible in the consuming application build?
```

## Task 6: Rewrite Consumer Examples

**Files:**

- Modify: `.claude/skills/lib-db/references/examples.md`

- [ ] **Step 1: Replace `examples.md` with production-safe consumer examples**

Use this content:

```markdown
# Examples

Use these examples as small consumer-facing templates. Keep secrets, full connection strings, high-privilege logins, certificate bypass defaults, repository paths, and verification commands out of examples.

## Dependency Injection

```csharp
builder.Services.AddLibDb(builder.Configuration, options =>
{
    options.UseProductionSecurityDefaults();
    options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;
    options.EnableObservability = true;
});
```

## Production-Oriented Configuration

```json
{
  "LibDb": {
    "ConnectionStringNames": [ "Default" ],
    "ConnectionSecurityProfile": "Production",
    "RawSqlPolicy": "DenyWriteText",
    "EnableObservability": true
  }
}
```

Store the actual connection string in the application's approved secret/configuration provider. Do not print it.

## Query Single Row By Stored Procedure

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);

if (!result.Success)
{
    logger.LogWarning("User lookup failed: {ErrorCode}", result.ErrorCode);
    return null;
}

return result.Value;
```

## Execute A Stored Procedure Write

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerCd })
    .ExecuteAsync(ct);

if (!result.Success)
{
    logger.LogWarning("Order insert failed: {ErrorCode}", result.ErrorCode);
}
```

## Intentional Parameterized Text SQL Read

```csharp
DbResult<long> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

Use text SQL only when intentional and allowed by application raw SQL policy.

## Stream Rows

```csharp
await foreach (DbResult<OrderDto> row in db.Default
    .Procedure("dbo.usp_StreamOrders")
    .With(new { Status = status })
    .QueryStreamAsync<OrderDto>(ct))
{
    if (!row.Success)
    {
        logger.LogWarning("Order stream row failed: {ErrorCode}", row.ErrorCode);
        continue;
    }

    await processor.HandleAsync(row.Value, ct);
}
```

## DateOnly And TimeOnly Parameters

```csharp
DbResult<IReadOnlyList<ScheduleDto>> result = await db.Default
    .Procedure("dbo.usp_SearchSchedule")
    .With(new
    {
        WorkDate = DateOnly.FromDateTime(request.WorkDate),
        StartAt = TimeOnly.FromDateTime(request.StartAt)
    })
    .QueryAsync<ScheduleDto>(ct);
```

## Result Mapping With SQL Aliases

SQL shape:

```sql
SELECT
    CELL_NO AS CellNo,
    CUSTOMER_CD AS CustomerCd
FROM dbo.Customer;
```

DTO shape:

```csharp
public sealed class CustomerDto
{
    public string CellNo { get; init; } = "";
    public string CustomerCd { get; init; } = "";
}
```

## Generated Result DTO

```csharp
[DbResult]
public sealed class OrderSummaryDto
{
    public string OrderNo { get; init; } = "";
    public DateOnly OrderDate { get; init; }
    public decimal TotalAmount { get; init; }
}
```

## TVP Row

```csharp
[TvpRow("dbo.OrderLineTvp")]
public sealed class OrderLineRow
{
    public int LineNo { get; init; }
    public string ItemCode { get; init; } = "";
    public decimal Quantity { get; init; }
}
```

Usage shape:

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_SaveOrderLines")
    .With(new
    {
        OrderNo = orderNo,
        Lines = orderLines
    })
    .ExecuteAsync(ct);
```
```

## Task 7: Final Consumer-Skill Verification

**Files:**

- Verify: `.claude/skills/lib-db/**`

- [ ] **Step 1: Confirm final file tree**

Run:

```powershell
$root = ".claude/skills/lib-db"
Get-ChildItem -Path $root -Recurse -File |
    ForEach-Object { $_.FullName.Substring((Resolve-Path $root).Path.Length + 1) } |
    Sort-Object
```

Expected:

```text
references\examples.md
references\mapping-contracts.md
references\runtime-api.md
references\security-guardrails.md
references\tvpgen-guide.md
SKILL.md
```

- [ ] **Step 2: Confirm no release-version strings remain**

Run:

```powershell
Select-String -Path .claude/skills/lib-db/**/*.md,.claude/skills/lib-db/SKILL.md -Pattern '\bv?\d+\.\d+\.\d+\b'
```

Expected: no matches.

- [ ] **Step 3: Confirm no repository-internal workflow guidance remains**

Run:

```powershell
Select-String -Path .claude/skills/lib-db/**/*.md,.claude/skills/lib-db/SKILL.md -Pattern 'Verification/|Verification\\|dotnet test|benchmark|coverage|chaos|AOT|release gate|test project|validate-skill|manifest\.json|Lib\.Db\.csproj'
```

Expected: no matches.

- [ ] **Step 4: Confirm security guardrails remain**

Run:

```powershell
Select-String -Path .claude/skills/lib-db/**/*.md,.claude/skills/lib-db/SKILL.md -Pattern 'secret|connection string|RawSqlPolicy|stored procedure|DbDataReader|DateOnly|TimeOnly|TvpRow|DbResult'
```

Expected: matches in the retained skill files.

- [ ] **Step 5: Review git diff for unrelated changes**

Run:

```powershell
git diff -- .claude/skills/lib-db
```

Expected:

- only `.claude/skills/lib-db` files changed
- `references/verification.md` deleted
- `tests/` files deleted
- no unrelated repository files included

- [ ] **Step 6: Commit only the skill package changes**

Run:

```powershell
git add -- .claude/skills/lib-db
git commit -m "docs: redesign lib-db consumer skill" -- .claude/skills/lib-db
```

Expected:

- commit succeeds
- unrelated dirty files remain unstaged/uncommitted

## Self-Review

- The plan covers every approved spec requirement.
- The plan intentionally deletes `references/verification.md` and `tests/`.
- The plan removes version-pinned skill identity.
- The plan does not require source repository internals for future consumers.
- The plan preserves and strengthens security guardrails.
- The plan leaves implementation scope limited to `.claude/skills/lib-db`.
