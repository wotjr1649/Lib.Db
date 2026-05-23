# Lib.Db v2.4.0 Bulk Implementation Handoff

This artifact is the required Task 5 checkpoint before implementing the
AOT-safe bulk executor path. Tasks 6-8 must consume these choices instead of
creating a second connection, transaction, or error-mapping path.

## Checkpoint

- Established after bulk Tasks 1-4 and before Task 6.
- Baseline gate: Task 0 completed before source changes.
- Prior checkpoint commits:
  - `9bda693 feat: add AOT-safe bulk shape contracts`
  - `2a5b9f3 feat: add AOT-safe bulk reader and SQL builders`
- Task 5 handoff artifact checkpoint: this document. If committed, use that
  commit as the no-runtime-diff checkpoint for the integration decisions below.

## Connection And Executor Seam

- Open configured SQL connections through the existing
  `IDbConnectionFactory.CreateConnectionAsync(string instanceHash, CancellationToken ct)`
  contract from `Lib.Db/Contracts/Infrastructure/InfrastructureContracts.cs`.
- `DbSession` already owns this factory as the primary constructor parameter
  `connectionFactory` in `Lib.Db/Core/DbSession.cs`.
- Task 6 should create `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs` as an
  internal executor that receives the existing connection factory. The preferred
  constructor contract is:

```csharp
internal sealed class BulkWriteExecutor(IDbConnectionFactory connectionFactory);
```

- `DbSession` public bulk overloads should call this executor and pass
  `instanceName`, destination table, records, shape, options, and cancellation
  token. Do not invent a parallel connection resolver.

## DbResult Contract

- Successful bulk methods return through `DbResult<T>.Ok(value)`.
- Failed public bulk methods return `DbResult<T>.Fail(DbError)`.
- Public failure messages for new AOT-safe bulk paths must be generic and
  redacted. Do not copy provider exception messages, raw SQL text, row values,
  parameter values, cache payloads, tenant/user identifiers, or connection
  strings into `DbError.Message`, `DbError.ObjectName`, or `InnerException`.
- Cancellation remains exceptional: `OperationCanceledException` propagates.

## Error Mapping

- SQL/provider failures should map to a generic bulk failure message and a
  `DbErrorKind.Unknown` or narrowly safe existing kind when one can be assigned
  without preserving raw provider text.
- Non-SQL failures should map to the same redacted public failure shape.
- Rollback failures must not replace the primary failure in the public
  `DbResult<T>`. If rollback fails, preserve the primary redacted failure.
- The legacy reflection `BulkInsertAsync` currently exposes general exception
  details and is not the model for v2.4.0 AOT-safe public errors.

## Transaction Ownership

- AOT-safe staged update/delete/upsert/merge own a local SQL transaction inside
  `BulkWriteExecutor`.
- Use `SqlConnection.BeginTransactionAsync(CancellationToken)` on the connection
  opened through `IDbConnectionFactory`.
- Pass the transaction to `SqlBulkCopy` and all staged DML commands.
- Roll back on SQL failures, general failures, and cancellation before final
  commit starts.
- Commit with `CancellationToken.None` after all staged work has succeeded.
  Cancellation is guaranteed only before commit begins.
- Direct AOT-safe insert may support `UseTransaction = false` as an explicit
  non-atomic opt-out. Staged update/delete/upsert/merge must reject
  `UseTransaction = false` before opening a connection.

## Option Boundaries

- Direct insert may map `FireTriggers`, `KeepIdentity`, and
  `CheckConstraints` to `SqlBulkCopyOptions` for the destination table.
- Staged update/delete/upsert/merge must reject `FireTriggers = true`,
  `KeepIdentity = true`, and `CheckConstraints = false` before opening a
  connection because `SqlBulkCopy` writes to the staging table, not the target.
- `BulkMergeOptions.Validate()` owns scalar/action validation, including
  unknown action-bit rejection.

## Test Seams

- Unit-test pre-open validation with a fake executor seam or fake connection
  factory that counts connection-open attempts.
- Integration tests may use the existing local SQL Server verification fixture
  through application/test code paths. Do not use direct SQL tools for DDL/DML.
- Add failure hooks in `BulkWriteExecutor` only as internal test seams when
  needed to verify rollback, cancellation, and final commit boundaries.
- Tests must assert public `DbResult` failures are generic/redacted and that
  staged validation failures occur before connection open where required.

## AOT No-DB Reachability Probe

- The final AOT smoke must reach a concrete public `DbSession` bulk path or an
  internal no-DB executor probe. Interface delegates plus `GC.KeepAlive` are not
  sufficient.
- Preferred no-DB probe: instantiate or call the concrete bulk executor path
  through a fake `IDbConnectionFactory` and validation inputs that fail before
  opening a connection. This exercises concrete method bodies, option
  validation, shape metadata, and redacted failure mapping without needing a DB.
- If a public `DbSession` path is used instead, select an input that fails
  pre-open by contract, such as staged mutation with `UseTransaction = false` or
  `BulkMergeOptions` with unknown action bits.

## SQL Builder Contract

- Tasks 6-8 must use `BulkIdentifier`, `BulkSqlTypeRenderer`, and
  `BulkSqlBuilder` from Tasks 3-4.
- Do not emit SQL Server `MERGE` as the default engine.
- Do not interpolate row values into SQL text. SQL text may contain only
  validated/quoted identifiers and metadata.
- Create the staging unique key index without `IGNORE_DUP_KEY`.
