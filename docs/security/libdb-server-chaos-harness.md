# Lib.Db Server Chaos Harness

The default verification path must stay database-scoped. Server-level chaos is opt-in only and is split into a separate SQL setup, Harness command, verification, and teardown.

## Scope

- Target: a local disposable chaos verification database only.
- Server observer: a dedicated Extended Events session created and removed by the internal runbook.
- SQL files and Harness commands live under the internal verification tree and are not consumer-facing API.

## Safety Gates

- Every SQL file requires `EnableServerChaos=1`.
- Every Harness command requires `--enable-server-chaos`.
- The SQL files reject databases outside the dedicated disposable chaos target.
- `KILL` stimulus is skipped unless the Harness also receives `--allow-kill`.
- Teardown is a separate command even when `all` is used.

## Required Permissions

- Extended Events setup requires `CREATE ANY EVENT SESSION` or `ALTER ANY EVENT SESSION`, or `sysadmin`.
- Extended Events start/stop requires `ALTER ANY EVENT SESSION`, or `sysadmin`.
- Extended Events teardown requires `DROP ANY EVENT SESSION` or `ALTER ANY EVENT SESSION`, or `sysadmin`.
- Reading the active ring buffer target requires server-level visibility permissions such as `VIEW SERVER STATE` or, on SQL Server 2022 and later, `VIEW SERVER PERFORMANCE STATE`.
- `KILL` stimulus requires `ALTER ANY CONNECTION` or membership in `sysadmin`/`processadmin`.

## Harness Flow

The executable flow is an internal maintainer runbook, not consumer API documentation, and should not be copied into application projects.

Use only a local disposable SQL Server instance. Do not point server-level chaos validation at shared, staging, production, or customer servers. The Harness must not print connection strings or passwords.
