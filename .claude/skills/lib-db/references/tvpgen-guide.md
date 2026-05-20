# Lib.Db.TvpGen Guide

Use this file when application code uses Lib.Db source generation for TVP rows or generated result mappers.

## Consumer Responsibilities

Application code owns:

- CLR row types used for TVP input
- DTO types used for result mapping
- alignment with SQL Server user-defined table types and stored procedure contracts
- nullability choices
- keeping generated code warnings visible during application builds

Lib.Db.TvpGen owns compile-time generation for supported patterns.

## TVP Rules

Use `[TvpRow]` for CLR types that represent SQL Server table-valued parameter rows.

Consumer guidance:

- Keep property names and order aligned with the SQL Server table type expected by stored procedures.
- Use supported CLR types only.
- Keep nullable CLR properties aligned with SQL Server nullability.
- Prefer immutable or init-only DTO-like row types when practical.

Example shape:

```csharp
[TvpRow("dbo.OrderLineTvp")]
public sealed class OrderLineRow
{
    public int LineNo { get; init; }
    public string ItemCode { get; init; } = "";
    public decimal Quantity { get; init; }
}
```

Stored procedure call shape:

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

## DbResult Rules

Use `[DbResult]` for DTOs that should have generated result mapping.

Consumer guidance:

- Generated result mappers should operate through `DbDataReader`.
- Concrete SQL reader types are compatibility details, not the primary consumer boundary.
- Keep SQL aliases aligned with DTO property names.
- Avoid ambiguous normalized names.

Example shape:

```csharp
[DbResult]
public sealed class OrderSummaryDto
{
    public string OrderNo { get; init; } = "";
    public DateOnly OrderDate { get; init; }
    public decimal TotalAmount { get; init; }
}
```

## Troubleshooting

If generated code fails or mapping is unexpected:

- check unsupported CLR property types
- check SQL Server table type and CLR row shape mismatch
- check nullable property mismatch
- check ambiguous result column names
- check whether SQL aliases should be added
- check whether the application build is suppressing generator diagnostics

## Consumer Checklist

- Are TVP row types aligned with SQL Server table types?
- Are `[DbResult]` DTOs aligned with result column names?
- Are unsupported types avoided?
- Are nullable values intentional?
- Are generated mapper diagnostics visible in the consuming application build?
