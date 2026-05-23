# Result Handling

Use this file for `DbResult<T>`, `DbError`, failed commands, null payloads, and logging.

## `DbResult<T>`

Public members:

- `IsSuccess`: command success flag.
- `Value`: success payload; may be null for nullable results.
- `Error`: failed command details.
- `AffectedRows`: row count for non-query style results.
- `DbResult<T>.Ok(value, affectedRows)`
- `DbResult<T>.Fail(error)`
- Deconstruction: `(success, value, error)`.

## `DbError`

Important members:

- `Kind`: `DbErrorKind`.
- `SqlErrorCode`: SQL Server error number.
- `Severity`: SQL Server severity.
- `IsTransient`: transient classification.
- `Message`: safe user-facing message from the library.
- `Hint`: optional remediation hint.
- `ObjectName`: optional database object name.
- `InnerException`: exception for controlled diagnostics.

`DbErrorKind` includes schema not found, authentication failed, connection lost, timeout, deadlock, constraint violation, data conversion, parameter mismatch, permission denied, resource exhausted, transaction aborted, query syntax, user-defined, cloud transient, and unknown.

## Handle Failure First

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct);

if (!result.IsSuccess)
{
    logger.LogWarning(
        "User lookup failed. Kind={Kind}, SqlErrorCode={SqlErrorCode}, Transient={Transient}",
        result.Error?.Kind,
        result.Error?.SqlErrorCode,
        result.Error?.IsTransient);
    return null;
}

return result.Value;
```

Do not log full connection strings, raw credentials, or parameter values containing secrets.

## Null Is Not Failure

`QuerySingleAsync<T>()` returns `DbResult<T?>`. If `IsSuccess` is true and `Value` is null, the command succeeded and no row was returned.

```csharp
if (result is { IsSuccess: true, Value: null })
    return NotFound();
```

## Affected Rows

```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_UpdateOrderStatus")
    .With(new { OrderId = orderId, Status = status })
    .ExecuteAsync(ct);

if (result.IsSuccess && result.AffectedRows == 0)
{
    logger.LogInformation("No rows were updated for order {OrderId}.", orderId);
}
```

`ExecuteAsync()` also returns the affected row count as `Value`.

## Retry Decisions

Use `Error?.IsTransient` and `Error?.Kind` as inputs to application retry policy. For library-level resilience options, read `diagnostics-resilience.md`.
