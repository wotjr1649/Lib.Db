# Lib.Db v2.4.0 Integrated Additional Scope Design

Date: 2026-05-22
Status: Draft, implementation not started from this document
Scope: The five adopted v2.4.0 additional-scope items and their release gates

## Purpose

This document integrates the five additional-scope items adopted for Lib.Db v2.4.0 so they are no longer scattered across separate discussions.

The release position remains:

> Lib.Db is a thin, safe operational library for SQL Server, stored procedures, TVPs, typed result mapping, caching helpers, and high-throughput bulk operations. It is not an ORM, not a general migration engine, and not a change tracker in the Entity Framework sense.

The goal is to make v2.4.0 stronger before NuGet publish/tag without turning the core package into a heavy platform.

## Official Sources Checked

Checked on 2026-05-22.

- Microsoft `HybridCache` docs describe tag assignment, `RemoveByTagAsync`, wildcard invalidation, local-only behavior without `IDistributedCache`, and the fact that tag invalidation is logical rather than physical removal from underlying caches: <https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid>
- `HybridCache` API exposes `GetOrCreateAsync`, `SetAsync`, `RemoveAsync`, and `RemoveByTagAsync` overloads with `IEnumerable<string>` tags: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.hybrid.hybridcache>
- ADO.NET `DataReader` guidance states that multiple result sets are read sequentially with `NextResult` and that the open reader owns the connection until closed: <https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/retrieving-data-using-a-datareader>
- SQL Server MARS guidance states that MARS must be explicitly enabled and permits interleaving, not parallel execution: <https://learn.microsoft.com/en-us/sql/connect/oledb/features/using-multiple-active-result-sets-mars>
- `SqlBulkCopy` efficiently bulk-loads SQL Server tables from an `IDataReader`, supports column mappings, streaming, and existing transactions: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy>
- `SqlBulkCopy` constructor docs state that `UseInternalTransaction` cannot be combined with an external transaction: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy.-ctor>
- SQL Server bulk-copy transaction guidance recommends an existing transaction when the bulk operation must be rollbackable with other work: <https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/transaction-bulk-copy-operations>
- SQL Server `QUOTENAME` documents bracket quoting and escaping for delimited identifiers: <https://learn.microsoft.com/en-us/sql/t-sql/functions/quotename-transact-sql>
- `sp_executesql` docs warn about runtime-compiled SQL and recommend parameterization for values: <https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-executesql-transact-sql>
- .NET Native AOT guidance requires removing or isolating runtime reflection/dynamic-code paths that produce trim/AOT warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings>
- Roslyn SDK docs describe source generators as compile-time metaprogramming that can inspect compilation and additional files: <https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview>
- Roslyn source generator cookbook states that source generators are additive and cannot rewrite existing user code; generator NuGet packages are placed under `analyzers/dotnet/cs`: <https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md>
- EF Core migration docs are used only as a comparison point for why Lib.Db should not become a general model-based migration engine in v2.4.0: <https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing>
- SQL Server Change Tracking docs describe lightweight row-change discovery, `CHANGETABLE`, and the fact that database/table Change Tracking must be enabled before use: <https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/about-change-tracking-sql-server>
- SQL Server Change Tracking work-flow docs describe obtaining changes and application responsibility for version management: <https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/work-with-change-tracking-sql-server>

## Adopted Scope Matrix

| Item | v2.4.0 Status | Implementation Owner | Notes |
| --- | --- | --- | --- |
| `WithHybridCacheAsync` tags overload | Implement in v2.4.0 | `Lib.Db.Extensions.QueryCacheExtensions` | Add explicit tag support while documenting local-only/L2 behavior and tag safety. |
| Typed `QueryMultiple` helper | Implement in v2.4.0 | new extension/helper around `IMultipleResultReader` | Add convenience wrappers for common arities without changing the low-level reader contract. |
| AOT-safe bulk shape overloads | Implement in v2.4.0 | existing AOT-safe bulk mutation sub-spec | Include insert/update/delete/upsert/merge, staged DML, and AOT smoke. |
| Generator/migration/change-tracking | Document and plan only in v2.4.0 | docs/spec roadmap | Do not implement these runtime capabilities in v2.4.0. |
| Release integration and docs | Implement in v2.4.0 | docs, verification, package guidance | API docs, cookbook, skill guidance, security gates, release verification. |

## Security Model

This section applies the Codex Security threat-model lens to the integrated scope.

### Assets

- SQL Server data modified by bulk operations.
- Stored procedure result sets and mapped DTOs.
- Cache keys, cache tags, cached query results, and provider topology diagnostics.
- Connection strings and provider credentials owned by the host application.
- Future generator/migration/change-tracking metadata and generated scripts.
- Release verification outputs and package metadata.

### Trust Boundaries

- Application code calling Lib.Db is trusted more than end-user input, but individual strings passed to public APIs must still be treated as attacker-influenced until validated.
- Cache keys and tags are application-owned. Lib.Db must not infer tenant, user, authorization, culture, feature flag, or freshness dimensions.
- SQL object identifiers are not parameterizable values. They require strict identifier validation and quoting.
- `IDistributedCache` providers are host-owned. Lib.Db may use the provider but must not print provider secrets or promise cross-host behavior without a verified provider.
- Future migration/change-tracking tools may generate or inspect SQL, but v2.4.0 must not auto-apply DDL/DML outside application/test execution paths.

### Invariants

- Do not log connection strings, provider credentials, raw SQL parameters, raw cache payloads, or row values.
- Never concatenate row values into SQL command text.
- Bulk write APIs validate and quote destination identifiers before execution.
- Bulk update/delete/upsert/merge require explicit key columns.
- `DeleteNotMatchedBySource` is rejected for v2.4.0.
- Cache tag APIs reject empty/whitespace tags and the reserved wildcard tag `*` for individual entries.
- Cache documentation states that tag invalidation is logical and that other servers' in-memory L1 entries are not affected by current-server invalidation alone.
- Typed multi-result helpers must dispose the underlying reader and preserve strict result-set ordering.
- Future generator/migration/change-tracking docs must stay explicit opt-in and must not imply that Lib.Db will own application schema evolution automatically.

## Item 1: HybridCache Tags Overload

### Current State

`QueryCacheExtensions.WithHybridCacheAsync<T>()` accepts a `HybridCache`, cache key, duration, and cancellation token. It creates `HybridCacheEntryOptions` but does not pass tags.

Microsoft `HybridCache.GetOrCreateAsync` supports tags, and `RemoveByTagAsync` provides logical tag invalidation. Lib.Db's AOT fallback cache already contains tag behavior tests, so the library has an internal precedent for tag semantics.

### v2.4.0 Design

Add overloads that accept `IEnumerable<string> tags`:

```csharp
public static Task<DbResult<T?>> WithHybridCacheAsync<T>(
    this Task<DbResult<T?>> resultTask,
    HybridCache hybridCache,
    string cacheKey,
    TimeSpan duration,
    IEnumerable<string> tags,
    CancellationToken ct = default);
```

The existing overload remains and delegates to the new overload with `tags: null` or an empty tag list.

Tag normalization rules:

- `null` means no tags.
- Empty and whitespace tags are rejected rather than silently dropped in the public helper.
- `*` is rejected for entry assignment because Microsoft reserves it for wildcard invalidation.
- Duplicate tags are removed with ordinal comparison.
- The implementation caps tags per entry at the same practical ceiling used by Lib.Db's AOT cache path unless the existing code exposes a different constant.

Security guidance:

- Tags must be stable, non-sensitive grouping labels such as `product`, `tenant-hash:<value>`, or `schema:<name>`.
- Tags must not contain raw user ids, e-mail addresses, access tokens, connection strings, or raw SQL text.
- Cache keys and tags are both app-owned. Lib.Db does not infer authorization scope.

Known limitation:

- The existing task-based `WithHybridCacheAsync` pattern cannot prevent the original DB task from starting if the caller has already created it. v2.4.0 will document this honestly. A future factory-style HybridCache helper can be considered, but it is not required to satisfy the adopted tags-overload scope.

## Item 2: Typed QueryMultiple Helper

### Current State

Lib.Db already exposes:

- `QueryMultipleAsync(CancellationToken ct)` returning `DbResult<IMultipleResultReader>`.
- `IMultipleResultReader.ReadAsync<T>()`.
- `IMultipleResultReader.ReadSingleAsync<T>()`.
- `SqlGridReader` sequentially advances result sets with `NextResultAsync`.

This is safe but verbose. Callers must remember order and disposal manually.

### v2.4.0 Design

Add small extension helpers that wrap the current reader without changing the executor contract.

Preferred public model:

```csharp
public readonly record struct DbMultiple<T1, T2>(
    List<T1> First,
    List<T2> Second);

public readonly record struct DbMultiple<T1, T2, T3>(
    List<T1> First,
    List<T2> Second,
    List<T3> Third);

public readonly record struct DbMultiple<T1, T2, T3, T4>(
    List<T1> First,
    List<T2> Second,
    List<T3> Third,
    List<T4> Fourth);
```

Extension methods:

```csharp
public static Task<DbResult<DbMultiple<T1, T2>>> ReadMultipleAsync<T1, T2>(
    this Task<DbResult<IMultipleResultReader>> readerTask,
    CancellationToken ct = default);

public static Task<DbResult<DbMultiple<T1, T2, T3>>> ReadMultipleAsync<T1, T2, T3>(
    this Task<DbResult<IMultipleResultReader>> readerTask,
    CancellationToken ct = default);

public static Task<DbResult<DbMultiple<T1, T2, T3, T4>>> ReadMultipleAsync<T1, T2, T3, T4>(
    this Task<DbResult<IMultipleResultReader>> readerTask,
    CancellationToken ct = default);
```

Usage:

```csharp
DbResult<DbMultiple<UserRow, OrderRow, SummaryRow>> result = await session
    .Use("Default")
    .Procedure("dbo.usp_Dashboard")
    .QueryMultipleAsync(ct)
    .ReadMultipleAsync<UserRow, OrderRow, SummaryRow>(ct);
```

Behavior:

- If `QueryMultipleAsync` returns failed `DbResult`, return the same failure pattern.
- If reading any expected result set fails, return a failed `DbResult` or preserve existing exception-wrapping convention after inspecting local patterns.
- Always dispose the reader with `await using`.
- Preserve existing strict result-set ordering and MARS policy.
- Do not add streaming typed multi-result support in v2.4.0.

This adds user ergonomics without broadening DB execution semantics.

## Item 3: AOT-Safe Bulk Shape Overloads

### Current State

The existing reflection-based `BulkInsertAsync<T>()` is AOT-incompatible and remains as legacy compatibility.

An approved sub-spec and plan already exist:

- `docs/superpowers/specs/2026-05-22-v240-aot-safe-bulk-mutations-design.md`
- `docs/superpowers/plans/2026-05-22-v240-aot-safe-bulk-mutations-implementation.md`

### v2.4.0 Design

The integrated scope adopts that sub-spec as-is with one clarification:

- The v2.4.0 scope is not only insert. It includes `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync`, `BulkUpsertAsync`, and `BulkMergeAsync` shape-based APIs.
- `BulkMergeAsync` is API-level merge implemented with staged DML, not SQL Server `MERGE` by default.
- `DeleteNotMatchedBySource` remains unsupported in v2.4.0.

Security controls:

- Strict destination identifier validation.
- Bracket quoting equivalent to SQL Server delimited identifier behavior.
- Row values moved through `SqlBulkCopy` and staged tables, not SQL string concatenation.
- Default local transaction for staged mutation operations.
- AOT smoke verifies the new static-shape path without introducing reflection warnings.

## Item 4: Generator, Migration, and Change-Tracking Roadmap

### v2.4.0 Rule

v2.4.0 documents these areas only. It does not implement them in the runtime package.

This is intentional. Implementing them now would increase release blast radius and push Lib.Db toward ORM/migration-framework responsibility.

### v2.5.0 Candidate: `Lib.Db.Generator`

Future package, not v2.4.0 runtime:

- Incremental source generator for optional AOT mapper or bulk/TVP shape code.
- Must be additive only; no code rewriting.
- Must not inspect live databases during normal compilation.
- If schema input is needed, use explicit checked-in additional files.
- Generator package should be delivered as analyzer assets, not a runtime dependency.

### v2.5.0 Candidate: Migration/Contract Tooling

Future CLI/tooling, not v2.4.0 runtime:

- Contract validation and script scaffolding for SQL Server objects Lib.Db already cares about: stored procedures, TVPs, table shapes used by bulk APIs.
- No automatic production DDL execution from the core library.
- No EF-style model snapshot engine in core.
- Generated scripts must be reviewable, deterministic, and opt-in.

### v2.5.0 Candidate: SQL Server Change Tracking Adapter

Future provider package or tooling, not v2.4.0 runtime:

- Adapter over SQL Server Change Tracking, not a custom trigger/table system.
- Requires the application/operator to enable Change Tracking at database and table level.
- Exposes changed primary keys and versions; callers decide how to fetch and apply current row values.
- Must account for retention windows and invalid stored versions.
- Must not silently enable Change Tracking or alter database settings from Lib.Db core.

## Item 5: Release Integration

v2.4.0 release readiness requires the additional scope to be visible in:

- API reference docs.
- Advanced guide.
- Cookbook.
- History/release notes.
- Lib.Db consumer skill, if public usage changes.
- Official release verification.
- Security review checklist.

No NuGet publish or tag should occur until:

- targeted tests for all three implemented scope items pass,
- Native AOT verification passes with no new Lib.Db trim/AOT warnings,
- official release verification passes from a clean environment,
- docs clearly distinguish v2.4.0 implemented features from v2.5.0 roadmap items,
- final review confirms no secrets, raw connection strings, or unsafe SQL examples were introduced.

## Alternatives Considered

### Option A: Implement only AOT-safe bulk in v2.4.0

Lowest code risk, but it would leave the previously adopted cache and QueryMultiple ergonomics untracked. Rejected because it does not honor the adopted five-item scope.

### Option B: Implement all five items fully in v2.4.0

Too much release risk. Generator, migration, and change tracking would require new packaging, new security boundaries, and DDL/change-version semantics. Rejected for v2.4.0.

### Option C: Implement three low-risk additive features and document the heavy roadmap

Recommended and adopted by this design:

- implement HybridCache tags overload,
- implement typed QueryMultiple helper,
- implement AOT-safe bulk mutation suite,
- document generator/migration/change-tracking for v2.5.0,
- bind all of it with release docs and verification gates.

## Definition of Done

The integrated additional scope is complete when:

- this design and the implementation plan are committed,
- the three v2.4.0 implemented areas have tests and docs,
- the roadmap areas are documented as v2.5.0 candidates only,
- official verification passes,
- security review finds no release-blocking issue,
- v2.4.0 package docs state exactly what is implemented and what is planned.
