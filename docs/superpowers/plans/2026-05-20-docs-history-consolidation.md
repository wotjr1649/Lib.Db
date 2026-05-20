# Lib.Db Docs History Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the README and active `docs/` pages compact and version-neutral, while moving version-specific feature history, release notes, migration summaries, and old reports into `docs/history.md`.

**Architecture:** Active docs describe the current public API and operational workflow without embedding release-history sections. `docs/history.md` becomes the single human-readable changelog/history document. Historical Superpowers specs/plans stay untouched because they are implementation records, not public current-user documentation.

**Tech Stack:** Markdown, PowerShell 7, ripgrep, .NET SDK, NuGet package README packing through `Lib.Db/Lib.Db.csproj`.

---

## Source Rules

Use these documentation rules throughout the implementation:

- Current-user docs should avoid release labels in titles and headings.
- Version-specific changes belong in `docs/history.md`.
- Version strings are still allowed in `Lib.Db.csproj`, package metadata, Git tags, `docs/history.md`, Superpowers plans/specs, and explicitly historical archive entries.
- Use `README.md` for onboarding and links only; avoid long release notes or benchmark explanations there.
- Follow SemVer and changelog conventions: versions group meaningful `Added`, `Changed`, `Fixed`, `Security`, `Verification`, and `Migration` entries.
- Do not print secrets, connection string values, passwords, or token values while verifying docs.

Primary external references already checked for this plan:

- Semantic Versioning 2.0.0: `https://semver.org/`
- NuGet package versioning: `https://learn.microsoft.com/en-us/nuget/concepts/package-versioning`
- .NET library versioning guidance: `https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning`
- Keep a Changelog: `https://keepachangelog.com/`

## File Structure

Create:

- `docs/history.md`
  Single public history/changelog document. Owns version-specific feature additions, migration notes, blocker fixes, release-grade verification summaries, and old report summaries.

Modify:

- `README.md`
  Compact public entry point. Target: roughly 120-160 lines. No versioned changelog sections.
- `docs/01_guide.md`
  Current guide. Remove old migration sections and link to `docs/history.md`.
- `docs/02_advanced.md`
  Current advanced guide. Remove versioned phrasing from headings and convert legacy TVP notes to neutral compatibility language.
- `docs/03_api_reference.md`
  Current API reference. Remove release-specific wording except deprecation target notes that are part of API policy.
- `docs/04_operations.md`
  Current operations guide. Point verification and historical regression details to `docs/history.md` and `docs/verification.md`.
- `docs/05_fluent_api_reference.md`
  Current Fluent API reference. Remove release-specific TVP wording.
- `docs/06_cookbook.md`
  Current cookbook. Remove release-specific TVP wording.
- `docs/security/libdb-server-chaos-harness.md`
  Keep as current security operational doc; adjust links if verification doc is renamed.
- `docs/security/libdb-v2.3-aot-tvp-risk-ledger.md`
  Rename to `docs/security/aot-tvp-risk-ledger.md` and make title version-neutral.
- `docs/v2.3.0-verification.md`
  Rename to `docs/verification.md` and make title version-neutral.

Delete after summary is moved into `docs/history.md`:

- `docs/v2.2.1-blocker-fixes.md`
- `docs/feature-completeness-report.md`
- `docs/qa-verification-report.md`
- `docs/test-coverage-report.md`

Do not modify:

- `docs/superpowers/specs/**`
- `docs/superpowers/plans/**`, except this plan file if corrections are needed
- `Verification/**`
- source code, tests, SQL files, workflows, or package metadata unless link validation proves a doc reference must change

---

### Task 1: Baseline Inventory And Guardrails

**Files:**
- Read: `README.md`
- Read: `docs/*.md`
- Read: `docs/security/*.md`
- Modify: none
- Test: command-output inspection only

- [ ] **Step 1: Record current line counts**

Run:

```powershell
$root = "C:\Users\js\Documents\Codex\Lib.Db"
Get-ChildItem -Path "$root\docs" -File -Recurse -Filter *.md |
  Where-Object { $_.FullName -notmatch '\\superpowers\\' } |
  ForEach-Object {
    $lines = (Get-Content -LiteralPath $_.FullName).Count
    [pscustomobject]@{ Path = $_.FullName.Substring($root.Length + 1); Lines = $lines }
  } |
  Sort-Object Lines -Descending |
  Format-Table -AutoSize
```

Expected:

```text
docs\06_cookbook.md
docs\05_fluent_api_reference.md
docs\02_advanced.md
docs\01_guide.md
README.md
```

The exact line numbers may differ because previous doc cleanup already changed files.

- [ ] **Step 2: Capture version-string distribution in active docs**

Run:

```powershell
$root = "C:\Users\js\Documents\Codex\Lib.Db"
rg -n "v2\.3|v2\.2|v2\.1|v1|2\.3\.0|2\.2\.1|Historical|변경 요약|마이그레이션|Report|Runbook|리포트" `
  "$root\README.md" "$root\docs" `
  -g "*.md" -g "!superpowers/**"
```

Expected before implementation:

```text
README.md contains versioned intro/change-summary sections
docs\01_guide.md contains migration sections
docs\v2.3.0-verification.md exists
docs\v2.2.1-blocker-fixes.md exists
old report docs exist
```

- [ ] **Step 3: Confirm old TvpGen public docs are gone**

Run:

```powershell
Test-Path -LiteralPath "C:\Users\js\Documents\Codex\Lib.Db\docs\TvpGen"
```

Expected:

```text
False
```

---

### Task 2: Create `docs/history.md`

**Files:**
- Create: `docs/history.md`
- Read: `README.md`
- Read: `docs/v2.2.1-blocker-fixes.md`
- Read: `docs/feature-completeness-report.md`
- Read: `docs/qa-verification-report.md`
- Read: `docs/test-coverage-report.md`
- Read: `docs/v2.3.0-verification.md`

- [ ] **Step 1: Create the history document with consolidated content**

Create `docs/history.md` with this structure and content, then adjust only if a source doc contains a concrete fact that contradicts the summary:

```markdown
# Lib.Db History

This document keeps version-specific feature history, migration notes, release verification summaries, and historical QA reports. Current usage documentation is intentionally version-neutral.

## 2.3.0

### Added

- Runtime TVP support in the single `Lib.Db` package through `LibDb.Tvp(...)`.
- Static-shape TVP fast path through `options.Tvp.Map<T>().Column(...)`.
- Reusable explicit TVP shapes through `TvpShape.For<T>().Column(...).Build()`.
- Schema-adaptive TVP descriptor path through `LibDb.Tvp(descriptor, rows, TvpBindingPolicy.Adaptive)`.
- Targeted TVP schema flush APIs for stale schema cache recovery.
- Verification root under `Verification/` for integration tests, SQL setup/verify scripts, coverage, AOT checks, BenchmarkDotNet reports, and chaos harness assets.
- Benchmark comparison between legacy generated-accessor baseline and runtime TVP paths.

### Changed

- `Lib.Db.TvpGen` is no longer part of the normal public TVP workflow.
- README and active docs were reorganized so current usage is version-neutral.
- Benchmark and test artifacts are expected under `Verification/artifacts/`.

### Verification

- Release-grade verification is driven by `Verification/scripts/Invoke-Verification.ps1`.
- Matrix DB tests cover `LIBDB_VERIFICATION_TEST`, `LIBDB_STRESS_TEST`, `LIBDB_CHAOS_TEST`, and `LIBDB_BENCH_TEST`.
- Native AOT verification tracks Lib.Db-owned warnings separately from provider-owned warnings.
- Benchmark artifacts must be scanned before release to avoid committing secrets or connection strings.

### Security

- SQL identifier validation for TVP type names is restricted to schema/name two-part identifiers.
- Server-level chaos tests are opt-in and separated from default verification.
- Production connection-string policy should use encryption, certificate validation, and non-`sa` least-privilege accounts.

## 2.2.1

### Fixed

- ResultSet name convention mapping now handles common SQL Server column styles such as `CELL_NO`, `cell_no`, and `CellNo` when mapping to PascalCase DTO properties.
- Generated/static result mapper reader compatibility supports `DbDataReader` wrapper paths such as diagnostic monitored readers.
- `DateOnly` and `TimeOnly` parameter binding uses SQL `date` and `time` types instead of ambiguous string or JSON-style conversions.
- Verification DDL uses `SET QUOTED_IDENTIFIER ON` where SQL Server computed column indexes require it.

### Verification

- `LIBDB_VERIFICATION_TEST` contains dedicated regression objects for result mapping, monitored reader compatibility, DateOnly/TimeOnly binding, and quoted-identifier DDL behavior.

## 2.2.0

### Added

- MARS policy options: `Disabled`, `Auto`, and `ForceEnable`.
- Unified observability option through `EnableObservability`.
- Improved result mapping and schema-cache behavior.

### Changed

- `EnableOpenTelemetry` became obsolete in favor of `EnableObservability`.
- Legacy TVP DB-first generation paths changed date/time mapping behavior toward `DateOnly` and `TimeOnly`.
- Bulk insert reflection cost was reduced through compiled accessors.

## 2.1

### Added

- Expanded stored-procedure-centered Fluent API usage patterns.
- Additional tests for custom errors, savepoints, deadlocks, and SQL Server data access behavior.

### Historical QA Summary

- Historical QA reports recorded a high feature-completeness score for the v2.1 branch.
- Those reports are preserved here as summary only because current API documentation should describe the current library, not old scorecards.

## 1.x To 2.x

### Changed

- The public API moved toward a single Fluent pipeline and structured `DbResult<T>` error handling.
- Older direct execution paths were consolidated into staged calls such as `Procedure(...).With(...).QueryAsync<T>()`.

## Documentation Policy

- `README.md` and active `docs/*.md` pages describe the current API.
- Version-specific release history belongs in this file.
- Implementation records remain in `docs/superpowers/specs/**` and `docs/superpowers/plans/**`.
```

- [ ] **Step 2: Verify the history file has no incomplete markers**

Run:

```powershell
$patterns = @('T' + 'BD', 'T' + 'ODO', 'fill' + ' in', '나' + '중', '추' + '후')
Select-String -LiteralPath "C:\Users\js\Documents\Codex\Lib.Db\docs\history.md" -Pattern $patterns
```

Expected:

```text
No output.
```

---

### Task 3: Compact `README.md`

**Files:**
- Modify: `README.md`
- Read: `docs/history.md`
- Read: `docs/02_advanced.md`
- Read: `docs/verification.md` after Task 5, or `docs/v2.3.0-verification.md` before Task 5

- [ ] **Step 1: Replace the README with a compact current-user structure**

Replace `README.md` with this outline and content. Keep package badges or repository-specific metadata only if already present above the title.

```markdown
# Lib.Db

High-performance SQL Server data access for .NET applications.

`Lib.Db` focuses on stored procedures, structured `DbResult<T>` errors, Fluent API calls, Runtime TVP binding, schema caching, resilience, observability, and release-grade local SQL Server verification.

## Install

```powershell
dotnet add package Lib.Db
```

## Quick Start

### Configure

```json
{
  "ConnectionStrings": {
    "Default": "<use user-secrets, environment variables, or deployment secret storage>"
  },
  "LibDb": {
    "ConnectionStringNames": ["Default"],
    "ConnectionSecurityProfile": "Production",
    "Mars": "ForceEnable",
    "EnableSchemaCaching": true,
    "EnableResilience": true,
    "EnableObservability": false
  }
}
```

Connection string values are secrets. Keep real values in local settings, environment variables, or secret stores.

### Register

```csharp
builder.Services.AddLibDb(builder.Configuration);
```

### Query

```csharp
DbResult<User?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<User>(ct);

if (!result.IsSuccess)
{
    logger.LogWarning("DB call failed: {Kind}", result.Error?.Kind);
    return null;
}

return result.Value;
```

## Runtime TVP

Use `LibDb.Tvp(...)` when the call site should explicitly mark a parameter as a SQL Server table-valued parameter.

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new
    {
        RequestedBy = userId,
        Products = LibDb.Tvp("dbo.T_Product", products)
    })
    .ExecuteAsync(ct);
```

For repeated calls or Native AOT paths, register a static shape once:

```csharp
builder.Services.AddLibDb(options =>
{
    options.Tvp.Map<ProductRow>("dbo.T_Product")
        .Column("ProductId", SqlDbType.Int, static row => row.ProductId)
        .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100)
        .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2);
});
```

## Key Features

- Fluent stored-procedure and raw SQL API
- `DbResult<T>` success/error model
- Runtime TVP binding with static-shape fast path
- SQL Server schema caching and warmup
- Resilience with Polly pipelines
- OpenTelemetry-friendly diagnostics
- Multi-result reader support
- Bulk insert helper for large inserts
- Verification assets under `Verification/`

## Verification

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-Verification.ps1 -BenchmarkJob Dry
pwsh -NoProfile -File .\Verification\scripts\Invoke-Coverage.ps1
pwsh -NoProfile -File .\Verification\scripts\Invoke-Benchmarks.ps1 -Job Short -Filter '*TvpBenchmarks*'
```

See [Verification](./docs/verification.md) for the full local SQL Server verification flow.

## Documentation

- [Guide](./docs/01_guide.md)
- [Advanced Features](./docs/02_advanced.md)
- [API Reference](./docs/03_api_reference.md)
- [Operations](./docs/04_operations.md)
- [Fluent API Reference](./docs/05_fluent_api_reference.md)
- [Cookbook](./docs/06_cookbook.md)
- [Verification](./docs/verification.md)
- [History](./docs/history.md)
- [AOT/TVP Risk Ledger](./docs/security/aot-tvp-risk-ledger.md)
- [Server Chaos Harness](./docs/security/libdb-server-chaos-harness.md)

## License

MIT
```

- [ ] **Step 2: Check README size**

Run:

```powershell
(Get-Content -LiteralPath "C:\Users\js\Documents\Codex\Lib.Db\README.md").Count
```

Expected:

```text
A value between 90 and 170.
```

---

### Task 4: Make Active Docs Version-Neutral

**Files:**
- Modify: `docs/01_guide.md`
- Modify: `docs/02_advanced.md`
- Modify: `docs/03_api_reference.md`
- Modify: `docs/04_operations.md`
- Modify: `docs/05_fluent_api_reference.md`
- Modify: `docs/06_cookbook.md`
- Read: `docs/history.md`

- [ ] **Step 1: Rename active doc headings**

Apply these heading changes:

```text
docs/01_guide.md: "# Lib.Db v2.3 가이드" -> "# Lib.Db Guide"
docs/02_advanced.md: "# Lib.Db v2.3 고급 기능" -> "# Lib.Db Advanced Features"
docs/03_api_reference.md: "# Lib.Db v2.3 API 레퍼런스" -> "# Lib.Db API Reference"
docs/04_operations.md: "# Lib.Db v2.3 운영 가이드" -> "# Lib.Db Operations"
docs/05_fluent_api_reference.md: "# Lib.Db v2.3 Fluent API 레퍼런스" -> "# Lib.Db Fluent API Reference"
docs/06_cookbook.md: "# Lib.Db v2.3 Cookbook" -> "# Lib.Db Cookbook"
```

Also remove version labels from the first paragraph of each file. Use "current API" or no qualifier.

- [ ] **Step 2: Move migration sections out of `docs/01_guide.md`**

In `docs/01_guide.md`, remove these sections after their content has been represented in `docs/history.md`:

```text
## 6. v1 → v2 마이그레이션
## 7. v2.1 신규 기능 요약
## 8. v2.1 → v2.2 마이그레이션
## 9. v2.3 Runtime TVP 전환 메모
```

Replace them with:

```markdown
## Migration And History

Version-specific migration notes and release history live in [History](./history.md). This guide describes the current API surface.
```

- [ ] **Step 3: Neutralize TVP compatibility wording**

Apply these wording changes:

```text
"v2.3.0부터 TVP 입력은" -> "TVP 입력은"
"### 1-5. v2.2 호환 fallback" -> "### 1-5. Legacy compatibility fallback"
"v2.3 신규 코드는" -> "New code"
"v2.2 호환 코드 유지" -> "Legacy compatibility"
"v2.3.0 기준 TVP는" -> "TVP is"
"v2.3 Runtime TVP wrapper" -> "Runtime TVP wrapper"
"v2.3 신규 호출부" -> "New call sites"
```

- [ ] **Step 4: Neutralize operations verification wording**

In `docs/04_operations.md`, replace the historical regression section with a compact current verification section:

```markdown
## 5. Verification

The canonical verification root is `Verification/`. It contains integration tests, SQL setup/verify scripts, coverage gates, AOT checks, BenchmarkDotNet projects, and chaos harness assets.

```powershell
.\Verification\scripts\Invoke-Verification.ps1 -BenchmarkJob Dry
```

Historical regression details are summarized in [History](./history.md).
```

Keep security notes about production DB permissions, raw SQL policy, and `SET QUOTED_IDENTIFIER ON`.

- [ ] **Step 5: Preserve API deprecation version only where it is policy**

Keep this type of sentence if present:

```text
EnableOpenTelemetry is obsolete and scheduled for removal in v3.0.
```

Reason: deprecation target version is active API policy, not release history.

---

### Task 5: Rename Versioned Current Docs

**Files:**
- Move: `docs/v2.3.0-verification.md` -> `docs/verification.md`
- Move: `docs/security/libdb-v2.3-aot-tvp-risk-ledger.md` -> `docs/security/aot-tvp-risk-ledger.md`
- Modify: `README.md`
- Modify: `docs/04_operations.md`
- Modify: `docs/security/libdb-server-chaos-harness.md`
- Modify: any active doc found by link search

- [ ] **Step 1: Move the verification runbook**

Run from repo root:

```powershell
git mv docs\v2.3.0-verification.md docs\verification.md
```

Expected:

```text
No output, exit code 0.
```

- [ ] **Step 2: Move the risk ledger**

Run from repo root:

```powershell
git mv docs\security\libdb-v2.3-aot-tvp-risk-ledger.md docs\security\aot-tvp-risk-ledger.md
```

Expected:

```text
No output, exit code 0.
```

- [ ] **Step 3: Make titles version-neutral**

In `docs/verification.md`, change:

```markdown
# Lib.Db v2.3.0 Verification Runbook
```

to:

```markdown
# Lib.Db Verification
```

In `docs/security/aot-tvp-risk-ledger.md`, change:

```markdown
# Lib.Db v2.3 AOT/TVP Risk Ledger
```

to:

```markdown
# Lib.Db AOT/TVP Risk Ledger
```

Keep concrete provider warning observations with the package version and current release context if they are release evidence; otherwise move them to `docs/history.md`.

- [ ] **Step 4: Update links**

Run:

```powershell
rg -n "v2\.3\.0-verification|libdb-v2\.3-aot-tvp-risk-ledger" README.md docs -g "*.md"
```

Replace active links:

```text
docs/v2.3.0-verification.md -> docs/verification.md
docs/security/libdb-v2.3-aot-tvp-risk-ledger.md -> docs/security/aot-tvp-risk-ledger.md
```

Expected after replacement:

```powershell
rg -n "v2\.3\.0-verification|libdb-v2\.3-aot-tvp-risk-ledger" README.md docs -g "*.md" -g "!superpowers/**"
```

No output.

---

### Task 6: Remove Historical Report Files After Consolidation

**Files:**
- Delete: `docs/v2.2.1-blocker-fixes.md`
- Delete: `docs/feature-completeness-report.md`
- Delete: `docs/qa-verification-report.md`
- Delete: `docs/test-coverage-report.md`
- Modify: `README.md`
- Modify: active docs if they link to these files

- [ ] **Step 1: Confirm active links before deletion**

Run:

```powershell
rg -n "v2\.2\.1-blocker-fixes|feature-completeness-report|qa-verification-report|test-coverage-report" README.md docs -g "*.md"
```

Expected:

```text
Only README.md or active docs should appear before this task. Superpowers historical files may appear if the search includes docs/superpowers.
```

- [ ] **Step 2: Delete the old report files**

Use `apply_patch` or `git rm`:

```powershell
git rm docs\v2.2.1-blocker-fixes.md docs\feature-completeness-report.md docs\qa-verification-report.md docs\test-coverage-report.md
```

Expected:

```text
rm 'docs/v2.2.1-blocker-fixes.md'
rm 'docs/feature-completeness-report.md'
rm 'docs/qa-verification-report.md'
rm 'docs/test-coverage-report.md'
```

- [ ] **Step 3: Confirm history has replacement coverage**

Run:

```powershell
rg -n "2\.2\.1|ResultSet|DateOnly|TimeOnly|Quoted|2\.1|QA|feature-completeness|coverage" docs\history.md
```

Expected:

```text
docs\history.md contains entries for the deleted reports and blocker fixes.
```

---

### Task 7: Link And Version Hygiene Verification

**Files:**
- Read: `README.md`
- Read: `docs/**/*.md`
- Modify: any active doc with broken links or unintended version wording

- [ ] **Step 1: Check active docs for unintended version wording**

Run:

```powershell
rg -n "v2\.3|v2\.2|v2\.1|v1|2\.3\.0|2\.2\.1|Historical|변경 요약|마이그레이션|Report|Runbook|리포트" `
  README.md docs `
  -g "*.md" -g "!superpowers/**" -g "!history.md"
```

Expected allowed output only:

```text
docs\03_api_reference.md may mention v3.0 for deprecation policy.
docs\verification.md may mention provider package versions if required as evidence.
docs\security\aot-tvp-risk-ledger.md may mention concrete provider package versions.
```

No README release-history sections should remain.

- [ ] **Step 2: Check deleted or moved links**

Run:

```powershell
rg -n "v2\.3\.0-verification|libdb-v2\.3-aot-tvp-risk-ledger|v2\.2\.1-blocker-fixes|feature-completeness-report|qa-verification-report|test-coverage-report|docs/TvpGen|TvpGen/" README.md docs -g "*.md" -g "!superpowers/**"
```

Expected:

```text
No output.
```

- [ ] **Step 3: Check Markdown whitespace**

Run:

```powershell
git diff --check -- README.md docs
```

Expected:

```text
No output.
```

- [ ] **Step 4: Build the package project to validate README packing**

Run:

```powershell
dotnet build .\Lib.Db\Lib.Db.csproj --no-restore -v:minimal
```

Expected:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

- [ ] **Step 5: Inspect final status**

Run:

```powershell
git status --short
```

Expected:

```text
Modified/deleted docs only for this task, plus any unrelated pre-existing changes.
```

Do not revert unrelated pre-existing changes.

---

## Self-Review Checklist

- [ ] `README.md` is compact and current-user oriented.
- [ ] `docs/history.md` contains version-specific history that was removed from active docs.
- [ ] Active docs have version-neutral titles.
- [ ] Current verification and risk-ledger docs have version-neutral filenames.
- [ ] Old report files are deleted only after their summary exists in `docs/history.md`.
- [ ] `docs/superpowers/**` remains untouched except this plan.
- [ ] `git diff --check` passes.
- [ ] `dotnet build .\Lib.Db\Lib.Db.csproj --no-restore -v:minimal` passes.