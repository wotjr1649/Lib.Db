# Lib.Db Complete API Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand `.claude/skills/lib-db` into a complete, token-efficient consumer skill for the Lib.Db NuGet package.

**Architecture:** Keep `SKILL.md` as a compact router and move API-family details into one-hop `references/` files. The skill remains version-neutral, consumer-facing, read-only, and safe by default for SQL Server usage.

**Tech Stack:** Markdown skill package, Lib.Db public API surface, SQL Server safety guidance, Codex/OpenAI skill progressive disclosure.

---

## File Structure

- Modify: `.claude/skills/lib-db/SKILL.md`
- Keep/replace: `.claude/skills/lib-db/references/mapping-contracts.md`
- Keep/replace: `.claude/skills/lib-db/references/examples.md`
- Delete after replacement: `.claude/skills/lib-db/references/runtime-api.md`
- Delete after replacement: `.claude/skills/lib-db/references/security-guardrails.md`
- Delete after replacement: `.claude/skills/lib-db/references/tvpgen-guide.md`
- Create: `.claude/skills/lib-db/references/quickstart.md`
- Create: `.claude/skills/lib-db/references/options-and-registration.md`
- Create: `.claude/skills/lib-db/references/connection-security.md`
- Create: `.claude/skills/lib-db/references/fluent-execution.md`
- Create: `.claude/skills/lib-db/references/parameters-and-binding.md`
- Create: `.claude/skills/lib-db/references/result-handling.md`
- Create: `.claude/skills/lib-db/references/tvp-source-generation.md`
- Create: `.claude/skills/lib-db/references/bulk-insert.md`
- Create: `.claude/skills/lib-db/references/schema-maintenance.md`
- Create: `.claude/skills/lib-db/references/caching.md`
- Create: `.claude/skills/lib-db/references/transactions.md`
- Create: `.claude/skills/lib-db/references/operations-integration.md`
- Create: `.claude/skills/lib-db/references/diagnostics-resilience.md`
- Create: `.claude/skills/lib-db/references/aot-trimming.md`

## Task 1: Router Skill

- [ ] Replace `SKILL.md` with a concise router.
- [ ] Include only trigger conditions, first-step routing, reference map, hard safety rules, and completion checks.
- [ ] Keep `allowed-tools` read-only and omit broad `paths` frontmatter.
- [ ] Ensure `SKILL.md` has no release, verification, test, package-source, benchmark, or version guidance.

## Task 2: Reference Set

- [ ] Replace broad starter references with API-family references.
- [ ] Cover DI/options, connection security, fluent execution, parameter binding, results/errors, mapping, TVP, bulk insert, schema maintenance, caching, transactions, operations integration, diagnostics/resilience, AOT/trimming, and curated examples.
- [ ] Keep references one hop from `SKILL.md`; avoid nested reference discovery.
- [ ] Prefer short code snippets that match actual public API names.

## Task 3: Safety Pass

- [ ] Remove examples that reveal full connection strings, passwords, tokens, or secrets.
- [ ] Avoid direct SQL CLI workflows.
- [ ] Keep raw SQL guidance parameterized and explain production stored-procedure preference.
- [ ] Mark dangerous families: raw SQL, `UseConnectionString`, bulk insert, schema maintenance, cache keys, interceptors, `IncludeParametersInTrace`, reflection TVP, configuration binding in Native AOT.

## Task 4: Verification

- [ ] Confirm expected reference files exist and obsolete starter references are gone.
- [ ] Scan skill package for version strings.
- [ ] Scan skill package for release/verification/test/package-source workflow text.
- [ ] Scan for disallowed broad `paths` frontmatter.
- [ ] Scan for known incorrect API shapes such as `result.Success`, `result.ErrorCode`, `QueryStreamAsync`, and `InTransactionAsync`.
- [ ] Scan for unsafe credential examples.
- [ ] Run `git diff --check`.
