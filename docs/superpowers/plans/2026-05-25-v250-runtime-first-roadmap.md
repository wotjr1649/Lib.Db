# v2.5.0 Runtime-First Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build v2.5.0 as a runtime-first Lib.Db release without making source generation, migrations, or Change Tracking part of the core runtime.

**Architecture:** `Lib.Db` remains the runtime package. `Lib.Db.Tools` owns contract validate/report and starts with no-DB checked-in metadata. `Lib.Db.Generator` is optional AOT/performance work only after public registration boundaries are designed. `Lib.Db.SqlServer.ChangeTracking` remains a provider/read-adapter candidate outside core.

**Tech Stack:** .NET 10, Microsoft.Testing.Platform, SQL Server metadata SELECT queries, PowerShell verification scripts, GitHub Actions, NuGet packaging.

**Current implementation status:** The current working tree has advanced beyond planning into the release guard and no-DB `Lib.Db.Tools` MVP. Generator and Change Tracking remain design-only/provider-candidate work and are not implemented in core.

---

## Branch And Release Rules

- Work on `v2.5.0` and feature branches targeting `v2.5.0`.
- Do not merge `v2.5.0` to `main` until release readiness is reviewed.
- Do not create tags, GitHub releases, or NuGet publish runs during implementation.
- Direct SQL tools may run SELECT only. Test/application code may set up local fixtures.
- Do not print secret/token/password/connection string values.
- Before tracking internal specs/plans, run a secret-like scan that reports only key names and presence.

## v2.5.0 MVP

- `Lib.Db` runtime public behavior remains stable.
- Release guard hardening is designed before any multi-package publish changes.
- `libdb.contracts.json` v1 schema is specified before generator or Change Tracking consumes it.
- `Lib.Db.Tools` starts with no-DB validate/report mode.
- Reports are deterministic and redacted.
- DDL/DML/EXEC automatic execution is out of scope.
- Generator and Change Tracking consume the contract model later; they do not define it.
- MVP source is checked-in explicit contract metadata. Operating DB metadata comparison is a later SELECT-only phase. Generated or scaffolded artifacts are review aids, not authority.

## Explicit Non-Goals

- Generator live DB inspect.
- Generator as DB drift source of truth.
- Core migration engine or EF-style model snapshot.
- Tools `apply`, `execute`, or automatic migration commands.
- Change Tracking automatic enablement or checkpoint table creation.
- Change Tracking inside the core package.
- Multi-package publish without package allowlist and dry-run guard.
- Main merge before v2.5.0 release readiness review.
- SELECT metadata inspect in the no-DB MVP.

## Implementation PR Order

The task order below records planning work and guard work in one roadmap. Actual implementation PRs must follow this order:

1. Release infra hardening.
2. Core/package boundary guard.
3. Contract metadata schema.
4. `Lib.Db.Tools` no-DB validate/report MVP.
5. SELECT-only metadata inspect.
6. Generator and Change Tracking previews.

## Task 1: Track Internal Specs And Plans

**Files:**
- Modify: `.gitignore`
- Create/Modify: `docs/superpowers/specs/2026-05-25-*.md`
- Create/Modify: `docs/superpowers/plans/2026-05-25-v250-*.md`

- [x] **Step 1: Unignore specs and plans**

Update `.gitignore` so only the v2.5.0 `2026-05-25` specs and `2026-05-25-v250` plans are tracked while older internal notes remain ignored.

- [x] **Step 2: Verify tracking**

Run:

```powershell
git -C "C:\Users\js\Documents\Codex\Lib.Db" status --short --ignored -- docs/superpowers
```

Expected: new v2.5.0 spec/plan files appear as untracked `??`, not ignored `!!`.
The `.gitignore` exceptions should list the four v2.5.0 spec files explicitly, not a broad date wildcard.

- [x] **Step 3: Scan tracked internal documents**

Run a secret-like scan over the v2.5.0 spec/plan files. The scan must report only key names and presence, never values.

## Task 2: Lock Runtime-First Specs

**Files:**
- Modify: `docs/superpowers/specs/2026-05-25-libdb-generator-design.md`
- Modify: `docs/superpowers/specs/2026-05-25-libdb-contract-tooling-design.md`
- Modify: `docs/superpowers/specs/2026-05-25-libdb-change-tracking-adapter-design.md`
- Modify: `docs/superpowers/specs/2026-05-25-libdb-release-infra-hardening-design.md`

- [x] **Step 1: Mark generator as optional**

Document that the generator is not the DB contract source of truth and must not require consumer rebuild for normal DB drift detection.

- [x] **Step 2: Promote Tools-first**

Document `Lib.Db.Tools` as the v2.5.0 core candidate with no-DB validate/report first and SELECT-only inspect later.

- [x] **Step 3: Keep Change Tracking outside core**

Document provider/read-adapter boundaries, validated identifiers, no automatic enablement, and invalid checkpoint hard failure.

- [x] **Step 4: Keep release infra separate**

Document multi-package guard, dry-run, redaction, and no publish/tag/main merge rules.

## Task 3: Release Infra Hardening Plan

**Files:**
- Create: `docs/superpowers/plans/2026-05-25-v250-release-infra-hardening.md`

- [x] **Step 1: Plan workflow static tests**

Specify tests for tag-only publish guard, main-contained tag target, package ID allowlist, version/tag match, missing credential, and artifact redaction.

- [x] **Step 2: Plan no-publish dry run**

Specify a command path that packs and scans artifacts without `dotnet nuget push`.

- [x] **Step 3: Plan MTP command verification**

Specify filter/report/coverage command matrix without changing release behavior.

## Task 4: Contract Tooling MVP Plan

**Files:**
- Create: `docs/superpowers/plans/2026-05-25-v250-contract-tooling-mvp.md`

This task started as plan-only. The current working tree now contains the no-DB `Lib.Db.Tools` MVP after the release guard and package boundary guard were added. `Lib.Db.Tools` remains `IsPackable=false` and is not part of publish workflows.

- [x] **Step 1: Plan metadata schema**

Define a versioned checked-in contract model for procedures, TVPs, and bulk targets.

- [x] **Step 2: Plan no-DB validate/report prototype**

Define a console/dotnet-tool project that reads local metadata and emits deterministic redacted report output.

- [x] **Step 3: Plan SELECT-only inspect phase**

List allowed metadata SELECT query families and non-SELECT guard tests.

## Task 5: Guard Core Boundaries

**Files:**
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/PackageGraphTests.cs`

- [x] **Step 1: Write focused package boundary tests**

Add tests proving `Lib.Db` does not reference future tool/generator/provider packages or analyzer/tool-only assemblies.

- [x] **Step 2: Run the focused tests and verify RED or existing-pass baseline**

Use the existing verification script with the narrow test filter. If tests already pass because packages do not exist, record that this is a regression guard rather than a red-green feature test.

- [x] **Step 3: Implement only the minimum test logic**

Keep production code unchanged.

## Task 6: Subagent Review Gate

**Files:**
- No required file changes.

- [ ] **Step 1: Close prior agents**

Close every previous advisory agent before starting the next review round.

- [ ] **Step 2: Start fresh agents**

Ask fresh Codex Security, code-review, and brainstorming agents to review only the current diff and next planned task.

- [ ] **Step 3: Apply accepted feedback**

Apply only feedback that preserves runtime-first scope and does not trigger publish/tag/main merge.

## Task 7: Verification Gate

**Files:**
- No required file changes.

- [ ] **Step 1: Static document checks**

Run incomplete-marker scan, encoding/LF check, and `git diff --check`.

- [ ] **Step 2: Focused tests**

Run focused architecture/package graph tests after test changes.

- [ ] **Step 3: Status review**

Run `git status --short` and confirm only intended files changed.
