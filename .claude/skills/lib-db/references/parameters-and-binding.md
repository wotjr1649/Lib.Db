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

For output parameters, keep a reference to the `SqlParameter` you supplied and read it only after a successful command. Prefer `QuerySingleAsync<T>()` or `ExecuteScalarAsync<T>()` for new application contracts when you control the stored procedure shape.

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

Do not read output or return parameters after a failed command unless the stored procedure contract explicitly guarantees them.
