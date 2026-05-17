# Lib.Db Skill Tests

This folder validates the `.claude/skills/lib-db` package as a skill, not the Lib.Db runtime library itself.

## Test Strategy

The tests check three things:

1. Skill package structure follows current skill guidance: concise `SKILL.md`, valid frontmatter, one-level supporting references, and clear reference routing.
2. Security guardrails are encoded in the skill package: no unsafe connection string examples, no high-privilege login defaults, no certificate-bypass defaults, and no direct SQL DDL/DML automation encouragement.
3. Pressure scenarios describe how an agent should behave when the user asks for unsafe, stale, or v2.2.1-sensitive Lib.Db work.

## Files

- `scenarios.md`: manual/agent evaluation scenarios with expected behavior.
- `validate-skill.ps1`: deterministic static validator for structure, security patterns, references, and core v2.2.1 terms.

## Run

From the repository root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .claude/skills/lib-db/tests/validate-skill.ps1
```

The script returns exit code `0` when the skill package passes.

## Scope

These tests do not prove that Claude Code will always invoke the skill. They verify that, once invoked, the skill package is structurally sound and contains the expected guardrails. Invocation reliability should be checked by manual Claude Code prompts using `scenarios.md`.
