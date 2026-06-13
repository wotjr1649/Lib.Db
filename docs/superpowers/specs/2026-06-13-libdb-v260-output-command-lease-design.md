# Lib.Db v2.6.0 Output Command Lease Design

## Purpose

Lib.Db v2.6.0 expands stored procedure output parameter support from the v2.5.1 narrow fix into a complete execution-lifecycle contract. The design adopts the command-lease approach: reader-based APIs retain enough command ownership to map output parameters only after the data reader has been closed, without buffering result sets into memory.

## Goals

- Move output parameter completion from the `ExecuteNonQueryAsync` special case into the command lifecycle shared by all supported execution shapes.
- Support output and input-output parameters for `ExecuteAsync`, `QuerySingleAsync`, `ExecuteScalarAsync`, `QueryAsync`, and `QueryMultipleAsync`.
- Keep streaming memory-safe by never materializing a full stream solely to read output values.
- Define explicit output timing for reader-based APIs.
- Add `DataRow` output mapping.
- Fail fast for SQL Server output combinations that Lib.Db cannot safely support.
- Keep advanced provider-specific parameter metadata under explicit `SqlParameter` pass-through rather than expanding attribute-based inference.

## Non-Goals

- Do not add automatic DTO/attribute support for every `SqlParameter` provider property.
- Do not expose output values after failed, canceled, timed-out, or partially failed commands.
- Do not support TVP output parameters, cursor output parameters, or legacy LOB output combinations that cannot be represented safely through Lib.Db's public abstractions.
- Do not change public result types to carry output values separately from caller-owned parameter objects.
- Do not buffer streaming results as a workaround for output lifecycle timing.

## Recommended Architecture

Introduce an internal command lease concept for reader-producing execution. A lease represents the active `SqlCommand`, its `DbDataReader`, the original parameter object, and a completion callback that maps output parameters once the reader is closed. The lease remains internal to the executor layer and does not change public APIs.

For non-reader execution, `ExecutePipelineAsync` should own output completion directly. After the operation succeeds and before internal executed interceptors run, it maps output parameters exactly once. `ExecuteNonQueryAsync` should no longer perform a separate mapping call that can duplicate the common lifecycle work.

For streaming execution, `QueryStreamCoreAsync` should acquire an output-aware reader lease. The async iterator reads rows one at a time as it does today. In the iterator `finally`, it disposes the reader and then completes output mapping. If reading throws, cancellation is requested, or execution fails before a lease is acquired, output values are not guaranteed.

For multiple-result execution, `QueryMultipleAsync` should return an output-aware `SqlGridReader`. `SqlGridReader.DisposeAsync()` disposes the underlying reader and then completes output mapping. Helper APIs that dispose the reader, such as `ReadMultipleAsync(...)`, inherit the same contract.

## Output Timing Contract

`ExecuteAsync`, `QuerySingleAsync`, and `ExecuteScalarAsync`:

- Output and input-output values are mapped after successful command execution.
- `QuerySingleAsync` maps output even when no row is returned, as long as the command succeeds.
- `ExecuteScalarAsync` maps output even when the scalar value is `null` or `DBNull.Value`, as long as the command succeeds.

`QueryAsync`:

- Output values are not available when the `IAsyncEnumerable<T>` is returned.
- Output values are available after full enumeration or after the async enumerator is disposed.
- If enumeration fails, is canceled, or the command fails, output values are not guaranteed.

`QueryMultipleAsync`:

- Output values are not available when `IMultipleResultReader` is returned.
- Output values are available after `IMultipleResultReader.DisposeAsync()` completes.
- Output values are available after `ReadMultipleAsync(...)` helper methods complete because they dispose the reader.
- If reading or disposal fails, output values are not guaranteed.

## Parameter Mapping Contract

Automatic reverse mapping covers `ParameterDirection.Output` and `ParameterDirection.InputOutput`.

`ParameterDirection.ReturnValue` is supported through explicit `SqlParameter` pass-through. Caller-owned `SqlParameter` references receive provider-populated values. Return values are not treated as ordinary output property values in DTO, dictionary, or DataRow reverse mapping by default because SQL Server return status is semantically distinct from output parameters.

Dictionary parameters:

- Output and input-output values update the dictionary entry whose key matches the parameter name with the leading `@` removed.
- Explicit `SqlParameter` entries remain the caller-owned object during binding and may be replaced by the mapped scalar output value only for output/input-output entries under the existing dictionary mapper contract.
- Return-value entries remain explicit `SqlParameter` objects.

DTO and anonymous-object-like parameters:

- Writable properties matching output/input-output parameters are updated.
- Properties of type `SqlParameter` preserve the same reference and rely on provider-populated values.
- Non-writable properties are ignored for reverse mapping.

DataRow parameters:

- Existing columns matching output/input-output parameter names are updated.
- `DBNull.Value` remains `DBNull.Value` for DataRow so table schema semantics are preserved.
- Missing output columns fail in strict mode and are ignored in non-strict mode.
- Read-only columns fail with a non-sensitive mapping error.

## Unsupported Edge Guards

Lib.Db should fail before execution when a parameter contract is known unsupported:

- `SqlDbType.Structured` with `Output`, `InputOutput`, or `ReturnValue`.
- Metadata or explicit parameters representing TVP output.
- Cursor output parameters, because Lib.Db does not expose a cursor consumer contract.
- Legacy `text`, `ntext`, and `image` output parameters unless the caller provides an explicit `SqlParameter` and the provider accepts it without requiring Lib.Db conversion.
- Duplicate return-value parameters.
- Explicit `SqlParameter` direction that conflicts with schema metadata in a way Lib.Db cannot safely normalize.

Error messages must be redacted. They may include parameter names and high-level type/direction facts, but must not include connection strings, SQL batches, server names, credentials, or raw provider traces.

## Advanced SqlParameter Policy

Advanced provider-specific metadata remains an explicit `SqlParameter` responsibility. This includes `SqlValue`, `UdtTypeName`, XML schema collection properties, Always Encrypted settings, precision/scale edge cases, and provider-specific type names.

Lib.Db should preserve caller-owned `SqlParameter` object identity and provider metadata where possible. Schema-based binding may continue to normalize safe public facets such as name, direction, SQL type, size, precision, scale, and TVP `TypeName` when the schema requires it.

## Testing Strategy

TDD starts by adding failing tests for:

- `QuerySingleAsync` output/input-output propagation after single-row read.
- `QuerySingleAsync` output propagation when the result set has no rows.
- `ExecuteScalarAsync` output propagation when the scalar is present.
- `ExecuteScalarAsync` output propagation when the scalar is `DBNull`.
- `QueryAsync` output propagation after full enumeration.
- `QueryAsync` output propagation after enumerator disposal without full buffering.
- `QueryMultipleAsync` output propagation after `DisposeAsync`.
- `ReadMultipleAsync(...)` output propagation after helper completion.
- DataRow output mapping, null mapping, missing-column strict behavior, and read-only-column failure.
- Explicit `SqlParameter` advanced property pass-through.
- TVP output, cursor output, duplicate return value, and structured output fail-fast guards.

Minimum verification gates:

- Targeted `OutputParameterTests`.
- Targeted `MapperCoverageTests`.
- Targeted TVP/binder tests.
- Targeted multiple-result reader/helper tests.
- `dotnet build` for the solution.

Extended verification gates:

- Verification DB setup and targeted DB-backed tests.
- Full integration test script when local SQL Server prerequisites are available.
- A memory guard that confirms streaming output completion does not require full result buffering.
- Codex Security diff scan after implementation changes exist.
- Code review and simplification review with findings driven to zero before final completion.

## Security Checkpoint

Sensitive surface: SQL parameter binding, stored procedure execution, reader disposal, output value propagation, and redacted error generation.

Trust boundary: caller-owned parameter objects cross into `SqlCommand.Parameters`, and provider-populated output values cross back into caller-owned objects.

Untrusted input: parameter names, values, dictionaries, DataRows, DTOs, explicit `SqlParameter` instances, and stored procedure metadata read from the database.

Abuse cases:

- Unsafe output type reaches provider execution and leaks raw provider error text.
- Failed or canceled command partially mutates application state through output reverse mapping.
- Streaming implementation buffers large results to force output availability.
- Error messages disclose SQL text, connection strings, hostnames, credentials, or raw values.
- Explicit provider metadata is stripped and causes incorrect SQL type behavior.

Mitigations:

- Complete output mapping only after successful execution or reader disposal.
- Do not guarantee output after failure, cancellation, timeout, or read exception.
- Keep streaming row-by-row.
- Add fail-fast guards for unsupported output direction/type combinations.
- Preserve explicit `SqlParameter` identity and metadata.
- Use redacted Lib.Db errors.

## Documentation Updates

Update consumer docs and skills after implementation:

- `docs/03_api_reference.md`
- `docs/05_fluent_api_reference.md`
- `docs/06_cookbook.md`
- `docs/verification.md` if test invocation or memory guard changes
- `.agents/skills/lib-db/references/parameters-and-binding.md`
- `.agents/skills/lib-db/references/fluent-execution.md`

Docs must state exactly when output values are available for each execution shape.

## Implementation Sequence

1. Add failing tests for non-reader APIs and DataRow mapping.
2. Move non-reader output completion into the shared pipeline.
3. Add DataRow output reverse mapping.
4. Add explicit unsupported edge guards.
5. Add command lease for `QueryAsync`.
6. Add output-aware `SqlGridReader` for `QueryMultipleAsync`.
7. Add explicit `SqlParameter` advanced pass-through tests.
8. Run targeted verification.
9. Run code review, security review, and simplification review.
10. Update docs and skills.
11. Run final verification gates.

Each implementation task should be committed separately after its tests and required reviews pass.
