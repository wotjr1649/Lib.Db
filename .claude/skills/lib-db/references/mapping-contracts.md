# Mapping and Binding Contracts

Use this file when work touches result mapping, parameter binding, `DbDataReader`, source-generated `[DbResult]` contracts, `DateOnly`/`TimeOnly`, or verification DB blocker coverage.

## Result Column Name Resolution

Default runtime result mapping uses this order:

1. exact case-insensitive property name match
2. normalized match that removes underscores and compares case-insensitively

Example:

```csharp
public sealed record SuspendRow(int CellNo, string SlotName);
```

SQL result columns such as `CELL_NO` and `SLOT_NAME` should map to `CellNo` and `SlotName`.

Collision rule: if two properties normalize to the same key, do not silently pick an arbitrary property. Preserve deterministic first-match behavior only where the runtime explicitly guards the ambiguity.

## Generated Result Mapper Contract

`[DbResult]` generated code must expose:

```csharp
public static MyRow Map(DbDataReader reader);
public static MyRow Map(SqlDataReader reader);
```

`Map(DbDataReader)` is the primary contract. `Map(SqlDataReader)` is a compatibility shim.

Runtime generated result mapping must work with any `DbDataReader`, including diagnostic wrappers such as `MonitoredSqlDataReader`.

If old generated code only has `Map(SqlDataReader)`, regenerate with the current `Lib.Db.TvpGen` package.

## DateOnly and TimeOnly Binding

Raw SQL and stored procedure parameter binding should treat:

- `DateOnly` as SQL `date`
- `TimeOnly` as SQL `time`

Runtime conversion expectations:

- `DateOnly` binds through a `DateTime` value at midnight with `SqlDbType.Date`
- `TimeOnly` binds through a `TimeSpan` value with `SqlDbType.Time`
- neither type should be treated as a complex object parameter container

## DTO Design Guidance

- Keep DTO property names aligned with database result semantics.
- Prefer explicit DTOs over dynamic dictionaries for public APIs.
- Positional records are supported, but constructor parameter names must be meaningful.
- For ambiguous legacy result sets, consider SQL aliases rather than relying on normalization.

## Verification Targets

When changing this area, include focused coverage for:

- `CELL_NO` to `CellNo` mapping
- duplicate normalized column/property collision behavior
- generated `[DbResult]` with a `DbDataReader` wrapper
- raw `DateOnly` and `TimeOnly` parameters
- real SQL Server verification when provider behavior matters
