# Mapping Contracts

Use this file for DTO result mapping, generated result mapper contracts, JSON column helpers, and naming rules.

## DTO Mapping Rules

Lib.Db maps result columns to public DTO properties.

1. Exact case-insensitive match first.
2. Underscore-insensitive normalized match second, such as `CELL_NO` to `CellNo`.
3. Ambiguous normalized matches must not silently bind.
4. Prefer SQL aliases when names are unclear.

```sql
SELECT
    u.USER_ID AS UserId,
    u.CELL_NO AS CellNo
FROM dbo.Users AS u
WHERE u.USER_ID = @UserId;
```

```csharp
public sealed record UserDto(int UserId, string? CellNo);
```

## Nullable Properties

Match database nullability in DTOs:

```csharp
public sealed record ProductDto(
    int ProductId,
    string Sku,
    string? Description,
    decimal? DiscountRate);
```

## Generated Result Mapper Marker

Use `[DbResult]` on result types that should use generated or static mapping paths when supported by the package:

The attribute type is `DbResultAttribute`; application code normally uses the short `[DbResult]` form.

```csharp
[DbResult]
public partial sealed class UserDto
{
    public int UserId { get; set; }
    public string? Name { get; set; }
}
```

Generated mapper contracts should be compatible with `DbDataReader` wrappers. Do not assume only a concrete SQL reader reaches mapping code.

## Manual Static Mapper Contract

`IMapableResult<T>` exposes:

```csharp
static abstract T Map(SqlDataReader reader);
```

The package may also use `DbDataReader`-compatible wrappers internally. Keep mapper logic reader-contract focused.

## JSON Helpers

Namespace:

```csharp
using Lib.Db.Extensions;
```

Dictionary result JSON column:

```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await db.Default
    .Procedure("dbo.usp_GetRowsWithJson")
    .With(new { UserId = userId })
    .QueryAsync<Dictionary<string, object?>>(ct);

if (result.IsSuccess)
{
    await foreach (Dictionary<string, object?> row in result.Value!.WithCancellation(ct))
    {
        ProfileDto? profile = row.MapJsonColumn<ProfileDto>("ProfileJson");
    }
}
```

String helpers:

```csharp
ProfileDto? profile = row.ProfileJson.FromJson<ProfileDto>();
string json = profile.ToJson();
```

For Native AOT-sensitive JSON mapping, prefer source-generated `JsonTypeInfo` patterns in application code where available. Read `aot-trimming.md`.
