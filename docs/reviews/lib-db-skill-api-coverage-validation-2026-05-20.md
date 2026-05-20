# Lib.Db Skill API Coverage Validation

Date: 2026-05-20

Target skill: `.claude/skills/lib-db`

Target API source: `Lib.Db/Lib.Db`

## Verdict

The current `lib-db` skill is not sufficient for an AI to use every Lib.Db feature from the skill alone.

It is adequate as a safe consumer quick-start for:

- dependency injection basics
- production-safe connection/security posture
- stored procedure execution
- intentional parameterized raw SQL reads
- `DbResult<T>` success/failure handling
- single-row and streaming query basics
- transaction basics
- common DTO result mapping
- `DateOnly` and `TimeOnly` parameter binding
- simple `[TvpRow]` and `[DbResult]` source-generator usage

It is not yet a complete Lib.Db API skill.

## Current Skill Scope Evidence

The current skill package contains only:

- `SKILL.md`
- `references/runtime-api.md`
- `references/security-guardrails.md`
- `references/mapping-contracts.md`
- `references/tvpgen-guide.md`
- `references/examples.md`

The skill's strongest coverage areas are visible in:

- `.claude/skills/lib-db/SKILL.md:29` reference map
- `.claude/skills/lib-db/references/runtime-api.md:36` fluent execution
- `.claude/skills/lib-db/references/runtime-api.md:66` `DbResult<T>` handling
- `.claude/skills/lib-db/references/runtime-api.md:87` streaming rows
- `.claude/skills/lib-db/references/runtime-api.md:111` transactions
- `.claude/skills/lib-db/references/security-guardrails.md:47` raw SQL policy
- `.claude/skills/lib-db/references/mapping-contracts.md:5` result column mapping
- `.claude/skills/lib-db/references/tvpgen-guide.md:17` TVP rules
- `.claude/skills/lib-db/references/examples.md:5` through `.claude/skills/lib-db/references/examples.md:142` consumer examples

## Coverage Gaps

### P1: Bulk Insert Is Not Covered

Source evidence:

- `Lib.Db/Contracts/Entry/DbEntryContracts.cs:86` exposes `BulkInsertAsync<T>(...)`.
- `Lib.Db/Contracts/Core/Primitives.cs:274` exposes `BulkInsertOptions`.

Skill evidence:

- No current skill file mentions `BulkInsertAsync`, `BulkInsertOptions`, or bulk insert usage.

Impact:

An AI using only the skill cannot correctly choose or explain Lib.Db's bulk insert API, options, or AOT/reflection limitations.

### P1: Caching APIs Are Not Covered

Source evidence:

- `Lib.Db/Extensions/HybridCacheExtensions.cs:37` exposes `AddLibDbHybridCache(...)`.
- `Lib.Db/Extensions/QueryCacheExtensions.cs:184` exposes `GetOrQueryAsync<T>(...)`.

Skill evidence:

- `runtime-api.md` has a high-level caching warning, but it does not document registration, cache query patterns, keys, invalidation, or `GetOrQueryAsync<T>`.

Impact:

The skill cannot teach cache-enabled Lib.Db usage from scratch.

### P1: Health Checks, Hosted Services, And Interceptors Are Not Covered

Source evidence:

- `Lib.Db/Extensions/LibDbHealthCheckExtensions.cs:31` exposes `AddLibDbHealthCheck(...)`.
- `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs:222` exposes `AddLibDbHostedServices(...)`.
- `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs:326` exposes `AddLibDbInterceptor<...>(...)`.
- `Lib.Db/Contracts/Execution/StrategyAndInterceptionContracts.cs:147` exposes `IDbCommandInterceptor`.

Skill evidence:

- No current skill file documents health check registration, hosted services, command interception lifecycle, or interceptor implementation patterns.

Impact:

An AI using only the skill will miss operational integration and extension points.

### P1: Schema Maintenance Is Undercovered

Source evidence:

- `Lib.Db/Core/DbSession.cs:125` exposes `Schema`.
- `Lib.Db/Contracts/Entry/DbEntryContracts.cs` exposes schema-maintenance stage contracts through public entry contracts.

Skill evidence:

- The skill mentions `Schema` only indirectly in the SQL Server computed-column note. It does not teach schema-maintenance stage usage.

Impact:

Schema setup/update APIs cannot be reliably used from the skill alone.

### P2: Multiple Result Sets Are Not Covered

Source evidence:

- `Lib.Db/Contracts/Execution/DbExecutionContracts.cs:121` exposes `IMultipleResultReader`.
- Fluent execution exposes `QueryMultipleAsync`.

Skill evidence:

- No current skill file documents `QueryMultipleAsync` or multiple result readers.

Impact:

Stored procedures returning multiple result sets are outside the skill's usable coverage.

### P2: DI And Options Surface Is Partial

Source evidence:

- `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs:94` exposes `AddHighPerformanceDb(...)`.
- `Lib.Db/Extensions/LibDbOptionsExtensions.cs:68` exposes `AddLibDbOptions(...)`.
- `Lib.Db/Extensions/LibDbOptionsExtensions.cs:103` exposes `AddLibDbOptionsFromConfiguration(...)`.
- `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs:203` exposes `AddLibDbResilience(...)`.
- `Lib.Db/Configuration/LibDbOptions.cs:426` exposes `Resilience`.
- `Lib.Db/Configuration/LibDbOptions.cs:444` exposes `ResilienceOptions`.

Skill evidence:

- The skill documents only the most common `AddLibDb(...)` shapes and a few security options.

Impact:

The skill is fine for common setup, but not enough for full options customization.

### P2: TVP/Generator Surface Is Partial

Source evidence:

- `Lib.Db/Contracts/Models/GenerateTvpFromDbAttribute.cs:28` exposes `GenerateTvpFromDbAttribute`.
- Public TVP model and infrastructure contracts include TVP column/type metadata such as `TvpColumnInfo` and `TvpTypeName` usage.

Skill evidence:

- The skill covers simple `[TvpRow]` and `[DbResult]` usage, but not DB-first TVP generation, TVP metadata, column ordering metadata, or type-name customization.

Impact:

The skill covers simple TVP authoring, but not the full generator feature set.

### P2: Error/Diagnostics Surface Is Partial

Source evidence:

- `Lib.Db/Contracts/Core/DbResult.cs` exposes `DbError`, `DbErrorKind`, `SqlErrorCode`, `IsTransient`, severity, and message fields.
- `Lib.Db/Diagnostics/LibDbTelemetry.cs:17` exposes `LibDbTelemetry`.
- `Lib.Db/Diagnostics/SqlServerPlanCacheAnalyzer.cs` exposes plan-cache analysis behavior internally through diagnostics infrastructure.

Skill evidence:

- The skill teaches `IsSuccess` and `Error?.SqlErrorCode`, but not error kind/transient/severity handling or telemetry metric surfaces.

Impact:

The skill can support basic error handling, but not robust retry/diagnostics decisions.

### P2: AOT And Trimming Guidance Is Missing

Source evidence:

- Public APIs include `RequiresUnreferencedCode` and `RequiresDynamicCode` annotations around bulk insert and cache helpers.
- `AddLibDb(IConfiguration)` includes trimming/dynamic-code warnings, while `AddLibDb(Action<LibDbOptions>)` is the safer shape for AOT-sensitive applications.

Skill evidence:

- The current skill intentionally removed AOT terms to avoid internal verification-workflow leakage.

Impact:

That removal was correct for avoiding repository verification content, but the consumer skill still needs AOT/trimming usage guidance if it aims to cover all public API use.

## Security Assessment

No direct unsafe credential or SQL execution pattern was found in the current skill package:

- release/version references: 0
- internal verification workflow references: 0
- broad `paths` frontmatter: 0
- unsafe credential examples: 0
- known bad API patterns from the previous review: 0

However, the incompleteness has an indirect safety risk: if an AI is asked for an uncovered feature, it may infer or invent API usage. For database libraries, invented API usage can lead to raw SQL misuse, incorrect transaction semantics, missing redaction, or insecure operational defaults.

## Recommendation

Do not claim this skill enables every Lib.Db feature yet.

Recommended next redesign step:

1. Keep the current consumer-safe core.
2. Add new reference files for missing API families:
   - `references/options-and-registration.md`
   - `references/bulk-insert.md`
   - `references/schema-maintenance.md`
   - `references/caching.md`
   - `references/health-interceptors.md`
   - `references/error-diagnostics-resilience.md`
   - `references/aot-trimming.md`
3. Expand `SKILL.md` reference map to route to those files.
4. Add a coverage validation script outside the shipped skill package, or keep it as repo-maintainer tooling, not inside `.claude/skills/lib-db`.

Bottom line: the current skill is a good safe starter skill, not a complete Lib.Db API skill.
