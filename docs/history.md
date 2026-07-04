# Lib.Db History

Current usage docs are intended to stay version-neutral and describe the current API. This file owns version-specific history: release changes, verification summaries, migration notes, and archived report summaries that should not remain scattered through active guides.

## 2.6.2 Summary

### Fixed

- Tightened `RawSqlPolicy.DenyWriteText` so bare and qualified `sp_executesql` text commands that wrap mutating SQL are blocked as write-like raw SQL. `DenyWriteText` remains a conservative guardrail, not a full SQL parser or permission boundary.
- Hardened `SharedMemoryCache` with keyed integrity metadata, user/path/isolation-key namespacing, and quota enforcement so quota-rejected or quota-unverified writes are treated as cache misses rather than written to fallback state.
- Aligned manual `LibDbOptions` configuration binding with the supported concrete `LibDbConfig` values, including `Mars`, resilience, schema warmup, diagnostics, chaos, and shared-memory cache options.

### Changed

- v2.6.2 freezes the public bulk-copy surface for this hardening patch. Optional `TableLock` and `KeepNulls` destination options are deferred to the v2.6.3 brainstorming backlog.
- The checked-in verification manifest keeps its `v2.6.0` workflow manifest marker; the NuGet package version is sourced from `Lib.Db.csproj`.

### Verification

- Added regression coverage for raw SQL `sp_executesql` policy cases, shared-memory cache isolation/integrity/quota behavior, and configuration binding parity.
- Added release metadata and docs/API coverage guards for the v2.6.2 package surface.
## 2.6.1 Summary

### Fixed

- Fixed typed multi-result `ReadMultipleAsync<...>()` failure handling so SQL Server error numbers, severity, kind, and transient classification survive both reader-creation and result-set read failures.
- Fixed `.With<TParams>(...)` when `TParams` is statically `object` by binding runtime concrete DTO public properties instead of silently treating every stored procedure parameter as missing/null. Dictionary and `DataRow` runtime parameter bags remain supported.

### Verification

- Added regression tests for multi-result SQL error-code preservation, including unit coverage for user-defined 51740 and constraint 2627 mappings plus local verification DB coverage for stored-procedure and mid-stream multi-result failures.
- Added final `SqlCommand.Parameters` binding tests for `object`-typed concrete DTOs, dictionary runtime values, and fail-fast empty object parameters.

## 2.6.0 Summary

### Added

- Added full stored procedure `Output`, `InputOutput`, and `ReturnValue` copy-back support for explicit `SqlParameter` values across non-streaming, streaming, multiple-result, and `DataRow` parameter paths.
- Clarified that input-consuming `OUTPUT` parameters require explicit `SqlParameter(Direction = InputOutput)` because SQL Server catalog metadata exposes regular `OUTPUT` parameters as output-capable, not caller-intent input-output contracts.
- Added reader command lease handling so `QueryAsync<T>()` copies output values after the async sequence is fully consumed or cleanly disposed, while raw `QueryMultipleAsync()` copies output values only after `IMultipleResultReader.DisposeAsync()` succeeds.
- Added transactional `DataRow` output mapping with row/source rollback on copy-back failure.

### Changed

- Explicit caller-owned `SqlParameter` values are cloned into command-owned parameters before execution, then copied back to the original only after successful completion.
- `InputOutput` explicit parameter input values are preserved and schema-normalized before execution.
- `ReturnValue` parameters follow the SQL Server integer return-code contract; non-`Int` explicit return parameters are rejected.
- `DataRow` schema binding now honors explicit `SqlParameter` cells; scalar `ReturnValue` columns remain excluded, so return codes require explicit `SqlParameter` cells.
- Strict schema binding now rejects missing or read-only `Output`/`InputOutput` copy-back targets before execution, and rejects output parameter name collisions after canonical underscore-insensitive normalization.
- DataRow output failure wrapping avoids preserving raw output values in public inner exception chains.
- Public `DbResult<T>` failure paths no longer retain raw provider exceptions in `DbError.InnerException` or raw SQL/SP command text in fluent execution error labels.
- Session disposal failures no longer retain raw cleanup exceptions in the public aggregate exception tree, and shared-memory cache, process-slot, and mutex-helper names/logs no longer include caller-supplied isolation keys in clear text.
- Maintainer verification wrappers now load `Set-LibDbVerificationEnvironment.local.ps1` only through explicit `-UseLocalEnvironment` opt-in, and GitHub release/publish workflows use an ephemeral SQL Server password instead of a long-lived SQL verification secret.
- Release verification build, publish, pack, coverage, and benchmark paths now disable shared Roslyn compilation so a failed `dotnet build-server shutdown` cannot leave verification-owned compiler servers behind.

### Verification

- Added mapper coverage for non-strict targetless output-only execution, strict output-target rejection, output parameter name collision rejection, DataRow output copy-back, explicit source success, `InputOutput` schema binding, `ReturnValue` source binding, rollback, ambiguous/read-only/expression columns, and sanitized failure wrapping.
- Added reader lease and non-streaming output tests covering scalar/single-row execution, async stream completion/disposal, multiple-result reader disposal, cancellation/failure boundaries, and command cleanup.
- Verified on 2026-06-13 with package and integration-test builds, maintainer direct-MTP wrapper runs for the mapper, output-parameter, executor, reader-lease, schema-preload, write-flow, status-branch, verification-entrypoint, and unit-test slices, plus script parsing and diff hygiene checks.
- Added regression tests for output target canonicalization, output parameter name collision rejection, strict targetless output rejection, public failure redaction, local verification environment opt-in, and GitHub workflow SQL credential hardening.

## 2.5.0 Summary

### Added

- Added `Lib.Db.Tools` as a separate, non-packable no-DB contract validate/report MVP. It reads checked-in `libdb.contracts.json` files, compares stored procedure/TVP/bulk target contracts, and emits deterministic JSON or Markdown reports.
- Added `docs/contracts/libdb-contracts-v1.md` to define the strict no-secret v1 contract shape, CLI commands, exit codes, and unsupported-command boundary.
- Added v2.5.0 design specs for generator, contract tooling, SQL Server Change Tracking adapter, and release infra hardening.

### Changed

- `Lib.Db` runtime package version moved to `2.5.0` while keeping generator and SQL Server Change Tracking out of the core runtime.
- Release package verification now builds only the allowlisted `Lib.Db` package through `Invoke-ReleasePackage.ps1`; `Lib.Db.Tools` remains `IsPackable=false`.
- Release verification writes dry-run package artifacts under an excluded release-package artifact directory, and workflows exclude package artifacts from verification artifact uploads.
- Repository-local `.agents` and `.claude` Lib.Db skills now distinguish the runtime package from `Lib.Db.Tools` and keep bulk mutation guidance aligned.

### Verification

- Added guard tests for package allowlist, dry-run non-publish behavior, unsupported `Lib.Db.Tools` commands, SQL non-execution, contract validation/report output, and verification artifact redaction.
- Artifact scanning now inspects NuGet archives, redacts secret-like archive entry names and paths, and reports marker/key names without echoing secret-like values.

### Security

- `Lib.Db.Tools` rejects secret-bearing contract fields and redacts secret-like object names, paths, and connection-string-shaped values in reports and failures.
- `Lib.Db.Tools` does not connect to SQL Server, inspect live metadata, execute SQL, or mutate databases in the v2.5.0 MVP.
- Publish workflows push an exact allowlisted package path instead of a wildcard package glob.

## 2.4.0 Summary

### Added

- Added HybridCache tag overloads for grouped logical invalidation. Tags are app-owned non-sensitive labels, reject invalid values, reserve wildcard `*`, and enforce a 32 distinct tag ceiling after ordinal dedupe.
- Added typed `ReadMultipleAsync<...>` helpers in `Lib.Db.Extensions` for common two-, three-, and four-ResultSet stored procedure patterns. The helpers dispose readers and map read failures to redacted `DbResult<T>` failures.
- Added AOT-safe `BulkShape<T>` bulk mutation APIs for insert, update, delete, upsert, and merge. These overloads avoid reflection and use `SqlBulkCopy` plus staged set-based DML. Existing reflection-based `BulkInsertAsync<T>` remains for compatibility.
- Documented generator, migration/contract tooling, and SQL Server Change Tracking adapter as v2.5.0-or-later roadmap items. v2.4.0 keeps these out of the core runtime implementation.

### Changed

- `AddLibDb()` is provider-neutral by default. It no longer registers the shared-memory `IDistributedCache` implementation implicitly.
- Host applications that need L2 cache behavior now own the `IDistributedCache` provider choice, such as Redis, SQL Server, PostgreSQL, NCache, or another cross-platform provider.
- `AddLibDbSharedMemoryCache()` remains available as an explicit local-host opt-in for deployments that intentionally want the legacy shared-memory path.
- Lib.Db diagnostics now report the detected cache topology so maintainers can distinguish local-only behavior, host-owned L2, and explicit shared-memory opt-in.
- Bulk write docs now distinguish direct `SqlBulkCopy` destination flags from staged target DML behavior and document direct insert `UseTransaction = false` as a non-atomic opt-out.

### Verification

- The release gate includes provider-neutral caching checks: existing host-owned `IDistributedCache` providers are preserved, shared-memory opt-in rejects pre-existing providers, and providers added after shared-memory opt-in fail Generic Host startup.
- Verification assets, script banners, package metadata, and CI gate labels were updated for v2.4.0.
- Native AOT GitHub Actions verification now runs as a Windows/Linux/macOS x64 matrix from PRs and non-main manual dispatches, while direct `main` branch execution is blocked.
- Repository-local consumer guidance moved from `.agent/skills/lib-db` to `.agents/skills/lib-db`.
- Release verification includes AOT-safe bulk shape/reader/SQL builder/mutation coverage, HybridCache tag behavior, typed QueryMultiple helper behavior, Native AOT reachability for the concrete bulk path, artifact scanning, and generated artifact tracking.

### Security

- Cache memory and L2 provider ownership moved out of the default library registration path. This reduces implicit OS-specific behavior and makes provider credentials, durability, eviction, and network exposure the host application's responsibility.
- Lib.Db does not treat provider-name detection as a security boundary. Cache topology detection is diagnostic-only and must not be used to grant trust or weaken validation.
- Public failure paths for cache, typed multi-result reads, and AOT-safe bulk writes use generic/redacted errors rather than exposing raw SQL, provider exceptions, row values, cache payloads, tenant/user identifiers, connection string values, or public `InnerException` details.
- Release artifact scanning is required to cover connection strings, passwords, tokens, API keys, client secrets, bearer/SAS markers, SQL parameter values, row values, cache payload values, and tenant/user identifier markers while printing only file paths and marker/key names.

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
