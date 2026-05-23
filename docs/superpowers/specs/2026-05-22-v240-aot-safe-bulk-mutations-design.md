# Lib.Db v2.4.0 AOT-Safe Bulk Mutations Design

Date: 2026-05-22
Status: Approved for planning; implementation not started
Scope: AOT-safe bulk insert, update, delete, upsert, and merge APIs for SQL Server

## Parent Integration

This is the authoritative sub-spec for AOT-safe bulk mutations. The v2.4.0 integrated scope and release orchestration remain in:

- `docs/superpowers/specs/2026-05-22-v240-integrated-additional-scope-design.md`
- `docs/superpowers/plans/2026-05-22-v240-integrated-additional-scope-implementation.md`

If this sub-spec and the integrated documents appear to overlap, use this document for bulk internals and the integrated documents for cross-feature release gates, public docs, and security review sequencing.

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
- `SqlBulkCopyOptions.CheckConstraints` explicitly controls whether destination constraints are checked during bulk copy: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopyoptions>
- Microsoft bulk copy transaction guidance says non-transactional bulk copy cannot be rolled back and recommends an existing transaction when the bulk copy must participate in a larger operation: <https://learn.microsoft.com/en-us/sql/connect/ado-net/sql/transaction-bulk-copy-operations>
- `SqlBulkCopyColumnMapping` maps source reader columns to destination columns when names or ordinals differ: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbulkcopycolumnmapping>
- SQL Server `MERGE` exists, but its semantics are large and should not become the default engine for a release-candidate feature: <https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql>
- SQL Server supports joined `UPDATE` through `UPDATE ... FROM`: <https://learn.microsoft.com/en-us/sql/t-sql/queries/update-transact-sql>
- SQL Server supports joined `DELETE` through the Transact-SQL `FROM` extension: <https://learn.microsoft.com/en-us/sql/t-sql/statements/delete-transact-sql>
- SQL Server unique indexes enforce duplicate-key rejection and can be created on temporary tables; this is the v2.4.0 duplicate-source-key guard: <https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-unique-indexes> and <https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql>
- SQL Server `QUOTENAME` documents bracket-delimited identifier escaping behavior and the `sysname` 128-character input limit; Lib.Db mirrors the escaping behavior for already validated identifiers and must reject malformed multipart names instead of normalizing them: <https://learn.microsoft.com/en-us/sql/t-sql/functions/quotename-transact-sql>
- SQL Server `MERGE` documentation states that the same matched row cannot be updated and deleted in one statement; Lib.Db's API-level merge must avoid equivalent contradictory action combinations even though it uses staged DML rather than SQL Server `MERGE` by default: <https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql>
- Microsoft.Data.SqlClient 5.1+ supports `DateOnly` and `TimeOnly` for parameter values and `GetFieldValue`; Lib.Db still normalizes bulk reader values to the SQL-facing provider types used by its existing TVP path: <https://learn.microsoft.com/en-us/sql/connect/ado-net/introduction-microsoft-data-sqlclient-namespace>
- Native AOT requires avoiding runtime code generation and reflection paths that produce AOT or trim warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings>
- Native AOT warning IL3050 is produced for APIs annotated with `RequiresDynamicCodeAttribute`; the new bulk path must avoid runtime dynamic-code APIs such as `MakeGenericType`, `Expression.Compile`, `DynamicMethod`, and `Reflection.Emit`: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050>
- `DbTransaction.CommitAsync` and `RollbackAsync` both accept cancellation tokens and can surface cancellation or provider exceptions through the returned task; Lib.Db's bulk transaction policy must preserve the primary failure and avoid caller-cancellation ambiguity at final commit: <https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbtransaction.commitasync> and <https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbtransaction.rollbackasync>

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
- static getter delegate,
- shape-build-time value converter selected from the static `TValue` and `SqlDbType`.

Value conversion is shape metadata, not row-time type discovery. `DateOnly`, `TimeOnly`, and enum conversion rules must be fixed when the shape is built so the reader does not call `value.GetType()` or `Enum.GetUnderlyingType(...)` for every row.
Shape construction also validates that the static CLR `TValue` is compatible
with the declared `SqlDbType`. Examples: `DateOnly` maps to `SqlDbType.Date`
only, `TimeOnly`/`TimeSpan` map to `SqlDbType.Time`, `Guid` maps to
`SqlDbType.UniqueIdentifier`, strings map only to `NVarChar`/`VarChar`,
`byte[]` maps only to `VarBinary`, and enums must map to the SQL integer type
matching their underlying type. Incompatible CLR/SQL type pairs fail before
`SqlBulkCopy` sees a reader row.

`SqlDbType.Decimal` is the exception to the "optional" precision/scale wording:
decimal bulk columns must declare both precision and scale explicitly, precision
must be between 1 and 38, and scale must not exceed precision. The stage SQL
renderer must never silently fall back to `decimal(18,0)`, because that can round
or truncate data before the application sees a failure.

Length and temporal metadata are also validated before any database connection
opens. `nvarchar` accepts `null` size for `max` or an explicit size from 1 to
4,000. `varchar` and `varbinary` accept `null` size for `max` or an explicit
size from 1 to 8,000. `time`, `datetime2`, and `datetimeoffset` accept `null`
scale for the SQL Server default or an explicit scale from 0 to 7. Invalid
non-null size/scale metadata fails during shape construction rather than after
stage DDL generation or `SqlBulkCopy` startup.

Shape validation:

- at least one column,
- at least one non-key writable column for update/upsert update actions,
- at least one key column for update/delete/upsert/merge,
- no duplicate destination column names under ordinal-insensitive comparison,
- no empty or invalid destination column names,
- key columns must also be included in the staging table,
- mutation key columns are treated as non-null operational keys; null key values are rejected before DML,
- mutation key columns must be indexable by SQL Server's staging unique index: no `nvarchar(max)`, `varchar(max)`, or `varbinary(max)` key columns, no more than 32 key columns, and conservative declared key width no greater than 900 bytes. The 900-byte portability limit is intentional for v2.4.0 so Lib.Db does not depend on SQL Server version-specific expanded nonclustered index key limits before it has opened a connection.
- delete operations stage key columns only unless merge options require action-specific data,
- nullable `false` columns reject null values before SQL Server sees them.

Key uniqueness contract:

- Source rows for update/delete/upsert/merge must contain no duplicate key tuples. The implementation enforces this by creating a unique index on the local staging table key columns after `SqlBulkCopy` loads the stage and before any target DML executes.
- Lib.Db validates the stage-key shape before staged operations so unsupported key metadata fails predictably instead of surfacing as a late SQL Server index-creation error. This deliberately rejects some wide string/binary key shapes that newer SQL Server versions might otherwise index; callers should use database-backed narrow keys or app-owned hashed/surrogate keys for bulk mutation joins.
- Target key columns must be backed by a database-enforced `PRIMARY KEY` or `UNIQUE` constraint/index owned by the application schema. v2.4.0 documents this as a caller/database contract and does not run default metadata probes against `sys.indexes` before each bulk mutation.
- The staging unique index must not use `IGNORE_DUP_KEY`; duplicate source keys fail the operation and roll back the whole transaction.

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
- leading, trailing, or repeated separators that would create empty name parts,
- identifier parts longer than 128 characters,
- embedded bracket syntax in public table-name input except for the simple `[schema].[table]` wrapper form,
- raw SQL fragments.

All emitted identifiers are bracket-quoted, and `]` is escaped as `]]`.

This is deliberately stricter than generic SQL Server identifier grammar. Bulk mutation writes data, so a narrow accepted form is the right default.

Destination column names in `BulkShape<T>` follow the same defensive posture: they are destination identifiers, not SQL fragments. They must be non-empty, at most 128 characters, and free of whitespace, comments, semicolons, and bracket syntax. The renderer quotes them after validation.

## Transaction Model

Shape-based bulk mutation should use a local transaction by default.

For insert:

- `SqlBulkCopy` can receive the local `SqlTransaction`.
- Insert failures roll back when `UseTransaction = true` and the failure occurs before final commit begins. Commit-outcome ambiguity after commit begins remains a provider/database reality and must not be reported as a guaranteed rollback.
- `UseTransaction = false` is an explicit non-atomic performance opt-out for insert only. If the provider fails or cancellation arrives after rows have been sent, some rows can remain in the target table; Lib.Db must not document or test this mode as rollback-capable.
- `CheckConstraints` defaults to `true` for the new AOT-safe options. Callers may opt out explicitly for controlled performance scenarios, but the safe release default is to keep destination constraints checked. Release tests must prove this with a real SQL Server `CHECK` constraint failure, not only with an options object assertion.

For update/delete/upsert/merge:

- reject `UseTransaction = false` before opening a connection in v2.4.0,
- create temp table,
- bulk copy stage rows,
- execute DML,
- drop temp table,
- commit transaction.

For transaction-enabled operations, if any step fails before final commit begins,
including cancellation, explicitly attempt rollback before returning or
rethrowing. Do not rely on connection/transaction disposal as the only rollback
mechanism in the documented implementation path. Rollback failure is a secondary
diagnostic event: it must not replace the original SQL/general/cancellation
failure in public `DbResult<T>` errors.

The final commit boundary uses `CancellationToken.None` after all cancellable staging and DML work has completed. This avoids reporting caller cancellation after the database may already be committing. The public contract is:

- cancellation before commit begins attempts rollback and rethrows `OperationCanceledException`;
- after commit begins, caller cancellation is no longer observed by the commit call;
- provider failures at commit are mapped through the existing redacted error path, with best-effort rollback only if the provider still considers the transaction pending;
- commit-outcome ambiguity caused by connection loss remains a provider/database reality and must be documented as such rather than hidden behind a false cancellation result.

`UseInternalTransaction` is not part of the new default path because Microsoft documents that `SqlBulkCopyOptions.UseInternalTransaction` cannot be combined with an external transaction and each batch is independent. The new API should prefer one explicit transaction for release safety.

This policy intentionally separates the fast insert opt-out from staged mutation
safety. Update/delete/upsert/merge are multi-step operations that load a staging
table and then mutate target rows. Allowing them to run without one transaction
would create partial-write states that are hard to reason about and easy to
misdocument, so v2.4.0 rejects that mode instead of downgrading guarantees.

`BulkWriteOptions` contains several `SqlBulkCopyOptions`-style flags. Their
meaning depends on the operation phase:

- For direct `BulkInsertAsync`, `FireTriggers`, `CheckConstraints`, and
  `KeepIdentity` apply to the user destination table because `SqlBulkCopy` writes
  directly into that table.
- For staged update/delete/upsert/merge, `SqlBulkCopy` writes only into the
  generated local temp table. The user target table is changed by ordinary
  SQL Server DML, so target constraints and triggers follow normal SQL Server
  DML semantics, and Lib.Db does not enable target `IDENTITY_INSERT`.
- To prevent callers from misunderstanding those flags as target-DML controls,
  staged update/delete/upsert/merge reject `FireTriggers = true`,
  `KeepIdentity = true`, and `CheckConstraints = false` before opening a
  connection.

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

SQL type rendering is generated from `SqlDbType`, size, precision, scale, and nullability. Unsupported types fail during shape construction or shape validation before any connection is opened, instead of producing partially valid SQL or failing after `SqlBulkCopy` starts. Decimal rendering must use the explicit shape metadata exactly, for example `decimal(19,4)`, after shape validation has guaranteed precision and scale are present.

For mutation operations, the engine creates a unique index on the stage key columns after stage loading:

```sql
CREATE UNIQUE INDEX [IX_LibDbBulk_Key]
ON #LibDbBulk_0123456789abcdef ([ProductId]);
```

This converts duplicate source keys into a deterministic validation failure before target data is changed. For delete operations, the stage table and reader use a key-only projected column set so the temp-table shape, `SqlBulkCopy` mappings, and joined delete SQL stay aligned.

Bulk reader value normalization follows the existing TVP path:

- `SqlDbType.Date` values from `DateOnly` are exposed as `DateTime` at midnight.
- `SqlDbType.Time` values from `TimeOnly` are exposed as `TimeSpan`.
- enums are exposed as their underlying numeric values.
- other supported values are passed through unchanged.

The normalization is performed by the shape metadata converter before the reader returns values. The reader also owns the row enumerator lifetime. It must expose `IsClosed` from an internal closed flag, make `Close()` and `Dispose(bool)` idempotent, dispose the underlying enumerator exactly once, clear the current row state when `Read()` reaches EOF, throw `IndexOutOfRangeException` for missing column names in `GetOrdinal`, and implement `HasRows` as "this result set contains at least one row" without consuming or skipping the first row.

`BulkShapeDataReader<T>` is an internal implementation type, not a public API. Tests and the AOT verification project can reach it through the repo's existing `InternalsVisibleTo` setup, but consumers should only see `BulkShape<T>` and the `IDbSession` bulk overloads.

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
- `BulkMergeActions.DeleteMatched` runs the joined delete step only when it is the sole selected action in v2.4.0.
- `BulkMergeActions.DeleteNotMatchedBySource` is not supported in v2.4.0.

`BulkMergeOptions.Validate()` must be polymorphic. A `BulkMergeOptions` instance referenced through `BulkWriteOptions` must still reject `DeleteNotMatchedBySource`; method hiding is not acceptable for this security boundary.

`DeleteMatched` is exclusive in v2.4.0. Combining it with `UpdateMatched` or `InsertMissing` can update or insert rows and then delete the same staged keys in the same operation. That is surprising for callers, risky for data integrity, and close to the matched update/delete ambiguity called out in SQL Server `MERGE` documentation. If a caller needs mixed delete and upsert semantics, they should run two explicit operations with their own reviewable predicates.

Rejecting `DeleteNotMatchedBySource` in v2.4.0 is intentional. It is easy for a caller to delete too much data if the source is incomplete. That operation needs a separate bounded target predicate design and more review.

The default merge path must have its own integration test. A passing
`BulkUpsertAsync` update+insert test does not prove that the public
`BulkMergeAsync` default actions, separated counts, and validation pipeline are
wired correctly.

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
- cancellation attempts rollback before propagating as cancellation, not a successful zero-row operation.

Bulk mutation public results must be redacted even when SQL Server raises a
provider exception. The implementation may use the existing `DbErrorMapper` for
classification, but the public `DbError` returned from AOT-safe bulk operations
must:

- use a generic bulk failure message,
- contain only the sanitized destination object name,
- preserve classification fields such as kind, SQL error code, severity, and transient flag where safe,
- set `InnerException = null`,
- send raw provider exception details only through the existing redacted diagnostics path.

This is stricter than some existing non-bulk paths because bulk-copy/provider
errors can contain object details, row payload context, or provider state in
`Exception.ToString()`.

Validation errors should be explicit and actionable:

- invalid destination table,
- empty shape,
- missing key columns for mutation,
- duplicate destination columns,
- unsupported SQL type rendering,
- non-nullable column produced a null value,
- invalid option values.

The public bulk methods return `DbResult<T>` for SQL and validation failures. Implementation samples must not leave a `catch { rollback; throw; }` path for non-cancellation errors. The correct pattern is:

- roll back the transaction on SQL/general/cancellation failure before returning or rethrowing,
- let `OperationCanceledException` propagate after the rollback attempt,
- map SQL exceptions through the existing `DbErrorMapper` and redaction path,
- map general exceptions to `DbErrorKind.Unknown` with a generic public message and without including raw `Exception.Message`, row values, connection strings, or raw payloads.

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
- reader tracks `IsClosed`, implements idempotent close/dispose, clears current row state at EOF, throws on missing `GetOrdinal` names, reports `HasRows` as result-set presence without skipping the first row, and disposes the underlying enumerator exactly once,
- reader normalizes `DateOnly`, `TimeOnly`, enums, `Guid`, `decimal`, `byte[]`, and nullable values to provider-compatible values,
- enum conversion is metadata-driven from the static shape, not per-row runtime type inspection,
- destination column names reject unsafe SQL identifier syntax and parts longer than 128 characters,
- table identifiers reject unsafe bracket syntax, whitespace around multipart separators, and parts longer than 128 characters,
- options reject invalid batch size and timeout.
- options expose `CheckConstraints = true` as the default.
- decimal columns reject missing or invalid precision/scale, and stage SQL renders only explicit decimal precision/scale.
- string/binary columns reject out-of-range non-null sizes, and temporal columns reject scale values outside SQL Server's 0..7 range before any connection opens.
- CLR getter type and declared `SqlDbType` compatibility is validated during shape construction.
- stage-key index metadata rejects max-length key columns, more than 32 key columns, and declared key widths over the 900-byte portability limit.
- `BulkMergeOptions` rejects `DeleteNotMatchedBySource` even when referenced as `BulkWriteOptions`.
- `BulkMergeOptions` rejects unknown flag bits instead of silently ignoring them.
- public `BulkMergeAsync` calls `BulkMergeOptions.Validate()` before opening a connection so unknown flag bits cannot bypass direct option validation tests.
- `BulkMergeOptions` rejects `DeleteMatched` combined with `UpdateMatched` or `InsertMissing`.
- source duplicate key tuples are rejected through the stage unique index before target DML.
- delete uses a key-only staging shape and does not attempt to bulk-copy non-key columns into a key-only temp table.

Required integration tests:

- AOT-safe insert writes rows and returns count,
- insert supports destination column names that differ from CLR member names,
- insert rolls back on failure when transaction is enabled,
- update changes only matching rows,
- delete removes only matching keys,
- upsert updates matched and inserts missing rows,
- merge default update+insert path returns separated counts,
- merge delete-matched path deletes only staged keys,
- default `CheckConstraints = true` rejects a row that violates a verification-table `CHECK` constraint and leaves target rows unchanged,
- SQL Server bulk failures return a redacted failed `DbResult<T>` without public `InnerException`,
- update/delete/upsert/merge failure and cancellation tests prove rollback after target DML has started,
- update/delete/upsert/merge reject `UseTransaction = false` before opening a connection,
- update/delete/upsert/merge reject staged-inapplicable direct-bulk-copy flags before opening a connection: `FireTriggers = true`, `KeepIdentity = true`, and `CheckConstraints = false`,
- insert `UseTransaction = false` has a non-atomic opt-out test or doc assertion that does not promise rollback,
- success tests assert final target row values and missing rows, not only affected-row counts,
- invalid destination table returns a failed `DbResult`,
- cancellation token is passed to cancellable async DB calls before commit,
- rollback failure preserves the original public failure and is only recorded through redacted diagnostics,
- final commit uses `CancellationToken.None` so caller cancellation cannot produce an ambiguous canceled result after commit starts.

Required AOT checks:

- AOT verification project references the shape API and the public AOT-safe bulk overloads.
- AOT smoke creates a shape with at least one enum column, reads values through the new reader, verifies that the enum value is normalized through shape metadata without row-time type discovery, and roots the concrete public bulk overload/executor path enough for publish-time AOT analysis to inspect it without requiring a live database. Interface delegate creation alone is not sufficient; the smoke must directly reach the final `DbSession`/bulk executor implementation or an internal no-DB executor probe selected during implementation.
- Publish output has no new Lib.Db AOT or trim warnings beyond the existing baseline.
- Static gates reject `RequiresDynamicCode`, `IL3050`, `MakeGenericType`, `Expression.Compile`, `DynamicMethod`, and `Reflection.Emit` in the new AOT-safe bulk path.

Required release gates:

- pre-implementation AOT and release-verification baseline capture,
- targeted unit tests,
- targeted integration tests with local SQL Server verification DB,
- `pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1`,
- `pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short`,
- a durable release-verification log under `Verification/artifacts/logs/` that is non-empty, post-scanned, ignored/untracked, and remains secret-safe,
- final code review and security review before tag/publish.

## Release Risk Assessment

This feature is acceptable for v2.4.0 only if these constraints hold:

- implementation stays additive,
- SQL Server `MERGE` statement is not the default engine,
- delete-not-matched-by-source is rejected for v2.4.0,
- delete-matched is exclusive and cannot be combined with update/insert actions in v2.4.0,
- no public API depends on reflection for the AOT-safe overloads,
- `BulkShapeDataReader<T>` remains internal so v2.4.0 does not accidentally ship a reader implementation as stable public API,
- static gates search for `value.GetType()`, `Enum.GetUnderlyingType`, `RequiresDynamicCode`, `IL3050`, `MakeGenericType`, `Expression.Compile`, `DynamicMethod`, and `Reflection.Emit` so row-time type discovery and runtime code generation cannot slip into the reader,
- all write operations have key/identifier validation,
- identifier validation enforces malformed-bracket rejection, separator-whitespace rejection, and 128-character table/column part limits,
- all staged mutation operations run in one local transaction by default,
- staged mutation operations reject `UseTransaction = false` in v2.4.0,
- staged mutation operations reject direct-bulk-copy destination flags that do not control target DML semantics,
- insert `UseTransaction = false` is explicitly documented as non-atomic and outside rollback guarantees,
- rollback failure cannot replace the primary public failure,
- public SQL/general bulk failures are redacted and do not retain raw provider exceptions as `DbError.InnerException`,
- decimal precision/scale is explicit and cannot silently default to `decimal(18,0)`,
- invalid string/binary size metadata and invalid temporal scale metadata are covered by shape tests,
- incompatible CLR/SQL type pairs, stage-key index metadata limits, and unknown merge action bits are covered by tests, including a public `BulkMergeAsync` unknown-bit test that fails before connection open,
- cancellation rollback is guaranteed only before final commit begins, and final commit is non-cancelable from the caller token,
- release verification passes from a clean environment and leaves a durable, non-empty, post-scanned, ignored/untracked, secret-safe audit log.

If any of those constraints breaks during implementation, the feature should be reduced to AOT-safe insert/update/upsert or postponed to v2.5.0. Any reduction from the approved insert/update/delete/upsert/merge v2.4.0 scope is not an implementation-only decision: update this sub-spec, the bulk sub-plan, the integrated spec and plan, revise public docs/history/API promises, rerun review on the reduced scope, and obtain explicit user approval before continuing release work.
