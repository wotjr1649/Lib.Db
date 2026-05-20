# Examples

Use these examples as small consumer-facing templates. Keep secrets, full connection strings, high-privilege logins, certificate bypass defaults, repository paths, and package-source commands out of examples.

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

## Production-Oriented Configuration

```json
{
  "LibDb": {
    "ConnectionStringNames": [ "Default" ],
    "ConnectionSecurityProfile": "Production",
    "RawSqlPolicy": "DenyWriteText",
    "EnableObservability": true
  }
}
```

Store the actual connection string in the application's approved secret/configuration provider. Do not print it.

## Query Single Row By Stored Procedure

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

## Execute A Stored Procedure Write

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerCd })
    .ExecuteAsync(ct);

if (!result.IsSuccess)
{
    logger.LogWarning("Order insert failed: {SqlErrorCode}", result.Error?.SqlErrorCode);
}
```

## Intentional Parameterized Text SQL Read

```csharp
DbResult<long?> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

Use text SQL only when intentional and allowed by application raw SQL policy.

## Stream Rows

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

## DateOnly And TimeOnly Parameters

```csharp
DbResult<IAsyncEnumerable<ScheduleDto>> result = await db.Default
    .Procedure("dbo.usp_SearchSchedule")
    .With(new
    {
        WorkDate = DateOnly.FromDateTime(request.WorkDate),
        StartAt = TimeOnly.FromDateTime(request.StartAt)
    })
    .QueryAsync<ScheduleDto>(ct);
```

## Result Mapping With SQL Aliases

SQL shape:

```sql
SELECT
    CELL_NO AS CellNo,
    CUSTOMER_CD AS CustomerCd
FROM dbo.Customer;
```

DTO shape:

```csharp
public sealed class CustomerDto
{
    public string CellNo { get; init; } = "";
    public string CustomerCd { get; init; } = "";
}
```

## Generated Result DTO

```csharp
[DbResult]
public sealed class OrderSummaryDto
{
    public string OrderNo { get; init; } = "";
    public DateOnly OrderDate { get; init; }
    public decimal TotalAmount { get; init; }
}
```

## TVP Row

```csharp
[TvpRow("dbo.OrderLineTvp")]
public sealed class OrderLineRow
{
    public int LineNo { get; init; }
    public string ItemCode { get; init; } = "";
    public decimal Quantity { get; init; }
}
```

Usage shape:

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_SaveOrderLines")
    .With(new
    {
        OrderNo = orderNo,
        Lines = orderLines
    })
    .ExecuteAsync(ct);
```
