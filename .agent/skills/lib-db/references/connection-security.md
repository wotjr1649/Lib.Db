# Connection Security

Use this file for secret handling, connection strings, production defaults, raw SQL policy, and direct SQL requests.

## Secret Handling

- Never print secret values, passwords, tokens, or full connection strings.
- Report only key names and existence, for example: `Default: present`.
- Do not log `ConnectionStrings` dictionary values.
- Do not include inline credentials in examples.

## Production Defaults

Use `UseProductionSecurityDefaults()` for production-oriented examples:

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
    options.UseProductionSecurityDefaults();
});
```

This sets production connection validation, disables parameter trace output, and tightens raw SQL policy if it was still permissive.

## Connection Security Profile

- `ConnectionSecurityProfile.Development`: local development default.
- `ConnectionSecurityProfile.Production`: rejects unsafe production connection settings unless explicit waiver flags are set.
- `AllowProductionTrustServerCertificateWaiver`: only for controlled migration windows.
- `AllowProductionSaLoginWaiver`: avoid; use least-privilege SQL Server principals.

## Raw SQL Policy

| Policy | Meaning |
| --- | --- |
| `RawSqlPolicy.Allow` | Allows text SQL. Use only with clear application policy. |
| `RawSqlPolicy.DenyAllText` | Blocks all `CommandType.Text`; stored procedures still work. |
| `RawSqlPolicy.DenyWriteText` | Blocks write, permission, schema, and operational-looking text SQL. |

`DenyWriteText` is a conservative guardrail, not a full SQL parser. For production permission boundaries, prefer stored procedures.

## Text SQL Rules

Safe value binding:

```csharp
DbResult<long?> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

Parameterized raw SQL text:

```csharp
DbResult<UserDto?> result = await db.Default
    .Sql("SELECT Id, Name FROM dbo.Users WHERE Id = @UserId")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

Do not concatenate user input into SQL text.

## Sensitive APIs

- `UseConnectionString(string)`: accepts a full connection string. Prefer named instances. Never print the value.
- `Use(string)` and `UseSchema(string)` accept registered instance names, not `Raw:` connection string shortcuts.
- In `ConnectionSecurityProfile.Production`, ad-hoc `UseConnectionString(...)` values are validated by the same encryption, certificate, and high-privilege login rules as configured connection strings.
- `IncludeParametersInTrace`: keep `false` except in tightly controlled diagnostics.
- `UseSnapshotOnlyUnsafe`, `UseServiceOnlyUnsafe`, `UseSnapshotPreferredUnsafe`: advanced schema lookup overrides. Keep inside domain-owned helpers.
- `BulkInsertAsync<T>`: validate tenant boundaries and destination table.
- Schema maintenance calls: keep operational authorization separate from application read paths.

## Direct SQL Tool Requests

If the user asks to run direct SQL through CLI tools, follow the repository SQL approval rules. SELECT-only inspection may be allowed; DDL, DML, procedure execution, backup, restore, and permission operations need explicit approval.
