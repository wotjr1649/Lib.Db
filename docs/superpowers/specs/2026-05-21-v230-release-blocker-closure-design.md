# Lib.Db v2.3.0 Release Blocker Closure Design

Date: 2026-05-21
Branch: `v2.3.0`
Current head at design time: `90700cc`

## Purpose

This design closes the remaining v2.3.0 release blockers found during final security,
API, documentation, process, and release-package reviews.

The target is not to add new user-facing features. The target is to make the existing
Runtime TVP, fluent API, package, observability, AOT, and documentation surfaces
release-safe and repeatably verifiable.

## Current Decision

Use the release-gate hardening approach.

This means the implementation must fix the known blockers and add automated checks so
the same class of blocker cannot silently reappear before v2.3.0 is published.

## Release Blockers To Close

### 1. Observability Default-Off Leak

`EnableObservability = false` currently gates most ActivitySource and `DbMetrics`
paths, but `DbConnectionFactory.CreateConnectionAsync` records connection instruments
directly through `LibDbTelemetry`.

The implementation must ensure that connection acquisition duration, connection pool
waits, and connection pool timeouts do not emit metrics when observability is disabled.

Acceptance criteria:

- `EnableObservability = false` produces no Lib.Db connection meter events.
- `EnableObservability = true` still records the same connection metrics as before.
- Instance tags must continue to use the redacted diagnostic instance id.
- A regression test must observe the meter and prove both enabled and disabled cases.

### 2. Global Observability State Ordering

`DbMetrics.IsEnabled` is process-wide. Multiple service providers or runtime configure
calls can overwrite the value based on call order.

For v2.3.0 the public model remains process-wide, but the behavior must be explicit,
tested, and not surprising:

- `AddHighPerformanceDb` applies `LibDbOptions.EnableObservability`.
- `LibDbRuntime.Configure(options, enableMetrics: null)` applies
  `options.EnableObservability`.
- `LibDbRuntime.ConfigureMetrics(bool)` remains the explicit override.
- Release notes and API docs must say observability is process-wide in v2.3.0.

Moving metrics state to a per-instance or per-service-provider model is out of scope
for v2.3.0 and should be treated as a future breaking-design candidate.

Acceptance criteria:

- Tests cover DI configuration, runtime configuration, and explicit override order.
- Tests restore global metric state after each case.
- Documentation states the process-wide behavior and recommended call order.

### 3. Stale Internal Review Documents

`docs/reviews` contains stale internal review records that contradict the current skill
surface and mention old files such as `runtime-api.md`, `security-guardrails.md`, and
`tvpgen-guide.md`.

The implementation must keep historical records from polluting consumer or AI guidance.

Recommended design:

- Move stale review records to `docs/reviews/archive/`.
- Add a strong archive banner at the top of each moved file:
  "Historical internal review. Not consumer documentation. Not current skill guidance."
- Keep `docs/reviews/README.md` as the entry point and describe the archive policy.
- Update tests so consumer docs and active skill guidance exclude archived internal
  review content.

Acceptance criteria:

- Active README/docs/skills do not contradict Runtime TVP guidance.
- Active consumer docs do not expose internal verification, benchmark, or coverage
  commands.
- Archived review files can remain in git, but tests must ensure they are excluded from
  consumer/skill validation.

### 4. Stale Package Consumer Check Artifacts

`artifacts/package-consumer-check/Lib.Db.2.3.0.nupkg` and `.snupkg` are stale and do
not represent HEAD.

The implementation must remove stale release-candidate ambiguity.

Recommended design:

- Delete stale package-consumer-check artifacts from the repository working tree if
  they are untracked or ignored.
- Regenerate package verification artifacts only under `Verification/artifacts/`.
- Ensure generated packages are ignored and never committed.

Acceptance criteria:

- No stale `Lib.Db.2.3.0.nupkg` or `.snupkg` remains under `artifacts/package-consumer-check`.
- Release package verification uses newly generated Release packages from HEAD.
- Generated artifact tracking gate confirms packages remain untracked.

### 5. Release Package Verification Gate

The current full verification gate covers tests, DB matrix, coverage, AOT, benchmark,
artifact scan, and generated artifact tracking. It does not prove the final Release
package is the package that would be published.

The implementation must add a release-package gate.

Recommended design:

- Add `Verification/scripts/Invoke-ReleasePackage.ps1`.
- Run `dotnet pack -c Release --no-restore` for `Lib.Db/Lib.Db.csproj`.
- Locate the generated `.nupkg` and `.snupkg` for the exact project version.
- Inspect package metadata without printing secrets:
  - package id is `Lib.Db`
  - version is `2.3.0`
  - repository URL is present
  - repository commit, if present, equals current HEAD
  - dependency versions match `Lib.Db.csproj`
  - README and license metadata are present
- Run `dotnet nuget verify`.

Package signing policy:

- v2.3.0 may remain unsigned if the project owner explicitly accepts unsigned packages.
- In that case, `NU3004` must be treated as an accepted unsigned-package policy result,
  not as an unexamined failure.
- If signing material is configured later, the same gate should allow strict
  `dotnet nuget verify --all` success.

Acceptance criteria:

- The release-package gate fails for stale package artifacts.
- The release-package gate passes for a HEAD-generated package with the accepted signing
  policy.
- `Invoke-Verification.ps1` runs the release-package gate in non-partial mode.
- `Verification/manifest.json` includes the release-package gate.

### 6. AOT Warning Baseline And Waiver Gate

AOT verification publishes successfully but provider-owned IL warnings remain. The
project already treats Lib.Db-owned warnings as release blockers; however, the exact
provider warning set must be tracked so new warnings cannot appear unnoticed.

The implementation must add an AOT warning baseline gate.

Recommended design:

- Add an AOT warning baseline file under `Verification/baselines/`.
- Record warning id, owning assembly/package, and rationale.
- Update `Invoke-Aot.ps1` to parse publish output.
- Fail if:
  - any warning is owned by `Lib.Db`
  - any warning id/assembly is not in the baseline
  - any baseline entry disappears without updating the baseline
- Keep provider-owned `IL2104` and `IL3053` allowed only when they match the baseline.

Acceptance criteria:

- Current known provider warnings are accepted by baseline.
- New or changed warnings fail the AOT gate.
- `docs/verification.md` documents the baseline policy and links to the baseline file.

## Secondary Fixes

The following are not release blockers by themselves, but should be handled in the same
implementation pass because they reduce review noise and test flakiness:

- Add tests for `ConnectionStringNames` error paths using connection-string-shaped
  names in missing, duplicate, blank-adjacent, and production profile cases. Existing
  code already rejects sensitive names early, but tests should prove no raw value can
  leak through later messages.
- Fix the stale `SqlGridReader.EmptyGridReader.ReadAsync` inline comment that still
  describes a zero-allocation array even though the method returns a list task.
- Make diagnostics and Activity tests restore global static state and reduce parallel
  listener contamination where practical.
- Fix benchmark filter expansion so `*TvpBenchmarks*` does not cause redundant
  execution of `WideTvpBenchmarks` through overlapping filters.

## Verification Plan

Focused verification:

- Options validation security tests.
- Observability meter tests.
- Runtime utility tests.
- Consumer docs and skill tests.
- Release package gate tests or script dry-run checks.
- AOT baseline parser checks.

Full verification:

- `Verification/scripts/Invoke-Verification.ps1` without skip flags.

Expected full gate contents:

- Debug build of integration tests and benchmarks.
- Matrix DB tests.
- Coverage gates.
- Native AOT publish plus AOT warning baseline gate.
- Tvp and WideTvp benchmarks.
- Release package gate.
- Artifact secret scan.
- Generated artifact tracking gate.

## Non-Goals

- No new Runtime TVP API surface.
- No reintroduction of `Lib.Db.TvpGen`.
- No per-tenant or per-service-provider observability redesign in v2.3.0.
- No forced package signing unless signing credentials are explicitly provided.
- No raw SQL DDL/DML execution outside existing verification/test scripts.

## Open Release Policy Decision

The only user policy decision remaining before implementation is package signing:

- Default recommendation: accept unsigned v2.3.0 packages for now, but make unsigned
  status explicit in the release-package gate and release checklist.
- Future improvement: add signing credentials and switch the same gate to strict
  `dotnet nuget verify --all`.

## Definition Of Done

The v2.3.0 release blockers are closed when:

- All six blocker areas above have code, script, test, or documentation changes.
- All secondary fixes are either completed or explicitly deferred with rationale.
- Security review no longer reports P1/High release blockers.
- Docs/skills review no longer reports stale guidance as a release blocker.
- Release/process review confirms the generated package is from HEAD and package/AOT
  gates are part of full verification.
- `Invoke-Verification.ps1` passes without skip flags.
