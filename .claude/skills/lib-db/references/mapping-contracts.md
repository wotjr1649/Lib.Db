# Mapping and Binding Contracts

Use this file when application code maps SQL Server result sets to DTOs, binds parameters, or uses generated result mappers.

## Result Column Name Resolution

Lib.Db result mapping should be treated as a public consumer contract:

1. Exact case-insensitive column/property names are preferred.
2. If no exact match exists, underscore-insensitive normalized names may match, such as `CELL_NO` to `CellNo`.
3. Normalized-name collisions must not silently bind ambiguous properties.

Prefer SQL aliases when database names are unclear:

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

## Generated Result Mapper Contract

Generated `[DbResult]` mappers should operate through `DbDataReader`.

Consumer guidance:

- Do not cast diagnostic or wrapped readers to concrete SQL reader types.
- Treat `DbDataReader` as the compatibility boundary.
- Keep generated and reflection-based mapping behavior aligned from the consumer perspective.

## DateOnly and TimeOnly Binding

Raw `DateOnly` values bind as SQL `date`.

Raw `TimeOnly` values bind as SQL `time`.

Example:

```csharp
DbResult<IAsyncEnumerable<ScheduleDto>> result = await db.Default
    .Procedure("dbo.usp_SearchSchedule")
    .With(new
    {
        WorkDate = DateOnly.FromDateTime(dateTime),
        StartAt = TimeOnly.FromDateTime(dateTime)
    })
    .QueryAsync<ScheduleDto>(ct);
```

Use explicit SQL aliases and matching DTO property types when provider behavior matters.

## DTO Design Guidance

- Prefer DTOs with clear property names.
- Use nullable reference types to express database nullability.
- Avoid ambiguous names that differ only by underscores or casing.
- Prefer SQL aliases when mapping legacy database columns.
- Keep DTO constructors and init-only properties compatible with the mapper behavior used by the application.

## Consumer Checklist

- Do SQL result columns clearly map to DTO properties?
- Are ambiguous normalized names avoided?
- Are nullability expectations explicit?
- Are `DateOnly` and `TimeOnly` parameters intentional?
- Do generated mappers use `DbDataReader` as the compatibility boundary?
