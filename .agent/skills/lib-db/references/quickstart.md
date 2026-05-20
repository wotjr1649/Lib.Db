# Quickstart

Use this file when the user asks a broad Lib.Db usage question and the right API family is not yet clear.

## Choose The Reference

| User task | Read |
| --- | --- |
| Register Lib.Db, bind options, choose config style | `options-and-registration.md` |
| Handle secrets, connection strings, raw SQL policy | `connection-security.md` |
| Build fluent queries or stored procedure calls | `fluent-execution.md` |
| Bind parameters or choose SQL parameter metadata | `parameters-and-binding.md` |
| Handle success, failure, null, or SQL error codes | `result-handling.md` |
| Map result DTOs, JSON columns, generated result mappers | `mapping-contracts.md` |
| Pass table-valued parameters | `tvp-source-generation.md` |
| Insert large row sets with `SqlBulkCopy` | `bulk-insert.md` |
| Flush schema cache or read TVP descriptors | `schema-maintenance.md` |
| Cache query results | `caching.md` |
| Run multiple commands atomically | `transactions.md` |
| Add health checks, hosted services, interceptors, host hook | `operations-integration.md` |
| Tune observability, retry, dry run, or fault injection | `diagnostics-resilience.md` |
| Avoid trimming or Native AOT hazards | `aot-trimming.md` |
| Need small templates | `examples.md` |

## Minimal Setup Shape

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
    options.UseProductionSecurityDefaults();
    options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;
});
```

## Minimal Call Shape

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

If a method or property is not listed in this skill, inspect the installed package metadata or application references before inventing an API shape.
