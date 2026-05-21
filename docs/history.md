# Lib.Db History

Current usage docs are intended to stay version-neutral and describe the current API. This file owns version-specific history: release changes, verification summaries, migration notes, and archived report summaries that should not remain scattered through active guides.

## 2.3.0 Summary

### Added

- Added Runtime TVP binding in the single `Lib.Db` runtime package, without requiring a separate `Lib.Db.TvpGen` package.
- Added the `LibDb.Tvp("dbo.TypeName", rows)` wrapper so TVP rows can be passed alongside regular scalar, output, return, and provider parameters.
- Added static-shape Runtime TVP registration for repeated and Native AOT-oriented TVP calls. `options.Tvp.Map<T>(...).Column(...)` records SQL metadata and static getters, then uses an `IEnumerable<SqlDataRecord>` fast path instead of runtime property discovery on hot paths.
- Added `TvpShape.For` as the standalone static-shape construction path for callers that need an explicit reusable TVP shape outside the options registration flow.
- Added schema-adaptive TVP binding through descriptors: callers can retrieve a DB descriptor with `db.UseSchema(...).GetTvpAsync(...)` and pass it to `LibDb.Tvp(descriptor, rows, TvpBindingPolicy.Adaptive)` so nullable/default-safe schema drift can be corrected deliberately.
- Added targeted TVP schema cache flush APIs such as `db.Schema.FlushTvpAsync(...)` and `db.UseSchema(...).FlushTvpAsync(...)` for refreshing a single TVP descriptor after verified drift.

### Changed

- TVP guidance became runtime-first: generated accessors remain a benchmark baseline, while runtime object streaming and runtime registered fast paths are first-class comparison paths.
- TVP schema cache invalidation is scoped to the affected TVP name. A targeted flush clears the local HybridCache entry, descriptor snapshot, negative cache, and descriptor row-accessor cache instead of treating all schema metadata as stale.
- Benchmark coverage now compares narrow and wide TVP cases across generated accessor baseline, runtime object streaming, and runtime registered fast path.

### Breaking / Migration

- Existing consumers that referenced the separate `Lib.Db.TvpGen` package should remove that package reference and move TVP calls to the runtime APIs in `Lib.Db`.
- Replace generated-accessor-only call paths with `LibDb.Tvp("schema.TypeName", rows)`, an explicit `TvpShape.For<T>()`, or option-level `options.Tvp.Map<T>("schema.TypeName")` registration for repeated hot paths.
- Scalar, output, return, and provider parameters continue to be passed through `.With(...)`; TVP values are supplied alongside them as regular properties whose value is a `LibDb.Tvp(...)` wrapper.
- Compatibility marker attributes remain in `Lib.Db` to ease migration of existing row models, but v2.3.0 does not require a separate generator package for normal runtime TVP binding.

### Removed

- The released `Lib.Db` package no longer depends on or ships the separate `Lib.Db.TvpGen` project/package as a required TVP path.
- TvpGen-specific operational guidance was moved out of active current-API docs. Historical mentions remain here only to explain migration and benchmark baseline context.

### Verification

- Maintainer release validation was consolidated into an internal runbook. Consumer applications do not need that workflow to install or use `Lib.Db`.
- Release validation covers package readiness, Runtime TVP behavior, Native AOT readiness, performance comparison, and artifact hygiene as maintainer responsibilities.
- Release verification now includes package provenance checks, explicit unsigned package
  policy handling, AOT warning baseline checks, and generated artifact tracking.

### Security

- Runtime TVP entry points normalize type names through `TvpTypeName.Parse`; invalid identifiers fail before SQL execution, and schema lookup must remain parameterized.
- TVP rows and scalar values are provider parameters. Raw SQL policy is a guardrail, not a security boundary, and production systems still need proper DB permissions and parameterization.
- Connection strings, passwords, tokens, and benchmark environment values must not be printed in reports. Documentation should mention expected key names or key presence only when necessary.
- Native AOT policy treats Lib.Db-owned IL warnings as release blockers. Provider-owned aggregate warnings such as `IL2104` and `IL3053` are visible but reviewed separately when provider packages change.
- The documented AOT fast path is static-shape registration via `.Map<T>().Column(...)` or equivalent explicit shapes. Reflection-based fallback paths and convenience APIs remain annotated or guarded so Native AOT risks stay visible.

## 2.2.1 Summary

### Fixed

- Fixed the ResultSet naming convention so default DTO mapping handles common SQL Server column forms such as `CELL_NO`, `cell_no`, and `CellNo` for PascalCase properties. Exact matches remain preferred, and ambiguous normalized matches are not auto-mapped.
- Fixed generated/static `[DbResult]` mapper compatibility with monitored readers by making `DbDataReader` the primary contract while retaining `SqlDataReader` compatibility overloads.
- Fixed `DateOnly` and `TimeOnly` binding for both raw SQL and stored procedure paths so `DateOnly` maps to SQL `date` and `TimeOnly` maps to SQL `time`.
- Fixed SQL Server verification DDL for computed-column index scenarios by requiring `SET QUOTED_IDENTIFIER ON`.

### Verification

- Added mock and real DB tests for UPPER_SNAKE ResultSet mapping to PascalCase positional records.
- Added generated mapper tests through a monitored `DbDataReader` wrapper.
- Added raw SQL `DateOnly` and `TimeOnly` parameter metadata tests.
- Added real SQL Server verification that the `SET QUOTED_IDENTIFIER ON` computed-column index path creates the expected index.

## 2.2.0 Summary

### Added

- Added `MarsPolicy` with `Auto`, `ForceEnable`, and `Disabled` modes so MARS behavior no longer depends on manual connection string edits.
- Added `EnableObservability` as the single observability configuration surface.
- Added compiled accessor support for `BulkInsert`, replacing reflection `GetValue` access on the hot path.

### Changed

- `EnableOpenTelemetry` became historical/obsolete in favor of `EnableObservability`.
- Nullable mapping changed from DB `NULL` becoming default value bugs to DB `NULL` mapping to `null`.
- Date/time mapping moved from only `DateTime`/`TimeSpan` toward `DateOnly`/`TimeOnly` support.
- Source generator work was reduced by extracting booleans instead of repeatedly passing full `Compilation` state.
- Health check throttling changed from a hard-coded one-second behavior to the `HealthCheckThrottleSeconds` option.

## 2.1 Summary

### Added

- v2.1 completed the earlier feature-completeness push: BulkCopy plus TVP bulk paths, transaction isolation API support, `IDbInterceptor`, automatic JSON mapping extensions, OpenTelemetry-oriented pool metrics, query cache extensions, and Always Encrypted verification/documentation.

### Historical QA Summary

- Historical reports recorded v2.1 as a 100/100 completeness release with all 12 tracked categories at S+.
- The old QA summary reported Release build success, package creation success, AOT compatibility, 114/114 integration tests passing, and no intentional breaking changes because the feature work was additive.
- Historical performance notes reported BulkInsert 10K under 500 ms, BulkCopy and TVP bulk paths both under 500 ms in the covered cases, 100 concurrent SELECT operations succeeding, and connection-pool pressure scenarios stabilizing.
- Older coverage reports are retained here only as summary context. Active docs should not keep detailed historical test inventories that no longer describe the current verification surface.

## 1.x To 2.x Summary

### Changed

- The main entry point changed from `IDbContext` to `IDbSession`.
- Query execution paths were consolidated from many separate paths into the 3-stage Fluent API model.
- Error handling moved from thrown exceptions as the primary application contract to `DbResult<T>` result values.
- Configuration changed from a single `ConnectionStringName` to `ConnectionStringNames` for multi-database support.
- Transaction APIs moved from void/exception outcomes toward explicit `DbResult<bool>` results.
- Public API style changed from multiple role-specific interfaces to Fluent API Only.

## Documentation Policy

- Active usage docs should describe the current API and avoid embedding version-specific release history.
- Version-specific additions, fixes, migration summaries, release notes, old QA summaries, and security verification history belong in this file.
- Historical reports should be summarized rather than copied into active docs. Keep enough context for migration and audit work, but avoid preserving obsolete test inventories as current guidance.
- Internal release validation details belong in maintainer-only runbooks, not active consumer usage docs.
- Never include actual secret values, tokens, passwords, or full connection string values in history. At most, document key names or presence requirements when they are needed to understand verification policy.
