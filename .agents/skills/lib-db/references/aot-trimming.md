# AOT And Trimming

Use this file when the application targets Native AOT, trimming, single-file deployment, or strict linker warnings.

## Prefer AOT-Friendly APIs

Use explicit code registration:

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
    options.UseProductionSecurityDefaults();
});
```

Avoid configuration binder convenience overloads in strict AOT applications:

```csharp
builder.Services.AddLibDb(builder.Configuration);
```

This overload is `ConfigurationBinder`-based and can require runtime code generation.

Prefer `AddLibDbOptionsFromConfiguration(...)` or explicit assignment when the application needs configuration input without reflection-heavy binding.

```csharp
builder.Services
    .AddLibDbOptionsFromConfiguration(builder.Configuration, "LibDb")
    .WithPostConfigure(options =>
    {
        options.UseProductionSecurityDefaults();
        options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;
    });

builder.Services.RegisterLibDbCoreServices();
builder.Services.AddLibDbHostedServices();
```

Verify strict AOT paths with a real publish-and-run step, not just `dotnet build`.
On Windows, Native AOT requires the Visual Studio C++ toolchain.

## TVP

Prefer static shapes:

```csharp
using System.Data;

TvpShape<OrderLineRow> shape = TvpShape.For<OrderLineRow>()
    .Column("OrderId", SqlDbType.Int, static row => row.OrderId)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
    .Build();
```

Avoid reflection-based TVP wrappers in Native AOT:

```csharp
LibDb.Tvp("dbo.OrderLineTvp", rows);
```

Use:

```csharp
LibDb.Tvp("dbo.OrderLineTvp", rows, shape);
```

## JSON

Convenience JSON helpers use `JsonSerializerOptions` and may require runtime metadata. For strict AOT apps, use source-generated `JsonSerializerContext` or application-owned serializers.

## AOT-Safe Bulk Mutations

Prefer `BulkShape<T>` overloads for bulk insert, update, delete, upsert, and merge-like mutations:

```csharp
BulkShape<OrderImportRow> shape = BulkShape.For<OrderImportRow>()
    .Key("OrderId", SqlDbType.Int, static row => row.OrderId)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
    .Column("Quantity", SqlDbType.Int, static row => row.Quantity)
    .Build();

DbResult<long> result = await db.BulkInsertAsync(
    "Default",
    "[dbo].[OrderImport]",
    rows,
    shape,
    new BulkWriteOptions { BatchSize = 10_000 },
    ct);
```

The legacy `BulkInsertAsync<T>(..., BulkInsertOptions?)` overload uses reflection over public properties. Avoid that legacy overload in Native AOT.

## Host Hook And Legacy TVP

`host.UseHighPerformanceDb()` and legacy generated TVP bridging use reflection. Avoid them in strict AOT paths unless the application explicitly accepts the annotations and roots required members.

## Generated Result Mappers

Use `[DbResult]` partial DTOs where the installed package generator supports them. Keep generated mappers compatible with `DbDataReader` wrappers.
