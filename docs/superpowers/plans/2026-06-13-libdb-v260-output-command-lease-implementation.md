# Lib.Db v2.6.0 Output Command Lease Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement complete, memory-safe stored procedure output, input-output, and explicit return-value support across Lib.Db non-streaming, streaming, and multiple-result execution APIs.

**Architecture:** Add an internal output lifecycle layer that validates targets before execution, binds command-owned `SqlParameter` clones, and copies provider-populated values back only from output-eligible terminal states. Reader-producing APIs use an internal command lease that owns the command, reader, connection lifecycle, metrics closure, and exactly-once output completion without buffering result sets.

**Tech Stack:** .NET 10/C# preview, Microsoft.Data.SqlClient 7.0.1, SQL Server local verification DB, xUnit v3, FluentAssertions, Moq.

---

## Reviewed Spec

Implementation must follow `docs/superpowers/specs/2026-06-13-libdb-v260-output-command-lease-design.md`.

Spec review gate is complete:

- Architect: CLEAR, findings 0
- Verifier: PASS, findings 0
- DB contract: PASS, findings 0
- Code-reviewer lane: APPROVE, findings 0
- Codex Security spec review: PASS, findings 0

## File Structure

Create:

- `Lib.Db/Execution/Output/OutputParameterName.cs`: canonical parameter and target name handling.
- `Lib.Db/Execution/Output/OutputParameterSnapshot.cs`: command-bound output value snapshots and original target snapshots.
- `Lib.Db/Execution/Output/OutputParameterCopyBack.cs`: two-phase validation and transactional caller-object copy-back.
- `Lib.Db/Execution/Output/SqlParameterCloneFactory.cs`: explicit `SqlParameter` clone, unsupported edge guards, duplicate reference guard, return-value type guard.
- `Lib.Db/Execution/Output/DbCommandLease.cs`: reader command lease, terminal state, exactly-once completion.
- `Verification/projects/Lib.Db.IntegrationTests/Unit/OutputCommandLeaseMemoryGuardTests.cs`: fake-reader streaming guard.

Modify:

- `Lib.Db/Contracts/Mapping/MappingContracts.cs`: document that `MapOutputParameters` is success-path only and transactional.
- `Lib.Db/Execution/Binding/DbBinder.cs`: route explicit `SqlParameter` binding through clone factory and edge guards.
- `Lib.Db/Execution/Binding/Mappers.cs`: replace direct output writes with `OutputParameterCopyBack`.
- `Lib.Db/Execution/Executors/Strategies.cs`: add output-aware stream lease acquisition while preserving existing stream behavior for no-output paths.
- `Lib.Db/Contracts/Execution/StrategyAndInterceptionContracts.cs`: add an internal output-aware stream method or overload.
- `Lib.Db/Execution/Executors/SqlDbExecutor.cs`: move non-reader output completion into the shared success lifecycle and use leases for `QueryAsync`/`QueryMultipleAsync`.
- `Lib.Db/Execution/Executors/SqlGridReader.cs`: own a lease, mark read failures, complete output on clean dispose.
- `Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`: mapper, clone, rollback, canonical-name, edge guard unit coverage.
- `Verification/projects/Lib.Db.IntegrationTests/Unit/SqlGridReaderCoverageTests.cs`: lease-backed grid reader behavior.
- `Verification/projects/Lib.Db.IntegrationTests/Unit/MultipleResultExtensionsTests.cs`: helper late failure and dispose behavior.
- `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs`: DB-backed API coverage.
- `Verification/scripts/Invoke-Tests.ps1`: include the memory guard filter if the script has targeted groups.
- `docs/03_api_reference.md`, `docs/05_fluent_api_reference.md`, `docs/06_cookbook.md`, `docs/verification.md`, `.agents/skills/lib-db/references/parameters-and-binding.md`, `.agents/skills/lib-db/references/fluent-execution.md`: public contract updates.

## Task 1: Output Names, Snapshots, and Edge Guards

**Files:**
- Create: `Lib.Db/Execution/Output/OutputParameterName.cs`
- Create: `Lib.Db/Execution/Output/OutputParameterSnapshot.cs`
- Create: `Lib.Db/Execution/Output/SqlParameterCloneFactory.cs`
- Modify: `Lib.Db/Execution/Binding/DbBinder.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`

- [ ] **Step 1: Add failing canonical-name and guard tests**

Add these test methods to `MapperCoverageTests`:

```csharp
[Fact]
public void DictionarySqlMapper_ShouldRejectAmbiguousOutputTargetNames()
{
    var mapper = new DictionarySqlMapper(strict: true);
    var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["Total"] = 1,
        ["total"] = 2
    };

    using var command = new SqlCommand();
    command.Parameters.Add(new SqlParameter("@Total", SqlDbType.Int)
    {
        Direction = ParameterDirection.Output,
        Value = 3
    });

    Action act = () => mapper.MapOutputParameters(command, parameters);

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*ambiguous*Total*");
    parameters["Total"].Should().Be(1);
    parameters["total"].Should().Be(2);
}

[Fact]
public void DbBinder_ShouldRejectNonIntReturnValueParameter()
{
    using var command = new SqlCommand();
    var parameter = new SqlParameter("@ReturnValue", SqlDbType.BigInt)
    {
        Direction = ParameterDirection.ReturnValue
    };

    Action act = () => DbBinder.TryBindExplicitReturnValueParameter(command, "ReturnValue", parameter);

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*ReturnValue*SqlDbType.Int*");
    command.Parameters.Should().BeEmpty();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MapperCoverageTests"
```

Expected: FAIL because ambiguous name validation and non-`int` return-value guard are not implemented.

- [ ] **Step 3: Add name and clone helpers**

Create `OutputParameterName.cs`:

```csharp
#nullable enable

namespace Lib.Db.Execution.Output;

internal readonly record struct OutputParameterName(string Raw, string Canonical)
{
    public static OutputParameterName From(string raw)
    {
        string value = raw.StartsWith('@') ? raw[1..] : raw;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Output parameter name is empty.");

        return new OutputParameterName(raw, value);
    }

    public string SafeDisplay()
    {
        string canonical = Canonical.ReplaceLineEndings(" ");
        return canonical.Length <= 128 ? canonical : canonical[..128];
    }
}
```

Create `SqlParameterCloneFactory.cs` with these public internal entry points:

```csharp
#nullable enable

using Microsoft.Data.SqlClient;

namespace Lib.Db.Execution.Output;

internal static class SqlParameterCloneFactory
{
    public static SqlParameter CloneForCommand(SqlParameter source, string parameterName)
    {
        ValidateSupportedDirectionType(source);

        var clone = new SqlParameter
        {
            ParameterName = parameterName,
            Direction = source.Direction,
            SqlDbType = source.SqlDbType,
            Size = source.Size,
            Precision = source.Precision,
            Scale = source.Scale,
            TypeName = source.TypeName,
            UdtTypeName = source.UdtTypeName,
            SourceColumn = source.SourceColumn,
            SourceVersion = source.SourceVersion,
            IsNullable = source.IsNullable,
            Value = source.Value ?? DBNull.Value
        };

        return clone;
    }

    public static void ValidateSupportedDirectionType(SqlParameter parameter)
    {
        if (parameter.Direction == ParameterDirection.ReturnValue &&
            parameter.SqlDbType != SqlDbType.Int)
        {
            throw new InvalidOperationException(
                "ReturnValue parameters must use SqlDbType.Int or DbType.Int32.");
        }

        if (parameter.Direction is ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue &&
            parameter.SqlDbType is SqlDbType.Structured or SqlDbType.Text or SqlDbType.NText or SqlDbType.Image)
        {
            throw new InvalidOperationException(
                $"Unsupported output parameter type '{parameter.SqlDbType}' for '{parameter.ParameterName}'.");
        }
    }
}
```

- [ ] **Step 4: Route explicit binding through the clone factory**

In `DbBinder.TryBindExplicitParameter`, do not mutate and add the caller parameter directly. Create a command-bound clone and add that clone:

```csharp
SqlParameter clone = SqlParameterCloneFactory.CloneForCommand(parameter, meta.Name);
clone.Direction = ResolveExplicitParameterDirection(meta.Direction, clone.Direction);
clone.SqlDbType = meta.SqlDbType;
NormalizeExplicitParameterValue(clone, meta, strictCheck);
cmd.Parameters.Add(clone);
```

In `DbBinder.TryBindExplicitReturnValueParameter`, clone and validate the return parameter before adding it.

- [ ] **Step 5: Run tests to verify they pass**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MapperCoverageTests"
```

Expected: PASS for the new guard tests and existing mapper tests.

- [ ] **Step 6: Review and commit Task 1**

Run subagent reviews for this task: `@codex-security`, `$code-review`, `@code-reviewer`, `@code-simplifier`, `@verifier`, and `@db_reader`. Fix findings to zero.

Commit:

```powershell
git add Lib.Db/Execution/Output/OutputParameterName.cs Lib.Db/Execution/Output/OutputParameterSnapshot.cs Lib.Db/Execution/Output/SqlParameterCloneFactory.cs Lib.Db/Execution/Binding/DbBinder.cs Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs
git commit -m "feat: add output parameter guard primitives"
```

## Task 2: Two-Phase Copy-Back for DTO, Dictionary, DataRow, and SqlParameter

**Files:**
- Create: `Lib.Db/Execution/Output/OutputParameterCopyBack.cs`
- Modify: `Lib.Db/Execution/Binding/Mappers.cs`
- Modify: `Lib.Db/Contracts/Mapping/MappingContracts.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`

- [ ] **Step 1: Add failing rollback tests**

Add tests that prove partial mutation does not survive a failure:

```csharp
[Fact]
public void DataRowSqlMapper_ShouldRollbackWhenSecondOutputColumnFails()
{
    var table = new DataTable();
    table.Columns.Add("Good", typeof(int));
    table.Columns.Add("Bad", typeof(string)).MaxLength = 2;
    DataRow row = table.Rows.Add(1, "ok");

    using var command = new SqlCommand();
    command.Parameters.Add(new SqlParameter("@Good", SqlDbType.Int)
    {
        Direction = ParameterDirection.Output,
        Value = 10
    });
    command.Parameters.Add(new SqlParameter("@Bad", SqlDbType.NVarChar)
    {
        Direction = ParameterDirection.Output,
        Value = "too long"
    });

    var mapper = new DataRowSqlMapper(strict: true);

    Action act = () => mapper.MapOutputParameters(command, row);

    act.Should().Throw<InvalidOperationException>();
    row["Good"].Should().Be(1);
    row["Bad"].Should().Be("ok");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MapperCoverageTests"
```

Expected: FAIL because `DataRowSqlMapper.MapOutputParameters` is currently not implemented and other mappers write directly.

- [ ] **Step 3: Implement `OutputParameterCopyBack`**

Create a helper with one entry point per parameter shape:

```csharp
internal static class OutputParameterCopyBack
{
    public static void ToDictionary(SqlCommand command, Dictionary<string, object?> target, bool strict) { }
    public static void ToDataRow(SqlCommand command, DataRow target, bool strict) { }
    public static void ToObject<T>(SqlCommand command, T target, bool strict) { }
}
```

Fill each method with this algorithm:

```csharp
// 1. Build output parameter snapshot.
// 2. Build canonical target map and reject duplicates.
// 3. Validate all target writes.
// 4. Snapshot original target values.
// 5. Apply writes.
// 6. On any exception, restore original values and throw a redacted InvalidOperationException.
```

The implementation must skip `ParameterDirection.ReturnValue` for DTO, dictionary scalar replacement, and `DataRow` targets unless the target itself is an explicit caller-owned `SqlParameter`.

- [ ] **Step 4: Route mapper output calls through the helper**

Change the mapper methods:

```csharp
public void MapOutputParameters(SqlCommand cmd, Dictionary<string, object?> parameters)
    => OutputParameterCopyBack.ToDictionary(cmd, parameters, strict);

public void MapOutputParameters(SqlCommand cmd, DataRow parameters)
    => OutputParameterCopyBack.ToDataRow(cmd, parameters, strict);
```

For `ExpressionTreeMapper<T>` and `ReflectionParameterMapper<T>`, route through `OutputParameterCopyBack.ToObject(cmd, parameters, strict)`.

- [ ] **Step 5: Run targeted tests**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MapperCoverageTests"
```

Expected: PASS.

- [ ] **Step 6: Review and commit Task 2**

Run task reviews through `@codex-security`, `$code-review`, `@code-reviewer`, `@code-simplifier`, `@verifier`, and `@db_reader`; fix findings to zero.

Commit:

```powershell
git add Lib.Db/Execution/Output/OutputParameterCopyBack.cs Lib.Db/Execution/Binding/Mappers.cs Lib.Db/Contracts/Mapping/MappingContracts.cs Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs
git commit -m "feat: make output copy-back transactional"
```

## Task 3: Non-Reader Shared Output Lifecycle

**Files:**
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`

- [ ] **Step 1: Add failing non-reader API tests**

Add DB-backed tests for `ExecuteAsync`, `QuerySingleAsync`, and `ExecuteScalarAsync`:

```csharp
[Fact]
public async Task ExecuteScalarAsync_WithOutputParameters_ShouldPopulateAfterSuccess()
{
    var output = new SqlParameter("@OutputVal", SqlDbType.Int)
    {
        Direction = ParameterDirection.Output
    };
    var returnValue = new SqlParameter("@ReturnValue", SqlDbType.Int)
    {
        Direction = ParameterDirection.ReturnValue
    };

    var parameters = new Dictionary<string, object?>
    {
        ["InputVal"] = 7,
        ["OutputVal"] = output,
        ["ReturnValue"] = returnValue
    };

    DbResult<int?> result = await _db.Procedure("dbo.LibDb_OutputScalar")
        .With(parameters)
        .ExecuteScalarAsync<int?>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue();
    output.Value.Should().Be(14);
    returnValue.Value.Should().Be(0);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputParameterTests"
```

Expected: FAIL for scalar/single-row output propagation gaps.

- [ ] **Step 3: Move output completion into `ExecutePipelineAsync`**

In `SqlDbExecutor.ExecutePipelineAsync`, after `operation(cmd, token)` and all failure-capable callbacks/interceptors complete, call:

```csharp
_mapperFactory.GetMapper<TParams>().MapOutputParameters(cmd, request.Parameters);
```

Remove the separate `MapOutputParameters` call from `ExecuteNonQueryAsync` to prevent duplicate copy-back.

- [ ] **Step 4: Add no-copy-back failure coverage**

Add a unit or DB-backed test that makes a failure-capable interceptor throw after command execution and assert that output targets keep original values.

- [ ] **Step 5: Run targeted tests and build**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputParameterTests|FullyQualifiedName~MapperCoverageTests"
dotnet build Lib.Db/Lib.Db.csproj
```

Expected: PASS.

- [ ] **Step 6: Review and commit Task 3**

Run requested subagent reviews, fix findings to zero, then commit:

```powershell
git add Lib.Db/Execution/Executors/SqlDbExecutor.cs Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs Verification/projects/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs
git commit -m "feat: complete output lifecycle for non-reader APIs"
```

## Task 4: Reader Command Lease for QueryAsync

**Files:**
- Create: `Lib.Db/Execution/Output/DbCommandLease.cs`
- Modify: `Lib.Db/Contracts/Execution/StrategyAndInterceptionContracts.cs`
- Modify: `Lib.Db/Execution/Executors/Strategies.cs`
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/OutputCommandLeaseMemoryGuardTests.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs`

- [ ] **Step 1: Add failing streaming tests**

Add tests for full enumeration, clean early disposal, cancellation/read failure no-copy-back, and the fake-reader memory guard.

Memory guard test shape:

```csharp
[Fact]
public async Task QueryAsync_OutputLease_ShouldNotMaterializeAllRowsForEarlyDispose()
{
    var reader = new CountingDbDataReader(totalRows: 1_000_000);
    await using var lease = DbCommandLease.ForTest(reader, completeOutputs: () => { });

    await foreach (int _ in lease.AsAsyncEnumerable(_ => 1, TestContext.Current.CancellationToken))
        break;

    reader.ReadCount.Should().Be(1);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputCommandLeaseMemoryGuardTests|FullyQualifiedName~OutputParameterTests"
```

Expected: FAIL because no lease exists.

- [ ] **Step 3: Implement `DbCommandLease`**

Use these states:

```csharp
internal enum DbCommandLeaseState
{
    Active,
    FullyConsumed,
    EarlyDisposedCleanly,
    CommandFailed,
    ReadFailed,
    Canceled,
    TimedOut,
    DisposeFailed,
    OutputMapped,
    CompletionAlreadyAttempted
}
```

Implement `MarkFullyConsumed`, `MarkReadFailed`, `MarkCanceled`, `CompleteAsync`, and `DisposeAsync`. `CompleteAsync` must map outputs exactly once only from `FullyConsumed` or `EarlyDisposedCleanly`.

- [ ] **Step 4: Add output-aware strategy acquisition**

Add an internal strategy method that returns `DbCommandLease?` and keeps the connection/metrics ownership currently held by `MonitoredSqlDataReader`.

- [ ] **Step 5: Wire `QueryStreamCoreAsync` to the lease**

Replace bare reader disposal with lease state transitions:

```csharp
try
{
    while (await lease.Reader.ReadAsync(ct).ConfigureAwait(false))
        yield return mapper.MapResult(lease.Reader);

    lease.MarkFullyConsumed();
}
catch (OperationCanceledException)
{
    lease.MarkCanceled();
    throw;
}
catch
{
    lease.MarkReadFailed();
    throw;
}
finally
{
    await lease.DisposeAsync().ConfigureAwait(false);
}
```

- [ ] **Step 6: Run targeted tests**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputCommandLeaseMemoryGuardTests|FullyQualifiedName~OutputParameterTests"
dotnet build Lib.Db/Lib.Db.csproj
```

Expected: PASS.

- [ ] **Step 7: Review and commit Task 4**

Run requested subagent reviews, fix findings to zero, then commit:

```powershell
git add Lib.Db/Execution/Output/DbCommandLease.cs Lib.Db/Contracts/Execution/StrategyAndInterceptionContracts.cs Lib.Db/Execution/Executors/Strategies.cs Lib.Db/Execution/Executors/SqlDbExecutor.cs Verification/projects/Lib.Db.IntegrationTests/Unit/OutputCommandLeaseMemoryGuardTests.cs Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs
git commit -m "feat: add output command lease for streaming queries"
```

## Task 5: QueryMultiple Lease and Helper Late Failure Policy

**Files:**
- Modify: `Lib.Db/Execution/Executors/SqlGridReader.cs`
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/SqlGridReaderCoverageTests.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/MultipleResultExtensionsTests.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs`

- [ ] **Step 1: Add failing grid-reader output tests**

Add tests for raw `DisposeAsync`, helper completion, read failure no-copy-back, and repeated dispose exactly-once.

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~SqlGridReaderCoverageTests|FullyQualifiedName~MultipleResultExtensionsTests|FullyQualifiedName~OutputParameterTests"
```

Expected: FAIL for output-aware dispose behavior.

- [ ] **Step 3: Make `SqlGridReader` lease-backed**

Change the constructor to accept a lease:

```csharp
internal sealed class SqlGridReader(DbCommandLease lease, IMapperFactory mapperFactory) : IMultipleResultReader
{
    private DbDataReader Reader => lease.Reader;
}
```

Mark read/helper failures before disposing and call `lease.CompleteAsync()` only through `DisposeAsync`.

- [ ] **Step 4: Ensure helpers convert late failures to `DbResult`**

In multiple-result helper extensions, keep `await using` inside the `try` block so dispose/output-completion failures become failed `DbResult` values.

- [ ] **Step 5: Run targeted tests and build**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~SqlGridReaderCoverageTests|FullyQualifiedName~MultipleResultExtensionsTests|FullyQualifiedName~OutputParameterTests"
dotnet build Lib.Db/Lib.Db.csproj
```

Expected: PASS.

- [ ] **Step 6: Review and commit Task 5**

Run requested subagent reviews, fix findings to zero, then commit:

```powershell
git add Lib.Db/Execution/Executors/SqlGridReader.cs Lib.Db/Execution/Executors/SqlDbExecutor.cs Verification/projects/Lib.Db.IntegrationTests/Unit/SqlGridReaderCoverageTests.cs Verification/projects/Lib.Db.IntegrationTests/Unit/MultipleResultExtensionsTests.cs Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs
git commit -m "feat: complete output lifecycle for multiple results"
```

## Task 6: DB-Backed Edge Matrix

**Files:**
- Modify: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs`
- Modify: `Verification/scripts/Invoke-Tests.ps1`

- [ ] **Step 1: Add DB-backed stored procedure matrix tests**

Cover:

- output/input-output/return value before and after reader close
- full enumeration and clean early dispose
- cancellation/read failure no-copy-back
- structured/TVP output rejection
- legacy LOB output rejection
- non-`int` return-value rejection

- [ ] **Step 2: Run DB-backed targeted tests**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputParameterTests"
```

Expected: PASS when local SQL Server verification prerequisites are available.

- [ ] **Step 3: Update the verification script targeted group**

Ensure `Invoke-Tests.ps1` includes:

```powershell
$OutputFilters = @(
    'FullyQualifiedName~OutputParameterTests',
    'FullyQualifiedName~MapperCoverageTests',
    'FullyQualifiedName~SqlGridReaderCoverageTests',
    'FullyQualifiedName~MultipleResultExtensionsTests',
    'FullyQualifiedName~OutputCommandLeaseMemoryGuardTests'
)
```

- [ ] **Step 4: Review and commit Task 6**

Run requested subagent reviews, fix findings to zero, then commit:

```powershell
git add Verification/projects/Lib.Db.IntegrationTests/VerificationDb/OutputParameterTests.cs Verification/scripts/Invoke-Tests.ps1
git commit -m "test: add output parameter db edge matrix"
```

## Task 7: Documentation and Consumer Skill Updates

**Files:**
- Modify: `docs/03_api_reference.md`
- Modify: `docs/05_fluent_api_reference.md`
- Modify: `docs/06_cookbook.md`
- Modify: `docs/verification.md`
- Modify: `.agents/skills/lib-db/references/parameters-and-binding.md`
- Modify: `.agents/skills/lib-db/references/fluent-execution.md`

- [ ] **Step 1: Update API docs**

Document this availability table:

```markdown
| API | Output availability |
| --- | --- |
| ExecuteAsync | After successful command and failure-capable callbacks complete |
| QuerySingleAsync | After successful command, including no-row success |
| ExecuteScalarAsync | After successful command, including null/DBNull scalar success |
| QueryAsync | After full enumeration or clean enumerator disposal |
| QueryMultipleAsync | After clean DisposeAsync |
| ReadMultipleAsync helpers | After helper completion |
```

- [ ] **Step 2: Update skill references**

Add the same timing contract plus no-copy-back failure states, explicit `SqlParameter` clone-and-copy-back, return-value `int` guard, and `DataRow` strict behavior.

- [ ] **Step 3: Run docs verification**

Run:

```powershell
rg -n "copy-back|ReturnValue|DataRow|QueryMultipleAsync|QueryAsync" docs .agents/skills/lib-db
git diff --check -- docs .agents/skills/lib-db
```

Expected: no ambiguous wording, no whitespace errors.

- [ ] **Step 4: Review and commit Task 7**

Run requested subagent reviews, fix findings to zero, then commit:

```powershell
git add docs/03_api_reference.md docs/05_fluent_api_reference.md docs/06_cookbook.md docs/verification.md .agents/skills/lib-db/references/parameters-and-binding.md .agents/skills/lib-db/references/fluent-execution.md
git commit -m "docs: document output parameter lifecycle"
```

## Task 8: Final Verification and Release Readiness

**Files:**
- No planned source edits unless verification finds defects.

- [ ] **Step 1: Run minimum verification**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~OutputParameterTests|FullyQualifiedName~MapperCoverageTests|FullyQualifiedName~SqlGridReaderCoverageTests|FullyQualifiedName~MultipleResultExtensionsTests|FullyQualifiedName~RuntimeTvpBindingTests|FullyQualifiedName~OutputCommandLeaseMemoryGuardTests"
dotnet build Lib.Db/Lib.Db.csproj
```

Expected: PASS.

- [ ] **Step 2: Run extended verification when local SQL Server is available**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File Verification/scripts/Invoke-Tests.ps1
```

Expected: PASS. If local SQL Server prerequisites are unavailable, record the skipped DB-backed gate in the final report.

- [ ] **Step 3: Run mandatory reviews**

Run subagent reviews for the final diff:

- `@codex-security`: diff scan for command lease, reader disposal, copy-back, redaction, and explicit parameter handling
- `$code-review` / `@code-reviewer`: correctness, regression, test adequacy
- `@code-simplifier`: clarity and unnecessary complexity
- `@verifier`: evidence and gate adequacy
- `@db_reader`: SQL Server contract consistency

Fix findings to zero.

- [ ] **Step 4: Commit final verification fixes**

If Step 3 requires edits, run `git status --short`, stage only the files changed by the final verification fixes, and commit them:

```powershell
git status --short
git commit -m "fix: close output lifecycle review findings"
```

If Step 3 requires no edits, do not create an empty commit.

## Self-Review

Spec coverage:

- Non-reader output lifecycle: Task 3.
- Reader command lease and no buffering: Task 4.
- QueryMultiple and helper late failures: Task 5.
- DataRow and transactional reverse mapping: Task 2.
- Explicit `SqlParameter` clone/copy-back: Task 1 and Task 2.
- Unsupported SQL Server edge guards: Task 1 and Task 6.
- Return-value `int` guard: Task 1 and Task 6.
- Docs and skills: Task 7.
- Final reviews and tests: Task 8.

Placeholder scan:

- No unresolved placeholder markers are intentionally present.
- Every task has exact files, commands, expected outcomes, and commit messages.

Type consistency:

- The plan uses `DbCommandLease`, `OutputParameterCopyBack`, `SqlParameterCloneFactory`, and `OutputCommandLeaseMemoryGuardTests` consistently across tasks.
