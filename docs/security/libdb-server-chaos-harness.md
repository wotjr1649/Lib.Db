# Lib.Db Server Chaos Harness

The default verification path must stay database-scoped. Server-level chaos is opt-in only and is split into a separate SQL setup, Harness command, verification, and teardown.

## Scope

- Target database: `LIBDB_CHAOS_TEST`
- Server observer: Extended Events session `libdb_chaos_observer`
- SQL files:
  - `Verification/databases/LIBDB_CHAOS_TEST/server-optin/setup-libdb-chaos-server-optin.sql`
  - `Verification/databases/LIBDB_CHAOS_TEST/server-optin/verify-libdb-chaos-server-optin.sql`
  - `Verification/databases/LIBDB_CHAOS_TEST/server-optin/teardown-libdb-chaos-server-optin.sql`
- Harness project: `Verification/projects/Lib.Db.ChaosHarness`

## Safety Gates

- Every SQL file requires `EnableServerChaos=1`.
- Every Harness command requires `--enable-server-chaos`.
- The SQL files reject databases other than `LIBDB_CHAOS_TEST`.
- `KILL` stimulus is skipped unless the Harness also receives `--allow-kill`.
- Teardown is a separate command even when `all` is used.

## Required Permissions

- Extended Events setup requires `CREATE ANY EVENT SESSION` or `ALTER ANY EVENT SESSION`, or `sysadmin`.
- Extended Events start/stop requires `ALTER ANY EVENT SESSION`, or `sysadmin`.
- Extended Events teardown requires `DROP ANY EVENT SESSION` or `ALTER ANY EVENT SESSION`, or `sysadmin`.
- Reading the active ring buffer target requires server-level visibility permissions such as `VIEW SERVER STATE` or, on SQL Server 2022 and later, `VIEW SERVER PERFORMANCE STATE`.
- `KILL` stimulus requires `ALTER ANY CONNECTION` or membership in `sysadmin`/`processadmin`.

## Harness Flow

Set either `LIBDB_CHAOS_CONNECTION`, or set `LIBDB_CHAOS_PASSWORD` and pass optional `--server` and `--user`. Direct Harness commands accept explicit connection input, so keep the target to a local disposable SQL Server instance only; do not point the Harness at shared, staging, production, or customer servers.

```powershell
dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- setup --enable-server-chaos
dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- run --enable-server-chaos
dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- verify --enable-server-chaos
dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- teardown --enable-server-chaos
```

To include the destructive session termination probe:

```powershell
dotnet run --project Verification/projects/Lib.Db.ChaosHarness -- run --enable-server-chaos --allow-kill
```

The Harness does not print connection strings or passwords.
