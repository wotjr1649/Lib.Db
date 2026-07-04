# Lib.Db v2.6.2 Hardening Implementation Plan

> **For agentic workers:** implement only after the design review gate below reaches findings 0. Use TDD for every behavior change. After each task: write/confirm failing tests, implement the smallest passing change, run targeted verification, run review/security checks for the touched surface, fix findings to zero, commit only that task, then proceed.

**Goal:** Ship v2.6.2 as a focused hardening patch that closes the validated security findings from the Lib.Db v2.6.1 audit, verifies the manually applied dependency updates, fixes configuration/release/docs drift, and adds only low-risk SQL Server usability improvements when they remain additive and verified.

**Branch:** `v2.6.2`

**Status:** design gate passed. Implementation entry approved after second-round security, quality, and release-verification reviews returned findings 0.

**Tech Stack:** .NET 10/C# preview, Microsoft.Data.SqlClient 7.0.x, SQL Server local verification DB, xUnit v3, FluentAssertions, Microsoft.Extensions 10.x, Polly 8.x.

---

## Current Inputs

- Codex Security rerun completed with 2 reportable medium findings:
  - `RawSqlPolicy.DenyWriteText` misses bare procedure execution through `sp_executesql`.
  - `SharedMemoryCache` file namespace does not include the same isolation boundary used by mutex identity and uses unkeyed CRC integrity.
- User manually updated package versions before this plan:
  - `Microsoft.Data.SqlClient` 7.0.1 to 7.0.2.
  - Microsoft.Extensions family 10.0.8 to 10.0.9 where referenced.
  - resilience/hybrid cache related packages 10.6.0 to 10.7.0 where referenced.
  - Polly 8.6.6 to 8.7.0.
  - test infrastructure packages updated in integration-test projects.
- The manual package update must be reviewed and verified as Task 0. Do not revert user changes unless the user explicitly requests it.
- v2.6.3 brainstorming-only items are recorded in `docs/roadmap/v2.6.3-brainstorming-backlog.md`.

## Official References

- SQL Server `sp_executesql` executes a dynamic Transact-SQL statement or batch and Microsoft warns that runtime-compiled SQL can expose applications to SQL injection when not parameterized: <https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-executesql-transact-sql?view=sql-server-ver17>
- SQL Server `EXECUTE` can execute command strings, system stored procedures, user stored procedures, CLR procedures, functions, and pass-through commands: <https://learn.microsoft.com/en-us/sql/t-sql/language-elements/execute-transact-sql?view=sql-server-ver17>
- `Microsoft.Data.SqlClient.SqlBulkCopyOptions` exposes `KeepIdentity`, `CheckConstraints`, `TableLock`, `KeepNulls`, `FireTriggers`, `UseInternalTransaction`, and related bulk-copy flags: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopyoptions?view=sqlclient-dotnet-core-6.1>
- `SqlBulkCopy.NotifyAfter` and `SqlRowsCopied` support progress notifications for bulk copy operations: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy.notifyafter?view=sqlclient-dotnet-core-6.1> and <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy.sqlrowscopied?view=sqlclient-dotnet-core-6.1>
- .NET Native AOT guidance for libraries recommends targeting the latest supported TFM and at least `net8.0` for AOT analysis warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/>
- .NET trimming guidance requires libraries to avoid or annotate trim-unsafe patterns and verify warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming>
- Context7 confirmed `/dotnet/sqlclient` for current Microsoft.Data.SqlClient documentation and `/dotnet/docs` for current .NET AOT/trimming guidance.

## v2.6.2 Non-Goals

Do not implement these in v2.6.2:

- Full SQL parser.
- DB introspection/scaffold.
- Generator package.
- Public API large redesign.
- Legacy bulk default breaking change.
- Broad dependency churn.

These are v2.6.3 brainstorming backlog items, not v2.6.2 work.

## Focused Security Checkpoint

Sensitive surface:

- SQL command execution policy, dynamic SQL, `CommandType.Text`, stored procedure execution syntax, shared-memory cache files, file namespaces, local-user/process boundaries, logs/diagnostics, dependency updates, and test databases.

Trust boundaries:

- Application caller to Lib.Db execution APIs.
- Lib.Db process to local SQL Server.
- Lib.Db process to local shared-memory cache files.
- Local cache writer to local cache reader across isolation keys and user scopes.
- Build/test tooling to package/dependency inputs.

Untrusted input:

- Raw SQL text, cache keys, cache values, cache files, configuration values, package restore artifacts, docs/search results, subagent output, and test database data.

Abuse cases:

- A caller hides mutating SQL inside `sp_executesql` while `RawSqlPolicy.DenyWriteText` is expected to block mutating text.
- Two tenants/processes with different isolation keys read or overwrite the same shared cache file.
- A local writer tampers with shared cache bytes and the reader accepts attacker-controlled payload as a cache hit.
- Oversized cache values force unexpected mapped-file growth despite a configured max size.
- Manual dependency updates introduce incompatible package behavior or stale XML formatting hides future diffs.
- Diagnostics expose raw SQL, parameters, connection strings, or private host material.

Lane decision:

- Main Codex owns integration, verification, and commits.
- Subagents perform bounded design/code-review/security review only for independent findings.
- No implementation worker may write code until the design review gate reaches findings 0.

Verification/mitigation:

- TDD regression tests for each validated finding.
- Targeted unit/integration tests after each task.
- AOT/trimming and package/dependency checks before release candidate.
- Redacted summaries only; no full connection strings, credential output, private hostnames, or raw sensitive logs in docs/final answers.

Full security scan:

- Required after P0/P1 security tasks land and before release candidate.
- Required again if any implementation changes command execution, file namespace, cache authentication, package install behavior, or diagnostics data exposure beyond the planned scope.

## Design Review Gate

Implementation starts only when all rows are complete and findings are 0.

| Reviewer | Scope | Status | Findings |
| --- | --- | --- | --- |
| Security design reviewer | Raw SQL policy, shared-memory cache isolation/integrity, diagnostics, dependency-risk boundaries | PASS | 0 |
| Code quality reviewer | Task slicing, TDD order, compatibility, over-engineering, duplicate/dead/boilerplate risk | PASS | 0 |
| Release verifier | Dependency update verification, AOT/trimming, SQL Server verification, packaging, docs/release gates | PASS | 0 |
| Main integrator | Conflict resolution and final implementation-entry decision | ENTER IMPLEMENTATION | 0 |

Gate rules:

- Any review finding must be either fixed in this plan or explicitly deferred to v2.6.3 with rationale.
- A deferred item cannot be a blocker for the two validated security findings.
- Findings 0 means no design-blocking issue remains; it does not mean implementation risk is zero.
- Subagent results are advisory. Main Codex must integrate, inspect conflicts, and verify.

## Implementation Discipline

For every task:

1. Confirm the exact files and behavior touched.
2. Add or update tests first.
3. Run the smallest command that proves the test is red for the intended reason, unless the test is a pure compile-time guard where existing code cannot compile with the new assertion.
4. Implement the smallest change that makes the test pass.
5. Run targeted verification.
6. Run review/security checks for the touched surface.
7. Fix all task findings to zero.
8. Run `git diff --check`.
9. Commit only the task's files with an atomic message.
10. Re-check `git status --short` before starting the next task.

Do not mix user/manual dependency updates with unrelated source edits unless the current task explicitly owns those files.

## Task 0: Dependency Update Review And Baseline

**Purpose:** Validate the user's manual package updates before relying on them for v2.6.2 work.

**Files:**

- `Lib.Db/Lib.Db.csproj`
- `Verification/projects/Lib.Db.Benchmarks/Lib.Db.Benchmarks.csproj`
- `Verification/projects/Lib.Db.ChaosHarness/Lib.Db.ChaosHarness.csproj`
- `Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj`
- `Verification/baselines/aot-warnings.json` if provider-version baseline drift is confirmed.

**Checks:**

- Review all package version changes for patch/minor risk.
- Normalize accidental XML formatting churn only if it is unrelated to intended package updates and doing so preserves the user's versions.
- Run restore/build against `Lib.Db.slnx`.
- Run package vulnerability and deprecation checks for every project touched by the manual update. If solution-level package checks are supported by the installed SDK, run them too; otherwise run each changed project explicitly.
- Run existing focused security/cache tests through `Verification/scripts/Invoke-Tests.ps1`, not raw `dotnet test`.
- Run AOT validation before accepting the SqlClient provider update. If the baseline still records the previous provider package version, review the new warnings and update `Verification/baselines/aot-warnings.json` intentionally in this task.

**Verification:**

```powershell
dotnet restore .\Lib.Db.slnx
dotnet build .\Lib.Db.slnx --no-restore
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*SqlDbExecutorSecurityPolicyTests*"
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*SharedMemory*"
dotnet list .\Lib.Db\Lib.Db.csproj package --vulnerable --include-transitive
dotnet list .\Lib.Db\Lib.Db.csproj package --deprecated --include-transitive
dotnet list .\Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj package --vulnerable --include-transitive
dotnet list .\Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj package --deprecated --include-transitive
dotnet list .\Verification\projects\Lib.Db.ChaosHarness\Lib.Db.ChaosHarness.csproj package --vulnerable --include-transitive
dotnet list .\Verification\projects\Lib.Db.ChaosHarness\Lib.Db.ChaosHarness.csproj package --deprecated --include-transitive
dotnet list .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj package --vulnerable --include-transitive
dotnet list .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj package --deprecated --include-transitive
pwsh -NoProfile -File .\Verification\scripts\Invoke-Aot.ps1
git diff --check
```

**Optional solution-wide package check if supported by the installed SDK:**

```powershell
dotnet list .\Lib.Db.slnx package --vulnerable --include-transitive
dotnet list .\Lib.Db.slnx package --deprecated --include-transitive
```

**Exit:** dependency update accepted or a documented rollback/fix request is raised. Commit only package, baseline, and formatting files if accepted.

## Task 1: Block Bare `sp_executesql` Under `DenyWriteText`

**Purpose:** Close the raw SQL policy bypass without adding a full parser.

**Files:**

- `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Unit/SqlDbExecutorSecurityPolicyTests.cs`
- Relevant docs/skill references for raw SQL policy wording.

**Tests first:**

- `sp_executesql N'DELETE FROM dbo.Users'` is blocked.
- `sys.sp_executesql N'UPDATE dbo.Users SET Name = N''x'''` is blocked.
- `[sys].[sp_executesql] N'DROP TABLE dbo.Users'` is blocked.
- Leading whitespace/comments/semicolons before `sp_executesql` are blocked.
- String literals and normal identifiers containing the text `sp_executesql` are not false positives.

**Design constraints:**

- Keep `RawSqlPolicy` documented as a guardrail, not a complete SQL parser.
- Block only normalized first executable multipart identifiers for `sp_executesql` and `sys.sp_executesql`.
- Do not block arbitrary stored procedure names in v2.6.2.
- Keep `DenyAllText` behavior unchanged.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*SqlDbExecutorSecurityPolicyTests*"
```

**Exit:** regression test exists, bypass is blocked, false-positive cases pass, review findings 0, commit.

## Task 2: Align SharedMemoryCache Storage Namespace With Isolation Boundary

**Purpose:** Ensure file paths and mutex identity use equivalent isolation material without exposing raw isolation data.

**Files:**

- `Lib.Db/Caching/SharedMemoryCache.cs`
- `Lib.Db/Caching/CachingInfrastructure.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Caching/SharedMemoryMappedCacheTests.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Caching/SharedMemorySecurityTests.cs`
- Relevant cache docs/skill references.

**Tests first:**

- Same `BasePath` and cache key but different `IsolationKey` values cannot cross-read.
- Same `BasePath`, key, and `IsolationKey` still share as intended.
- Generated paths do not contain raw isolation key, raw user identity, raw host, or connection-like material.
- Existing flat files are treated as disposable cache misses.

**Design constraints:**

- Use a storage identity directory under `BasePath`.
- Include safe hashed/sanitized scope, user/path, and isolation material.
- Do not migrate old files.
- Do not claim stronger cross-user OS ACL guarantees than the implementation provides.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*SharedMemory*"
```

**Exit:** isolation regression is covered, no raw sensitive material in paths/logs, review findings 0, commit.

## Task 3: Add Keyed Integrity To SharedMemoryCache Files

**Purpose:** Treat cache tampering as a miss instead of accepting attacker-controlled bytes.

**Files:**

- `Lib.Db/Caching/SharedMemoryCache.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Caching/SharedMemorySecurityTests.cs`
- Cache docs/skill references if behavior is documented.

**Tests first:**

- Tampered payload returns miss.
- Tampered header returns miss.
- Legacy/unrecognized header version returns miss and does not throw.
- Valid file round-trips.
- Changing `IsolationKey` makes an existing file a miss.
- Raw key material never appears in file paths, logs, activity tags, or exceptions.

**Design constraints:**

- Add a versioned header.
- Add keyed MAC over header fields and payload using non-logged raw configured/generated isolation material with domain separation. Do not derive the MAC key from the path-safe or mutex-safe digest because those values can be observable or predictable.
- Keep raw key material and derived MAC keys out of file names, diagnostics, activity tags, exceptions, and logs.
- Avoid public opt-out.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*SharedMemorySecurityTests*"
```

**Exit:** tampering is miss-only, compatibility behavior is documented, review findings 0, commit.

## Task 4: Enforce `SharedMemoryCacheOptions.MaxCacheSizeBytes`

**Purpose:** Make the existing size option operational.

**Files:**

- `Lib.Db/Caching/SharedMemoryCache.cs`
- `Lib.Db/Configuration/LibDbOptions.cs`
- `Lib.Db/Configuration/LibDbOptionsValidator.cs` only if validation wording changes.
- Shared-memory cache tests.

**Tests first:**

- A single oversized entry does not create or resize a mapped file beyond the configured max.
- Many under-limit entries cannot grow the isolated storage namespace beyond `MaxCacheSizeBytes`.
- Expired or corrupt entries are compacted once before giving up when applicable.
- Fallback cache receives value when configured and shared-memory write is skipped.
- No fallback means cache write is dropped safely and read returns miss.

**Design constraints:**

- Do not throw from normal cache writes solely because a value is too large.
- Prefer fallback/drop with safe logging.
- Keep diagnostics redacted.
- v2.6.2 defines `MaxCacheSizeBytes` as an aggregate quota per isolated storage namespace. Before a write, compute projected total storage for that namespace, compact expired/corrupt files once, then fallback/drop if the write would still exceed the quota. Do not add indexed LRU or a global cache database in this patch.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*SharedMemory*"
```

**Exit:** max-size behavior is enforced and documented, review findings 0, commit.

## Task 5: Manual Options Binder Parity

**Purpose:** Make configuration entry points bind the same supported concrete option values.

**Files:**

- `Lib.Db/Configuration/Internal/LibDbConfig.cs`
- `Lib.Db/Extensions/LibDbOptionsExtensions.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Unit/RuntimeUtilityCoverageTests.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Unit/OptionsValidationTests.cs` if needed.

**Tests first:**

- Equivalent in-memory configuration produces equivalent `LibDbOptions` for:
  - `RawSqlPolicy`
  - tracing/diagnostic booleans
  - schema/cache/resilience options already represented in `LibDbConfig`
  - `SharedMemoryCache.BasePath`
  - `SharedMemoryCache.Scope`
  - `SharedMemoryCache.MaxCacheSizeBytes`
  - `SharedMemoryCache.IsolationKey`

**Design constraints:**

- Bind concrete values only.
- Do not bind runtime services such as fallback cache.
- Preserve production profile defaults.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*RuntimeUtilityCoverageTests*"
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*OptionsValidationTests*"
```

**Exit:** parity covered by tests, review findings 0, commit.

## Task 6: Bulk Copy Scope Decision Gate

**Purpose:** Decide whether v2.6.2 remains pure hardening or includes a narrowly scoped additive SQL Server bulk-copy improvement.

**Default decision:** skip public bulk API changes in v2.6.2 unless the user explicitly approves the narrowed Task 7 after P0/P1 hardening is green.

**Rules:**

- `NotifyAfter`, `SqlRowsCopied`, callback shape, and `IProgress<long>` public API are deferred to `docs/roadmap/v2.6.3-brainstorming-backlog.md`.
- If Task 7 is skipped, record the skip rationale and proceed to Task 8.
- If Task 7 is approved, implement only the narrowed `TableLock` and `KeepNulls` mapping before Task 8.
- Task 8 docs/API/version guard must run after this decision and after any Task 7 implementation, so public surface and docs cannot pass before final scope is known.

**Exit:** Task 7 is either skipped/deferred or explicitly approved with narrow scope. Review findings 0 before continuing.

## Task 7: Optional Narrow Bulk Copy Knobs

**Purpose:** Add only low-risk SQL Server usability if it remains small, additive, and compatible.

**Files:**

- `Lib.Db/Contracts/Core/Primitives.cs`
- `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs`
- `Lib.Db/Core/DbSession.cs` only if legacy bulk path is extended without changing defaults.
- Bulk unit and verification DB tests.
- Bulk docs/skill references.

**Candidate options for v2.6.2 only:**

- `TableLock`
- `KeepNulls`

**Tests first:**

- `TableLock` and `KeepNulls` map to `SqlBulkCopyOptions` for direct bulk insert.
- Staged update/delete/upsert paths either reject misleading options or explicitly document that the options affect only stage loading.
- Defaults preserve current behavior.

**Design constraints:**

- Additive only.
- No legacy bulk default breaking change.
- No broad public API redesign.
- No progress callback, `NotifyAfter`, or `SqlRowsCopied` public API in v2.6.2.
- Skip this task if P0/P1 work introduces enough risk that v2.6.2 should remain a pure hardening patch.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*BulkShapeTests*"
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -UseLocalEnvironment -NoRestore -NoBuild -FilterClass "*BulkMutationTests*"
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -UseLocalEnvironment -NoRestore -NoBuild -FilterClass "*BulkInsertTests*"
```

**Exit:** optional knobs accepted or explicitly skipped with rationale, review findings 0, commit if implemented.

## Task 8: Version, Manifest, Docs, And API Coverage Guard

**Purpose:** Eliminate release/docs drift after the final v2.6.2 public surface is known.

**Files:**

- Version-bearing project/package files.
- `Verification/manifest.json` only after deciding whether its `version` is release metadata or a verification schema/version marker.
- `docs/01_guide.md`
- `docs/02_advanced.md`
- `docs/03_api_reference.md`
- `docs/04_operations.md`
- `docs/05_fluent_api_reference.md`
- `docs/06_cookbook.md`
- `.agents/skills/lib-db/references/*.md`
- `Verification/projects/Lib.Db.IntegrationTests/Unit/ReleaseMetadataGuardTests.cs` or the nearest existing unit-test location for one explicit guard class.

**Tests/checks first:**

- Add an explicit `ReleaseMetadataGuardTests` class that checks expected v2.6.2 package metadata, manifest semantics, curated public API entries, and required docs checklist entries.
- If `Verification/manifest.json` is release metadata, update and assert v2.6.2. If it is a verification schema/version marker, keep it stable and assert/document that meaning instead of rewriting it as release proof.
- Public docs mention `IDbSession.Schema`, `UseSchema`, AOT-safe bulk operations, cache behavior changes, and the new `sp_executesql` rule.
- API snapshot guard covers only curated public members, not a generated full-doc system.

**Design constraints:**

- Run this task after the Task 6/7 bulk decision so docs and API guard cover the final v2.6.2 public surface.
- Keep docs factual and avoid claiming `DenyWriteText` is a security boundary.
- Do not introduce a full documentation generator in v2.6.2.
- Do not expose private paths, raw hostnames, connection strings, or credential output.

**Verification:**

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -SkipTestEnvGuard -NoRestore -NoBuild -FilterClass "*ReleaseMetadataGuardTests*"
git diff --check
```

**Exit:** release metadata/docs are aligned, review findings 0, commit.

## Task 9: Release Candidate Verification And Security Rerun

**Purpose:** Prove v2.6.2 is ready to package or identify remaining blockers.

**Prerequisites:**

- Worktree is clean except intentionally generated verification artifacts expected by the verification scripts.
- `Lib.Db` project package metadata already says `2.6.2` before package verification.
- Task 8 guard has resolved whether `Verification/manifest.json` is release metadata or verification schema metadata.
- Do not run `Invoke-ReleasePackage.ps1 -PackageVersion 2.6.2` separately unless debugging the package gate; `Invoke-Verification.ps1` already runs the release-package gate.

**Verification:**

```powershell
dotnet restore .\Lib.Db.slnx
dotnet build .\Lib.Db.slnx --no-restore
pwsh -NoProfile -File .\Verification\scripts\Invoke-Tests.ps1 -UseLocalEnvironment -NoRestore -NoBuild
dotnet list .\Lib.Db\Lib.Db.csproj package --vulnerable --include-transitive
dotnet list .\Lib.Db\Lib.Db.csproj package --deprecated --include-transitive
dotnet list .\Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj package --vulnerable --include-transitive
dotnet list .\Verification\projects\Lib.Db.Benchmarks\Lib.Db.Benchmarks.csproj package --deprecated --include-transitive
dotnet list .\Verification\projects\Lib.Db.ChaosHarness\Lib.Db.ChaosHarness.csproj package --vulnerable --include-transitive
dotnet list .\Verification\projects\Lib.Db.ChaosHarness\Lib.Db.ChaosHarness.csproj package --deprecated --include-transitive
dotnet list .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj package --vulnerable --include-transitive
dotnet list .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj package --deprecated --include-transitive
pwsh -NoProfile -File .\Verification\scripts\Invoke-VerificationDb.ps1 -Db Verification -Setup -Verify
pwsh -NoProfile -File .\Verification\scripts\Invoke-Verification.ps1 -UseLocalEnvironment -BenchmarkJob Short
```

**SQL Server optional probes:**

- `Invoke-VerificationDb.ps1 -Db Verification -Setup -Verify` runs default setup/verify only.
- SQL Server 2025 syntax and feature-gap probes are optional/non-blocking in v2.6.2 unless a concrete switch or reviewed command is added before Task 9.

**Security rerun:**

- Run Codex Security scan after P0/P1 changes are complete.
- Expected result before final release: findings 0 for the v2.6.2 scope.
- If findings remain, stop implementation sequencing, triage, fix with TDD, review, verify, and commit before continuing.

**Exit:** full verification passed, security rerun findings 0 or all remaining items explicitly documented as non-blocking with user approval, final release notes ready.

## Review Checklist Per Commit

- Tests prove the changed behavior.
- No broad dependency churn beyond user-approved package updates.
- No full SQL parser, generator, scaffold, large API redesign, or breaking bulk default change.
- No secrets, full connection strings, private hostnames, credential output, or raw sensitive logs in tests/docs/output.
- No dead code introduced.
- No repeated boilerplate beyond existing local style.
- No new abstraction unless it removes real complexity.
- .NET 10/AOT/trimming posture is preserved.
- SQL Server behavior is backed by either official documentation or local verification DB tests.

## Implementation Entry Decision Template

Decision before starting Task 0:

- Security design reviewer: PASS, findings 0.
- Code quality reviewer: PASS, findings 0.
- Release verifier: PASS, findings 0.
- Main integrator: ENTER IMPLEMENTATION.
- Remaining non-blocking notes:
  - SQL Server 2025 syntax and feature-gap probes are optional/non-blocking for v2.6.2 unless a concrete switch or reviewed command is added later.
  - Bulk progress notification API shape is deferred to v2.6.3 brainstorming.

Implementation entry is allowed. Task 0 is the next task and must still pass its own TDD/review/verification/commit gate before Task 1 starts.
