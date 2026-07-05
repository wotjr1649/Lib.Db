# Parameters And Binding

Use this file for `.With(...)`, anonymous object binding, raw SQL parameters, SQL type metadata, nulls, `DateOnly`, `TimeOnly`, and TVP handoff.

## Namespaces

```csharp
using System.Data;
using Lib.Db.Contracts.Mapping;
using Microsoft.Data.SqlClient;
```

## Basic Anonymous Object

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId, IncludeInactive = false })
    .QuerySingleAsync<UserDto>(ct);
```

Property names should match stored procedure parameters or text SQL parameter names.

When the static parameter type is `object`, Lib.Db binds the runtime concrete object's public readable properties. A top-level scalar `string` value passed to `.With(...)` remains a string parameter value and is not reflected as a parameter bag. Runtime `Dictionary<string, object?>` and `DataRow` parameter bags keep their specialized binding behavior. If an `object`-typed non-scalar value has no readable public properties, treat it as an invalid parameter bag instead of relying on silent null/missing parameter binding.

## DTO Parameters

```csharp
public sealed record SearchOrdersParams(
    int CustomerId,
    DateOnly FromDate,
    DateOnly ToDate,
    string? Status);

DbResult<IAsyncEnumerable<OrderDto>> result = await db.Default
    .Procedure("dbo.usp_SearchOrders")
    .With(new SearchOrdersParams(customerId, fromDate, toDate, status))
    .QueryAsync<OrderDto>(ct);
```

## Raw SQL Parameters

```csharp
DbResult<UserDto?> result = await db.Default
    .Sql("SELECT Id, Name FROM dbo.Users WHERE Id = @UserId")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);
```

Prefer `.SqlInterpolated(...)` when composing text SQL with values:

```csharp
DbResult<long?> result = await db.Default
    .SqlInterpolated($"SELECT COUNT_BIG(*) FROM dbo.Users WHERE IsActive = {isActive}")
    .ExecuteScalarAsync<long>(ct);
```

## SQL Metadata Overrides

Use `DbParameterAttribute` when automatic inference is not precise enough:

```csharp
public sealed record UpdateProductParams(
    int ProductId,
    [property: DbParameter(DbType = SqlDbType.NVarChar, Size = 64)]
    string Sku,
    [property: DbParameter(DbType = SqlDbType.Decimal, Precision = 18, Scale = 2)]
    decimal Price);
```

Useful for strings, binary payloads, decimal precision, time scale, and large object columns.

## Explicit `SqlParameter`

Lib.Db passes existing `Microsoft.Data.SqlClient.SqlParameter` values through the binder. Use this when direction, provider type, size, precision, or scale must be fully explicit:

```csharp
var total = new SqlParameter("@Total", SqlDbType.Int)
{
    Direction = ParameterDirection.Output
};

DbResult<int> result = await db.Default
    .Procedure("dbo.usp_RecalculateOrder")
    .With(new
    {
        OrderId = orderId,
        Total = total
    })
    .ExecuteAsync(ct);
```

Prefer result sets or scalar return values for new APIs unless the stored procedure contract already requires output parameters.

## Nulls

- Nullable CLR values bind as database null values when the value is null.
- Model nullable DTO properties to match optional database parameters.
- For stored procedures, `StrictRequiredParameterCheck` helps catch missing required parameters before execution.

## Date And Time

- `DateOnly` binds as SQL `date`.
- `TimeOnly` binds as SQL `time`.
- Use `DbParameterAttribute` when scale or provider type must be explicit.

## TVP Parameters

Use `LibDb.Tvp(...)`, `TvpShape.For<T>()`, or `options.Tvp.Map<T>()` for table-valued parameters. Read `tvp-source-generation.md`.

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_ImportLines")
    .With(new
    {
        Lines = LibDb.Tvp("dbo.OrderLineTvp", rows, shape)
    })
    .ExecuteAsync(ct);
```

## Output Parameters

For output parameters, keep a reference to the `SqlParameter` you supplied and read it only after a successful command. Use an explicit `SqlParameter` with `Direction = InputOutput` when the stored procedure `OUTPUT` parameter also consumes an input value; scalar DTO, dictionary, and `DataRow` values are copy-back targets, not input-output opt-ins. `ReturnValue` parameters must use `SqlDbType.Int`. Prefer `QuerySingleAsync<T>()` or `ExecuteScalarAsync<T>()` for new application contracts when you control the stored procedure shape.

```csharp
var total = new SqlParameter("@Total", SqlDbType.Int)
{
    Direction = ParameterDirection.Output
};

var returnValue = new SqlParameter("@ReturnValue", SqlDbType.Int)
{
    Direction = ParameterDirection.ReturnValue
};

DbResult<int> result = await db.Default
    .Procedure("dbo.usp_RecalculateOrder")
    .With(new
    {
        OrderId = orderId,
        Total = total,
        ReturnValue = returnValue
    })
    .ExecuteAsync(ct);

if (!result.IsSuccess)
    return;

int? totalValue = total.Value is DBNull ? null : (int?)total.Value;
int statusCode = returnValue.Value is DBNull ? 0 : (int)returnValue.Value;
```

Output and return values are copied back for non-streaming execution APIs after successful command completion. For `QueryAsync<T>()`, copy-back happens when the returned async sequence is fully consumed or disposed cleanly, including clean early disposal. For raw `QueryMultipleAsync()`, copy-back happens only after `IMultipleResultReader.DisposeAsync()` completes successfully; `ReadMultipleAsync(...)` helpers dispose the reader internally, so helper success is the copy-back point. Do not read output values before the relevant completion point. If the command is canceled, enumeration fails, or reader disposal fails, treat output values as unavailable.

In strict schema binding, every `Output` or `InputOutput` parameter must have a writable DTO property, dictionary entry, `DataRow` column, or explicit `SqlParameter` source before execution. Anonymous objects, read-only DTO properties, and missing output targets are rejected in strict mode because Lib.Db cannot observe the copied-back value. In non-strict binding, targetless output-only parameters may still execute for compatibility; scalar read-only targets are ignored and dictionary bags may receive a missing non-return output key after successful copy-back. `ReturnValue` is explicit `SqlParameter` only; DTO scalar return members are rejected in strict schema binding, and scalar `DataRow` columns named like a return value are not updated.

SQL Server cursor-reference output parameters are unsupported and are detected from stored procedure metadata (`sys.parameters.is_cursor_ref`) before execution. Structured/TVP outputs and legacy `text`, `ntext`, or `image` outputs are also rejected.

`DataRow` parameter bags also support output copy-back:

- `Output` values write back to matching `DataColumn` values. When an `OUTPUT` parameter must also carry an input value, put an explicit `SqlParameter(Direction = InputOutput)` in the matching cell.
- `ReturnValue` is not written to a scalar `DataRow` column; use an explicit `SqlParameter` cell with `SqlDbType.Int` when you need the return value.
- When a `DataRow` cell contains an explicit `SqlParameter`, Lib.Db clones it for the command and copies output values back to the original only after the row update succeeds.
- Failed `DataRow` copy-back restores row values and explicit `SqlParameter.Value` values as a single rollback boundary.

Do not read output or return parameters after a failed or canceled command, failed reader enumeration, or failed reader disposal.
