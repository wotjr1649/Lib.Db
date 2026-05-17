# Runtime API Reference

Use this file when work touches `Lib.Db/Lib.Db`, dependency injection, fluent execution, options, transactions, resilience, observability, or caching.

## Package Boundary

- `Lib.Db` is the runtime SQL Server data access library.
- `IDbSession` is the main application entry point.
- `Lib.Db.TvpGen` generates TVP and `[DbResult]` mapping code but does not own runtime connection policy.

## Dependency Injection

Common registration paths:

```csharp
builder.Services.AddLibDb(builder.Configuration);
```

```csharp
builder.Services.AddHighPerformanceDb(options =>
{
    options.ConnectionStringNames = ["Default"];
    options.UseProductionSecurityDefaults();
    options.EnableSchemaCaching = true;
    options.PrewarmSchemas = ["dbo"];
});
```

Do not place full connection string values in examples. Configuration keys may be shown, values should come from secret or environment providers.

## Fluent Execution Shape

Core flow:

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

Raw SQL should be explicit and policy-aware:

```csharp
DbResult<int?> count = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<int>(ct);
```

Prefer stored procedures for writes:

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerCd })
    .ExecuteAsync(ct);
```

## Important Options

| Option | Default | Guidance |
| --- | --- | --- |
| `ConnectionStringNames` | `["Default"]` | First name is the default instance. |
| `ConnectionSecurityProfile` | `Development` | Use `Production` for production validation. |
| `RawSqlPolicy` | `Allow` | Use `DenyWriteText` or `DenyAllText` for operational guardrails. |
| `Mars` | `MarsPolicy.Auto` | Use `MarsPolicy.ForceEnable` when multi-result workflows are required. |
| `StrictRequiredParameterCheck` | `true` | Keep enabled unless compatibility requires otherwise. |
| `EnableSchemaCaching` | `true` | Keep enabled for repeated stored procedure metadata lookup. |
| `EnableObservability` | `false` | Single observability master switch. |
| `EnableOpenTelemetry` | obsolete alias | Do not add new usage. |

## Transactions

Use `BeginTransactionAsync` when multiple commands must commit or roll back together.

```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", ct);

await tx.Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo })
    .ExecuteAsync(ct);

await tx.CommitAsync(ct);
```

Keep transaction scopes small. Avoid streaming large result sets while holding a transaction unless the caller explicitly needs that consistency boundary.

## Observability

Use `EnableObservability` for activity/metric emission. Do not introduce new `EnableOpenTelemetry` usage except migration documentation.

Do not log secret values, full connection strings, or sensitive parameter payloads.

## Caching

Use cache helpers only when the result is safe to cache for the requested scope and the cache key does not expose sensitive data. Include invalidation or freshness expectations in docs and tests when adding caching behavior.
