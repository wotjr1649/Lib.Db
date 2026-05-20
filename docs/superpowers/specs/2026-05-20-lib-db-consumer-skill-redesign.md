# Lib.Db Consumer Skill Redesign

## Goal

Redesign `.claude/skills/lib-db` as a consumer-facing domain skill for using the Lib.Db NuGet package in application code.

The skill must not behave like an internal repository development, release, verification, or test workflow guide. A user of the skill is assumed to have only the Lib.Db package and the skill package available.

## Non-Goals

- Do not document repository-internal verification, release, benchmark, coverage, chaos, or AOT workflows.
- Do not require access to project files such as package project files, internal manifests, repository README files, test projects, or verification scripts.
- Do not keep a `tests/` folder inside the skill package.
- Do not keep `references/verification.md`.
- Do not pin the skill to any release version or mention release-version strings inside the skill package.
- Do not use broad path-scoped skill frontmatter such as `**/*.cs`, `**/*.json`, or `**/*.md`; discovery should rely on the Lib.Db-specific description.

## Target Skill Identity

The skill should present itself as a Lib.Db usage guide for application developers.

The skill exists to help agents choose safe and correct Lib.Db usage patterns:

- dependency injection setup
- SQL Server connection security
- stored procedure execution
- intentional raw SQL usage and raw SQL policy
- `DbResult<T>` handling
- result mapping into DTOs
- `DateOnly` and `TimeOnly` binding
- TVP row usage
- source-generated row and result mappers
- production-safe examples
- secret and connection string redaction

## Target Structure

```text
lib-db/
  SKILL.md
  references/
    security-guardrails.md
    runtime-api.md
    mapping-contracts.md
    tvpgen-guide.md
    examples.md
```

Delete:

```text
lib-db/references/verification.md
lib-db/tests/
```

## Skill Entrypoint Design

`SKILL.md` should be short and act as a router.

Required content:

- frontmatter with `name: lib-db`
- a version-neutral `description`
- no broad `paths` frontmatter
- a purpose section explaining that this is a consumer-facing skill for using the Lib.Db NuGet package
- a reference map to the five retained reference files
- non-negotiable safety rules
- stable public API and behavior contracts
- completion criteria focused on safe consumer code, not repository verification

The description should describe triggering conditions only. It should not summarize workflow.

Candidate description:

```yaml
description: Use when using the Lib.Db NuGet package in application code, especially for dependency injection, SQL Server connection security, stored procedure execution, raw SQL policy, result mapping, DbResult handling, TVP rows, source-generated mappers, or production-safe examples.
```

## Reference File Design

### `security-guardrails.md`

Keep and refine as the strongest reference.

It should cover:

- never print secrets, tokens, passwords, or full connection strings
- report only configuration key names and value presence
- production connection settings
- least-privilege database permissions
- stored procedures for write and permission-boundary operations
- raw SQL policy as a guardrail, not a SQL parser or complete security boundary
- no high-privilege login, inline password, or certificate validation bypass defaults

Remove:

- direct SQL CLI workflow language
- repository approval language that only makes sense inside the source repository
- internal verification setup language

### `runtime-api.md`

Keep as public runtime usage guidance.

It should cover:

- DI registration
- fluent execution shape
- stored procedure calls
- parameter binding
- scalar, single-row, multi-row, and streaming result usage
- transaction usage from a consumer app
- options relevant to production consumers
- observability as public behavior, without internal test or release guidance

### `mapping-contracts.md`

Keep as consumer DTO and result binding guidance.

It should cover:

- result column name resolution
- exact case-insensitive matching before normalized underscore-insensitive matching
- collision behavior expectations
- DTO design guidance
- generated result mapper expectations
- `DbDataReader` as the public compatibility shape
- `DateOnly` and `TimeOnly` binding

### `tvpgen-guide.md`

Keep as consumer-oriented source generator guidance.

It should cover:

- `[TvpRow]` usage
- `[DbResult]` usage
- supported CLR-to-SQL type mapping at a practical level
- generated mapper compatibility expectations
- consumer troubleshooting guidance for schema mismatch and unsupported types

Remove:

- internal generator maintenance guidance
- repository test strategy
- release compatibility wording tied to a specific version

### `examples.md`

Keep only consumer examples.

Examples should include:

- DI setup
- production-safe configuration
- stored procedure query
- stored procedure write
- intentional parameterized raw SQL read
- TVP usage
- result DTO mapping
- safe `DbResult<T>` handling

Examples must not include:

- real secrets
- full connection strings
- high-privilege login defaults
- certificate validation bypass defaults
- repository paths
- verification database commands
- release or package validation commands

## Removal Rules

The skill package must not contain:

- release version strings matching `v?\d+\.\d+\.\d+`
- source repository verification instructions
- source repository release gate instructions
- source repository test project commands
- benchmark, coverage, chaos, or AOT workflow guidance
- references to internal manifests or project files as source of truth
- a `tests/` directory
- `references/verification.md`

## Success Criteria

- `.claude/skills/lib-db/SKILL.md` reads as a consumer-facing Lib.Db usage skill.
- `.claude/skills/lib-db/references/verification.md` is deleted.
- `.claude/skills/lib-db/tests/` is deleted.
- The skill package contains no release-version strings.
- The skill frontmatter does not broadly match unrelated C#, JSON, Markdown, or project files.
- The remaining reference files explain public package usage without requiring repository internals.
- Security guidance is preserved or strengthened.
- Examples are production-safe by default.
- The skill remains concise and uses references for detailed guidance.

## Self-Review

- No placeholder sections remain.
- Scope is intentionally limited to NuGet consumer usage.
- The design does not require repository-internal files or verification infrastructure.
- The deletion of `tests/` is intentional because test scenarios and validation scripts are skill-maintainer artifacts, not consumer-facing usage material.
- The deletion of `verification.md` is intentional because verification and release workflows are out of scope for the consumer skill.
