# Bulk Insert

Use this file for `IDbSession.BulkInsertAsync<T>` and `BulkInsertOptions`.

## API Shape

```csharp
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkInsertOptions? options = null,
    CancellationToken ct = default)
    where T : class;
```

Example:

```csharp
DbResult<long> result = await db.BulkInsertAsync(
    "Default",
    "[dbo].[OrderImport]",
    records,
    new BulkInsertOptions
    {
        BatchSize = 10_000,
        TimeoutSeconds = 600,
        EnableStreaming = true,
        CheckConstraints = true
    },
    ct);
```

## `BulkInsertOptions`

- `BatchSize`: default batch rows.
- `TimeoutSeconds`: bulk command timeout.
- `EnableStreaming`: stream records to `SqlBulkCopy`.
- `FireTriggers`: run insert triggers.
- `CheckConstraints`: enforce constraints.
- `KeepIdentity`: preserve identity values.

## Mapping

Bulk insert uses public instance properties of `T` for column mapping. Keep CLR property names aligned with destination table columns or use an intermediate row type with explicit property names.

## Safety

- Validate destination table names; do not pass user-controlled table identifiers.
- Validate tenant or authorization boundaries before bulk inserting.
- Use least-privilege SQL permissions for the destination table.
- Consider transaction scope when the bulk load must be atomic with other commands.
- Bulk insert is reflection-based and not Native AOT friendly. Read `aot-trimming.md`.

## Empty Input

Materialize or validate input when the source can only be enumerated once. Decide whether an empty set should be a no-op or an application error before calling bulk insert.
