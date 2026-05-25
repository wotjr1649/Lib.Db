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

`QueryMultipleAsync()` requires MARS support in Lib.Db's current execution policy.

- `MarsPolicy.Disabled`: blocks `QueryMultipleAsync()`.
- `MarsPolicy.Auto`: requires the configured connection string to already include `MultipleActiveResultSets=True`; otherwise Lib.Db throws before executing.
- `MarsPolicy.ForceEnable`: injects `MultipleActiveResultSets=True` during `AddLibDb(...)` registration. Use this only when the application accepts automatic connection-string adjustment.

```csharp
using Lib.Db.Extensions;

DbResult<DbMultiple<OrderDto, OrderSummaryDto>> result = await db.Default
    .Procedure("dbo.usp_GetOrderDashboard")
    .With(new { CustomerId = customerId })
    .QueryMultipleAsync(ct)
    .ReadMultipleAsync<OrderDto, OrderSummaryDto>(ct);

if (!result.IsSuccess)
    return Dashboard.Empty;

List<OrderDto> orders = result.Value.First;
OrderSummaryDto? summary = result.Value.Second.SingleOrDefault();
```

`ReadMultipleAsync<T1,T2>()`, `ReadMultipleAsync<T1,T2,T3>()`, and `ReadMultipleAsync<T1,T2,T3,T4>()` read result sets in stored-procedure order, dispose the reader, and return `DbMultiple<...>` with `List<T>` fields named `First`, `Second`, `Third`, and `Fourth`. Missing result sets or read failures return the redacted message `Reading multiple result sets failed.`.

Use `IMultipleResultReader` directly only when you need manual consumption. It is stateful and single-consumer. Read result sets in order.

## Advanced Snapshot Overrides

`UseSnapshotOnlyUnsafe`, `UseServiceOnlyUnsafe`, and `UseSnapshotPreferredUnsafe` are hidden advanced extensions in `Lib.Db.Extensions`. Use them only inside domain-owned infrastructure when the schema cache strategy is intentionally controlled.
