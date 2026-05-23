# Transactions

Use this file for `BeginTransactionAsync`, `IDbTransactionScope`, commit, rollback, and transactional fluent calls.

## Start A Transaction

```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", ct);
```

With isolation level:

```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync(
    "Default",
    IsolationLevel.Serializable,
    ct);
```

## Execute In Scope

`IDbTransactionScope` implements the same command-selection stage as `db.Default`.

```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", ct);

DbResult<int> header = await tx
    .Procedure("dbo.usp_InsertOrder")
    .With(new { request.OrderNo, request.CustomerId })
    .ExecuteAsync(ct);

if (!header.IsSuccess)
    return header;

DbResult<int> lines = await tx
    .Procedure("dbo.usp_InsertOrderLines")
    .With(new { request.OrderNo, Lines = LibDb.Tvp("dbo.OrderLineTvp", request.Lines, lineShape) })
    .ExecuteAsync(ct);

if (!lines.IsSuccess)
    return lines;

DbResult<bool> commit = await tx.CommitAsync(ct);
```

## Commit And Rollback

- `CommitAsync(ct)` returns `DbResult<bool>`.
- `RollbackAsync(ct)` returns `DbResult<bool>`.
- If `CommitAsync` is not called, dispose rolls back.
- Prefer `await using` for scope lifetime.

## Safety

- Keep transactions short.
- Do not mix unrelated tenant work in one transaction.
- Decide how to handle failed `CommitAsync` separately from failed commands.
- Bulk insert inside a transaction should use constrained batch sizes and timeout settings.
