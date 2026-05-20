# Security Guardrails

Use this file when Lib.Db application code touches SQL Server connection security, raw SQL policy, credentials, logging, or production examples.

## Threat Model

This skill influences agent-generated application code. The main risk is unsafe code generation:

- high-privilege database login examples copied into applications
- certificate validation bypasses copied into production settings
- raw SQL used where stored procedures or least-privilege permissions are expected
- secret values or full connection strings printed into chat, logs, docs, commits, or tests
- local development shortcuts treated as production security guarantees

Tool allowlists and application guardrails are not security boundaries by themselves. User instructions, repository permissions, runtime configuration, SQL Server permissions, code review, and deployment controls still matter.

## Secret and Connection String Handling

- Do not print secret values, passwords, tokens, or full connection strings.
- It is acceptable to name configuration keys and say whether a value exists.
- Prefer configuration providers, user secrets, environment variables, managed secret stores, or CI/CD secret injection over inline values.
- Redact logs and exception messages that could expose credentials or sensitive parameter values.

Safe reporting shape:

```text
Connection setting checked:
- Key: ConnectionStrings:Default
- Value present: yes
- Value printed: no
```

## Production Connection Security

For production-oriented code or docs:

- use production security defaults when configuring Lib.Db options
- use least-privilege SQL Server principals
- prefer integrated identity or managed credentials where available
- avoid high-privilege SQL logins in examples
- avoid certificate validation bypasses as defaults
- keep development-only shortcuts labeled as development-only
- route write and administrative operations through stored procedures when possible

Development-only shortcuts must not be shown as the default path.

## Raw SQL Policy

Prefer `.Procedure(...)` for:

- mutations
- administrative operations
- tenant-sensitive data access
- permission boundaries
- any operation governed through SQL Server permissions

Use `.Sql(...)` or `.SqlInterpolated(...)` only when text SQL is intentional and covered by policy.

Policy guidance:

- `RawSqlPolicy.Allow`: compatibility mode; not an operational safety posture
- `RawSqlPolicy.DenyWriteText`: transition guardrail for mutating or administrative text commands
- `RawSqlPolicy.DenyAllText`: strongest application-level guardrail when raw text SQL should not execute

Do not describe raw SQL policy as a complete SQL parser or standalone security boundary. Pair it with stored procedures, least-privilege SQL permissions, review, and focused tests in the consuming application.

## Direct SQL Execution

If the user asks to run SQL directly through command-line tools, follow the active user and repository rules for SQL execution. Do not run DDL, DML, backup/restore, or stored procedure execution through direct SQL CLI tools unless the user explicitly approves that exact activity.

Application code may call stored procedures or execute configured Lib.Db commands when the user task and local project rules permit it.

## Logging and Diagnostics

- Do not log full SQL parameter values when they may contain sensitive data.
- Prefer structured metadata: command type, instance name, elapsed time, row count, and error classification.
- Treat diagnostic wrappers as normal `DbDataReader` implementations.
- Keep exception logs useful without including credentials or full SQL connection details.

## Review Checklist

- Are secrets and full connection strings absent from generated code, logs, and docs?
- Are production examples free of high-privilege login defaults and certificate bypass defaults?
- Are writes and permission-boundary operations routed through stored procedures where practical?
- Is raw SQL intentional and covered by an explicit raw SQL policy?
- Does the text avoid claiming that application policy alone is a complete security boundary?
