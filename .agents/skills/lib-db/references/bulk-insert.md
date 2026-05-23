# Bulk Insert and Mutations

Use this file for `IDbSession.BulkInsertAsync<T>`, AOT-safe `BulkShape<T>` overloads, and bulk mutation options/results.

## Legacy API Shape

```csharp
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkInsertOptions? options = null,
    CancellationToken ct = default)
    where T : class;
```

This overload is compatibility-focused and uses reflection over public instance properties.

## AOT-Safe API Shape

```csharp
BulkShape<T> shape = BulkShape.For<T>()
    .Key("Id", SqlDbType.Int, static row => row.Id)
    .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 200)
    .Build();

Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<long>> BulkUpdateAsync<T>(..., BulkShape<T> shape, BulkWriteOptions? options = null, CancellationToken ct = default);
Task<DbResult<long>> BulkDeleteAsync<T>(..., BulkShape<T> shape, BulkWriteOptions? options = null, CancellationToken ct = default);
Task<DbResult<BulkUpsertResult>> BulkUpsertAsync<T>(..., BulkShape<T> shape, BulkWriteOptions? options = null, CancellationToken ct = default);
Task<DbResult<BulkMergeResult>> BulkMergeAsync<T>(..., BulkShape<T> shape, BulkMergeOptions? options = null, CancellationToken ct = default);
```

Example:

```csharp
DbResult<long> result = await db.BulkInsertAsync(
    "Default",
    "[dbo].[OrderImport]",
    records,
    shape,
    new BulkWriteOptions
    {
        BatchSize = 10_000,
        TimeoutSeconds = 600,
        EnableStreaming = true,
        CheckConstraints = true
    },
    ct);
```

## Options

- `BatchSize`: default batch rows.
- `TimeoutSeconds`: bulk command timeout.
- `EnableStreaming`: stream records to `SqlBulkCopy`.
- `FireTriggers`: run insert triggers.
- `CheckConstraints`: enforce constraints. AOT-safe `BulkWriteOptions` defaults this to `true`.
- `KeepIdentity`: preserve identity values.
- `UseTransaction`: AOT-safe bulk transaction control. Keep the default `true` unless intentionally accepting non-atomic direct insert.
- `BulkMergeOptions.Actions`: defaults to `UpdateMatched | InsertMissing`; unknown bits and `DeleteNotMatchedBySource` are rejected in v2.4.0. `DeleteMatched` is exclusive and must be used by itself.

## Mapping

Legacy bulk insert uses public instance properties of `T` for column mapping. AOT-safe bulk uses `BulkShape<T>` metadata and static getters.

Shape metadata must include explicit SQL type information. Decimal columns require precision/scale. String/binary key columns need fixed size; non-key string/binary columns may use explicit size or `max`. Temporal scale, CLR value type to `SqlDbType` compatibility, enum underlying type alignment, key column count, key width, and unsupported SQL types are validated before opening a connection.

## Safety

- Validate destination table names; do not pass user-controlled table identifiers.
- Validate tenant or authorization boundaries before bulk inserting.
- Use least-privilege SQL permissions for the destination table.
- Consider transaction scope when the bulk load must be atomic with other commands.
- Legacy bulk insert is reflection-based and not Native AOT friendly. Read `aot-trimming.md`.
- Prefer `BulkShape<T>` overloads in Native AOT/trimming-sensitive applications.
- Update/delete/upsert/merge use staged DML and do not use SQL Server `MERGE` as the default engine.
- Staged mutation keys must be non-null and backed by application-owned `PRIMARY KEY` or `UNIQUE` constraints on the target table.
- Staged update/delete/upsert/merge reject `UseTransaction = false`, `FireTriggers = true`, `KeepIdentity = true`, and `CheckConstraints = false` before opening a connection.
- Direct AOT-safe insert with `UseTransaction = false` is a non-atomic opt-out; partial rows can remain after failure or cancellation.
- Public bulk failures must be handled as generic/redacted `DbResult<T>` errors. Do not log raw SQL, row values, payload values, connection string values, provider exception details, or public `InnerException`.

## Empty Input

Materialize or validate input when the source can only be enumerated once. Decide whether an empty set should be a no-op or an application error before calling bulk insert.
