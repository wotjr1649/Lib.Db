# Examples

Use these as small consumer-facing templates. Do not include secrets, full connection strings, high-privilege logins, certificate bypass defaults, direct SQL tool workflows, or package maintenance commands.

## Dependency Injection

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

## Query Single Row

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

## Execute Write Stored Procedure

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_UpdateOrderStatus")
    .With(new { OrderId = orderId, Status = status })
    .ExecuteAsync(ct);

if (!result.IsSuccess)
{
    logger.LogWarning(
        "Order update failed. Kind={Kind}, SqlErrorCode={SqlErrorCode}",
        result.Error?.Kind,
        result.Error?.SqlErrorCode);
}
```

## Parameterized Text Read

```csharp
DbResult<long?> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

## Stream Rows

```csharp
DbResult<IAsyncEnumerable<OrderDto>> result = await db.Default
    .Procedure("dbo.usp_StreamOrders")
    .With(new { CustomerId = customerId })
    .QueryAsync<OrderDto>(ct);

if (result.IsSuccess)
{
    await foreach (OrderDto row in result.Value!.WithCancellation(ct))
    {
        Handle(row);
    }
}
```

## Multiple Result Sets

```csharp
using Lib.Db.Extensions;

DbResult<DbMultiple<OrderDto, OrderSummaryDto>> result = await db.Default
    .Procedure("dbo.usp_GetDashboard")
    .With(new { CustomerId = customerId })
    .QueryMultipleAsync(ct)
    .ReadMultipleAsync<OrderDto, OrderSummaryDto>(ct);

if (result.IsSuccess)
{
    List<OrderDto> orders = result.Value.First;
    OrderSummaryDto? summary = result.Value.Second.SingleOrDefault();
}
```

## Transaction

```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", ct);

DbResult<int> write = await tx
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerId })
    .ExecuteAsync(ct);

if (!write.IsSuccess)
    return write;

DbResult<bool> commit = await tx.CommitAsync(ct);
```

## TVP With Static Shape

```csharp
using System.Data;

TvpShape<OrderLineRow> shape = TvpShape.For<OrderLineRow>()
    .Column("OrderId", SqlDbType.Int, static row => row.OrderId)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
    .Column("Quantity", SqlDbType.Int, static row => row.Quantity)
    .Build();

DbResult<int> result = await db.Default
    .Procedure("dbo.usp_ImportOrderLines")
    .With(new { Lines = LibDb.Tvp("dbo.OrderLineTvp", rows, shape) })
    .ExecuteAsync(ct);
```

## TVP With Scalars

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_ImportOrderLines")
    .With(new
    {
        RequestedBy = userId,
        Lines = LibDb.Tvp("dbo.OrderLineTvp", rows)
    })
    .ExecuteAsync(ct);
```

## Bulk Insert

```csharp
using System.Data;

BulkShape<OrderImportRow> shape = BulkShape.For<OrderImportRow>()
    .Key("OrderId", SqlDbType.Int, static row => row.OrderId)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
    .Column("Quantity", SqlDbType.Int, static row => row.Quantity)
    .Build();

DbResult<long> result = await db.BulkInsertAsync(
    "Default",
    "[dbo].[OrderImport]",
    records,
    shape,
    new BulkWriteOptions { BatchSize = 10_000, CheckConstraints = true },
    ct);
```

## Cache-Aside

```csharp
string userProfileCacheKey = cacheKeys.UserProfile(userId); // opaque app-owned label, not the raw identifier

DbResult<UserDto?> result = await QueryCacheExtensions.GetOrQueryAsync(
    cache,
    userProfileCacheKey,
    TimeSpan.FromMinutes(5),
    () => db.Default
        .Procedure("dbo.usp_GetUser")
        .With(new { UserId = userId })
        .QuerySingleAsync<UserDto>(ct),
    ct: ct);
```
