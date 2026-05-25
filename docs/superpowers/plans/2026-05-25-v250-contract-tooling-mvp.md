# v2.5.0 Contract Tooling MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a no-DB contract validate/report MVP that keeps Lib.Db runtime-first and avoids automatic DB changes.

**Architecture:** `Lib.Db.Tools` starts as a separate CLI/dotnet-tool candidate that reads checked-in contract files and emits deterministic reports. SQL Server inspect is a later SELECT-only phase. Core `Lib.Db` runtime behavior stays unchanged.

**Tech Stack:** .NET 10 console/tool project, System.Text.Json source generation, deterministic Markdown/JSON report output, PowerShell verification scripts, Microsoft.Testing.Platform.

---

## Precondition

The v2.5.0 release infra hardening PR, package allowlist, and artifact redaction guard must land before creating or adding any packable `Lib.Db.Tools` project. If a local project is created earlier for tests, it must set `IsPackable=false`, stay excluded from release pack/push paths, and not be added to any workflow that can publish artifacts.

## Scope

Included:

- `libdb.contracts.json` v1 model.
- No-DB validate/report mode.
- Redacted deterministic report output.
- Unknown result shape semantics.
- DDL/DML/EXEC guard policy.

Excluded:

- Live DB inspect in the MVP.
- CREATE/ALTER/DROP/INSERT/UPDATE/DELETE/MERGE/TRUNCATE/EXEC execution.
- Script scaffold.
- Generator package.
- Change Tracking package.
- Core runtime behavior changes.

## Task 1: Contract Schema v1

**Files:**
- Create: `docs/contracts/libdb-contracts-v1.md`
- Create: `Lib.Db.Tools/Contracts/LibDbContractDocument.cs`
- Create: `Lib.Db.Tools/Contracts/LibDbContractJsonContext.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/LibDbToolsContractTests.cs`

- [x] **Step 1: Write schema tests**

Test that a minimal contract document with one procedure, one TVP, and one bulk target round-trips deterministically.

- [x] **Step 2: Define document model**

Model includes schema version, procedures, parameters, TVPs, bulk targets, result shape status, and diagnostic metadata.

- [x] **Step 3: Verify no secret-bearing fields**

Contract model must not contain connection string, password, token, or arbitrary SQL parameter values.

## Task 2: Validate Engine

**Files:**
- Create: `Lib.Db.Tools/Validation/LibDbContractValidator.cs`
- Create: `Lib.Db.Tools/Validation/LibDbContractDifference.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/LibDbToolsContractTests.cs`

- [x] **Step 1: Write mismatch tests**

Test missing procedure, parameter type mismatch, TVP column order mismatch, and unknown result shape.

- [x] **Step 2: Implement severity model**

Use `Breaking`, `Warning`, and `Informational`.

- [x] **Step 3: Implement validator**

Compare expected and actual documents without connecting to a database.

## Task 3: Report Output

**Files:**
- Create: `Lib.Db.Tools/Reporting/LibDbContractReportWriter.cs`
- Create: `Lib.Db.Tools/Reporting/ContractOutputRedactor.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/LibDbToolsContractTests.cs`

- [x] **Step 1: Write golden report tests**

Assert deterministic JSON and Markdown output for a fixed diff.

- [x] **Step 2: Escape report content**

Escape Markdown and JSON values for object names and diagnostics.

- [x] **Step 3: Redact secret-like values**

Reporter outputs key names and presence only for secret-like input.

## Task 4: CLI Entry

**Files:**
- Create: `Lib.Db.Tools/Lib.Db.Tools.csproj`
- Create: `Lib.Db.Tools/Program.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/LibDbToolsGuardTests.cs`

- [x] **Step 1: Write CLI parse tests**

Test `validate --expected --actual --format json --out` and `report --contracts --format markdown --out`.

- [x] **Step 2: Implement no-DB commands**

Commands read local files and write local reports only.

- [x] **Step 3: Guard unsupported commands**

`inspect`, `scaffold`, `apply`, `execute`, and `migrate` return unsupported command errors in v2.5.0 MVP.
Connection-bearing commands such as `inspect --connection` and `validate --connection` must be rejected in the no-DB MVP without echoing the supplied value.

## Task 5: Packaging Guard

**Files:**
- Modify: `Lib.Db.slnx`
- Modify only after release infra plan lands: package allowlist tests.

- [x] **Step 1: Add project to solution only after release guard exists**

Do not add `Lib.Db.Tools` to release pack/push until package allowlist is implemented.

- [x] **Step 2: Verify pack is disabled or isolated**

Ensure the MVP project uses `IsPackable=false` until the release allowlist explicitly permits it, and verify it cannot accidentally publish as part of the existing `Lib.Db` release workflow.

## Verification

- [x] `dotnet test` focused Tools tests.
- [x] Release guard tests pass before adding packable tool.
- [x] Artifact redaction tests pass.
- [x] `git diff --check`.
- [x] No direct SQL DDL/DML/EXEC commands are used.
