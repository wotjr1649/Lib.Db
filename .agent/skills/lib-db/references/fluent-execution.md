# Fluent Execution

Use this file for `IDbSession`, fluent command construction, result-set shapes, and command execution.

## Entry Point

```csharp
public sealed class UserRepository(IDbSession db)
{
}
```

`IDbSession` members:

- `Default`: start a fluent command on the first configured instance.
- `Use(string instanceName)`: start on a named configured instance.
- `UseConnectionString(string connectionString)`: ad-hoc connection. Treat as sensitive.
- `Schema`: schema maintenance on the default instance.
- `UseSchema(string instanceName)`: schema maintenance on a named instance.
- `BeginTransactionAsync(...)`: start a transaction.
- `BulkInsertAsync<T>(...)`: bulk insert row objects.

## Command Selection

| Method | Use |
| --- | --- |
| `.Procedure("dbo.usp_Name")` | Stored procedure call. Preferred for writes and permission boundaries. |
| `.Sql("... @Param ...")` | Intentional text SQL with `.With(...)` parameters. |
| `.Sql(FormattableString)` | Interpolated parameterized SQL. |
| `.SqlInterpolated(FormattableString)` | Explicit name for interpolated parameterized SQL. Prefer this over ambiguous overloads. |

## Parameter And Option Stage

```csharp
var stage = db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId });
```

- `.With<TParams>(TParams parameters)` accepts anonymous objects, DTOs, dictionaries, and supported parameter values.
- `.WithTimeout(int timeoutSeconds)` overrides command timeout for that command.

## Execution Methods

| Method | Return |
| --- | --- |
| `QueryAsync<T>(ct)` | `Task<DbResult<IAsyncEnumerable<T>>>` |
| `QuerySingleAsync<T>(ct)` | `Task<DbResult<T?>>` |
| `ExecuteScalarAsync<T>(ct)` | `Task<DbResult<T?>>` |
| `QueryMultipleAsync(ct)` | `Task<DbResult<IMultipleResultReader>>` |
| `ExecuteAsync(ct)` | `Task<DbResult<int>>` |

## Stream Rows

```csharp
DbResult<IAsyncEnumerable<OrderDto>> result = await db.Default
    .Procedure("dbo.usp_StreamOrders")
    .With(new { CustomerId = customerId })
    .QueryAsync<OrderDto>(ct);

if (!result.IsSuccess)
    return [];

List<OrderDto> orders = [];
await foreach (OrderDto row in result.Value!.WithCancellation(ct))
    orders.Add(row);
```

## Single Row

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

No row is a successful result with `Value == null`; it is not a failed command.

## Scalar

```csharp
DbResult<long?> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Orders WHERE Status = {status}")
    .ExecuteScalarAsync<long>(ct);
```

## Multiple Result Sets

`QueryMultipleAsync()` needs MARS support. Set `options.Mars = MarsPolicy.ForceEnable` only when the application accepts automatic connection-string adjustment.

```csharp
DbResult<IMultipleResultReader> result = await db.Default
    .Procedure("dbo.usp_GetOrderDashboard")
    .With(new { CustomerId = customerId })
    .QueryMultipleAsync(ct);

if (!result.IsSuccess)
    return Dashboard.Empty;

await using IMultipleResultReader grid = result.Value!;
List<OrderDto> orders = await grid.ReadAsync<OrderDto>(ct);
OrderSummaryDto? summary = await grid.ReadSingleAsync<OrderSummaryDto>(ct);
```

`IMultipleResultReader` is stateful and single-consumer. Read result sets in order.

## Advanced Snapshot Overrides

`UseSnapshotOnlyUnsafe`, `UseServiceOnlyUnsafe`, and `UseSnapshotPreferredUnsafe` are hidden advanced extensions in `Lib.Db.Extensions`. Use them only inside domain-owned infrastructure when the schema cache strategy is intentionally controlled.
