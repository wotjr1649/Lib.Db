# Lib.Db Skill Evaluation

Date: 2026-05-20

Target: `Lib.Db/.claude/skills/lib-db`

Tools/lenses used:

- Codex Security: threat model -> finding discovery -> validation -> attack-path calibration.
- Superpowers: `using-superpowers` and `writing-skills` quality criteria.
- Official references checked on 2026-05-20:
  - [Agent Skills specification](https://agentskills.io/specification)
  - [Claude Agent Skills overview](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/overview)
  - [Claude skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
  - [Claude Code skills documentation](https://code.claude.com/docs/en/skills)

## Executive Verdict

The `lib-db` skill is structurally sound and security-conscious, but it is no longer current for this repository. As a v2.2.1 hardening/mapping skill it is good. As the active project skill for the current `Lib.Db` tree, it is stale enough to mislead agents during verification, release, and documentation work.

Overall rating:

- Skill structure and progressive disclosure: A-
- Security guardrails: A-
- Runtime/mapping/domain specificity: B+
- Current-repo accuracy: C
- Skill test evidence: C+
- Net rating for current use: B- if manually corrected by an expert, C+ if relied on automatically.

No direct malicious behavior or secret-exfiltration pattern was found. The primary risk is unsafe or incomplete generated work caused by stale instructions.

## Evidence Summary

Observed package shape:

- One skill: `lib-db`
- Files: 10 total
- Markdown files: 9
- Main `SKILL.md`: 96 lines
- References: 6
- Static validator: `tests/validate-skill.ps1`

Validation command run:

```text
pwsh -NoProfile -ExecutionPolicy Bypass -File C:/Users/js/Documents/Codex/Lib.Db/.claude/skills/lib-db/tests/validate-skill.ps1
```

Result:

```text
PASS
SkillRoot=C:\Users\js\Documents\Codex\Lib.Db\.claude\skills\lib-db
MarkdownFiles=9
SkillLines=95
References=6
UnsafeExamples=0
```

## What Is Strong

1. The entrypoint is concise and well-routed.

`SKILL.md` stays under the recommended 500-line threshold and delegates detailed content to focused references:

- `references/security-guardrails.md`
- `references/runtime-api.md`
- `references/mapping-contracts.md`
- `references/tvpgen-guide.md`
- `references/examples.md`
- `references/verification.md`

This matches the progressive-disclosure model described by Agent Skills and Claude Code documentation.

2. The security posture is unusually explicit for a project skill.

Strong lines:

- `SKILL.md:42`: no secret/token/password/full connection string output.
- `SKILL.md:43`: no direct SQL CLI DDL/DML without approval.
- `SKILL.md:45`: `RawSqlPolicy.DenyWriteText` is a guardrail, not a parser or complete security boundary.
- `SKILL.md:48`: `allowed-tools` is not treated as a security boundary.
- `references/security-guardrails.md:15`: correctly explains that runtime permissions and repository instructions still govern side effects.
- `references/security-guardrails.md:75`: direct SQL tools are read-only by default and DDL/DML/backup/restore/SP execution requires explicit approval.

3. The domain guidance is concrete.

The skill captures important Lib.Db-specific invariants:

- `CELL_NO` -> `CellNo` mapping behavior.
- normalized column/property collision handling.
- generated `[DbResult]` `Map(DbDataReader)` contract.
- `MonitoredSqlDataReader : DbDataReader` compatibility.
- raw `DateOnly` and `TimeOnly` binding.
- `SET QUOTED_IDENTIFIER ON` for computed-column index setup.

That is the kind of project-specific knowledge that belongs in a skill rather than in generic global instructions.

## Findings

### P1: The Skill Is Version-Stale Against The Current Repository

Evidence:

- `Lib.Db/.claude/skills/lib-db/SKILL.md:3` says the skill applies to `Lib.Db v2.2.1`.
- `Lib.Db/.claude/skills/lib-db/SKILL.md:16` titles it `Lib.Db v2.2.1 Skill`.
- `Lib.Db/.claude/skills/lib-db/SKILL.md:52` declares `v2.2.1 Invariants`.
- `Lib.Db/Lib.Db/Lib.Db.csproj:44` now has `<Version>2.3.0</Version>`.
- `Lib.Db/Verification/manifest.json:2` declares `"version": "v2.3.0"`.

Impact:

Agents may preserve or reintroduce v2.2.1 framing while working in a v2.3.0 repository. This is especially risky for README, release notes, package verification, and current operational scripts.

Security calibration:

Not a direct vulnerability. It is a process/supply-chain risk: stale instructions can cause incomplete verification and stale security documentation.

Recommendation:

Update the skill to be version-neutral for persistent invariants, and add a small current-version section that points to `Verification/manifest.json` or the project file as the source of truth.

### P1: Verification Instructions Conflict With Current Verification Workflow

Evidence:

- `references/verification.md:40` recommends raw `dotnet test Tests/Lib.Db.IntegrationTests/...`.
- `references/verification.md:46` recommends raw `dotnet test ... V221BlockerVerificationTests`.
- `Lib.Db/Verification/README.md:18` says not to run database-backed tests through raw `dotnet test`; use `Invoke-Tests.ps1` for focused tests and `Invoke-Verification.ps1` for release gates.
- `Lib.Db/Verification/README.md:16` says the verification scripts load local environment setup automatically and print only key presence, not values.
- `Lib.Db/Verification/README.md:27` says direct SQL execution is restricted to allowlisted files under `Verification/databases/<DB>/`.

Impact:

An agent following the skill may choose the wrong test path, miss the wrapper scripts that enforce environment loading and redaction behavior, or report false blockers from the MSBuild guard.

Security calibration:

Medium process risk. The secret-handling guidance is good, but the stale command path bypasses the current verification wrapper design.

Recommendation:

Make `Verification/scripts/Invoke-Tests.ps1`, `Invoke-Coverage.ps1`, `Invoke-Benchmarks.ps1`, and `Invoke-Verification.ps1` the primary commands. Keep raw `dotnet test` only for narrow non-DB unit-test cases where the current repo docs permit it.

### P1: v2.3.0 Verification Surfaces Are Missing

Evidence:

The current repository contains v2.3.0 verification scope for AOT, coverage, benchmark, chaos, and consolidated verification assets:

- `Lib.Db/Verification/manifest.json:2`: v2.3.0 manifest.
- `Lib.Db/Verification/README.md:16`: wrapper scripts for tests, coverage, benchmark, full verification.
- `Lib.Db/docs/superpowers/specs/2026-05-19-verification-root-consolidation-design.md:9`: v2.3.0 verification, benchmark, SQL setup/verify, AOT, coverage, and harness assets are consolidated under `Verification/`.
- The skill package contains zero mentions of `AOT`, `NativeAOT`, `Lib.Db.AotVerification`, `BenchmarkDotNet`, `Invoke-Benchmarks`, `Invoke-Coverage`, `Invoke-Verification`, `ChaosHarness`, `LIBDB_CHAOS_TEST`, `Verification/manifest.json`, or `Verification/scripts`.

Impact:

The skill will under-trigger or under-instruct on current release gates and important runtime compatibility work.

Security calibration:

Medium. Missing AOT/coverage/benchmark/chaos guidance is not inherently exploitable, but it can cause incomplete release validation and misplaced confidence.

Recommendation:

Add a `references/verification-v2.3.md` or refresh `references/verification.md` to cover the consolidated verification root, manifest, AOT publish gate, benchmark artifacts, coverage, and chaos harness.

### P2: Skill Tests Are Mostly Static, Not Behavioral Proof

Evidence:

- `tests/scenarios.md` has useful pressure scenarios.
- `tests/validate-skill.ps1` checks package shape and unsafe-example patterns.
- There is no recorded baseline failure, agent run transcript, pass/fail matrix, or automated harness that proves an agent actually follows the skill under pressure.

Impact:

The skill can pass static validation while still failing in realistic prompts, especially around stale version handling and current verification scripts.

Superpowers calibration:

The `writing-skills` method treats skill creation like TDD for process documentation. This package has scenarios and static checks, but not a RED/GREEN behavioral evidence loop.

Recommendation:

Add a small `tests/results/` record or markdown matrix:

- scenario id
- prompt
- expected references loaded
- observed agent behavior without skill
- observed agent behavior with skill
- pass/fail
- date/model/client

### P3: Claude-Code-Specific Frontmatter Is Fine, But Portability Is Limited

Evidence:

- `SKILL.md:4-13` uses `allowed-tools` and `paths`.
- Claude Code docs support YAML-list `allowed-tools` and `paths`.
- Agent Skills open spec is more minimal and treats some fields as optional or implementation-specific.

Impact:

This is not a defect if the skill is intentionally for Claude Code project use. It is a portability caveat for other agents or Codex-style skill loaders.

Recommendation:

Add a short compatibility note if cross-agent use matters:

```yaml
compatibility: Designed for Claude Code project skills in the Lib.Db repository; verify frontmatter support in other agents.
```

## Codex Security Attack-Path Assessment

Threat model:

The skill influences agent-generated code and commands. The realistic attacker is not a remote end user; it is a bad or stale instruction path that causes the agent to generate unsafe examples, skip verification, or mishandle secrets.

Candidate findings considered:

1. Secret exfiltration via skill instructions: suppressed. The skill explicitly forbids printing secret values and the static validator found no unsafe examples.
2. Tool preapproval abuse: suppressed. The skill only preapproves read/search tools and explicitly warns that `allowed-tools` is not a security boundary.
3. SQL DDL/DML unsafe execution: suppressed for the skill package itself. Direct SQL execution guidance requires explicit approval.
4. Stale verification path causing missed release/security gates: survives as a process finding.
5. Stale version framing causing incorrect generated docs/code: survives as a quality/process finding.

Reportable security finding:

No direct code-execution, credential-leak, or SQL-execution vulnerability was found in the skill package. The reportable issue is a high-priority maintenance risk: stale verification guidance can create false confidence during security-sensitive DB/release work.

## Recommended Fix Order

1. Update all frontmatter/title/doc-test references from hard-coded `v2.2.1` to current-version-aware wording.
2. Make `Verification/manifest.json` and `Verification/README.md` the authoritative verification workflow sources.
3. Replace raw DB-backed `dotnet test` guidance with the current wrapper scripts.
4. Add v2.3.0 verification surfaces: AOT, coverage, benchmark, chaos, consolidated verification root.
5. Add behavioral skill test results for the existing six pressure scenarios.
6. Optionally add `compatibility` frontmatter for Claude Code-specific behavior.

## Bottom Line

Keep the skill, but do not trust it as-is for current Lib.Db work. Its security instincts are good and its v2.2.1 mapper/security knowledge is valuable, but the repository has moved on to v2.3.0 verification infrastructure. The next edit should be a refresh, not a rewrite.
