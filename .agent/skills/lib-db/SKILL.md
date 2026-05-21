---
name: lib-db
description: Use when using the Lib.Db NuGet package in application code, especially for SQL Server data access, dependency injection, options, connection security, fluent queries, parameters, DbResult handling, mapping, TVP, bulk insert, schema maintenance, caching, transactions, health checks, interceptors, diagnostics, resilience, AOT/trimming, JSON helpers, or production-safe examples.
---

# Lib.Db

Use this skill for application code that consumes the Lib.Db NuGet package.

Keep this file as the router. Read only the reference files needed for the current task, then write application code against public APIs.

## Scope

This skill documents application consumer-facing Lib.Db APIs. Internal, infrastructure-level, and low-level public contracts are outside normal application scope unless the user is extending Lib.Db itself.

Exact-name index for validation and routing: `DbResultAttribute`, `ConfigurationBinder`, `IIsolationKeyGenerator`, `IQueryAnalyzer`, `IDbCommandInterceptor`, `IResiliencePipelineProvider`, `ITransientSqlErrorDetector`, `ISchemaService`, `ITvpSchemaValidator`, `ITvpStaticValidator`, and `ISchemaFlushCoordinator`.

## First Step

1. Identify the API family the user needs.
2. Read `references/quickstart.md` when the task is broad or ambiguous.
3. Read the narrow reference file for the chosen API family.
4. Follow repository rules for secrets, SQL execution, encoding, and local checks.

## Reference Map

- Setup, DI, options: `references/options-and-registration.md`
- Credential handling, raw SQL policy, production defaults: `references/connection-security.md`
- Fluent calls and result-set shapes: `references/fluent-execution.md`
- Anonymous objects, `SqlParameter`, SQL types, nulls: `references/parameters-and-binding.md`
- `DbResult<T>`, `DbError`, failure handling: `references/result-handling.md`
- DTO mapping, JSON helpers, generated result mapper contracts: `references/mapping-contracts.md`
- Runtime TVP APIs, old TvpGen migration, static shapes: `references/tvp-source-generation.md`
- `BulkInsertAsync<T>` and `BulkInsertOptions`: `references/bulk-insert.md`
- `db.Schema`, `UseSchema`, schema cache flush: `references/schema-maintenance.md`
- Query cache, HybridCache, shared-memory cache: `references/caching.md`
- Transactions and rollback rules: `references/transactions.md`
- Health checks, hosted services, interceptors, advanced extensibility contracts, host hook: `references/operations-integration.md`
- Observability, resilience, dry run, chaos options: `references/diagnostics-resilience.md`
- Native AOT and trimming choices: `references/aot-trimming.md`
- Small copy-ready patterns: `references/examples.md`

## Hard Rules

- Never print secret values, tokens, passwords, or full connection strings. Report only key names and whether values exist.
- Prefer stored procedures for writes, administrative work, tenant-sensitive access, and SQL Server permission boundaries.
- Treat `RawSqlPolicy` as a guardrail, not as a complete SQL parser or security boundary.
- Use `SqlInterpolated(...)` or `.With(...)` for values in text SQL. Do not concatenate user input into SQL text.
- Use `UseProductionSecurityDefaults()` for production-oriented examples unless the user explicitly asks for a constrained local-only sample.
- Treat `UseConnectionString`, bulk insert, schema maintenance, interceptors, `IncludeParametersInTrace`, and `Use*Unsafe` snapshot extensions as sensitive APIs.
- Do not add package maintenance, repository-internal, package build, or lifecycle workflows to this skill.

## Stable Public Contracts

- `IDbSession` is the main entry point.
- Fluent execution returns `DbResult<T>` with `IsSuccess`, `Value`, `Error`, and `AffectedRows`.
- `QueryAsync<T>()` returns `DbResult<IAsyncEnumerable<T>>`.
- `QuerySingleAsync<T>()` and `ExecuteScalarAsync<T>()` return nullable value payloads.
- `QueryMultipleAsync()` returns `DbResult<IMultipleResultReader>`.
- `ExecuteAsync()` returns `DbResult<int>`.
- `BulkInsertAsync<T>()` returns `DbResult<long>`.
- Result mapping tries exact case-insensitive column/property matches first, then underscore-insensitive normalized matches.
- `DateOnly` binds as SQL `date`; `TimeOnly` binds as SQL `time`.

## Completion Check

- The needed reference file was consulted.
- Examples use real Lib.Db public API names.
- Examples do not reveal secrets or full connection strings.
- Raw SQL is avoided or explicitly parameterized and policy-covered.
- Sensitive API families include safety context.
- When verifying official skill installation, report Claude Code repo-local discovery and Codex discovery separately; do not treat one as proof of the other.
