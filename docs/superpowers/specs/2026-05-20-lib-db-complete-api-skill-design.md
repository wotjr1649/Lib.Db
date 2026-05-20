# Lib.Db Complete API Skill Design

## Goal

Expand `.claude/skills/lib-db` from a safe starter skill into a complete, token-efficient consumer skill for the Lib.Db NuGet package.

The target user has the Lib.Db package and application code. The skill must teach AI agents how to select and use Lib.Db public API families without requiring access to Lib.Db source repository internals.

## Quality Target

This redesign targets a "100-point" skill:

- broad enough to cover every major Lib.Db public API family
- narrow enough that `SKILL.md` stays a concise router
- structured through progressive disclosure so agents load only the reference file needed for the task
- safe by default for SQL Server, connection strings, logging, raw SQL, bulk operations, cache keys, interceptors, and schema maintenance
- grounded in public API behavior rather than release, verification, benchmark, or repository-maintenance workflow

## External Guidance Used

- Claude Code Skills documentation: `description` drives discovery; supporting files are loaded only when needed.
- Claude Skill authoring best practices: keep `SKILL.md` concise and split larger content into reference files.
- Agent Skills format: `SKILL.md` is required; `references/` is an appropriate optional supporting directory.
- Local `skill-creator` guidance: include specialized domain/API knowledge, keep content concise, and avoid spending tokens on what the model already knows.
- Codex Security review: keep tool access read-only, do not use broad `paths` frontmatter, do not include secret values or direct SQL execution workflows, and keep dangerous API families guarded.

## Non-Goals

- Do not reintroduce repository verification, release, benchmark, coverage, chaos, or package-source maintenance workflows.
- Do not include version strings or version-pinned release guidance.
- Do not require access to `Lib.Db.csproj`, manifests, repository README files, test projects, or verification scripts.
- Do not add `tests/` back into the shipped skill package.
- Do not turn `SKILL.md` into a full API manual.
- Do not document private implementation details unless needed to explain public API safety.

## Target Structure

```text
lib-db/
  SKILL.md
  references/
    quickstart.md
    options-and-registration.md
    connection-security.md
    fluent-execution.md
    parameters-and-binding.md
    result-handling.md
    mapping-contracts.md
    tvp-source-generation.md
    bulk-insert.md
    schema-maintenance.md
    caching.md
    transactions.md
    operations-integration.md
    diagnostics-resilience.md
    aot-trimming.md
    examples.md
```

## `SKILL.md` Design

`SKILL.md` remains the router.

It should contain:

- frontmatter:
  - `name: lib-db`
  - a trigger-focused `description`
  - read-only `allowed-tools`
  - no broad `paths` frontmatter
- purpose
- first-step decision process
- reference map
- non-negotiable safety rules
- completion criteria

It should not contain:

- long API explanations
- long examples
- release/verification/test workflows
- broad file path triggers

Candidate description:

```yaml
description: Use when using the Lib.Db NuGet package in application code, especially for SQL Server data access, dependency injection, options, connection security, fluent queries, parameters, DbResult handling, mapping, TVP/source generation, bulk insert, schema maintenance, caching, transactions, health checks, interceptors, diagnostics, resilience, AOT/trimming, or production-safe examples.
```

## Reference File Responsibilities

### `quickstart.md`

Purpose: a short decision guide, not an example catalog.

Contents:

- "What are you trying to do?" routing table
- minimal order of operations for common application setup
- links to the specific reference file for each task
- warnings to avoid guessing unsupported API shapes

Token rule: keep this under roughly one page.

### `options-and-registration.md`

Purpose: registration and option mechanics.

Must cover:

- `AddLibDb(IConfiguration)`
- `AddLibDb(Action<LibDbOptions>)`
- `AddHighPerformanceDb`
- `AddLibDbOptions`
- `AddLibDbOptionsFromConfiguration`
- `AddLibDbResilience`
- `LibDbOptions`
- `ConnectionStringNames`
- `ConnectionStrings`
- `CommandTimeoutSeconds`
- MARS policy
- resilience options

Boundary:

- mechanics here
- security policy belongs in `connection-security.md`

### `connection-security.md`

Purpose: security posture for credentials, connection strings, production profiles, raw SQL policy, logging, and direct SQL requests.

Must cover:

- no secret value output
- no full connection string output
- safe reporting shape: key name and presence only
- `UseProductionSecurityDefaults`
- `ConnectionSecurityProfile`
- `RawSqlPolicy`
- development-only shortcut labeling
- least-privilege SQL Server principals
- stored procedures for write/admin/permission-boundary work
- direct SQL CLI DDL/DML approval rule

Boundary:

- do not duplicate option mechanics except where needed for safe examples

### `fluent-execution.md`

Purpose: fluent command construction and execution shapes.

Must cover:

- `.Procedure(...)`
- `.Sql(...)`
- `.SqlInterpolated(...)`
- `.With(...)`
- `QueryAsync<T>()`
- `QuerySingleAsync<T>()`
- `ExecuteScalarAsync<T>()`
- `ExecuteAsync(...)`
- `QueryMultipleAsync(...)`
- `IMultipleResultReader`
- cancellation token flow
- choosing stored procedures versus text SQL

### `parameters-and-binding.md`

Purpose: parameter binding and SQL type decisions.

Must cover:

- anonymous object binding
- `SqlParameter`
- `DbType`
- null handling
- `DateOnly` -> SQL `date`
- `TimeOnly` -> SQL `time`
- TVP parameter handoff pointer to `tvp-source-generation.md`
- output parameter caveats if public API supports them; otherwise state the absence plainly

### `result-handling.md`

Purpose: handling results and errors consistently.

Must cover:

- `DbResult<T>`
- `IsSuccess`
- `Value`
- `Error`
- `DbError`
- `DbErrorKind`
- `SqlErrorCode`
- `Severity`
- `IsTransient`
- null result versus failed command
- logging without sensitive details
- retry decisions as application policy, linked to `diagnostics-resilience.md`

### `mapping-contracts.md`

Purpose: DTO result mapping contract.

Must cover:

- exact case-insensitive match first
- underscore-insensitive normalized match second
- collision handling
- SQL aliases
- nullable DTO properties
- `DbDataReader` boundary for generated mappers
- wrapper readers as normal `DbDataReader`

### `tvp-source-generation.md`

Purpose: TVP and source-generated mapping.

Must cover:

- `[TvpRow]`
- `[DbResult]`
- `GenerateTvpFromDb`
- TVP type name decisions
- column order and nullability
- unsupported CLR type behavior
- generator diagnostics
- relation to `parameters-and-binding.md` and `bulk-insert.md`

### `bulk-insert.md`

Purpose: bulk insert API usage and safety.

Must cover:

- `BulkInsertAsync<T>`
- `BulkInsertOptions`
- destination table naming
- batch sizing
- transaction interaction
- reflection and AOT/trimming limitations
- least-privilege permissions
- never bulk insert unvalidated tenant-crossing data

### `schema-maintenance.md`

Purpose: schema maintenance public API usage.

Must cover:

- `db.Schema`
- schema maintenance stage
- table/procedure/TVP setup/update APIs exposed publicly
- when application-managed schema setup is acceptable
- when production schema changes should be handled outside app startup
- `SET QUOTED_IDENTIFIER ON` for computed-column index scenarios
- direct SQL approval boundaries

### `caching.md`

Purpose: Lib.Db cache integration and safe cache key design.

Must cover:

- `AddLibDbHybridCache`
- `GetOrQueryAsync<T>`
- cache key construction
- tenant/user/permission-sensitive dimensions
- invalidation strategy
- staleness decisions
- serialization/AOT caveats linked to `aot-trimming.md`

### `transactions.md`

Purpose: transaction API usage.

Must cover:

- `BeginTransactionAsync`
- `IDbTransactionScope`
- `CommitAsync`
- `RollbackAsync`
- `await using`
- automatic rollback on dispose without commit
- keeping transaction scopes short
- interaction with stored procedure writes, bulk insert, and schema maintenance

### `operations-integration.md`

Purpose: operational app integration.

Must cover:

- `AddLibDbHealthCheck`
- `AddLibDbHostedServices`
- `AddLibDbInterceptor`
- `IDbCommandInterceptor`
- interceptor lifecycle
- safe redaction in interception
- application startup behavior
- health-check failure interpretation

This replaces the earlier `health-interceptors.md` name because the file covers more than health and interceptors.

### `diagnostics-resilience.md`

Purpose: observability, telemetry, error classification, and resilience.

Must cover:

- `EnableObservability`
- `LibDbTelemetry`
- structured logging metadata
- `DbErrorKind`
- `IsTransient`
- resilience options
- `AddLibDbResilience`
- retry policy boundaries
- avoid logging raw SQL parameter values

### `aot-trimming.md`

Purpose: consumer guidance for AOT/trimming-sensitive applications.

Must cover:

- `RequiresUnreferencedCode`
- `RequiresDynamicCode`
- safe registration shape for AOT-sensitive apps
- configuration binding caveats
- bulk insert reflection caveat
- cache serialization caveat
- source generation as an AOT-friendly path when applicable

### `examples.md`

Purpose: curated snippets only.

Contents:

- one compact example per major family
- no long explanations
- no full connection strings
- no high-privilege login examples
- no certificate validation bypass defaults
- no package-source maintenance commands

## Token Efficiency Rules

- `SKILL.md` should remain a short router.
- Each reference file should start with "Use this file when..." and a 3-5 bullet quick map.
- Each reference file should prefer small compilable snippets over prose.
- Do not repeat the same security policy in every file; link to `connection-security.md` and include only local safety notes.
- Do not include comprehensive API docs that merely copy XML documentation.
- Do include API selection rules, sharp edges, and failure patterns.
- Keep examples small and reusable.

## Security Rules

- Keep `allowed-tools` read-only.
- Do not add broad `paths` frontmatter.
- Do not include secret values, tokens, passwords, or full connection strings.
- Do not recommend high-privilege SQL logins or certificate validation bypasses.
- Do not include direct SQL DDL/DML execution recipes.
- For dangerous families, include local security warnings:
  - `bulk-insert.md`: tenant and validation safety
  - `schema-maintenance.md`: production schema-change boundaries
  - `caching.md`: tenant/user/permission key dimensions
  - `operations-integration.md`: redaction in interceptors
  - `diagnostics-resilience.md`: no sensitive parameter logging
  - `aot-trimming.md`: avoid unsafe fallback claims

## API Coverage Acceptance Criteria

The redesigned skill should document all public API families previously found missing or partial:

- DI/options: `AddLibDb`, `AddHighPerformanceDb`, `AddLibDbOptions`, `AddLibDbOptionsFromConfiguration`, `AddLibDbResilience`
- connection/options/security: connection string names, raw SQL policy, production security profile, MARS, timeout, resilience
- fluent execution: procedure, SQL, interpolated SQL, query, single query, scalar, execute, multiple result sets
- result/error handling: `DbResult<T>`, `DbError`, `DbErrorKind`, transient errors
- transactions: begin, commit, rollback
- parameters/binding: anonymous objects, SQL parameters, DB types, `DateOnly`, `TimeOnly`
- TVP/source generation: row/result attributes, DB-first generation, type name and column metadata
- bulk insert
- schema maintenance
- caching
- health checks, hosted services, command interceptors
- diagnostics, telemetry, resilience
- AOT/trimming

## Completion Criteria

- The skill package still contains no version strings.
- The skill package still contains no package-source verification workflow.
- `SKILL.md` remains concise and acts as a router.
- Every reference in the `SKILL.md` map exists.
- Every public API family in the acceptance criteria is represented by a reference file.
- Dangerous API families include security notes.
- The package contains no unsafe credential examples.
- The package contains no broad `paths` frontmatter.

## Self-Review

- The file list is intentionally larger than the current skill because the target is complete API coverage.
- Progressive disclosure controls token cost: agents should load only the relevant family file.
- `quickstart.md` and `examples.md` are separated to avoid duplication.
- `connection-security.md` and `options-and-registration.md` are separated by policy versus mechanics.
- `operations-integration.md` groups health checks, hosted services, and interceptors to reduce file count.
- `diagnostics-resilience.md` and `aot-trimming.md` remain separate because their decision criteria differ.
