# Examples

Use these as safe, compact patterns. Adapt names to the local code and tests.

## Query Single Row by Stored Procedure

```csharp
public async Task<UserDto?> GetUserAsync(int userId, CancellationToken ct)
{
    DbResult<UserDto?> result = await db.Default
        .Procedure("dbo.usp_GetUser")
        .With(new { UserId = userId })
        .QuerySingleAsync<UserDto>(ct);

    return result is { IsSuccess: true } ? result.Value : null;
}
```

## Stream Rows

```csharp
public async IAsyncEnumerable<OrderDto> GetOrdersAsync(
    string status,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    DbResult<IAsyncEnumerable<OrderDto>> result = await db.Default
        .Procedure("dbo.usp_GetOrders")
        .With(new { Status = status })
        .QueryAsync<OrderDto>(ct);

    if (!result.IsSuccess || result.Value is null)
        yield break;

    await foreach (OrderDto order in result.Value.WithCancellation(ct))
        yield return order;
}
```

## Parameterized Text SQL

Use this only when text SQL is intentional and allowed by `RawSqlPolicy`.

```csharp
DbResult<int?> count = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<int>(ct);
```

Avoid text SQL for writes in examples. Prefer stored procedures.

## Production-Oriented Options

```csharp
builder.Services.AddHighPerformanceDb(options =>
{
    options.ConnectionStringNames = ["Default"];
    options.UseProductionSecurityDefaults();
    options.Mars = MarsPolicy.ForceEnable;
    options.EnableObservability = true;
});
```

The connection string value should be supplied by configuration providers such as environment variables, user secrets, or CI secrets.

## DateOnly and TimeOnly Parameters

```csharp
DbResult<IAsyncEnumerable<ScheduleRow>> result = await db.Default
    .Procedure("dbo.usp_GetSchedule")
    .With(new
    {
        BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
        StartTime = new TimeOnly(9, 0)
    })
    .QueryAsync<ScheduleRow>(ct);
```

## Result Mapping With SQL Aliases

Normalization supports common SQL identifier styles, but aliases are still the clearest contract for public queries.

```sql
SELECT
    CELL_NO AS CellNo,
    SLOT_NAME AS SlotName
FROM verify.ResultMappingRows;
```

## Generated Result DTO

```csharp
[DbResult]
public sealed partial record UserResult(
    int UserId,
    string UserName,
    string Email);
```

The generated code must include `Map(DbDataReader)` and remain compatible with `MonitoredSqlDataReader`.
