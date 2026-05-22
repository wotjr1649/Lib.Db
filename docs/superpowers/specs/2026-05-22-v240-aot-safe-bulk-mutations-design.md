# Lib.Db v2.4.0 AOT-Safe Bulk Mutations Design

Date: 2026-05-22
Status: Approved for planning; implementation not started
Scope: AOT-safe bulk insert, update, delete, upsert, and merge APIs for SQL Server

## Context

Lib.Db v2.4.0 is not tagged or published to NuGet yet. The current branch can still accept one final feature if the release risk is controlled through conservative API design and heavier verification than a normal feature would require.

The existing `IDbSession.BulkInsertAsync<T>()` path is useful for high-throughput insert workloads, but it is reflection based:

- `DbSession.BulkInsertAsync<T>()` inspects `typeof(T).GetProperties()`.
- `ObjectDataReader<T>` uses `PropertyInfo` and compiled accessors.
- The public method is annotated with `RequiresUnreferencedCode`.
- Native AOT callers cannot use it as a release-grade API.

v2.4.0 already moved Lib.Db toward explicit, provider-neutral, AOT-aware infrastructure. AOT-safe bulk mutation fits that positioning if it remains a thin SQL Server/SP/TVP-aware operational helper, not a broad ORM or change tracker.

Official references checked on 2026-05-22:

- `SqlBulkCopy` supports efficient SQL Server bulk loading from `IDataReader`: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy>
- `SqlBulkCopy(SqlConnection, SqlBulkCopyOptions, SqlTransaction)` can participate in an existing transaction; `UseInternalTransaction` conflicts with an external transaction: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopy.-ctor>
- Microsoft bulk copy transaction guidance says non-transactional bulk copy cannot be rolled back and recommends an existing transaction when the bulk copy must participate in a larger operation: <https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/transaction-bulk-copy-operations>
- `SqlBulkCopyColumnMapping` maps source reader columns to destination columns when names or ordinals differ: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopycolumnmapping>
- SQL Server `MERGE` exists, but its semantics are large and should not become the default engine for a release-candidate feature: <https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql>
- SQL Server supports joined `UPDATE` through `UPDATE ... FROM`: <https://learn.microsoft.com/en-us/sql/t-sql/queries/update-transact-sql>
- SQL Server supports joined `DELETE` through the Transact-SQL `FROM` extension: <https://learn.microsoft.com/en-us/sql/t-sql/statements/delete-transact-sql>
- Native AOT requires avoiding runtime code generation and reflection paths that produce AOT or trim warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings>

## Problem

Lib.Db currently has no AOT-safe public bulk mutation surface.

The reflection-based `BulkInsertAsync<T>()` is acceptable for normal JIT deployments, but it leaves AOT consumers with only TVP/SP patterns. TVP is excellent for stored-procedure-centric workloads, but bulk insert/update/delete/upsert workloads often need:

- destination table column mappings independent of CLR property names,
- very large row streams,
- set-based mutation without manually creating a TVP type and stored procedure per shape,
- transactionally staged update/delete/upsert behavior,
- predictable AOT compatibility without `RequiresUnreferencedCode`.

Adding only AOT-safe insert would still leave a capability gap for operational synchronization jobs. Adding a full ORM-style unit-of-work/change tracker would over-expand the library. The correct middle is a small, explicit, shape-driven bulk mutation engine.

## Goals

- Add AOT-safe `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync`, `BulkUpsertAsync`, and `BulkMergeAsync` overloads.
- Keep the existing reflection-based `BulkInsertAsync<T>()` for compatibility.
- Use static caller-provided shape metadata instead of reflection, dynamic code, or source generator dependency.
- Stream rows through `DbDataReader`/`IDataReader` without materializing all records into a `List<T>`.
- Use `SqlBulkCopy` for high-throughput staging or destination load.
- Use staging-table-based set DML for update, delete, upsert, and merge.
- Default non-insert mutation operations to a single local SQL transaction.
- Validate identifiers and shape metadata before touching the database.
- Return structured row-count results for multi-action operations.
- Provide enough test coverage to make this safe for a v2.4.0 release.

## Non-Goals

- Do not build a general ORM.
- Do not add change tracking in v2.4.0.
- Do not add generator or migration implementation in v2.4.0.
- Do not use SQL Server `MERGE` as the default implementation engine.
- Do not support provider-agnostic bulk mutation beyond SQL Server in v2.4.0.
- Do not support arbitrary SQL fragments in table names, column names, join conditions, or predicates.
- Do not implement destructive "delete target rows not present in source" as a default behavior.
- Do not remove or silently change the existing reflection-based `BulkInsertAsync<T>()`.

## Design Summary

Add a new AOT-safe bulk model under `Lib.Db.Execution.Bulk`.

The public shape type is `BulkShape<T>`. It declares destination columns and key columns with static lambdas:

```csharp
BulkShape<ProductRow> shape = BulkShape.For<ProductRow>()
    .Key("ProductId", SqlDbType.Int, static row => row.ProductId)
    .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
    .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 200, nullable: false)
    .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
    .Column("UpdatedAtUtc", SqlDbType.DateTime2, static row => row.UpdatedAtUtc, scale: 7)
    .Build();
```

`BulkInsertAsync` can use every writable shape column. `BulkUpdateAsync`, `BulkDeleteAsync`, `BulkUpsertAsync`, and `BulkMergeAsync` require at least one key column.

The engine uses two paths:

1. Insert path: `SqlBulkCopy` writes a `BulkShapeDataReader<T>` directly to the destination table.
2. Mutation path: create a local temp staging table on the same connection, bulk copy rows into it, then run deterministic set-based DML inside the same transaction.

`BulkMergeAsync` is an API-level merge, not SQL Server `MERGE` by default. It composes explicit staged operations:

- update matched rows,
- insert missing rows,
- delete matched rows only when the caller selects a delete action.

This gives callers the expected merge capability while avoiding the SQL `MERGE` statement as the default release-candidate implementation.

## Public API Shape

The preferred API is additive:

```csharp
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<long>> BulkUpdateAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<long>> BulkDeleteAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<BulkUpsertResult>> BulkUpsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<BulkMergeResult>> BulkMergeAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkMergeOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;
```

The existing legacy method remains:

```csharp
[RequiresUnreferencedCode(
    "BulkInsertAsync는 Reflection을 사용하여 T의 속성을 열거합니다. AOT 환경에서는 사용할 수 없습니다.")]
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkInsertOptions? options = null,
    CancellationToken ct = default)
    where T : class;
```

## Shape Model

`BulkShape<T>` is immutable after `Build()`.

Each `BulkColumn<T>` contains:

- source ordinal,
- destination column name,
- SQL type,
- nullable flag,
- key flag,
- optional size,
- optional precision,
- optional scale,
- static getter delegate.

Shape validation:

- at least one column,
- at least one non-key writable column for update/upsert update actions,
- at least one key column for update/delete/upsert/merge,
- no duplicate destination column names under ordinal-insensitive comparison,
- no empty or invalid destination column names,
- key columns must also be included in the staging table,
- delete operations stage key columns only unless merge options require action-specific data,
- nullable `false` columns reject null values before SQL Server sees them.

## Identifier Safety

The destination table parser accepts these forms:

- `TableName`, interpreted as `[dbo].[TableName]`,
- `schema.table`,
- `[schema].[table]`.

It rejects:

- three-part names,
- linked-server names,
- semicolons,
- comments,
- whitespace-only names,
- bracket imbalance,
- empty schema or table,
- raw SQL fragments.

All emitted identifiers are bracket-quoted, and `]` is escaped as `]]`.

This is deliberately stricter than generic SQL Server identifier grammar. Bulk mutation writes data, so a narrow accepted form is the right default.

## Transaction Model

Shape-based bulk mutation should use a local transaction by default.

For insert:

- `SqlBulkCopy` can receive the local `SqlTransaction`.
- All inserted rows roll back on failure when `UseTransaction = true`.

For update/delete/upsert/merge:

- create temp table,
- bulk copy stage rows,
- execute DML,
- drop temp table,
- commit transaction.

If any step fails, roll back.

`UseInternalTransaction` is not part of the new default path because Microsoft documents that `SqlBulkCopyOptions.UseInternalTransaction` cannot be combined with an external transaction and each batch is independent. The new API should prefer one explicit transaction for release safety.

## Staging Table Model

For update/delete/upsert/merge, the engine creates a local temp table such as:

```sql
CREATE TABLE #LibDbBulk_0123456789abcdef (
    [ProductId] int NOT NULL,
    [Sku] nvarchar(64) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    [UpdatedAtUtc] datetime2(7) NOT NULL
);
```

The temp table exists only on the current connection. It is not visible cross-session, and it disappears when the connection closes. The implementation still attempts an explicit `DROP TABLE` on the success and failure paths for clean resource usage.

SQL type rendering is generated from `SqlDbType`, size, precision, scale, and nullability. Unsupported types fail during shape validation instead of producing partially valid SQL.

## DML Semantics

`BulkUpdateAsync`:

```sql
UPDATE target
SET
    [Sku] = source.[Sku],
    [Name] = source.[Name],
    [Price] = source.[Price],
    [UpdatedAtUtc] = source.[UpdatedAtUtc]
FROM [dbo].[Products] AS target
INNER JOIN #LibDbBulk_0123456789abcdef AS source
    ON target.[ProductId] = source.[ProductId];
```

`BulkDeleteAsync`:

```sql
DELETE target
FROM [dbo].[Products] AS target
INNER JOIN #LibDbBulk_0123456789abcdef AS source
    ON target.[ProductId] = source.[ProductId];
```

`BulkUpsertAsync`:

1. update matched rows,
2. insert rows where no target key exists.

The insert-missing step should use `WITH (UPDLOCK, HOLDLOCK)` on the target existence check while inside the transaction:

```sql
INSERT INTO [dbo].[Products] ([ProductId], [Sku], [Name], [Price], [UpdatedAtUtc])
SELECT source.[ProductId], source.[Sku], source.[Name], source.[Price], source.[UpdatedAtUtc]
FROM #LibDbBulk_0123456789abcdef AS source
WHERE NOT EXISTS (
    SELECT 1
    FROM [dbo].[Products] AS target WITH (UPDLOCK, HOLDLOCK)
    WHERE target.[ProductId] = source.[ProductId]
);
```

`BulkMergeAsync`:

- `BulkMergeActions.UpdateMatched` runs the update step.
- `BulkMergeActions.InsertMissing` runs the insert-missing step.
- `BulkMergeActions.DeleteMatched` runs the joined delete step.
- `BulkMergeActions.DeleteNotMatchedBySource` is not supported in v2.4.0.

Rejecting `DeleteNotMatchedBySource` in v2.4.0 is intentional. It is easy for a caller to delete too much data if the source is incomplete. That operation needs a separate bounded target predicate design and more review.

## Result Model

Single-action APIs return `DbResult<long>`.

Multi-action APIs return structured counts:

```csharp
public readonly record struct BulkUpsertResult(long Inserted, long Updated)
{
    public long TotalAffected => Inserted + Updated;
}

public readonly record struct BulkMergeResult(long Inserted, long Updated, long Deleted)
{
    public long TotalAffected => Inserted + Updated + Deleted;
}
```

The result counts come from `ExecuteNonQueryAsync` row counts or `@@ROWCOUNT` immediately after the action. Counts must not depend on user-visible SQL messages.

## Error Handling

Bulk mutation methods follow the existing `DbResult<T>` pattern:

- success returns `DbResult<T>.Ok(...)`,
- failure returns existing error result behavior without leaking connection strings or row values,
- validation errors fail before opening a connection where possible,
- SQL errors are redacted through existing diagnostic/error infrastructure,
- cancellation propagates as cancellation, not a successful zero-row operation.

Validation errors should be explicit and actionable:

- invalid destination table,
- empty shape,
- missing key columns for mutation,
- duplicate destination columns,
- unsupported SQL type rendering,
- non-nullable column produced a null value,
- invalid option values.

## Performance Expectations

The new AOT-safe path should improve or preserve the important performance characteristics:

- no reflection per row,
- no expression compilation at runtime,
- no list materialization for large enumerables,
- streaming `DbDataReader` into `SqlBulkCopy`,
- one round-trip group per staged operation rather than row-by-row DML,
- explicit column mappings to avoid ordinal-name assumptions.

The release claim should be conservative: this feature is expected to be at least safer for AOT and memory behavior than the legacy reflection path. Any numeric performance claim must come only after benchmark output is captured.

## Documentation Impact

Update docs after implementation:

- `docs/02_advanced.md`: add AOT-safe bulk mutation section and legacy reflection warning.
- `docs/03_api_reference.md`: add shape, options, result types, and new methods.
- `docs/05_fluent_api_reference.md`: add `IDbSession` bulk mutation entries.
- `docs/06_cookbook.md`: add insert/update/delete/upsert examples.
- `docs/history.md`: add v2.4.0 AOT-safe bulk mutation entry.
- `.agents/skills/lib-db/SKILL.md` if public consumer guidance needs the new API.

## Verification Strategy

The implementation must be tested more heavily than a normal late-cycle feature.

Required unit tests:

- shape requires at least one column,
- duplicate destination column rejected,
- mutation requires key column,
- update requires at least one non-key column,
- invalid identifiers rejected,
- bracket escaping works,
- SQL type rendering covers supported types,
- nullable false column rejects null,
- reader streams rows in order,
- reader handles `DateOnly`, `TimeOnly`, `Guid`, `decimal`, `byte[]`, enums, and nullable values,
- options reject invalid batch size and timeout.

Required integration tests:

- AOT-safe insert writes rows and returns count,
- insert supports destination column names that differ from CLR member names,
- insert rolls back on failure when transaction is enabled,
- update changes only matching rows,
- delete removes only matching keys,
- upsert updates matched and inserts missing rows,
- merge update+insert path returns separated counts,
- merge delete-matched path deletes only staged keys,
- invalid destination table returns a failed `DbResult`,
- cancellation token is passed to async DB calls.

Required AOT checks:

- AOT verification project references the shape API.
- AOT smoke creates a shape and reads values through the new reader.
- Publish output has no new Lib.Db AOT or trim warnings beyond the existing baseline.

Required release gates:

- targeted unit tests,
- targeted integration tests with local SQL Server verification DB,
- `pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1`,
- `pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short`,
- final code review and security review before tag/publish.

## Release Risk Assessment

This feature is acceptable for v2.4.0 only if these constraints hold:

- implementation stays additive,
- SQL Server `MERGE` statement is not the default engine,
- delete-not-matched-by-source is rejected for v2.4.0,
- no public API depends on reflection for the AOT-safe overloads,
- all write operations have key/identifier validation,
- all staged mutation operations run in one local transaction by default,
- release verification passes from a clean environment.

If any of those constraints breaks during implementation, the feature should be reduced to AOT-safe insert/update/upsert or postponed to v2.5.0.
