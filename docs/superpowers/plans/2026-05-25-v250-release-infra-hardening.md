# v2.5.0 Release Infra Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden release verification before v2.5.0 introduces any additional package, without publishing to NuGet or changing main.

**Architecture:** Keep publish behavior guarded by tests and dry-run scripts before altering package outputs. Static workflow tests validate trigger, permission, tag, package ID, and artifact redaction rules. Runtime package code remains unchanged.

**Tech Stack:** PowerShell 7, GitHub Actions YAML, .NET 10 CLI, Microsoft.Testing.Platform, NuGet package metadata, existing `Verification/scripts` patterns.

**Current implementation status:** The current working tree implements this plan through existing verification locations rather than new `Release/` test folders:

- `Verification/projects/Lib.Db.IntegrationTests/Unit/ReleasePackageGuardTests.cs`
- `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationArtifactScanTests.cs`
- `Verification/scripts/Invoke-ReleasePackage.ps1`
- `Verification/scripts/Scan-VerificationArtifacts.ps1`
- `.github/workflows/publish.yml`
- `.github/workflows/release-verification.yml`
- `.github/workflows/native-aot.yml`

---

## Scope

Included:

- Static tests for `.github/workflows/publish.yml`.
- Package allowlist design before multi-package pack/push.
- Pack-only dry-run plan.
- Artifact scanner redaction tests.
- MTP command matrix documentation.

Excluded:

- NuGet publish.
- Tag creation.
- GitHub release creation.
- `main` merge.
- Trusted Publishing activation.
- `id-token: write` workflow change.

## Task 1: Publish Guard Static Tests

**Files:**
- Create/Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/ReleasePackageGuardTests.cs`
- Modify: `.github/workflows/publish.yml`
- Modify: `.github/workflows/release-verification.yml`
- Modify: `.github/workflows/native-aot.yml`

- [x] **Step 1: Write tests for publish trigger shape**

Test that `.github/workflows/publish.yml` has push tag trigger for `v*` and no branch trigger.

- [x] **Step 2: Write tests for main-contained tag guard**

Test that the workflow contains a guard proving the tag target is contained in `origin/main`.

- [x] **Step 3: Write tests for no broad permissions**

Test that workflow permissions remain minimal and that `id-token: write` is absent until Trusted Publishing is explicitly adopted.

- [x] **Step 4: Write tests for version-neutral gate names**

Test that release verification gate names are version-neutral and do not keep v2.4.0-only labels.

- [x] **Step 5: Run focused tests**

Run the release guard test filter through the existing verification script.

## Task 2: Package Allowlist And Dry Run

**Files:**
- Modify: `Verification/scripts/Invoke-ReleasePackage.ps1`
- Create/Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/ReleasePackageGuardTests.cs`

- [x] **Step 1: Define package allowlist**

Allow `Lib.Db` only for the current release path. Future packages require explicit allowlist entries before wildcard push is permitted.

- [x] **Step 2: Test unexpected package rejection**

Create fixture metadata in the test project and assert unexpected package IDs fail.

- [x] **Step 3: Plan pack-only dry run**

Document a command that packs into a local artifact directory, validates `.nupkg` metadata, and stops before `dotnet nuget push`.
Add an invariant test proving the dry-run path does not invoke `dotnet nuget push`, create tags, or create GitHub releases.

## Task 3: Artifact Redaction Scanner

**Files:**
- Modify: `Verification/scripts/Scan-VerificationArtifacts.ps1`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/VerificationArtifactScanTests.cs`

- [x] **Step 1: Write failing tests with seeded secret-like artifact**

Seed an artifact fixture containing secret-like values and assert the scanner fails without printing the value.

- [x] **Step 2: Implement scanner**

Scanner reports file path, key name or pattern name, and presence only.
It scans every upload or publish candidate artifact, including unpacked `.nupkg` contents, without printing matched values.

- [x] **Step 3: Add clean artifact test**

Assert scanner succeeds on benign report content.

## Task 4: MTP Command Matrix

**Files:**
- Modify: `docs/verification.md`
- Modify: `Verification/README.md`

- [x] **Step 1: Document smoke command**

Document focused `dotnet test` or existing script command for non-database smoke tests.

- [x] **Step 2: Document report command**

Document TRX/report output command without changing publish behavior.

- [x] **Step 3: Document coverage command**

Document coverage command and expected artifact paths.

## Verification

- [x] `git diff --check`
- [x] Placeholder/incomplete-marker scan for new plans and tests.
- [x] Focused release guard tests.
- [x] Artifact redaction tests.
- [x] Dry-run non-publish invariant test: no `dotnet nuget push`, no tag creation, no GitHub release creation.
- [x] `git status --short` confirms only intended files changed.
