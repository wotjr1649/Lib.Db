# Verification

Use this file before claiming a Lib.Db skill-guided change is complete.

## Static Skill Checks

For this skill package:

- `SKILL.md` should remain under 500 lines.
- Detailed reference content should live under `references/`.
- Do not add inline secret values or full connection strings.
- Do not add examples that default to high-privilege database logins or certificate validation bypasses.
- Keep references one directory deep from `SKILL.md`.

Suggested checks:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .claude/skills/lib-db/tests/validate-skill.ps1
Get-Content .claude/skills/lib-db/SKILL.md | Measure-Object -Line
Select-String -Path .claude/skills/lib-db/**/*.md -Pattern 'Password\s*=|User\s+Id\s*=\s*sa|TrustServerCertificate\s*=\s*True'
Select-String -Path .claude/skills/lib-db/**/*.md -Pattern 'RawSqlPolicy|ConnectionSecurityProfile|DbDataReader|DateOnly|TimeOnly'
```

## Runtime Build Checks

Run focused builds for touched packages:

```powershell
dotnet build Lib.Db/Lib.Db.csproj -c Release
dotnet build Lib.Db.TvpGen/Lib.Db.TvpGen.csproj -c Release
```

Use the repository's required execution environment. If the user requires external PowerShell for `dotnet`, follow that requirement.

## Targeted Tests

Prefer targeted tests first:

```powershell
dotnet test Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MappersTests|FullyQualifiedName~DataBindingTests|FullyQualifiedName~DbFirstTvpGeneratorTests"
```

For v2.2.1 blocker regression coverage, include the verification DB tests when the local SQL Server verification database is available:

```powershell
dotnet test Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~V221BlockerVerificationTests"
```

Do not print database passwords or full connection strings when setting up test environment variables. Report only which keys were present.

## Documentation Checks

When docs are touched:

- confirm v2.2.1 behavior is represented
- confirm obsolete `EnableOpenTelemetry` is not recommended for new code
- confirm `RawSqlPolicy.DenyWriteText` is described as a guardrail, not a complete security boundary
- confirm `[DbResult]` mentions `Map(DbDataReader)`

## Completion Statement

A completion report should include:

- changed files
- focused commands run and pass/fail result
- broader commands run and pass/fail result
- proof that the original issue no longer appears
- remaining risks or skipped validation
