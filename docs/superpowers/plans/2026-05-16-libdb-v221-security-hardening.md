# Lib.Db v2.2.1 Security Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise Lib.Db v2.2.1 from "no P0/P1 blockers found" to an evidence-backed early-90s security-hardening posture without claiming complete security.

**Architecture:** Keep all changes non-breaking by adding explicit safe APIs, opt-in runtime policy, production-only connection-string validation, sanitized diagnostic boundaries, and observable shared-memory fallback state. Existing APIs remain source-compatible, but docs and tests make unsafe boundaries explicit.

**Tech Stack:** .NET 10, C#, Microsoft.Data.SqlClient, Microsoft.Extensions.Options, Microsoft.Extensions.Diagnostics.HealthChecks, xUnit/FluentAssertions.

---

### Task 1: Raw SQL Safe API And Documentation

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbStageContracts.cs`
- Modify: `Lib.Db/Core/DbSession.cs`
- Modify: `Lib.Db/Fluent/DbRequestBuilder.cs`
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/DbRequestBuilderTests.cs`
- Modify: `README.md`

- [x] Add a failing test proving `SqlInterpolated(FormattableString)` creates parameterized SQL without requiring a cast.
- [x] Add `SqlInterpolated(FormattableString)` to the public stage contract, `DbSession`, and `DbRequestBuilder` by delegating to the existing FormattableString implementation.
- [x] Strengthen XML docs for `Sql(string)` so raw SQL is explicit and parameterized APIs are preferred.
- [x] Update README examples to use production-safe connection string defaults.

### Task 2: Raw SQL Runtime Policy

**Files:**
- Modify: `Lib.Db/Configuration/LibDbOptions.cs`
- Modify: `Lib.Db/Configuration/Internal/LibDbConfig.cs`
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- Modify: `Tests/Lib.Db.IntegrationTests/Executors/SqlDbExecutorTests.cs`

- [x] Add a failing test for opt-in denial of all `CommandType.Text` execution.
- [x] Add `RawSqlPolicy` with default `Allow`, plus `DenyAllText` and `DenyWriteText`.
- [x] Enforce the policy before command execution and dry-run logging.
- [x] Keep default behavior fully compatible.

### Task 3: Production Connection Security Profile

**Files:**
- Modify: `Lib.Db/Configuration/LibDbOptions.cs`
- Modify: `Lib.Db/Configuration/Internal/LibDbConfig.cs`
- Modify: `Lib.Db/Configuration/LibDbOptionsValidator.cs`
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/OptionsValidationTests.cs`

- [x] Add failing tests for production profile rejecting `TrustServerCertificate=True`, weak encryption, and `sa`.
- [x] Add `ConnectionSecurityProfile` defaulting to `Development`.
- [x] Validate production profile without printing connection-string values.
- [x] Keep development/test behavior compatible.

### Task 4: Diagnostic Boundary And Cache Fallback Visibility

**Files:**
- Modify: `Lib.Db/Contracts/Infrastructure/IDbInterceptor.cs`
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- Modify: `Lib.Db/Caching/SharedMemoryCache.cs`
- Modify: `Lib.Db/Extensions/LibDbHealthCheckExtensions.cs`
- Modify: `Tests/Lib.Db.IntegrationTests/Diagnostics/DbDiagnosticsTests.cs`
- Modify: `Tests/Lib.Db.IntegrationTests/Caching/SharedMemoryMappedCacheTests.cs`

- [x] Add `DiagnosticCommandText` to interception context while preserving raw `CommandText`.
- [x] Ensure tests cover sanitized diagnostic command text in default mode.
- [x] Expose `SharedMemoryCache.IsFallbackMode` and `CacheMode`.
- [x] Add health-check data for shared-memory cache fallback when the cache is registered.

### Task 5: Verification And Persistent Agent Review

**Files:**
- No production files expected.

- [x] Run focused tests for new behavior.
- [ ] Send implementation summaries to Parfit, Sartre, and Pauli in their existing agent threads.
- [ ] Run product builds, integration test build, full DB test, `git diff --check`, LF/BOM scan, and sensitive literal scan.
- [ ] Commit all v2.2.1 changes and push only `v2.2.1`.
