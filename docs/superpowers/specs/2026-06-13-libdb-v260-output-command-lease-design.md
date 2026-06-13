# Lib.Db v2.6.0 Output Command Lease Design

## Purpose

Lib.Db v2.6.0 expands stored procedure output parameter support from the v2.5.1 narrow fix into a complete execution-lifecycle contract. The design adopts an internal command lease so reader-based APIs retain command ownership until the data reader is closed, then copy output values back to caller-owned parameter targets without buffering result sets into memory.

## Reference Constraints

- Microsoft ADO.NET documents that output parameters and return values are not available from `ExecuteReader` paths until the `DataReader` is closed.
- `SqlDataReader.Close` populates output parameters, return values, and `RecordsAffected` by consuming pending results; this can be a long operation for large result sets.
- A `DataReader` is an unbuffered stream and is the preferred shape for large result processing. Lib.Db must not materialize a result stream only to make output values available.
- SQL Server table-valued parameters are input-only; `OUTPUT` is not supported for TVPs.
- SQL Server `text`, `ntext`, and `image` are deprecated. Lib.Db v2.6.0 does not support these legacy types for output, input-output, or return-value parameters; callers should use `varchar(max)`, `nvarchar(max)`, and `varbinary(max)` instead.

## Goals

- Move output parameter completion from the `ExecuteNonQueryAsync` special case into the command lifecycle shared by all supported execution shapes.
- Support output and input-output parameters for `ExecuteAsync`, `QuerySingleAsync`, `ExecuteScalarAsync`, `QueryAsync`, and `QueryMultipleAsync`.
- Support explicit `SqlParameter` return-value propagation for all successful execution shapes.
- Keep streaming memory-safe by never materializing a full stream solely to read output values.
- Define exact output timing, failure behavior, and late-failure behavior for reader-based APIs.
- Add transactional `DataRow` output mapping.
- Fail fast for SQL Server output combinations that Lib.Db cannot safely support.
- Keep advanced provider-specific parameter metadata under explicit `SqlParameter` metadata pass-through rather than expanding attribute-based inference.

## Non-Goals

- Do not add automatic DTO/attribute support for every `SqlParameter` provider property.
- Do not expose or copy output values after failed, canceled, timed-out, read-faulted, dispose-faulted, interceptor-faulted, or partially failed commands.
- Do not support TVP output parameters, cursor output parameters, or legacy `text`/`ntext`/`image` output combinations.
- Do not change public result types to carry output values separately from caller-owned parameter objects.
- Do not buffer streaming results as a workaround for output lifecycle timing.
- Do not add caller-owned `SqlParameter` instances directly to an executing `SqlCommand`; preserve caller object identity through clone-and-copy-back.

## Recommended Architecture

Introduce an internal `DbCommandLease` for reader-producing execution. The lease owns:

- the command-bound `SqlCommand`
- the active `DbDataReader`
- any connection, transaction, metrics, and diagnostic disposal owner currently tied to the reader lifecycle
- the original caller parameter object
- a validated output target map
- command-bound output snapshots
- a single output completion callback
- a lease state that controls whether copy-back is allowed

The lease remains internal to the executor layer and does not change public APIs. The strategy/executor boundary must return a lease, not only a bare reader, for output-aware reader APIs. If compatibility requires keeping existing strategy methods, add output-aware overloads and keep the old reader-returning methods as wrappers for no-output paths.

For non-reader execution, `ExecutePipelineAsync` owns output completion directly. The public operation is successful only after the command and every pipeline callback/interceptor that can turn the operation into a failure has completed successfully. Caller-object output copy-back happens after that point and exactly once. `ExecuteNonQueryAsync` must not perform a separate mapping call that can duplicate common lifecycle work.

For streaming execution, `QueryStreamCoreAsync` acquires an output-aware lease. The async iterator reads rows one at a time. The iterator always disposes the reader/lease to release resources, but it completes output copy-back only when the lease reaches an output-eligible terminal state.

For multiple-result execution, `QueryMultipleAsync` returns an output-aware `SqlGridReader` that owns a lease. `SqlGridReader.DisposeAsync()` disposes the underlying reader and then completes output copy-back only when the lease is output-eligible. Helper APIs that dispose the reader, such as `ReadMultipleAsync(...)`, inherit the same lease contract and must mark the lease failed before disposal when a read or mapping failure occurs.

## Lease State Machine

Output copy-back is allowed only from these terminal states:

- `FullyConsumed`: all result sets were read successfully and reader close/dispose completed successfully.
- `EarlyDisposedCleanly`: the caller stopped enumeration or disposed `IMultipleResultReader` without a read failure, cancellation request, timeout, command failure, or disposal failure, and reader close/dispose completed successfully.
- `NonReaderSucceeded`: a non-reader command and all failure-capable callbacks/interceptors completed successfully.

Output copy-back is forbidden from these states:

- `CommandFailed`
- `ReadFailed`
- `Canceled`
- `TimedOut`
- `DisposeFailed`
- `InterceptorFailed`
- `OutputMappingFailed`
- `CompletionAlreadyAttempted`

Rules:

- Copy-back is exactly once. Repeated disposal or repeated helper completion must not copy values twice.
- The lease must distinguish normal early disposal from cancellation. Normal early disposal can map outputs after successful close; cancellation must not.
- The lease must attempt resource cleanup even when copy-back is forbidden.
- A failed close/dispose forbids copy-back even if the provider has populated command parameters.
- Output mapping failure forbids partial caller mutation and surfaces as the API-specific late failure described below.

## Output Timing Contract

`ExecuteAsync`, `QuerySingleAsync`, and `ExecuteScalarAsync`:

- Output, input-output, and explicit return-value `SqlParameter` values are copied back after successful command execution and after every failure-capable callback/interceptor succeeds.
- `QuerySingleAsync` copies output even when no row is returned, as long as the command succeeds.
- `ExecuteScalarAsync` copies output even when the scalar value is `null` or `DBNull.Value`, as long as the command succeeds.
- If the command, timeout, cancellation, interceptor, or output copy-back fails, caller-owned output targets remain unchanged.

`QueryAsync`:

- Output and return values are not available when the `IAsyncEnumerable<T>` is returned.
- Output and return values are copied back after full enumeration or normal async enumerator disposal, provided reader close/dispose succeeds and no read failure, cancellation, timeout, or command failure occurred.
- Output and return values are not copied back after read failure, cancellation, timeout, command failure, or reader disposal failure.
- The implementation must not buffer rows to force output availability.

`QueryMultipleAsync`:

- Output and return values are not available when `IMultipleResultReader` is returned.
- Output and return values are copied back after `IMultipleResultReader.DisposeAsync()` completes successfully from an output-eligible lease state.
- Output and return values are copied back after `ReadMultipleAsync(...)` helper methods complete successfully because they dispose the reader.
- Output and return values are not copied back after read failure, helper failure, cancellation, timeout, command failure, or reader disposal failure.

## Late Failure Policy

Late output completion failures occur after a reader-producing API has returned its stream or multi-result reader. They must be handled consistently:

- `QueryAsync` surfaces read, dispose, and output-completion failures from enumeration or enumerator disposal as redacted Lib.Db exceptions.
- Raw `IMultipleResultReader.DisposeAsync()` surfaces dispose and output-completion failures as redacted Lib.Db exceptions.
- Fluent helper APIs that return `DbResult` convert late failures into failed `DbResult` values through the existing execution-helper error policy.
- No late failure may leave caller-owned DTOs, dictionaries, `DataRow`s, or explicit `SqlParameter` instances partially updated.
- Error text may include sanitized parameter display names and high-level type/direction facts only.

## Parameter Name Canonicalization

Lib.Db must build a target map before execution:

- Canonical parameter names remove exactly one leading `@` and compare with `StringComparer.OrdinalIgnoreCase`.
- Empty canonical names are invalid.
- Dictionary keys, DTO property names, generated mapper parameter names, and `DataRow` column names are matched through the same canonical rule.
- If two parameters or two target members produce the same canonical name, the contract is ambiguous and must fail before execution.
- Strict mode fails when an output target is missing. Non-strict mode ignores missing output targets only when the missing target is unambiguous.
- Ambiguous matches always fail, regardless of strict mode.
- Parameter display names in errors must be sanitized for length and control characters before logging or exception construction.

## Parameter Mapping Contract

Automatic reverse mapping covers `ParameterDirection.Output` and `ParameterDirection.InputOutput`.

`ParameterDirection.ReturnValue` is supported only through explicit `SqlParameter` values. Return values are copied back to caller-owned `SqlParameter` instances after the same successful terminal states as output parameters. Return values are not treated as ordinary DTO, dictionary, or `DataRow` output targets.

Stored procedure return status values are SQL Server `int` values. Explicit return-value parameters must use `SqlDbType.Int` or equivalent `DbType.Int32`; other return-value types fail before execution with a redacted Lib.Db error.

Dictionary parameters:

- Output and input-output values update the dictionary entry whose key uniquely matches the canonical parameter name.
- Explicit `SqlParameter` dictionary entries are cloned into command-bound parameters before execution.
- On successful copy-back, caller-owned `SqlParameter` instances receive provider-populated values. If the existing dictionary mapper contract replaces output/input-output entries with scalar values, the copy-back to the original `SqlParameter` must happen before replacement.
- Return-value entries remain explicit `SqlParameter` objects and are not replaced with scalar values.

DTO and anonymous-object-like parameters:

- Writable properties matching output/input-output parameters are updated only after all output targets validate.
- Properties of type `SqlParameter` preserve the caller's object reference externally, but command execution uses a cloned command-bound parameter.
- Non-writable properties are ignored in non-strict mode and fail in strict mode when they are required output targets.
- DTO copy-back must roll back any Lib.Db-written property values if a later output target fails. Custom setter side effects outside the written property values are outside Lib.Db's guarantee and should be avoided by consumers.

DataRow parameters:

- Existing columns matching output/input-output parameter names are updated only after all output targets validate.
- `DBNull.Value` remains `DBNull.Value` for `DataRow` so table schema semantics are preserved.
- Missing output columns fail in strict mode and are ignored in non-strict mode.
- Read-only, expression, type-incompatible, `AllowDBNull=false`, `MaxLength`, and constraint-violating columns fail with a non-sensitive mapping error.
- `DataRow` copy-back is transactional: snapshot original values, apply updates, and roll back all Lib.Db-written values if any assignment, `EndEdit`, or constraint validation fails.

## Two-Phase Output Copy-Back

Output completion must be two-phase:

1. Collect command-bound output, input-output, and return values into an internal snapshot after the command or reader reaches an output-eligible state.
2. Validate every caller target, including canonical name uniqueness, writability, type conversion, `DataRow` constraints, and explicit `SqlParameter` copy-back compatibility.
3. Apply all caller mutations only after validation succeeds.
4. If validation or application fails, restore every Lib.Db-written target to its original value and surface a redacted error through the API-specific failure policy.

The implementation must not copy one output value to the caller and then discover that a later output target is invalid without rolling back the earlier write.

## Explicit SqlParameter Policy

Advanced provider-specific metadata remains an explicit `SqlParameter` responsibility. This includes `SqlValue`, `UdtTypeName`, XML schema collection properties, Always Encrypted settings, precision/scale edge cases, and provider-specific type names.

Lib.Db must preserve caller-owned `SqlParameter` object identity from the caller's perspective, but it must bind a cloned command parameter during execution. The clone copies supported metadata and bind-time input state from the caller parameter, including `Value` or `SqlValue` for input and input-output directions. Provider-populated output values are copied back to the caller object only after an output-eligible terminal state.

Rules:

- The same caller-owned `SqlParameter` instance cannot be used for more than one bound command parameter in the same command.
- Mutating caller-owned parameter objects during execution is unsupported. The command-bound clone prevents such mutation from bypassing Lib.Db's validation guards.
- Schema-based binding may normalize safe public facets such as name, direction, SQL type, size, precision, scale, and TVP `TypeName` when the schema requires it.
- Unsupported direction/type combinations still fail fast even when supplied through explicit `SqlParameter`.

## Unsupported Edge Guards

Lib.Db must fail before execution when a parameter contract is known unsupported:

- `SqlDbType.Structured` with `Output`, `InputOutput`, or `ReturnValue`.
- Metadata or explicit parameters representing TVP output.
- Cursor output parameters detected from stored procedure metadata.
- `SqlDbType.Text`, `SqlDbType.NText`, or `SqlDbType.Image` with `Output`, `InputOutput`, or `ReturnValue`, including explicit `SqlParameter` paths.
- Duplicate return-value parameters.
- Return-value parameters whose SQL type is not `SqlDbType.Int` or equivalent `DbType.Int32`.
- Duplicate or ambiguous canonical output target names.
- Duplicate caller-owned `SqlParameter` references in one command.
- Explicit `SqlParameter` direction that conflicts with schema metadata in a way Lib.Db cannot safely normalize.

Provider errors that remain possible after fail-fast validation must be wrapped or mapped through redacted Lib.Db errors. Error messages must not include connection strings, SQL batches, server names, credentials, raw parameter values, or raw provider traces.

## Acceptance Criteria

- Every supported execution shape maps output, input-output, and explicit return values after success.
- Reader-based APIs map only after reader close/dispose reaches an output-eligible terminal state.
- Failure, cancellation, timeout, read fault, disposal fault, interceptor fault, and output mapping fault leave caller-owned output targets unchanged.
- Output copy-back runs exactly once per command.
- Explicit `SqlParameter` metadata is preserved through clone-and-copy-back.
- Automatic reverse mapping is canonical, unique, strict-mode aware, and transactional.
- Streaming tests prove rows are processed incrementally and no full materialization is introduced for output completion.
- Unsupported SQL Server output edge cases fail before execution with redacted errors.
- Documentation and consumer skills state API-specific availability, early-dispose behavior, failure no-copy-back behavior, explicit `SqlParameter` behavior, `DataRow` strict behavior, and unsupported edge cases.
- Each implementation task ends green before commit; intentionally failing TDD tests are local-only and must not be committed alone.

## Testing Strategy

TDD starts by adding failing tests inside the active local work item, then implementing the matching slice before commit.

Required tests:

- `ExecuteAsync` output/input-output propagation for DTO, dictionary, and explicit `SqlParameter` paths.
- `ExecuteAsync` explicit return-value propagation.
- `QuerySingleAsync` output/input-output propagation after single-row read.
- `QuerySingleAsync` output propagation when the result set has no rows.
- `QuerySingleAsync` explicit return-value propagation.
- `ExecuteScalarAsync` output propagation when the scalar is present.
- `ExecuteScalarAsync` output propagation when the scalar is `DBNull`.
- `ExecuteScalarAsync` explicit return-value propagation.
- `QueryAsync` output propagation after full enumeration.
- `QueryAsync` output propagation after normal enumerator disposal without full buffering.
- `QueryAsync` no-copy-back after cancellation, timeout, read exception, command failure, and dispose failure.
- `QueryMultipleAsync` output and return-value propagation after `DisposeAsync`.
- `ReadMultipleAsync(...)` output and return-value propagation after helper completion.
- `QueryMultipleAsync` and `ReadMultipleAsync(...)` no-copy-back after read/helper failure and dispose failure.
- Output copy-back exactly-once coverage for repeated disposal/helper paths.
- Non-reader executed-interceptor failure no-copy-back coverage.
- DataRow output mapping, `DBNull.Value`, missing-column strict behavior, read-only column failure, expression column failure, type mismatch, nullability/length/constraint rollback, and case-ambiguous column failure.
- DTO and dictionary canonical-name collision failure.
- Explicit `SqlParameter` clone metadata preservation, return-value propagation, duplicate reference rejection, and unsupported direction/type guard coverage.
- Explicit `InputOutput SqlParameter` bind-time `Value`/`SqlValue` cloning.
- Non-`int` explicit return-value fail-fast coverage.
- TVP output, cursor output, duplicate return value, legacy LOB output, and structured output fail-fast guards.
- Output mapping failure rollback for DTO, dictionary, DataRow, and explicit `SqlParameter` targets.
- A fake/controlled reader memory guard that proves streaming output completion does not call `ToList`, materialize all rows, or enumerate beyond the caller's requested rows except for provider close/dispose behavior.

Minimum verification gates:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputParameterTests|FullyQualifiedName~MapperCoverageTests|FullyQualifiedName~SqlGridReaderCoverageTests|FullyQualifiedName~MultipleResultExtensionsTests|FullyQualifiedName~RuntimeTvpBindingTests|FullyQualifiedName~OutputCommandLeaseMemoryGuardTests"
dotnet build Lib.Db/Lib.Db.csproj
```

Extended verification gates:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputParameterTests"
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Verification/scripts/Invoke-Tests.ps1
```

Extended gates require local SQL Server prerequisites for DB-backed verification. If those prerequisites are unavailable, the final report must state that DB-backed gates were not run.

After implementation changes exist, run a Codex Security diff scan, code review, verifier review, DB contract review, and simplification review until findings are zero or explicitly deferred by the user.

## Security Checkpoint

Sensitive surface: SQL parameter binding, stored procedure execution, reader disposal, output value propagation, explicit `SqlParameter` clone/copy-back, caller-owned mutable object updates, and redacted error generation.

Trust boundary: caller-owned parameter objects and stored procedure metadata cross into `SqlCommand.Parameters`; provider-populated output values cross back into caller-owned objects only through validated snapshots.

Untrusted input: parameter names, values, dictionaries, `DataRow`s, DTOs, explicit `SqlParameter` instances, and stored procedure metadata read from the database.

Abuse cases:

- Unsafe output type reaches provider execution and leaks raw provider error text.
- Failed or canceled command mutates application state through output reverse mapping.
- Streaming implementation buffers large results to force output availability.
- Error messages disclose SQL text, connection strings, hostnames, credentials, raw values, or log-injection payloads in parameter names.
- Explicit provider metadata is stripped or mutated and causes incorrect SQL type behavior.
- Caller mutates explicit `SqlParameter` objects after validation to bypass direction/type guards.
- Output target collision updates the wrong dictionary entry, DTO property, or `DataRow` column.

Mitigations:

- Complete output copy-back only from output-eligible terminal states.
- Clone explicit `SqlParameter` instances for command execution and copy back after success.
- Use two-phase snapshot validation and transactional caller mutation.
- Keep streaming row-by-row.
- Add fail-fast guards for unsupported output direction/type combinations.
- Sanitize parameter display names in errors.
- Use redacted Lib.Db errors.

## Documentation Updates

Update consumer docs and skills after implementation:

- `docs/03_api_reference.md`
- `docs/05_fluent_api_reference.md`
- `docs/06_cookbook.md`
- `docs/verification.md` if test invocation or memory guard changes
- `.agents/skills/lib-db/references/parameters-and-binding.md`
- `.agents/skills/lib-db/references/fluent-execution.md`

Docs and skills must cover:

- exact output availability for each execution shape
- early disposal behavior
- failure, cancellation, timeout, read fault, dispose fault, and interceptor fault no-copy-back behavior
- explicit `SqlParameter` clone-and-copy-back behavior
- explicit return-value support and DTO/dictionary/DataRow exclusion
- DataRow strict/non-strict and transactional rollback behavior
- unsupported TVP, cursor, structured, and legacy LOB output combinations
- memory-safe streaming expectations

## Implementation Sequence

Implement as green vertical slices. Do not commit tests that are intentionally left failing.

1. Non-reader output lifecycle: tests and implementation for `ExecuteAsync`, `QuerySingleAsync`, `ExecuteScalarAsync`, explicit return values, interceptor failure, exactly-once copy-back, and no-copy-back failure cases.
2. Two-phase reverse mapping: tests and implementation for canonical names, DTO/dictionary rollback, `DataRow` transactional copy-back, strict/non-strict behavior, and mapping failure rollback.
3. Explicit `SqlParameter` clone/copy-back: tests and implementation for metadata preservation, duplicate reference rejection, return values, and unsupported explicit direction/type guards.
4. Unsupported SQL Server edge guards: tests and implementation for structured/TVP output, cursor output metadata, legacy LOB output, duplicate return values, and redacted errors.
5. `QueryAsync` command lease: tests and implementation for full enumeration, clean early disposal, cancellation/read/timeout/dispose failures, exactly-once completion, and memory guard.
6. `QueryMultipleAsync` command lease: tests and implementation for raw `DisposeAsync`, helper completion, helper/read/dispose failures, return values, and late failure policy.
7. Documentation and skill updates: docs/skills/tests updated with the final public contract.
8. Final verification: targeted tests, build, DB-backed tests when available, Codex Security diff scan, code review, verifier review, DB contract review, and simplification review.

Each implementation task should be committed separately after its tests and required reviews pass.

## External References

- Microsoft Learn, `SqlDataReader.Close`: <https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqldatareader.close>
- Microsoft Learn, retrieving data using a `DataReader`: <https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/retrieving-data-using-a-datareader>
- Microsoft Learn, executing a command with Microsoft SqlClient: <https://learn.microsoft.com/sql/connect/ado-net/execute-command>
- Microsoft Learn, table-valued parameters: <https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql/table-valued-parameters>
- Microsoft Learn, `ntext`, `text`, and `image`: <https://learn.microsoft.com/en-us/sql/t-sql/data-types/ntext-text-and-image-transact-sql>
