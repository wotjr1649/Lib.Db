# Runtime TVP And TvpGen Migration

Use this file for table-valued parameters, `LibDb.Tvp(...)`, static TVP shapes, option-level TVP mappings, and legacy compatibility markers.

## Namespaces

```csharp
using System.Data;
using Lib.Db;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Tvp;
```

## Default Runtime TVP Wrapper

Use `LibDb.Tvp(...)` inside the normal `.With(...)` parameter object. Scalar parameters and TVP parameters can be passed together.

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

Use this form for migration, low-frequency calls, and simple application paths.

## Preferred Explicit TVP Wrapper

`TvpShape.For<T>()` returns a `TvpShapeBuilder<T>`; call `TvpShapeBuilder<T>.Column(...)` for each SQL column and finish with `.Build()`.

```csharp
TvpShape<OrderLineRow> shape = TvpShape.For<OrderLineRow>()
    .Column("OrderId", SqlDbType.Int, static row => row.OrderId)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
    .Column("Quantity", SqlDbType.Int, static row => row.Quantity)
    .Build();

DbResult<int> result = await db.Default
    .Procedure("dbo.usp_ImportOrderLines")
    .With(new
    {
        Lines = LibDb.Tvp("dbo.OrderLineTvp", rows, shape)
    })
    .ExecuteAsync(ct);
```

Use static lambdas in AOT-sensitive paths.

## TVP Wrapper APIs

| API | Use |
| --- | --- |
| `LibDb.Tvp(string typeName, IEnumerable<T> rows, TvpBindingPolicy policy = Strict)` | Reflection-based row discovery. Avoid in Native AOT. |
| `LibDb.Tvp(string typeName, IEnumerable<T> rows, TvpShape<T> shape, TvpBindingPolicy policy = Strict)` | Preferred AOT-friendly explicit shape. |
| `LibDb.Tvp(TvpSchemaDescriptor descriptor, IEnumerable<T> rows, TvpBindingPolicy policy = Adaptive)` | Descriptor-driven runtime binding. Reflection-based. |

`TvpBindingPolicy.Strict` fails on drift. `Adaptive` allows nullable/default-safe adjustments.

## Option-Level TVP Mapping

For repeated row types, register the mapping during Lib.Db setup:

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");

    options.Tvp.Map<OrderLineRow>("dbo.OrderLineTvp")
        .Column("OrderId", SqlDbType.Int, static row => row.OrderId)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Quantity", SqlDbType.Int, static row => row.Quantity);
});
```

`options.Tvp.EnableAutoTvpBinding` controls automatic binding for registered row sequences.

## Runtime Descriptor

```csharp
TvpSchemaDescriptor descriptor = await db.Schema.GetTvpAsync("dbo.OrderLineTvp", ct);
```

Descriptor members include `TypeName`, `VersionToken`, `Columns`, and `Fingerprint`.

## Migrating From `Lib.Db.TvpGen`

`Lib.Db.TvpGen` is not required for current TVP usage. For application code:

1. Remove the `Lib.Db.TvpGen` package/analyzer reference.
2. Replace generated accessor calls with `LibDb.Tvp("schema.TypeName", rows)` for the default runtime path.
3. For hot paths or Native AOT, register `options.Tvp.Map<T>()` or pass a `TvpShape<T>` built with static lambdas.
4. Keep SQL Server permissions explicit: callers need `EXECUTE` on the procedure and `REFERENCES` on the TVP type, schema, or database.

Generated-accessor code may remain only as a historical compatibility reference. Do not add new source-generator setup to consumer applications.

## Legacy Compatibility Markers

`[TvpRow]`, `[TvpLength]`, `[TvpPrecision]`, and `[GenerateTvpFromDb]` exist for compatibility with older generated or attributed TVP models. New application code should prefer `LibDb.Tvp(...)`, `TvpShape.For<T>()`, or `options.Tvp.Map<T>()`.

```csharp
[TvpRow(TypeName = "dbo.OrderLineTvp")]
public sealed record OrderLineRow(
    int OrderId,
    [property: TvpLength(64)] string Sku,
    [property: TvpPrecision(18, 2)] decimal UnitPrice,
    int Quantity);
```

Treat attributed reflection fallback as convenience, not the default for AOT-sensitive or high-throughput paths.

## Column Rules

- TVP type names should include schema.
- Column names must match the SQL Server TVP type.
- Column order should match the database type when using generated or static shapes.
- Use `allowNull: true` for nullable columns.
- Use `size`, `precision`, and `scale` for string, binary, decimal, and time precision.
