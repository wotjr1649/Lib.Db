# Runtime API Reference

Use this file when application code consumes Lib.Db runtime APIs: dependency injection, fluent execution, options, transactions, resilience, observability, or caching.

## Package Boundary

Treat Lib.Db as a NuGet dependency. Use public APIs from application code and avoid relying on package-source-only structure or maintenance tooling.

## Dependency Injection

Prefer registering Lib.Db through the application host and configuration system.

Typical shape:

```csharp
builder.Services.AddLibDb(builder.Configuration);
```

When custom options are needed, keep them explicit and production-safe:

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
    options.UseProductionSecurityDefaults();
    options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;
    options.EnableObservability = true;
});
```

Do not place full connection strings or passwords directly in source code examples.

## Fluent Execution Shape

Core stored procedure flow:

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

Stored procedure write flow:

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerCd })
    .ExecuteAsync(ct);
```

Intentional parameterized text SQL read:

```csharp
DbResult<long?> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

Use raw SQL only when it is intentional and allowed by application policy.

## `DbResult<T>` Handling

Handle success and failure explicitly:

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);

if (!result.IsSuccess)
{
    logger.LogWarning("User lookup failed: {SqlErrorCode}", result.Error?.SqlErrorCode);
    return null;
}

return result.Value;
```

Do not assume every command returns a non-null value. Distinguish missing rows, null database values, failed commands, and cancellation according to the consuming application's behavior.

## Streaming Rows

Use streaming APIs when result sets can be large and the consuming code can process rows incrementally:

```csharp
DbResult<IAsyncEnumerable<OrderDto>> result = await db.Default
    .Procedure("dbo.usp_StreamOrders")
    .With(new { Status = status })
    .QueryAsync<OrderDto>(ct);

if (!result.IsSuccess || result.Value is null)
{
    logger.LogWarning("Order stream failed: {SqlErrorCode}", result.Error?.SqlErrorCode);
    return;
}

await foreach (OrderDto order in result.Value.WithCancellation(ct))
{
    await processor.HandleAsync(order, ct);
}
```

Keep cancellation tokens flowing through the entire call chain.

## Transactions

Use Lib.Db transaction APIs when multiple commands must share one SQL transaction. Keep transaction scope small and avoid long-running work inside the transaction.

Example shape:

```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", ct);

DbResult<int> orderResult = await tx.Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerCd })
    .ExecuteAsync(ct);

if (!orderResult.IsSuccess)
{
    return;
}

DbResult<int> auditResult = await tx.Procedure("dbo.usp_InsertOrderAudit")
    .With(new { request.OrderNo, Action = "Created" })
    .ExecuteAsync(ct);

if (!auditResult.IsSuccess)
{
    return;
}

DbResult<bool> commitResult = await tx.CommitAsync(ct);
```

## Observability

Enable observability through public options and application logging infrastructure. Prefer structured metadata over raw SQL text or parameter values.

Useful metadata:

- command type
- configured instance name
- elapsed time
- row count
- success or error classification

## Caching

Use caching only for reads where staleness is acceptable. Do not cache tenant-sensitive or permission-sensitive results unless the cache key includes the relevant tenant, user, permission, and query dimensions.

## Consumer Checklist

- Is Lib.Db configured through application DI/configuration?
- Are secrets externalized?
- Are writes routed through stored procedures?
- Is raw SQL policy explicit?
- Are `DbResult<T>` failures handled?
- Are cancellation tokens passed through?
- Are logs structured and redacted?
