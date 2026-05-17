# Security Guardrails

Use this file when work touches connection configuration, raw SQL, SQL Server verification, secrets, diagnostics, or operational guidance.

## Threat Model

This skill influences agent-generated code. The main risk is not direct runtime execution from the skill file; it is unsafe code generation:

- high-privilege database login examples copied into applications
- certificate validation bypasses copied into production settings
- raw SQL used where stored procedures or least-privilege permissions are expected
- secret values or full connection strings printed into chat, logs, docs, commits, or tests
- local verification assumptions treated as production security guarantees

`allowed-tools` is not a security boundary. In Claude Code it can pre-approve tools while a skill is active, but repository instructions, user approval requirements, and runtime permission settings still govern side effects.

## Secret and Connection String Handling

- Do not print secret values, passwords, tokens, or full connection strings.
- It is acceptable to name configuration keys and say whether a value exists.
- Prefer configuration providers, user secrets, environment variables, or CI secrets over inline values.
- Documentation examples should show the key shape, not a real connection string value.

Safe shape:

```json
{
  "ConnectionStrings": {
    "Default": "<configured via secret or environment>"
  },
  "LibDb": {
    "ConnectionStringNames": ["Default"],
    "ConnectionSecurityProfile": "Production",
    "RawSqlPolicy": "DenyWriteText",
    "Mars": "ForceEnable",
    "EnableObservability": false
  }
}
```

## Production Connection Security

For production-oriented code or docs:

- apply `UseProductionSecurityDefaults()` when configuring `LibDbOptions`, or set equivalent options explicitly
- use `ConnectionSecurityProfile.Production`
- avoid high-privilege SQL logins in examples
- avoid certificate validation bypasses as defaults
- use least-privilege database permissions and stored procedures for write boundaries

Development-only shortcuts must be labeled as development-only and must not be shown as the default path.

## Raw SQL Policy

Prefer `.Procedure(...)` for:

- mutations
- administrative operations
- tenant-sensitive data access
- permission boundaries
- any operation that should be governed through SQL Server permissions

Use `.Sql(...)` or `.SqlInterpolated(...)` only when text SQL is intentional and covered by policy.

Policy guidance:

- `RawSqlPolicy.Allow`: compatibility mode; not an operational safety posture
- `RawSqlPolicy.DenyWriteText`: transition guardrail for mutating or administrative text commands
- `RawSqlPolicy.DenyAllText`: strongest application-level guardrail when raw text SQL should not execute

Do not describe `DenyWriteText` as a complete SQL parser or standalone security boundary. Pair it with stored procedures, least-privilege SQL permissions, review, and tests.

## Direct SQL Execution

Direct SQL tools are read-only by default in this project. Do not run DDL/DML, backup/restore, or stored procedure execution through direct SQL CLI unless the user explicitly approves that exact activity.

Application/test-code execution may create verification schemas, stored procedures, or test data when the repository workflow or user request permits it.

## Logging and Diagnostics

- Do not log full SQL parameter values when they may contain sensitive data.
- Prefer structured metadata: command type, instance name, elapsed time, row count, and error classification.
- Treat diagnostic wrappers such as `MonitoredSqlDataReader` as normal `DbDataReader` implementations.

## Security Review Checklist

- No inline secret or full connection string values were added.
- Production examples use production security defaults or equivalent explicit settings.
- Raw SQL examples state why raw SQL is acceptable.
- Mutating text SQL is avoided or covered by policy.
- Verification claims distinguish local test coverage from production guarantees.
