# Lib.Db v2.4.0 AOT-Safe Bulk Mutations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add AOT-safe bulk insert, update, delete, upsert, and merge APIs without using reflection, runtime code generation, or SQL Server `MERGE` as the default engine.

**Architecture:** Add a static shape model, AOT-safe data reader, identifier-safe SQL builder, and staged bulk executor. Insert uses `SqlBulkCopy` directly; update/delete/upsert/merge bulk-copy into a local temp table and execute deterministic set-based DML inside one local SQL transaction.

**Tech Stack:** .NET 10, C# 14 preview syntax already used by the repo, Microsoft.Data.SqlClient, SQL Server local verification DB, xUnit v3, FluentAssertions, existing `DbResult<T>` and `IDbSession` patterns.

---

## Implementation Status

The user approved the staged-DML design and later requested implementation planning hardening before source changes. This plan is ready for an implementation session only after the pre-implementation baseline gate below passes. It is the authoritative bulk sub-plan, but implementation must also satisfy the integrated orchestration plan listed below.

## Reviewed Spec

Spec: `docs/superpowers/specs/2026-05-22-v240-aot-safe-bulk-mutations-design.md`

Parent integration:

- `docs/superpowers/specs/2026-05-22-v240-integrated-additional-scope-design.md`
- `docs/superpowers/plans/2026-05-22-v240-integrated-additional-scope-implementation.md`

The integrated plan owns v2.4.0 release orchestration, documentation updates, HybridCache tags, typed QueryMultiple, release gates, and security review. This sub-plan owns the detailed AOT-safe bulk implementation and test contract.

The implementation must preserve these decisions:

- Existing reflection-based `BulkInsertAsync<T>` remains for compatibility.
- New AOT-safe overloads require `BulkShape<T>`.
- `BulkMergeAsync` is API-level merge implemented with staged DML, not SQL Server `MERGE` by default.
- `DeleteNotMatchedBySource` is rejected in v2.4.0.
- Non-insert mutation operations require at least one key column.
- Staged operations run in one local `SqlTransaction` by default.
- Identifiers are validated and bracket-quoted.
- Row values are never interpolated into SQL command text.
- AOT verification must not gain new Lib.Db trim/AOT warnings.

## Pre-Implementation Baseline Gate

Before source changes begin, capture the current verification baseline. If this
gate fails, stop and classify the failure as pre-existing instead of mixing it
with the bulk implementation.

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: no new Lib.Db trim/AOT warnings compared with the current baseline.
Record the warning count summary and whether only the known provider warnings
remain.

Then run:

```powershell
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = "Verification/artifacts/logs/v240-bulk-preimplementation-release-verification-$stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$verificationOutput = & pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short *>&1
$exitCode = $LASTEXITCODE
$verificationOutput | Tee-Object -FilePath $log
$postLogExitCode = 0
if (-not (Test-Path -LiteralPath $log) -or (Get-Item -LiteralPath $log).Length -eq 0) {
    Write-Warning "Release verification log was not created or is empty: $log"
    $postLogExitCode = 1
}
pwsh -NoProfile -File Verification/scripts/Scan-VerificationArtifacts.ps1 -Paths $log
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
pwsh -NoProfile -File Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
if ($exitCode -ne 0) { exit $exitCode }
if ($postLogExitCode -ne 0) { exit $postLogExitCode }
```

Expected: release verification passes, the durable log exists and is non-empty,
the log-specific artifact scan passes, and generated artifacts remain
ignored/untracked.

## File Structure

Create:

- `Lib.Db/Execution/Bulk/BulkShape.cs`
  Public shape entry point, immutable `BulkShape<T>`, and `BulkShapeBuilder<T>`.

- `Lib.Db/Execution/Bulk/BulkColumn.cs`
  Immutable column metadata and getter delegate wrapper.

- `Lib.Db/Execution/Bulk/BulkShapeDataReader.cs`
  Internal `DbDataReader` that streams `IEnumerable<T>` through `BulkShape<T>` without reflection. It must not become a public API surface; existing `InternalsVisibleTo` entries cover tests and AOT verification.

- `Lib.Db/Execution/Bulk/BulkIdentifier.cs`
  Internal parser/renderer for safe two-part table names and bracket-quoted identifiers.

- `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs`
  Internal SQL type renderer from shape metadata.

- `Lib.Db/Execution/Bulk/BulkStagingSqlBuilder.cs`
  Internal SQL builder for temp table DDL and staged DML.

- `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs`
  Internal connection/transaction orchestration, `SqlBulkCopy`, staging, DML execution, and cleanup.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkShapeTests.cs`
  Unit tests for shape validation and metadata.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkShapeDataReaderTests.cs`
  Unit tests for streaming reader behavior.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkSqlBuilderTests.cs`
  Unit tests for identifier parsing, SQL type rendering, and staged SQL generation.

- `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/BulkMutationTests.cs`
  Integration tests for insert/update/delete/upsert/merge.

Modify:

- `Lib.Db/Contracts/Core/Primitives.cs`
  Add `BulkWriteOptions`, `BulkMergeOptions`, `BulkMergeActions`, `BulkUpsertResult`, and `BulkMergeResult`.

- `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
  Add AOT-safe overloads to `IDbSession`.

- `Lib.Db/Core/DbSession.cs`
  Delegate new public methods to `BulkWriteExecutor`.

- `Verification/projects/Lib.Db.IntegrationTests/Infrastructure/SchemaInitializer.cs`
  Add verification table for bulk mutation tests.

- `Verification/projects/Lib.Db.AotVerification/Program.cs`
  Add no-DB AOT smoke for `BulkShape<T>` and `BulkShapeDataReader<T>`.

- `Verification/scripts/Assert-Coverage.ps1`
  Add new bulk classes to the targeted coverage list if the coverage gate expects explicit prefixes.

- `docs/02_advanced.md`
  Add AOT-safe bulk mutation guide.

- `docs/03_api_reference.md`
  Add API reference entries.

- `docs/05_fluent_api_reference.md`
  Add session method list entries.

- `docs/06_cookbook.md`
  Add practical examples.

- `docs/history.md`
  Add v2.4.0 release note.

---

### Task 1: Add Public Contract Tests for Shape and Options

**Files:**
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkShapeTests.cs`
- Create during this task: `Lib.Db/Execution/Bulk/BulkShape.cs`
- Create during this task: `Lib.Db/Execution/Bulk/BulkColumn.cs`
- Modify in Task 2: `Lib.Db/Contracts/Core/Primitives.cs`

- [ ] **Step 1: Write failing tests for basic shape creation**

Create `BulkShapeTests.cs` with these tests:

```csharp
using System.Collections;
using System.Data;
using FluentAssertions;
using Lib.Db.Execution.Bulk;
using Xunit;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class BulkShapeTests
{
    [Fact]
    public void BulkShape_ShouldBuildColumnsAndKeysWithoutReflection()
    {
        BulkShape<BulkShapeRow> shape = BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        shape.Columns.Should().HaveCount(3);
        shape.KeyColumns.Should().ContainSingle(column => column.DestinationName == "Id");
        shape.WritableColumns.Should().Contain(column => column.DestinationName == "Name");
        shape.WritableColumns.Should().Contain(column => column.DestinationName == "Price");
    }

    [Fact]
    public void Build_ShouldRejectEmptyShape()
    {
        Action act = () => BulkShape.For<BulkShapeRow>().Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one column*");
    }

    [Fact]
    public void Build_ShouldRejectDuplicateDestinationColumns()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("id", SqlDbType.Int, static row => row.Id)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*Id*");
    }

    [Fact]
    public void Build_ShouldRejectNullableKeyColumns()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id, nullable: true)
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*key*Id*non-null*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Sku Name")]
    [InlineData("Sku;DROP")]
    [InlineData("Sku--Comment")]
    public void Build_ShouldRejectUnsafeDestinationColumnNames(string destinationName)
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column(destinationName, SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Build();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_ShouldRejectDestinationColumnNamesLongerThanSysname()
    {
        string tooLong = new('A', 129);

        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column(tooLong, SqlDbType.NVarChar, static row => row.Name, size: 100, nullable: false)
            .Build();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*128*");
    }

    [Fact]
    public void Column_ShouldRejectUnsupportedSqlDbTypeBeforeBuild()
    {
        Action act = () => BulkShape.For<BulkShapeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Payload", SqlDbType.Structured, static row => row.Name);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Structured*not supported*");
    }

    private sealed record BulkShapeRow(int Id, string Name, decimal Price);
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
```

Expected: build fails because `Lib.Db.Execution.Bulk.BulkShape` does not exist.

- [ ] **Step 3: Add minimal shape model**

Create `Lib.Db/Execution/Bulk/BulkColumn.cs`:

```csharp
using System.Data;
using System.Globalization;

namespace Lib.Db.Execution.Bulk;

public sealed class BulkColumn<T>
{
    internal BulkColumn(
        int ordinal,
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, object?> getter,
        bool isKey,
        bool nullable,
        int? size,
        byte? precision,
        byte? scale)
    {
        Ordinal = ordinal;
        DestinationName = destinationName;
        SqlDbType = sqlDbType;
        Getter = getter;
        IsKey = isKey;
        Nullable = nullable;
        Size = size;
        Precision = precision;
        Scale = scale;
    }

    public int Ordinal { get; }
    public string DestinationName { get; }
    public SqlDbType SqlDbType { get; }
    public bool IsKey { get; }
    public bool Nullable { get; }
    public int? Size { get; }
    public byte? Precision { get; }
    public byte? Scale { get; }
    internal Func<T, object?> Getter { get; }
}
```

Create a metadata-based value converter helper in the same namespace. It may use `typeof(TValue)` once while the shape is being built, but the reader must not call `value.GetType()` or `Enum.GetUnderlyingType(...)` per row:

```csharp
internal static class BulkValueConverter
{
    public static Func<TValue, object?> Create<TValue>(SqlDbType sqlDbType)
    {
        Type valueType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        if (valueType == typeof(DateOnly) && sqlDbType == SqlDbType.Date)
            return static value => value is null ? null : ((DateOnly)(object)value).ToDateTime(TimeOnly.MinValue);

        if (valueType == typeof(TimeOnly) && sqlDbType == SqlDbType.Time)
            return static value => value is null ? null : ((TimeOnly)(object)value).ToTimeSpan();

        if (valueType.IsEnum)
        {
            Type underlyingType = Enum.GetUnderlyingType(valueType);
            return value => value is null
                ? null
                : Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
        }

        return static value => value;
    }
}
```

Create `Lib.Db/Execution/Bulk/BulkShape.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Data;

namespace Lib.Db.Execution.Bulk;

public static class BulkShape
{
    public static BulkShapeBuilder<T> For<T>() where T : notnull => new();
}

public sealed class BulkShape<T> where T : notnull
{
    internal BulkShape(IReadOnlyList<BulkColumn<T>> columns)
    {
        Columns = new ReadOnlyCollection<BulkColumn<T>>(columns.ToArray());
        KeyColumns = new ReadOnlyCollection<BulkColumn<T>>(columns.Where(column => column.IsKey).ToArray());
        WritableColumns = new ReadOnlyCollection<BulkColumn<T>>(columns.Where(column => !column.IsKey).ToArray());
    }

    public IReadOnlyList<BulkColumn<T>> Columns { get; }
    public IReadOnlyList<BulkColumn<T>> KeyColumns { get; }
    public IReadOnlyList<BulkColumn<T>> WritableColumns { get; }
}

public sealed class BulkShapeBuilder<T> where T : notnull
{
    private const int MaxSqlIdentifierLength = 128;
    private readonly List<BulkColumn<T>> _columns = [];

    public BulkShapeBuilder<T> Key<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool nullable = false,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
        => Add(destinationName, sqlDbType, CreateGetter(getter, sqlDbType), isKey: true, nullable, size, precision, scale);

    public BulkShapeBuilder<T> Column<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool nullable = true,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
        => Add(destinationName, sqlDbType, CreateGetter(getter, sqlDbType), isKey: false, nullable, size, precision, scale);

    public BulkShape<T> Build()
    {
        if (_columns.Count == 0)
            throw new InvalidOperationException("Bulk shape must contain at least one column.");

        string? duplicate = _columns
            .GroupBy(column => column.DestinationName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        if (duplicate is not null)
            throw new InvalidOperationException($"Bulk shape contains duplicate destination column '{duplicate}'.");

        BulkColumn<T>? nullableKey = _columns.FirstOrDefault(static column => column.IsKey && column.Nullable);
        if (nullableKey is not null)
            throw new InvalidOperationException($"Bulk key column '{nullableKey.DestinationName}' must be non-null.");

        return new BulkShape<T>(_columns);
    }

    private BulkShapeBuilder<T> Add(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, object?> getter,
        bool isKey,
        bool nullable,
        int? size,
        byte? precision,
        byte? scale)
    {
        ValidateDestinationColumnName(destinationName);
        ValidateSqlDbType(sqlDbType);

        _columns.Add(new BulkColumn<T>(
            _columns.Count,
            destinationName,
            sqlDbType,
            getter,
            isKey,
            nullable,
            size,
            precision,
            scale));

        return this;
    }

    private static Func<T, object?> CreateGetter<TValue>(Func<T, TValue> getter, SqlDbType sqlDbType)
    {
        Func<TValue, object?> converter = BulkValueConverter.Create<TValue>(sqlDbType);
        return row => converter(getter(row));
    }

    private static void ValidateDestinationColumnName(string destinationName)
    {
        if (string.IsNullOrWhiteSpace(destinationName))
            throw new ArgumentException("Destination column name cannot be empty.", nameof(destinationName));

        if (destinationName.Length > MaxSqlIdentifierLength)
            throw new ArgumentException("Destination column names cannot exceed 128 characters.", nameof(destinationName));

        if (destinationName.Any(char.IsWhiteSpace)
            || destinationName.Contains(';', StringComparison.Ordinal)
            || destinationName.Contains("--", StringComparison.Ordinal)
            || destinationName.Contains("/*", StringComparison.Ordinal)
            || destinationName.Contains("*/", StringComparison.Ordinal)
            || destinationName.Contains('[')
            || destinationName.Contains(']'))
        {
            throw new ArgumentException("Destination column name contains unsupported SQL identifier syntax.", nameof(destinationName));
        }
    }

    private static void ValidateSqlDbType(SqlDbType sqlDbType)
    {
        if (!IsSupportedSqlDbType(sqlDbType))
            throw new ArgumentOutOfRangeException(nameof(sqlDbType), sqlDbType, $"SqlDbType '{sqlDbType}' is not supported by AOT-safe bulk shapes.");
    }

    private static bool IsSupportedSqlDbType(SqlDbType sqlDbType)
        => sqlDbType is SqlDbType.Bit
            or SqlDbType.TinyInt
            or SqlDbType.SmallInt
            or SqlDbType.Int
            or SqlDbType.BigInt
            or SqlDbType.UniqueIdentifier
            or SqlDbType.Date
            or SqlDbType.Time
            or SqlDbType.DateTime2
            or SqlDbType.DateTimeOffset
            or SqlDbType.Decimal
            or SqlDbType.NVarChar
            or SqlDbType.VarChar
            or SqlDbType.VarBinary;
}
```

- [ ] **Step 4: Run the tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
```

Expected: tests pass.

### Task 2: Add Options and Public Interface Contracts

**Files:**
- Modify: `Lib.Db/Contracts/Core/Primitives.cs`
- Modify: `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
- Modify in Task 6: `Lib.Db/Core/DbSession.cs`

- [ ] **Step 1: Add compile-facing tests by extending `BulkShapeTests`**

Append these tests to `BulkShapeTests.cs`:

```csharp
[Fact]
public void BulkWriteOptions_ShouldExposeSafeDefaults()
{
    BulkWriteOptions options = new();

    options.BatchSize.Should().Be(5_000);
    options.TimeoutSeconds.Should().Be(600);
    options.EnableStreaming.Should().BeTrue();
    options.UseTransaction.Should().BeTrue();
    options.CheckConstraints.Should().BeTrue();
}

[Fact]
public void BulkWriteOptions_ShouldNotRejectTransactionOptOutInScalarValidation()
{
    BulkWriteOptions options = new() { UseTransaction = false };

    Action act = () => options.Validate();

    act.Should().NotThrow();
    options.UseTransaction.Should().BeFalse();
}

[Theory]
[InlineData(0)]
[InlineData(-1)]
public void BulkWriteOptions_ShouldRejectInvalidBatchSize(int batchSize)
{
    BulkWriteOptions options = new() { BatchSize = batchSize };

    Action act = () => options.Validate();

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*batch size*greater than zero*");
}

[Theory]
[InlineData(0)]
[InlineData(-1)]
public void BulkWriteOptions_ShouldRejectInvalidTimeout(int timeoutSeconds)
{
    BulkWriteOptions options = new() { TimeoutSeconds = timeoutSeconds };

    Action act = () => options.Validate();

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*timeout*greater than zero*");
}

[Fact]
public void BulkMergeOptions_ShouldRejectDeleteNotMatchedBySourceInV240()
{
    BulkWriteOptions options = new BulkMergeOptions
    {
        Actions = BulkMergeActions.DeleteNotMatchedBySource
    };

    Action act = () => options.Validate();

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*DeleteNotMatchedBySource*not supported*v2.4.0*");
}

[Theory]
[InlineData(BulkMergeActions.UpdateMatched | BulkMergeActions.DeleteMatched)]
[InlineData(BulkMergeActions.InsertMissing | BulkMergeActions.DeleteMatched)]
[InlineData(BulkMergeActions.UpdateMatched | BulkMergeActions.InsertMissing | BulkMergeActions.DeleteMatched)]
public void BulkMergeOptions_ShouldRejectDeleteMatchedWithOtherActions(BulkMergeActions actions)
{
    BulkWriteOptions options = new BulkMergeOptions { Actions = actions };

    Action act = () => options.Validate();

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*DeleteMatched*exclusive*");
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
```

Expected: build fails because `BulkWriteOptions`, `BulkMergeOptions`, and `BulkMergeActions` do not exist.

- [ ] **Step 3: Add option/result contracts**

In `Lib.Db/Contracts/Core/Primitives.cs`, after `BulkInsertOptions`, add:

```csharp
public class BulkWriteOptions
{
    public int BatchSize { get; init; } = 5_000;
    public int TimeoutSeconds { get; init; } = 600;
    public bool EnableStreaming { get; init; } = true;
    public bool UseTransaction { get; init; } = true;
    public bool FireTriggers { get; init; }
    public bool CheckConstraints { get; init; } = true;
    public bool KeepIdentity { get; init; }

    public virtual void Validate()
    {
        if (BatchSize <= 0)
            throw new InvalidOperationException("Bulk batch size must be greater than zero.");

        if (TimeoutSeconds <= 0)
            throw new InvalidOperationException("Bulk timeout seconds must be greater than zero.");
    }
}

[Flags]
public enum BulkMergeActions
{
    None = 0,
    UpdateMatched = 1,
    InsertMissing = 2,
    DeleteMatched = 4,
    DeleteNotMatchedBySource = 8
}

public sealed class BulkMergeOptions : BulkWriteOptions
{
    public BulkMergeActions Actions { get; init; } =
        BulkMergeActions.UpdateMatched | BulkMergeActions.InsertMissing;

    public override void Validate()
    {
        base.Validate();

        if (Actions == BulkMergeActions.None)
            throw new InvalidOperationException("Bulk merge actions cannot be empty.");

        if ((Actions & BulkMergeActions.DeleteNotMatchedBySource) != 0)
            throw new InvalidOperationException("DeleteNotMatchedBySource is not supported by Lib.Db v2.4.0 bulk merge.");

        if ((Actions & BulkMergeActions.DeleteMatched) != 0
            && Actions != BulkMergeActions.DeleteMatched)
        {
            throw new InvalidOperationException("DeleteMatched is exclusive in Lib.Db v2.4.0 bulk merge.");
        }
    }
}

public readonly record struct BulkUpsertResult(long Inserted, long Updated)
{
    public long TotalAffected => Inserted + Updated;
}

public readonly record struct BulkMergeResult(long Inserted, long Updated, long Deleted)
{
    public long TotalAffected => Inserted + Updated + Deleted;
}
```

`BulkWriteOptions.Validate()` validates scalar option values only. Operation-level
bulk executors own the transaction-safety contract:

- `BulkInsertAsync` may accept `UseTransaction = false` as an explicit
  performance opt-out.
- `BulkUpdateAsync`, `BulkDeleteAsync`, `BulkUpsertAsync`, and `BulkMergeAsync`
  must reject `UseTransaction = false` before opening a connection in v2.4.0.
- Any future relaxation of this rule requires a new design entry and tests that
  document non-atomic partial-write semantics.

- [ ] **Step 4: Keep `IDbSession` unchanged in this task**

Do not modify `Lib.Db/Contracts/Entry/DbEntryContracts.cs` yet. Adding interface methods before their implementations would break every later test task. The public signatures to add during Tasks 6, 7, and 8 are:

```csharp
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<long>> BulkUpdateAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<long>> BulkDeleteAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<BulkUpsertResult>> BulkUpsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<BulkMergeResult>> BulkMergeAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkMergeOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;
```

- [ ] **Step 5: Run options and shape tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
```

Expected: tests pass. Public `IDbSession` methods are added only in the task that implements each operation.

### Task 3: Add AOT-Safe Reader

**Files:**
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkShapeDataReaderTests.cs`
- Create: `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs`
- Create: `Lib.Db/Execution/Bulk/BulkShapeDataReader.cs`

- [ ] **Step 1: Write reader tests**

Create `BulkShapeDataReaderTests.cs`:

```csharp
using System.Collections;
using System.Data;
using FluentAssertions;
using Lib.Db.Execution.Bulk;
using Xunit;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class BulkShapeDataReaderTests
{
    [Fact]
    public void Read_ShouldStreamRowsThroughShapeColumns()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();

        using BulkShapeDataReader<BulkReaderRow> reader = new(
            [new BulkReaderRow(7, "SKU-7", new DateOnly(2026, 5, 22))],
            shape);

        reader.FieldCount.Should().Be(3);
        reader.GetName(0).Should().Be("Id");
        reader.Read().Should().BeTrue();
        reader.GetValue(0).Should().Be(7);
        reader.GetValue(1).Should().Be("SKU-7");
        reader.GetValue(2).Should().Be(new DateTime(2026, 5, 22));
        reader.GetFieldType(2).Should().Be(typeof(DateTime));
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void Read_ShouldNormalizeTimeOnlyAndEnums()
    {
        BulkShape<BulkTimeRow> shape = BulkShape.For<BulkTimeRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("StartsAt", SqlDbType.Time, static row => row.StartsAt, scale: 7)
            .Column("Status", SqlDbType.Int, static row => row.Status)
            .Build();

        using BulkShapeDataReader<BulkTimeRow> reader = new(
            [new BulkTimeRow(9, new TimeOnly(14, 30, 15), BulkStatus.Active)],
            shape);

        reader.Read().Should().BeTrue();
        reader.GetValue(1).Should().Be(new TimeSpan(14, 30, 15));
        reader.GetFieldType(1).Should().Be(typeof(TimeSpan));
        reader.GetValue(2).Should().Be(1);
    }

    [Fact]
    public void Read_ShouldPreserveProviderCompatibleScalarAndNullableValues()
    {
        Guid token = Guid.NewGuid();
        byte[] payload = [1, 2, 3];
        BulkShape<BulkProviderValueRow> shape = BulkShape.For<BulkProviderValueRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Token", SqlDbType.UniqueIdentifier, static row => row.Token)
            .Column("Amount", SqlDbType.Decimal, static row => row.Amount, precision: 18, scale: 2)
            .Column("Payload", SqlDbType.VarBinary, static row => row.Payload, size: 16)
            .Column("OptionalQty", SqlDbType.Int, static row => row.OptionalQty, nullable: true)
            .Build();

        using BulkShapeDataReader<BulkProviderValueRow> reader = new(
            [new BulkProviderValueRow(1, token, 12.34m, payload, null)],
            shape);

        reader.Read().Should().BeTrue();
        reader.GetValue(1).Should().Be(token);
        reader.GetFieldType(1).Should().Be(typeof(Guid));
        reader.GetValue(2).Should().Be(12.34m);
        reader.GetFieldType(2).Should().Be(typeof(decimal));
        reader.GetValue(3).Should().BeSameAs(payload);
        reader.GetFieldType(3).Should().Be(typeof(byte[]));
        reader.GetValue(4).Should().Be(DBNull.Value);
    }

    [Fact]
    public void GetValue_ShouldRejectNullForNonNullableColumn()
    {
        BulkShape<BulkNullableRow> shape = BulkShape.For<BulkNullableRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 50, nullable: false)
            .Build();

        using BulkShapeDataReader<BulkNullableRow> reader = new(
            [new BulkNullableRow(1, null)],
            shape);

        reader.Read().Should().BeTrue();

        Action act = () => reader.GetValue(1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Name*null*");
    }

    [Fact]
    public void Close_ShouldSetIsClosedAndDisposeUnderlyingEnumeratorOnce()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();
        var rows = new DisposableRows<BulkReaderRow>(
            [new BulkReaderRow(7, "SKU-7", new DateOnly(2026, 5, 22))]);

        BulkShapeDataReader<BulkReaderRow> reader = new(rows, shape);
        reader.IsClosed.Should().BeFalse();

        reader.Read().Should().BeTrue();
        reader.Close();
        reader.IsClosed.Should().BeTrue();
        reader.Dispose();

        rows.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_ShouldSetIsClosedAndDisposeUnderlyingEnumeratorOnce()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();
        var rows = new DisposableRows<BulkReaderRow>(
            [new BulkReaderRow(7, "SKU-7", new DateOnly(2026, 5, 22))]);

        BulkShapeDataReader<BulkReaderRow> reader = new(rows, shape);

        reader.Read().Should().BeTrue();
        reader.Dispose();
        reader.IsClosed.Should().BeTrue();
        reader.Dispose();

        rows.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void HasRows_ShouldBufferWithoutSkippingFirstRow()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();

        using BulkShapeDataReader<BulkReaderRow> reader = new(
            [new BulkReaderRow(7, "SKU-7", new DateOnly(2026, 5, 22))],
            shape);

        reader.HasRows.Should().BeTrue();
        reader.Read().Should().BeTrue();
        reader.GetValue(1).Should().Be("SKU-7");
        reader.Read().Should().BeFalse();
        reader.HasRows.Should().BeTrue();
    }

    [Fact]
    public void Read_ShouldClearCurrentWhenEndIsReached()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();

        using BulkShapeDataReader<BulkReaderRow> reader = new(
            [new BulkReaderRow(7, "SKU-7", new DateOnly(2026, 5, 22))],
            shape);

        reader.Read().Should().BeTrue();
        reader.Read().Should().BeFalse();

        Action act = () => reader.GetValue(1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Read*");
    }

    [Fact]
    public void GetOrdinal_ShouldThrowWhenColumnIsMissing()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();

        using BulkShapeDataReader<BulkReaderRow> reader = new([], shape);

        Action act = () => reader.GetOrdinal("MissingColumn");

        act.Should().Throw<IndexOutOfRangeException>()
            .WithMessage("*MissingColumn*");
    }

    [Fact]
    public void HasRows_ShouldReturnFalseForEmptyRows()
    {
        BulkShape<BulkReaderRow> shape = BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();

        using BulkShapeDataReader<BulkReaderRow> reader = new([], shape);

        reader.HasRows.Should().BeFalse();
        reader.Read().Should().BeFalse();
    }

    private sealed record BulkReaderRow(int Id, string Sku, DateOnly CreatedOn);
    private sealed record BulkTimeRow(int Id, TimeOnly StartsAt, BulkStatus Status);
    private sealed record BulkProviderValueRow(int Id, Guid Token, decimal Amount, byte[] Payload, int? OptionalQty);
    private sealed record BulkNullableRow(int Id, string? Name);
    private enum BulkStatus { Inactive = 0, Active = 1 }

    private sealed class DisposableRows<T>(IReadOnlyList<T> rows) : IEnumerable<T>
    {
        public int DisposeCount { get; private set; }

        public IEnumerator<T> GetEnumerator() => new Enumerator(this, rows);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(DisposableRows<T> owner, IReadOnlyList<T> rows) : IEnumerator<T>
        {
            private int _index = -1;
            public T Current => rows[_index];
            object IEnumerator.Current => Current!;
            public bool MoveNext() => ++_index < rows.Count;
            public void Reset() => _index = -1;
            public void Dispose() => owner.DisposeCount++;
        }
    }
}
```

- [ ] **Step 2: Run reader tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeDataReaderTests*"
```

Expected: build fails because `BulkShapeDataReader<T>` does not exist.

- [ ] **Step 3: Implement minimal renderer dependency and reader**

Create `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs` before the reader so this task reaches a real GREEN state without waiting for Task 4:

```csharp
using System.Data;

namespace Lib.Db.Execution.Bulk;

internal static class BulkSqlTypeRenderer
{
    public static bool IsSupported(SqlDbType sqlDbType)
        => sqlDbType is SqlDbType.Bit
            or SqlDbType.TinyInt
            or SqlDbType.SmallInt
            or SqlDbType.Int
            or SqlDbType.BigInt
            or SqlDbType.UniqueIdentifier
            or SqlDbType.Date
            or SqlDbType.Time
            or SqlDbType.DateTime2
            or SqlDbType.DateTimeOffset
            or SqlDbType.Decimal
            or SqlDbType.NVarChar
            or SqlDbType.VarChar
            or SqlDbType.VarBinary;

    public static Type GetFieldType<T>(BulkColumn<T> column) where T : notnull
        => column.SqlDbType switch
        {
            SqlDbType.Bit => typeof(bool),
            SqlDbType.TinyInt => typeof(byte),
            SqlDbType.SmallInt => typeof(short),
            SqlDbType.Int => typeof(int),
            SqlDbType.BigInt => typeof(long),
            SqlDbType.UniqueIdentifier => typeof(Guid),
            SqlDbType.Date => typeof(DateTime),
            SqlDbType.Time => typeof(TimeSpan),
            SqlDbType.DateTime2 => typeof(DateTime),
            SqlDbType.DateTimeOffset => typeof(DateTimeOffset),
            SqlDbType.Decimal => typeof(decimal),
            SqlDbType.NVarChar or SqlDbType.VarChar => typeof(string),
            SqlDbType.VarBinary => typeof(byte[]),
            _ => throw new NotSupportedException($"SqlDbType '{column.SqlDbType}' is not supported by AOT-safe bulk shapes.")
        };
}
```

Create `Lib.Db/Execution/Bulk/BulkShapeDataReader.cs`:

```csharp
using System.Collections;
using System.Data;
using System.Data.Common;

namespace Lib.Db.Execution.Bulk;

internal sealed class BulkShapeDataReader<T> : DbDataReader where T : notnull
{
    private readonly IEnumerator<T> _enumerator;
    private readonly IReadOnlyList<BulkColumn<T>> _columns;
    private T? _current;
    private T? _bufferedRow;
    private bool _hasCurrent;
    private bool _hasBufferedRow;
    private bool _hasRowsKnown;
    private bool _hasRows;
    private bool _closed;

    public BulkShapeDataReader(IEnumerable<T> rows, BulkShape<T> shape, IReadOnlyList<BulkColumn<T>>? columns = null)
    {
        _enumerator = rows.GetEnumerator();
        _columns = columns ?? shape.Columns;
    }

    public override int FieldCount => _columns.Count;
    public long RowsRead { get; private set; }
    public override bool HasRows
    {
        get
        {
            if (_closed)
                return false;

            if (!_hasRowsKnown)
                BufferFirstRow();

            return _hasRows;
        }
    }

    public override bool IsClosed => _closed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;

    public override bool Read()
    {
        if (_closed)
            return false;

        if (_hasBufferedRow)
        {
            _current = _bufferedRow;
            _hasCurrent = true;
            _bufferedRow = default;
            _hasBufferedRow = false;
            RowsRead++;
            return true;
        }

        if (!_enumerator.MoveNext())
        {
            _current = default;
            _hasCurrent = false;
            _hasRowsKnown = true;
            if (RowsRead == 0)
                _hasRows = false;
            return false;
        }

        _current = _enumerator.Current;
        _hasCurrent = true;
        _hasRowsKnown = true;
        _hasRows = true;
        RowsRead++;
        return true;
    }

    private void BufferFirstRow()
    {
        if (_enumerator.MoveNext())
        {
            _bufferedRow = _enumerator.Current;
            _hasBufferedRow = true;
            _hasRows = true;
        }
        else
        {
            _hasRows = false;
        }

        _hasRowsKnown = true;
    }

    public override string GetName(int ordinal) => _columns[ordinal].DestinationName;
    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _columns.Count; i++)
        {
            if (string.Equals(_columns[i].DestinationName, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new IndexOutOfRangeException($"Column '{name}' not found in bulk shape.");
    }

    public override object GetValue(int ordinal)
    {
        if (!_hasCurrent)
            throw new InvalidOperationException("Read must be called before reading values.");

        BulkColumn<T> column = _columns[ordinal];
        object? value = column.Getter(_current!);
        if (value is null)
        {
            if (!column.Nullable)
                throw new InvalidOperationException($"Bulk column '{column.DestinationName}' produced null for a non-nullable column.");

            return DBNull.Value;
        }

        return value;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
            values[i] = GetValue(i);
        return count;
    }

    public override string GetDataTypeName(int ordinal) => _columns[ordinal].SqlDbType.ToString();
    public override Type GetFieldType(int ordinal) => BulkSqlTypeRenderer.GetFieldType(_columns[ordinal]);
    public override bool NextResult() => false;
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override void Close()
    {
        if (_closed)
            return;

        _closed = true;
        _current = default;
        _hasCurrent = false;
        _bufferedRow = default;
        _hasBufferedRow = false;
        _enumerator.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();

        base.Dispose(disposing);
    }
}
```

The shape metadata converter must follow the existing TVP normalization contract for `DateOnly`, `TimeOnly`, enums, `Guid`, `decimal`, `byte[]`, and nullable values. `BulkShapeDataReader<T>` should only consume already-normalized or provider-compatible getter values, report `IsClosed` from an internal closed flag, make `Close()`/`Dispose(bool)` idempotent, dispose its enumerator exactly once, clear the current row state when `Read()` reaches EOF, throw `IndexOutOfRangeException` for missing column names in `GetOrdinal`, and implement `HasRows` as "this result set contains at least one row" with a one-row buffer so the first row is not skipped when callers inspect `HasRows` before `Read()`.

- [ ] **Step 4: Run reader tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeDataReaderTests*"
```

Expected: tests pass.

### Task 4: Add Identifier and SQL Builder Tests

**Files:**
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkSqlBuilderTests.cs`
- Create: `Lib.Db/Execution/Bulk/BulkIdentifier.cs`
- Modify: `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs`
- Create: `Lib.Db/Execution/Bulk/BulkStagingSqlBuilder.cs`

- [ ] **Step 1: Write identifier and SQL builder tests**

Create `BulkSqlBuilderTests.cs`:

```csharp
using System.Data;
using FluentAssertions;
using Lib.Db.Execution.Bulk;
using Xunit;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class BulkSqlBuilderTests
{
    [Theory]
    [InlineData("Products", "[dbo].[Products]")]
    [InlineData("sales.Products", "[sales].[Products]")]
    [InlineData("[sales].[Products]", "[sales].[Products]")]
    public void ParseTableName_ShouldRenderSafeTwoPartName(string input, string expected)
    {
        BulkIdentifier.ParseTableName(input).ToSql().Should().Be(expected);
    }

    [Theory]
    [InlineData("server.database.schema.table")]
    [InlineData("dbo.Products;DELETE FROM dbo.Products")]
    [InlineData("dbo.Products -- comment")]
    [InlineData("[dbo].[Products")]
    [InlineData(".Products")]
    [InlineData("Products.")]
    [InlineData("dbo..Products")]
    [InlineData("[dbo].[]")]
    [InlineData("[dbo].[Products]]Archive]")]
    [InlineData("dbo.[Products")]
    [InlineData("dbo .Products")]
    [InlineData("dbo. Products")]
    [InlineData("[dbo] .[Products]")]
    public void ParseTableName_ShouldRejectUnsafeNames(string input)
    {
        Action act = () => BulkIdentifier.ParseTableName(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseTableName_ShouldRejectIdentifierPartsLongerThanSysname()
    {
        string tooLong = new('A', 129);

        Action act = () => BulkIdentifier.ParseTableName($"dbo.{tooLong}");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*128*");
    }

    [Fact]
    public void CreateStageTable_ShouldRenderColumnTypesAndNullability()
    {
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        string sql = BulkStagingSqlBuilder.CreateStageTable("#LibDbBulk_Test", shape.Columns);

        sql.Should().Contain("CREATE TABLE #LibDbBulk_Test");
        sql.Should().Contain("[Id] int NOT NULL");
        sql.Should().Contain("[Sku] nvarchar(64) NOT NULL");
        sql.Should().Contain("[Price] decimal(18,2) NULL");
    }

    [Fact]
    public void CreateUniqueStageKeyIndex_ShouldRenderKeyColumns()
    {
        BulkShape<BulkSqlRow> shape = BulkShape.For<BulkSqlRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Build();

        string sql = BulkStagingSqlBuilder.CreateUniqueStageKeyIndex("#LibDbBulk_Test", shape);

        sql.Should().Be("CREATE UNIQUE INDEX [IX_LibDbBulk_Key] ON #LibDbBulk_Test ([Id]);");
    }

    private sealed record BulkSqlRow(int Id, string Sku, decimal Price);
}
```

- [ ] **Step 2: Run SQL builder tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkSqlBuilderTests*"
```

Expected: build fails because SQL builder classes do not exist.

- [ ] **Step 3: Implement safe identifier parser**

Create `Lib.Db/Execution/Bulk/BulkIdentifier.cs`:

```csharp
namespace Lib.Db.Execution.Bulk;

internal readonly record struct BulkIdentifier(string Schema, string Name)
{
    private const int MaxSqlIdentifierLength = 128;

    public static BulkIdentifier ParseTableName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Destination table name cannot be empty.", nameof(input));

        string value = input.Trim();
        if (value.Contains(';') || value.Contains("--") || value.Contains("/*") || value.Contains("*/"))
            throw new ArgumentException("Destination table name contains unsupported SQL syntax.", nameof(input));

        string[] parts = SplitTwoPartName(value);
        return parts.Length switch
        {
            1 => new BulkIdentifier("dbo", NormalizePart(parts[0], input)),
            2 => new BulkIdentifier(NormalizePart(parts[0], input), NormalizePart(parts[1], input)),
            _ => throw new ArgumentException("Destination table name must be one or two parts.", nameof(input))
        };
    }

    public string ToSql() => $"{Quote(Schema)}.{Quote(Name)}";

    internal static string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string[] SplitTwoPartName(string value)
    {
        string[] rawParts = value.Split('.');
        if (rawParts.Any(part => !string.Equals(part, part.Trim(), StringComparison.Ordinal)))
            throw new ArgumentException("Destination table name cannot contain whitespace around separators.");

        if (rawParts.Length == 2
            && IsSimpleBracketedPart(rawParts[0])
            && IsSimpleBracketedPart(rawParts[1]))
        {
            return [UnwrapBracketed(rawParts[0]), UnwrapBracketed(rawParts[1])];
        }

        if (rawParts.Any(static part => part.Contains('[') || part.Contains(']')))
            throw new ArgumentException("Destination table name contains unsupported bracket syntax.");

        return rawParts;
    }

    private static string NormalizePart(string part, string original)
    {
        if (string.IsNullOrWhiteSpace(part))
            throw new ArgumentException($"Invalid destination table name '{original}'.");

        if (part.Length > MaxSqlIdentifierLength)
            throw new ArgumentException("Destination identifier parts cannot exceed 128 characters.");

        if (part.Any(char.IsWhiteSpace)
            || part.Contains(';', StringComparison.Ordinal)
            || part.Contains("--", StringComparison.Ordinal)
            || part.Contains("/*", StringComparison.Ordinal)
            || part.Contains("*/", StringComparison.Ordinal))
            throw new ArgumentException($"Invalid destination table name '{original}'.");

        return part;
    }

    private static bool IsSimpleBracketedPart(string value)
        => value.Length >= 3
            && value[0] == '['
            && value[^1] == ']'
            && !value[1..^1].Contains('[')
            && !value[1..^1].Contains(']');

    private static string UnwrapBracketed(string value) => value[1..^1];
}
```

This parser intentionally accepts only plain one/two-part names and the simple `[schema].[table]` form. It rejects embedded bracket escapes and whitespace around multipart separators in input rather than trying to repair them. The managed `Quote` helper still escapes `]` for internally validated identifiers, but public table-name parsing stays narrower than SQL Server's full delimited identifier grammar to avoid surprising normalization.

- [ ] **Step 4: Implement SQL type renderer and staging SQL builder**

Extend the Task 3 `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs` with SQL type rendering:

```csharp
using System.Data;

namespace Lib.Db.Execution.Bulk;

internal static class BulkSqlTypeRenderer
{
    public static bool IsSupported(SqlDbType sqlDbType)
        => sqlDbType is SqlDbType.Bit
            or SqlDbType.TinyInt
            or SqlDbType.SmallInt
            or SqlDbType.Int
            or SqlDbType.BigInt
            or SqlDbType.UniqueIdentifier
            or SqlDbType.Date
            or SqlDbType.Time
            or SqlDbType.DateTime2
            or SqlDbType.DateTimeOffset
            or SqlDbType.Decimal
            or SqlDbType.NVarChar
            or SqlDbType.VarChar
            or SqlDbType.VarBinary;

    public static string Render<T>(BulkColumn<T> column) where T : notnull
        => column.SqlDbType switch
        {
            SqlDbType.Int => "int",
            SqlDbType.BigInt => "bigint",
            SqlDbType.SmallInt => "smallint",
            SqlDbType.TinyInt => "tinyint",
            SqlDbType.Bit => "bit",
            SqlDbType.UniqueIdentifier => "uniqueidentifier",
            SqlDbType.Date => "date",
            SqlDbType.Time => column.Scale is null ? "time" : $"time({column.Scale})",
            SqlDbType.DateTime2 => column.Scale is null ? "datetime2" : $"datetime2({column.Scale})",
            SqlDbType.DateTimeOffset => column.Scale is null ? "datetimeoffset" : $"datetimeoffset({column.Scale})",
            SqlDbType.Decimal => $"decimal({column.Precision ?? 18},{column.Scale ?? 0})",
            SqlDbType.NVarChar => column.Size is > 0 ? $"nvarchar({column.Size})" : "nvarchar(max)",
            SqlDbType.VarChar => column.Size is > 0 ? $"varchar({column.Size})" : "varchar(max)",
            SqlDbType.VarBinary => column.Size is > 0 ? $"varbinary({column.Size})" : "varbinary(max)",
            _ => throw new NotSupportedException($"SqlDbType '{column.SqlDbType}' is not supported by AOT-safe bulk operations.")
        };

    public static Type GetFieldType<T>(BulkColumn<T> column) where T : notnull
        => column.SqlDbType switch
        {
            SqlDbType.Bit => typeof(bool),
            SqlDbType.TinyInt => typeof(byte),
            SqlDbType.SmallInt => typeof(short),
            SqlDbType.Int => typeof(int),
            SqlDbType.BigInt => typeof(long),
            SqlDbType.UniqueIdentifier => typeof(Guid),
            SqlDbType.Date => typeof(DateTime),
            SqlDbType.Time => typeof(TimeSpan),
            SqlDbType.DateTime2 => typeof(DateTime),
            SqlDbType.DateTimeOffset => typeof(DateTimeOffset),
            SqlDbType.Decimal => typeof(decimal),
            SqlDbType.NVarChar or SqlDbType.VarChar => typeof(string),
            SqlDbType.VarBinary => typeof(byte[]),
            _ => throw new NotSupportedException($"SqlDbType '{column.SqlDbType}' is not supported by AOT-safe bulk shapes.")
        };
}
```

Create `Lib.Db/Execution/Bulk/BulkStagingSqlBuilder.cs`:

```csharp
using System.Text;

namespace Lib.Db.Execution.Bulk;

internal static class BulkStagingSqlBuilder
{
    public static string CreateStageTable<T>(string stageTableName, IReadOnlyList<BulkColumn<T>> columns)
        where T : notnull
    {
        StringBuilder sql = new();
        sql.Append("CREATE TABLE ").Append(stageTableName).AppendLine(" (");
        for (int i = 0; i < columns.Count; i++)
        {
            BulkColumn<T> column = columns[i];
            sql.Append("    ")
                .Append(BulkIdentifier.Quote(column.DestinationName))
                .Append(' ')
                .Append(BulkSqlTypeRenderer.Render(column))
                .Append(column.Nullable ? " NULL" : " NOT NULL");

            if (i + 1 < columns.Count)
                sql.Append(',');

            sql.AppendLine();
        }

        sql.Append(");");
        return sql.ToString();
    }

    public static string CreateUniqueStageKeyIndex<T>(string stageTableName, BulkShape<T> shape)
        where T : notnull
    {
        if (shape.KeyColumns.Count == 0)
            throw new InvalidOperationException("Bulk stage key index requires at least one key column.");

        string keyList = string.Join(", ", shape.KeyColumns.Select(column => BulkIdentifier.Quote(column.DestinationName)));
        return $"CREATE UNIQUE INDEX [IX_LibDbBulk_Key] ON {stageTableName} ({keyList});";
    }
}
```

Do not use `IGNORE_DUP_KEY`. Duplicate source key tuples must fail the operation and roll back before target DML runs.

- [ ] **Step 5: Run SQL builder tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkSqlBuilderTests*"
```

Expected: tests pass after tightening malformed bracket rejection if needed.

### Task 5: Map `DbSession` Connection Integration

**Files:**
- Read: `Lib.Db/Core/DbSession.cs`
- Read: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`
- Read: `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
- Create in Task 6: `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs`
- Modify in Task 6: `Lib.Db/Core/DbSession.cs`

- [ ] **Step 1: Inspect existing connection ownership**

Run:

```powershell
rg -n "class DbSession|SqlConnection|BeginTransactionAsync|GetConnection|ConnectionString|BulkInsertAsync" Lib.Db/Core/DbSession.cs Lib.Db/Execution/Executors/SqlDbExecutor.cs Lib.Db/Configuration
```

Expected: identify the existing connection resolver, redaction path, transaction behavior, and `DbResult<T>` failure construction. Record the exact private fields or helpers to reuse in Task 6 implementation notes before writing code.

- [ ] **Step 2: Inspect legacy bulk error handling**

Run:

```powershell
rg -n "BulkInsertAsync|ObjectDataReader|SqlBulkCopy|DbResult<long>" Lib.Db/Core/DbSession.cs Lib.Db/Execution/Bulk Lib.Db/Contracts
```

Expected: confirm how the current method maps `BulkInsertOptions` to `SqlBulkCopyOptions`, catches exceptions, and avoids exposing connection strings.

- [ ] **Step 3: Decide the executor constructor contract**

Before Task 6 writes code, choose one of these two local integration forms based on the inspected `DbSession` fields:

```csharp
internal sealed class BulkWriteExecutor(/* same resolver/logger/options dependencies DbSession already uses */);
```

or:

```csharp
private Task<DbResult<TValue>> ExecuteBulkAsync<TValue>(
    string instanceName,
    Func<SqlConnection, SqlTransaction?, CancellationToken, Task<TValue>> operation,
    CancellationToken ct);
```

Pick the form that preserves existing `DbSession` ownership and minimizes public surface. Do not add compile-green stubs for operations that are not implemented in the same task.

- [ ] **Step 4: Run a no-change status check**

Run:

```powershell
git status --short
```

Expected: only planned test/production files from Tasks 1-4 are modified. If Task 5 only inspects files, it should create no new diff.

### Task 6: Implement AOT-Safe Bulk Insert

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
- Modify: `Lib.Db/Core/DbSession.cs`
- Modify: `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/BulkMutationTests.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Infrastructure/SchemaInitializer.cs`

- [ ] **Step 1: Add verification table setup**

In `SchemaInitializer.cs`, add a verification table under the existing `gap` schema setup:

```sql
IF OBJECT_ID('[gap].[BulkMutationTarget]', 'U') IS NULL
BEGIN
    CREATE TABLE [gap].[BulkMutationTarget] (
        [Id] int NOT NULL CONSTRAINT [PK_BulkMutationTarget] PRIMARY KEY,
        [Sku] nvarchar(64) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Qty] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [UpdatedAtUtc] datetime2(7) NOT NULL
    );
END;
```

- [ ] **Step 2: Add only the AOT-safe insert public method**

In `Lib.Db/Contracts/Entry/DbEntryContracts.cs`, add only this overload after the legacy reflection-based `BulkInsertAsync<T>`:

```csharp
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;
```

In `DbSession.cs`, implement this overload in the same task as the executor insert path. Do not add update/delete/upsert/merge interface methods until Tasks 7 and 8.

- [ ] **Step 3: Write failing insert integration test**

Create `BulkMutationTests.cs`:

```csharp
using System.Data;
using FluentAssertions;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Execution.Bulk;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lib.Db.IntegrationTests.VerificationDb;

[Collection("MultiDb")]
public sealed class BulkMutationTests(MultiDbFixture fixture)
{
    private readonly IDbSession _session = fixture.Services.GetRequiredService<IDbSession>();

    [Fact]
    public async Task BulkInsertAsync_WithStaticShape_ShouldInsertRows()
    {
        await ClearTargetAsync();
        BulkShape<BulkMutationRow> shape = CreateShape();
        BulkMutationRow[] rows =
        [
            new(1, "SKU-1", "One", 10, 12.50m, DateTime.UtcNow),
            new(2, "SKU-2", "Two", 20, 22.50m, DateTime.UtcNow)
        ];

        DbResult<long> result = await _session.BulkInsertAsync(
            TestDatabaseNames.Default,
            "[gap].[BulkMutationTarget]",
            rows,
            shape,
            ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().Be(2);
        long count = await CountTargetAsync();
        count.Should().Be(2);
        await AssertRowAsync(1, "SKU-1", "One", 10, 12.50m);
        await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
    }

    [Fact]
    public async Task BulkInsertAsync_WithDestinationNamesDifferentFromClrMembers_ShouldMapColumns()
    {
        await ClearTargetAsync();
        BulkShape<BulkAliasMutationRow> shape = BulkShape.For<BulkAliasMutationRow>()
            .Key("Id", SqlDbType.Int, static row => row.ProductKey)
            .Column("Sku", SqlDbType.NVarChar, static row => row.ProductCode, size: 64, nullable: false)
            .Column("Name", SqlDbType.NVarChar, static row => row.DisplayName, size: 200, nullable: false)
            .Column("Qty", SqlDbType.Int, static row => row.Quantity)
            .Column("Price", SqlDbType.Decimal, static row => row.UnitPrice, precision: 18, scale: 2)
            .Column("UpdatedAtUtc", SqlDbType.DateTime2, static row => row.ChangedAtUtc, scale: 7)
            .Build();

        DbResult<long> result = await _session.BulkInsertAsync(
            TestDatabaseNames.Default,
            "[gap].[BulkMutationTarget]",
            [new BulkAliasMutationRow(10, "SKU-10", "Ten", 100, 10.10m, DateTime.UtcNow)],
            shape,
            ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().Be(1);
        await AssertRowAsync(10, "SKU-10", "Ten", 100, 10.10m);
    }

    private static BulkShape<BulkMutationRow> CreateShape()
        => BulkShape.For<BulkMutationRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
            .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 200, nullable: false)
            .Column("Qty", SqlDbType.Int, static row => row.Qty)
            .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
            .Column("UpdatedAtUtc", SqlDbType.DateTime2, static row => row.UpdatedAtUtc, scale: 7)
            .Build();

    private async Task ClearTargetAsync()
    {
        DbResult<int> result = await _session.Use(TestDatabaseNames.Default)
            .Sql("DELETE FROM [gap].[BulkMutationTarget]")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private async Task<long> CountTargetAsync()
    {
        DbResult<long> result = await _session.Use(TestDatabaseNames.Default)
            .Sql("SELECT COUNT_BIG(*) FROM [gap].[BulkMutationTarget]")
            .QuerySingleAsync<long>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task AssertRowAsync(int id, string expectedSku, string expectedName, int expectedQty, decimal expectedPrice)
    {
        DbResult<BulkMutationReadRow?> result = await _session.Use(TestDatabaseNames.Default)
            .Sql((FormattableString)$"SELECT Id, Sku, Name, Qty, Price FROM [gap].[BulkMutationTarget] WHERE Id = {id}")
            .QuerySingleAsync<BulkMutationReadRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().NotBeNull();
        BulkMutationReadRow row = result.Value!;
        row.Sku.Should().Be(expectedSku);
        row.Name.Should().Be(expectedName);
        row.Qty.Should().Be(expectedQty);
        row.Price.Should().Be(expectedPrice);
    }

    private async Task AssertMissingAsync(int id)
    {
        DbResult<BulkMutationReadRow?> result = await _session.Use(TestDatabaseNames.Default)
            .Sql((FormattableString)$"SELECT Id, Sku, Name, Qty, Price FROM [gap].[BulkMutationTarget] WHERE Id = {id}")
            .QuerySingleAsync<BulkMutationReadRow>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().BeNull();
    }

    private sealed record BulkMutationRow(
        int Id,
        string Sku,
        string Name,
        int Qty,
        decimal Price,
        DateTime UpdatedAtUtc);

    private sealed record BulkAliasMutationRow(
        int ProductKey,
        string ProductCode,
        string DisplayName,
        int Quantity,
        decimal UnitPrice,
        DateTime ChangedAtUtc);

    private sealed record BulkMutationReadRow(int Id, string Sku, string Name, int Qty, decimal Price);
}
```

This plan uses the repo's current ad-hoc SQL style: `Sql(...).ExecuteAsync(...)` for setup/cleanup and `QuerySingleAsync<T>(...)` or the nearest existing typed scalar helper for verification queries. If the implementation session finds a local helper name drift, update only the test call sites to the current helper; do not weaken the assertions.

Also add focused failure-path coverage before marking Task 6 complete:

- `BulkInsertAsync_WhenCanceledBeforeCommit_ShouldAttemptRollbackBeforeRethrow`: use the smallest local seam available after Task 5 inventory (for example a test bulk-copy/executor factory, cancellation hook, or fake transaction wrapper) to prove cancellation attempts rollback before the `OperationCanceledException` escapes.
- `BulkInsertAsync_WhenGeneralExceptionOccurs_ShouldReturnRedactedFailure`: force a non-SQL exception from the bulk reader/getter/executor path and assert the returned `DbError` uses a generic public message, contains the sanitized destination object name only, and does not expose the row value, connection string, raw payload, or raw exception message.
- Keep the normal insert success test above as the positive control so rollback/error hardening does not break the happy path.

- [ ] **Step 4: Run insert integration test and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkInsertAsync*"
```

Expected: fails because the public insert overload, executor insert path, and
required insert failure/commit-boundary behaviors are not implemented yet. Do
not narrow this to the happy-path test after adding the failure-path tests in
this task.

- [ ] **Step 5: Implement `SqlBulkCopy` insert path**

In `BulkWriteExecutor`, implement:

- instance connection resolution through existing `DbSession` infrastructure,
- destination validation through `BulkIdentifier.ParseTableName`,
- option validation,
- `SqlBulkCopyOptions` mapping, including `CheckConstraints` enabled by default,
- local transaction when `UseTransaction = true`,
- `SqlBulkCopy.ColumnMappings` from shape column names,
- `BulkShapeDataReader<T>` as the data source,
- `WriteToServerAsync(reader, ct)`,
- return `reader.RowsRead`.

Use this core pattern inside the existing repository error-handling style:

Before constructing `SqlBulkCopy`, map options with `CheckConstraints` honoring the safe default:

```csharp
SqlBulkCopyOptions copyOptions = SqlBulkCopyOptions.Default;
if (options.FireTriggers) copyOptions |= SqlBulkCopyOptions.FireTriggers;
if (options.CheckConstraints) copyOptions |= SqlBulkCopyOptions.CheckConstraints;
if (options.KeepIdentity) copyOptions |= SqlBulkCopyOptions.KeepIdentity;
```

`UseTransaction = false` is allowed only for `BulkInsertAsync` in v2.4.0. Treat
it as a non-atomic opt-out: if `SqlBulkCopy` or the provider fails after sending
some rows, those rows can remain in the target table, and Lib.Db must not claim a
rollback guarantee. Do not replace this with
`SqlBulkCopyOptions.UseInternalTransaction`; that option is batch-scoped and does
not provide the same cross-step transaction model as the explicit local
transaction used by the safe default path.

```csharp
await using SqlConnection connection = await OpenConnectionAsync(instanceName, ct).ConfigureAwait(false);
SqlTransaction? transaction = options.UseTransaction
    ? (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false)
    : null;

try
{
    using BulkShapeDataReader<T> reader = new(records, shape);
    using SqlBulkCopy bulkCopy = new(connection, copyOptions, transaction)
    {
        DestinationTableName = destination.ToSql(),
        BatchSize = options.BatchSize,
        BulkCopyTimeout = options.TimeoutSeconds,
        EnableStreaming = options.EnableStreaming
    };

    foreach (BulkColumn<T> column in shape.Columns)
        bulkCopy.ColumnMappings.Add(column.DestinationName, column.DestinationName);

    await bulkCopy.WriteToServerAsync(reader, ct).ConfigureAwait(false);

    if (transaction is not null)
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);

    return DbResult<long>.Ok(reader.RowsRead);
}
catch
{
    if (transaction is not null)
        await TryRollbackAsync(transaction).ConfigureAwait(false);
    throw; // only inside raw operation delegates wrapped by ExecuteBulkAsync
}
finally
{
    transaction?.Dispose();
}
```

Use the exact local connection-opening and success-result APIs identified in Task 5. Do not introduce a second connection resolver.

If this pattern lives directly inside a public `DbResult<T>` method, replace the `throw` path with existing error mapping:

```csharp
catch (OperationCanceledException)
{
    if (transaction is not null)
        await TryRollbackAsync(transaction).ConfigureAwait(false);

    throw;
}
catch (SqlException ex)
{
    if (transaction is not null)
        await TryRollbackAsync(transaction).ConfigureAwait(false);

    return DbResult<long>.Fail(DbErrorMapper.FromSqlException(ex, destination.ToSql()));
}
catch (Exception ex)
{
    if (transaction is not null)
        await TryRollbackAsync(transaction).ConfigureAwait(false);

    return DbResult<long>.Fail(new DbError
    {
        Kind = DbErrorKind.Unknown,
        Message = "Bulk operation failed.",
        ObjectName = destination.ToSql()
    });
}
```

Use `CancellationToken.None` for final `CommitAsync` once all cancellable staging and DML work has completed. The public cancellation contract is: cancellation before commit begins attempts rollback and rethrows `OperationCanceledException`; after commit begins, Lib.Db does not report caller cancellation because the database outcome can be ambiguous. If commit itself fails with a provider exception, return the established redacted failure and attempt best-effort rollback only if the provider still considers the transaction pending.

`TryRollbackAsync` must preserve the primary failure:

```csharp
private static async Task TryRollbackAsync(SqlTransaction transaction)
{
    try
    {
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
    }
    catch (Exception rollbackException)
    {
        // Diagnostic-only path. Public DbResult errors must preserve the original failure.
        // Log only through the existing redacted diagnostics policy; never copy raw rollback messages into DbError.
        _ = rollbackException;
    }
}
```

If the implementation has an existing redacted diagnostic hook available at this layer, replace the discard with that hook. Do not introduce a new public logging surface just for rollback failures.

Do not leak connection strings, row values, raw payloads, or raw exception messages in public mapped errors. If the final implementation preserves the original exception for in-process diagnostics, it must be behind the existing diagnostic redaction policy and must not be logged or serialized by Lib.Db. If the final implementation uses `ExecuteBulkAsync<TValue>`, that helper owns rollback and non-cancellation exception mapping; raw operation delegates may rethrow only after rollback is best-effort attempted without replacing the primary failure.

Add required rollback/commit boundary tests before marking Task 5 complete:

- `BulkInsertAsync_WhenRollbackFails_ShouldPreserveOriginalFailureAndRedactRollbackError`: inject a rollback failure after a primary bulk failure and assert the returned/propagated public error still describes the original generic bulk failure, not the rollback exception.
- `BulkInsertAsync_WhenCanceledBeforeCommit_ShouldAttemptRollbackBeforeRethrow`: cancellation before commit attempts rollback and rethrows `OperationCanceledException`.
- `BulkInsertAsync_WhenCommitHasStarted_ShouldUseNonCancelableCommit`: verify the final commit call receives `CancellationToken.None` or the local abstraction equivalent so caller cancellation cannot create a false canceled result after commit has started.
- `BulkInsertAsync_WhenUseTransactionFalseFails_ShouldDocumentPartialWriteRisk`: use a controlled non-transactional insert failure or a narrow executor seam to prove the test name and assertions do not promise rollback. If a deterministic partial-write integration test would be flaky, keep the behavioral test at the executor seam and add public-doc assertions that describe the non-atomic contract.

- [ ] **Step 6: Run insert integration test and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkInsertAsync*"
```

Expected: all insert success, cancellation rollback, redacted general failure,
rollback-failure preservation, non-cancelable commit, and non-atomic opt-out
tests pass.

### Task 7: Implement Update and Delete

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
- Modify: `Lib.Db/Core/DbSession.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/BulkMutationTests.cs`
- Modify: `Lib.Db/Execution/Bulk/BulkStagingSqlBuilder.cs`
- Modify: `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs`

- [ ] **Step 1: Add update/delete public methods**

In `Lib.Db/Contracts/Entry/DbEntryContracts.cs`, add:

```csharp
Task<DbResult<long>> BulkUpdateAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<long>> BulkDeleteAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;
```

Add matching `DbSession` methods in the same task that implements the executor update/delete paths.

- [ ] **Step 2: Add failing update/delete tests**

Append tests:

```csharp
[Fact]
public async Task BulkUpdateAsync_WithStaticShape_ShouldUpdateOnlyMatchingRows()
{
    await ClearTargetAsync();
    await SeedRowsAsync();
    BulkShape<BulkMutationRow> shape = CreateShape();

    DbResult<long> result = await _session.BulkUpdateAsync(
        TestDatabaseNames.Default,
        "[gap].[BulkMutationTarget]",
        [new BulkMutationRow(1, "SKU-1U", "One Updated", 99, 19.99m, DateTime.UtcNow)],
        shape,
        ct: TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.Should().Be(1);
    await AssertRowAsync(1, "SKU-1U", "One Updated", 99, 19.99m);
    await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
}

[Fact]
public async Task BulkDeleteAsync_WithStaticShape_ShouldDeleteOnlyMatchingKeys()
{
    await ClearTargetAsync();
    await SeedRowsAsync();
    BulkShape<BulkMutationRow> shape = CreateShape();

    DbResult<long> result = await _session.BulkDeleteAsync(
        TestDatabaseNames.Default,
        "[gap].[BulkMutationTarget]",
        [new BulkMutationRow(1, "ignored", "ignored", 0, 0m, DateTime.UtcNow)],
        shape,
        ct: TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.Should().Be(1);
    (await CountTargetAsync()).Should().Be(1);
    await AssertMissingAsync(1);
    await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
}

[Fact]
public async Task BulkUpdateAsync_WithDuplicateSourceKeys_ShouldFailBeforeChangingTarget()
{
    await ClearTargetAsync();
    await SeedRowsAsync();
    BulkShape<BulkMutationRow> shape = CreateShape();

    DbResult<long> result = await _session.BulkUpdateAsync(
        TestDatabaseNames.Default,
        "[gap].[BulkMutationTarget]",
        [
            new BulkMutationRow(1, "SKU-1A", "One A", 10, 10.00m, DateTime.UtcNow),
            new BulkMutationRow(1, "SKU-1B", "One B", 20, 20.00m, DateTime.UtcNow)
        ],
        shape,
        ct: TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    (await CountTargetAsync()).Should().Be(2);
    await AssertRowAsync(1, "SKU-1", "One", 10, 12.50m);
    await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
}
```

Add `SeedRowsAsync()` using the new AOT-safe insert method from Task 6.
Use the `AssertRowAsync` and `AssertMissingAsync` helpers created in Task 6 so these tests verify the actual target row state, not only affected-row counts. The duplicate-source test must fail because the staging unique index rejects the duplicate key tuple before target DML runs.

Also add failure-path coverage before marking Task 7 complete:

- `BulkUpdateAsync_WhenActionSqlFails_ShouldRollbackTargetChanges`: inject a controlled DML failure after the stage load and before commit using the smallest executor seam available after Task 5. Assert the operation returns a redacted failed `DbResult<long>` and that seeded target rows are unchanged.
- `BulkDeleteAsync_WhenCanceledBeforeCommit_ShouldAttemptRollbackBeforeRethrow`: use the same seam or a cancellation hook after delete DML but before commit. Assert cancellation propagates and the deleted row is still present after rollback.
- `BulkUpdateAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection`: pass `new BulkWriteOptions { UseTransaction = false }` and assert a validation failure before any connection/session executor opens SQL Server.
- `BulkDeleteAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection`: same validation contract for delete.

- [ ] **Step 3: Run update/delete tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkUpdateAsync*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkDeleteAsync*"
```

Expected: fails because update/delete executor paths are not implemented yet.

- [ ] **Step 4: Add staged SQL builder methods**

Add methods:

```csharp
public static string UpdateFromStage<T>(BulkIdentifier destination, string stageTableName, BulkShape<T> shape)
    where T : notnull
{
    if (shape.KeyColumns.Count == 0)
        throw new InvalidOperationException("Bulk update requires at least one key column.");

    if (shape.WritableColumns.Count == 0)
        throw new InvalidOperationException("Bulk update requires at least one non-key column.");

    string setClause = string.Join(", ", shape.WritableColumns.Select(column =>
        $"target.{BulkIdentifier.Quote(column.DestinationName)} = source.{BulkIdentifier.Quote(column.DestinationName)}"));
    string joinClause = JoinOnKeys(shape);

    return $"UPDATE target SET {setClause} FROM {destination.ToSql()} AS target INNER JOIN {stageTableName} AS source ON {joinClause};";
}

public static string DeleteFromStage<T>(BulkIdentifier destination, string stageTableName, BulkShape<T> shape)
    where T : notnull
{
    if (shape.KeyColumns.Count == 0)
        throw new InvalidOperationException("Bulk delete requires at least one key column.");

    string joinClause = JoinOnKeys(shape);
    return $"DELETE target FROM {destination.ToSql()} AS target INNER JOIN {stageTableName} AS source ON {joinClause};";
}

private static string JoinOnKeys<T>(BulkShape<T> shape)
    where T : notnull
    => string.Join(" AND ", shape.KeyColumns.Select(column =>
        $"target.{BulkIdentifier.Quote(column.DestinationName)} = source.{BulkIdentifier.Quote(column.DestinationName)}"));
```

- [ ] **Step 5: Implement staging executor**

In `BulkWriteExecutor`, add a shared method:

```csharp
private async Task<long> ExecuteStagedSingleActionAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions options,
    Func<BulkIdentifier, string, BulkShape<T>, string> buildActionSql,
    bool stageKeysOnly,
    CancellationToken ct)
    where T : notnull
```

It must:

- parse destination,
- create a unique local temp table name,
- reject `UseTransaction = false` before opening the connection, then begin one local transaction for every staged update/delete path,
- open connection after option validation succeeds,
- create stage table,
- bulk copy rows into stage,
- create a unique index on stage key columns when the operation uses keys,
- execute action SQL,
- capture affected row count,
- drop stage table,
- commit transaction,
- roll back on failure.

When `stageKeysOnly` is `true`, build stage SQL and `SqlBulkCopy` mappings from `shape.KeyColumns` only. Do not construct a key-only temp table and then feed it a reader over all shape columns.

Add a helper such as:

```csharp
private static IReadOnlyList<BulkColumn<T>> GetStageColumns<T>(BulkShape<T> shape, bool stageKeysOnly)
    where T : notnull
    => stageKeysOnly ? shape.KeyColumns : shape.Columns;
```

Then make `BulkShapeDataReader<T>` accept an optional column list:

```csharp
public BulkShapeDataReader(IEnumerable<T> rows, BulkShape<T> shape, IReadOnlyList<BulkColumn<T>>? columns = null)
```

`FieldCount`, `GetName`, `GetValue`, `GetFieldType`, and mappings must use the selected stage columns. DML SQL builders still receive the full shape so update/upsert can set writable columns.

- [ ] **Step 6: Run update/delete tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkUpdateAsync*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkDeleteAsync*"
```

Expected: tests pass.

### Task 8: Implement Upsert and Merge

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
- Modify: `Lib.Db/Core/DbSession.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/VerificationDb/BulkMutationTests.cs`
- Modify: `Lib.Db/Execution/Bulk/BulkStagingSqlBuilder.cs`
- Modify: `Lib.Db/Execution/Bulk/BulkWriteExecutor.cs`

- [ ] **Step 1: Add upsert/merge public methods**

In `Lib.Db/Contracts/Entry/DbEntryContracts.cs`, add:

```csharp
Task<DbResult<BulkUpsertResult>> BulkUpsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;

Task<DbResult<BulkMergeResult>> BulkMergeAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    Lib.Db.Execution.Bulk.BulkShape<T> shape,
    BulkMergeOptions? options = null,
    CancellationToken ct = default)
    where T : notnull;
```

Add matching `DbSession` methods in the same task that implements the executor upsert/merge paths.

- [ ] **Step 2: Add failing upsert/merge tests**

Append tests:

```csharp
[Fact]
public async Task BulkUpsertAsync_WithStaticShape_ShouldUpdateMatchedAndInsertMissing()
{
    await ClearTargetAsync();
    await SeedRowsAsync();
    BulkShape<BulkMutationRow> shape = CreateShape();

    DbResult<BulkUpsertResult> result = await _session.BulkUpsertAsync(
        TestDatabaseNames.Default,
        "[gap].[BulkMutationTarget]",
        [
            new BulkMutationRow(1, "SKU-1U", "One Updated", 99, 19.99m, DateTime.UtcNow),
            new BulkMutationRow(3, "SKU-3", "Three", 30, 33.33m, DateTime.UtcNow)
        ],
        shape,
        ct: TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.Updated.Should().Be(1);
    result.Value.Inserted.Should().Be(1);
    result.Value.TotalAffected.Should().Be(2);
    (await CountTargetAsync()).Should().Be(3);
    await AssertRowAsync(1, "SKU-1U", "One Updated", 99, 19.99m);
    await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
    await AssertRowAsync(3, "SKU-3", "Three", 30, 33.33m);
}

[Fact]
public async Task BulkMergeAsync_WithDeleteMatched_ShouldDeleteOnlyStagedKeys()
{
    await ClearTargetAsync();
    await SeedRowsAsync();
    BulkShape<BulkMutationRow> shape = CreateShape();

    DbResult<BulkMergeResult> result = await _session.BulkMergeAsync(
        TestDatabaseNames.Default,
        "[gap].[BulkMutationTarget]",
        [new BulkMutationRow(1, "ignored", "ignored", 0, 0m, DateTime.UtcNow)],
        shape,
        new BulkMergeOptions { Actions = BulkMergeActions.DeleteMatched },
        TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.Deleted.Should().Be(1);
    result.Value.TotalAffected.Should().Be(1);
    (await CountTargetAsync()).Should().Be(1);
    await AssertMissingAsync(1);
    await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
}

[Theory]
[InlineData(BulkMergeActions.UpdateMatched | BulkMergeActions.DeleteMatched)]
[InlineData(BulkMergeActions.InsertMissing | BulkMergeActions.DeleteMatched)]
public async Task BulkMergeAsync_WithInvalidDeleteMatchedCombination_ShouldFailBeforeChangingTarget(BulkMergeActions actions)
{
    await ClearTargetAsync();
    await SeedRowsAsync();
    BulkShape<BulkMutationRow> shape = CreateShape();

    DbResult<BulkMergeResult> result = await _session.BulkMergeAsync(
        TestDatabaseNames.Default,
        "[gap].[BulkMutationTarget]",
        [new BulkMutationRow(1, "SKU-1U", "One Updated", 99, 19.99m, DateTime.UtcNow)],
        shape,
        new BulkMergeOptions { Actions = actions },
        TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    (await CountTargetAsync()).Should().Be(2);
    await AssertRowAsync(1, "SKU-1", "One", 10, 12.50m);
    await AssertRowAsync(2, "SKU-2", "Two", 20, 22.50m);
}
```

- [ ] **Step 3: Run upsert/merge tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkUpsertAsync*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkMergeAsync*"
```

Expected: fails because upsert/merge executor paths are not implemented yet.

- [ ] **Step 4: Add insert-missing SQL builder**

Add:

```csharp
public static string InsertMissingFromStage<T>(BulkIdentifier destination, string stageTableName, BulkShape<T> shape)
    where T : notnull
{
    if (shape.KeyColumns.Count == 0)
        throw new InvalidOperationException("Bulk upsert requires at least one key column.");

    string columnList = string.Join(", ", shape.Columns.Select(column => BulkIdentifier.Quote(column.DestinationName)));
    string sourceList = string.Join(", ", shape.Columns.Select(column => $"source.{BulkIdentifier.Quote(column.DestinationName)}"));
    string joinClause = JoinOnKeys(shape);

    return $"""
        INSERT INTO {destination.ToSql()} ({columnList})
        SELECT {sourceList}
        FROM {stageTableName} AS source
        WHERE NOT EXISTS (
            SELECT 1
            FROM {destination.ToSql()} AS target WITH (UPDLOCK, HOLDLOCK)
            WHERE {joinClause}
        );
        """;
}
```

- [ ] **Step 5: Implement upsert and merge orchestration**

Implement shared staged multi-action execution that:

- creates and loads stage once,
- rejects `UseTransaction = false` before opening a connection,
- creates the stage unique index on key columns before any target DML,
- runs update when selected,
- runs insert-missing when selected,
- runs delete-matched only when it is the sole selected action,
- rejects `DeleteNotMatchedBySource`,
- rejects `DeleteMatched` combined with update or insert actions before opening a connection,
- returns separated counts.

Use `ExecuteNonQueryAsync(ct)` for each action and store the returned affected rows immediately.

The executor does not probe `sys.indexes` by default in v2.4.0. Public docs must state that target key columns are expected to be backed by an application-owned `PRIMARY KEY` or `UNIQUE` constraint/index. This keeps the library thin and avoids adding per-call metadata permissions, query cost, and lock-surface risk to the hot path.

Also add failure-path coverage before marking Task 8 complete:

- `BulkUpsertAsync_WhenInsertMissingFails_ShouldRollbackPriorUpdate`: inject a controlled failure in the insert-missing step after the update step has run but before commit. Assert previously matched rows are unchanged after rollback and the returned failure is redacted.
- `BulkMergeAsync_WhenCanceledAfterActionBeforeCommit_ShouldAttemptRollbackBeforeRethrow`: cancel after the selected action reports affected rows and before commit. Assert cancellation propagates and target rows remain unchanged.
- `BulkUpsertAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection`: non-atomic staged upsert is not supported in v2.4.0.
- `BulkMergeAsync_WhenUseTransactionFalse_ShouldRejectBeforeOpeningConnection`: non-atomic staged merge is not supported in v2.4.0.

- [ ] **Step 6: Run upsert/merge tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkUpsertAsync*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterMethod "*BulkMergeAsync*"
```

Expected: tests pass.

### Task 9: Add AOT Smoke Coverage

**Files:**
- Modify: `Verification/projects/Lib.Db.AotVerification/Program.cs`
- Modify: `Verification/scripts/Invoke-Aot.ps1` only if warning baseline behavior needs adjustment

- [ ] **Step 1: Add AOT smoke and public API reachability steps**

In `Program.cs`, ensure these imports exist:

```csharp
using System.Data;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Execution.Bulk;
```

Add:

```csharp
RunStep("AotSafeBulkShape", VerifyAotSafeBulkShape);
RunStep("AotSafeBulkPublicApiReachability", VerifyAotSafeBulkPublicApiReachability);
```

Update `RunStep` so successful steps are visible in the script output. This avoids a false-positive manual gate where the process exits successfully but the new smoke path was not actually wired:

```csharp
static void RunStep(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"AOT verification step '{name}' completed.");
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"AOT verification step '{name}' failed.", ex);
    }
}
```

Add:

```csharp
static void VerifyAotSafeBulkShape()
{
    BulkShape<AotBulkRow> shape = BulkShape.For<AotBulkRow>()
        .Key("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Column("Status", SqlDbType.Int, static row => row.Status)
        .Build();

    using BulkShapeDataReader<AotBulkRow> reader = new(
        [new AotBulkRow(1, "AOT-SKU", 3, AotBulkStatus.Active)],
        shape);

    if (!reader.Read())
        throw new InvalidOperationException("AOT bulk reader did not read the smoke row.");

    if (!Equals(reader.GetValue(1), "AOT-SKU"))
        throw new InvalidOperationException("AOT bulk reader returned an unexpected value.");

    if (!Equals(reader.GetValue(3), 1))
        throw new InvalidOperationException("AOT bulk reader did not normalize enum values through shape metadata.");
}

internal enum AotBulkStatus { Inactive = 0, Active = 1 }
internal readonly record struct AotBulkRow(int Id, string Sku, int Qty, AotBulkStatus Status);
```

Also add a no-DB reachability smoke that roots the public AOT-safe bulk overloads and option types during publish analysis. This is not a behavioral DB test; integration tests still own SQL Server execution:

```csharp
static void VerifyAotSafeBulkPublicApiReachability()
{
    BulkShape<AotBulkRow> shape = BulkShape.For<AotBulkRow>()
        .Key("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Column("Status", SqlDbType.Int, static row => row.Status)
        .Build();

    BulkWriteOptions writeOptions = new();
    BulkMergeOptions mergeOptions = new();
    mergeOptions.Validate();

    Func<IDbSession, CancellationToken, Task<DbResult<long>>> insert =
        (session, token) => session.BulkInsertAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<long>>> update =
        (session, token) => session.BulkUpdateAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<long>>> delete =
        (session, token) => session.BulkDeleteAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<BulkUpsertResult>>> upsert =
        (session, token) => session.BulkUpsertAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, writeOptions, token);
    Func<IDbSession, CancellationToken, Task<DbResult<BulkMergeResult>>> merge =
        (session, token) => session.BulkMergeAsync("Default", "dbo.AotBulk", Array.Empty<AotBulkRow>(), shape, mergeOptions, token);

    GC.KeepAlive(insert);
    GC.KeepAlive(update);
    GC.KeepAlive(delete);
    GC.KeepAlive(upsert);
    GC.KeepAlive(merge);
}
```

- [ ] **Step 2: Run AOT verification and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: `AotWarningCount=0` or the existing baseline count with no new Lib.Db warning. The output must show both `AotSafeBulkShape` and `AotSafeBulkPublicApiReachability` steps completed.

### Task 10: Prepare Bulk Documentation Inputs

**Files:**
- No direct public documentation edits when this sub-plan is executed from the
  integrated v2.4.0 plan.
- The integrated plan's Task 5 owns final edits to `docs/02_advanced.md`,
  `docs/03_api_reference.md`, `docs/05_fluent_api_reference.md`,
  `docs/06_cookbook.md`, `docs/history.md`, and `.agents/skills/lib-db/SKILL.md`.

This task produces the bulk-specific documentation checklist for the integrated
documentation pass. If this bulk sub-plan is executed standalone outside the
integrated v2.4.0 plan, either perform these edits here and mark integrated Task
5 as already satisfied for bulk, or defer the edits to the integrated task. Do
not edit the same public docs twice or add duplicate history entries.

- [ ] **Step 1: Prepare advanced-docs content**

Ensure the integrated docs pass includes:

- legacy reflection `BulkInsertAsync<T>` is still available,
- new `BulkShape<T>` overloads are AOT-safe,
- staged DML is used for update/delete/upsert/merge,
- SQL Server `MERGE` is not the default engine,
- duplicate source keys are rejected before target DML,
- target keys must be backed by `PRIMARY KEY` or `UNIQUE` schema constraints,
- `DateOnly` and `TimeOnly` are normalized through the same provider-facing convention used by TVP.

- [ ] **Step 2: Prepare API reference content**

Ensure public signatures and result types are documented exactly as implemented.

- [ ] **Step 3: Prepare cookbook content**

Ensure the cookbook includes examples for:

- insert,
- update,
- delete by key,
- upsert,
- merge delete matched.

- [ ] **Step 4: Prepare history entry**

Ensure the final `docs/history.md` v2.4.0 entry includes:

```markdown
- Added AOT-safe `BulkShape<T>` bulk mutation APIs for insert, update, delete, upsert, and merge. These overloads avoid reflection and use `SqlBulkCopy` plus staged set-based DML. Existing reflection-based `BulkInsertAsync<T>` remains for compatibility.
```

### Task 11: Final Verification and Review Gates

**Files:**
- No planned source changes unless verification identifies an issue.

- [ ] **Step 1: Run targeted bulk unit tests**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeDataReaderTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkSqlBuilderTests*"
```

Expected: all targeted unit tests pass.

- [ ] **Step 2: Run targeted bulk integration tests**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkMutationTests*"
```

Expected: all targeted bulk integration tests pass against the local verification DB.

- [ ] **Step 3: Run AOT verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: no new Lib.Db trim/AOT warnings; AOT smoke executable completes.

- [ ] **Step 4: Run official release verification**

Run:

```powershell
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = "Verification/artifacts/logs/v240-release-verification-$stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$verificationOutput = & pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short *>&1
$exitCode = $LASTEXITCODE
$verificationOutput | Tee-Object -FilePath $log
$postLogExitCode = 0
if (-not (Test-Path -LiteralPath $log) -or (Get-Item -LiteralPath $log).Length -eq 0) {
    Write-Warning "Release verification log was not created or is empty: $log"
    $postLogExitCode = 1
}
pwsh -NoProfile -File Verification/scripts/Scan-VerificationArtifacts.ps1 -Paths $log
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
pwsh -NoProfile -File Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
if ($exitCode -ne 0) { exit $exitCode }
if ($postLogExitCode -ne 0) { exit $postLogExitCode }
```

Expected: release-grade verification completes successfully and leaves a durable
audit log under `Verification/artifacts/logs/`. The log must remain secret-safe:
no connection-string values, passwords, tokens, SQL parameter values, row values,
or cache payloads. Log creation, log non-emptiness, log-specific artifact
scanning, and generated-artifact tracking are hard failure gates.

- [ ] **Step 5: Run review gates**

Review checklist:

- no SQL Server `MERGE` statement in generated default SQL,
- no row value interpolation into SQL text,
- no reflection in new AOT-safe path,
- no `RequiresUnreferencedCode` on new shape overloads,
- no `RequiresDynamicCode`, `IL3050`, `MakeGenericType`, `Expression.Compile`, `DynamicMethod`, or `Reflection.Emit` in the new AOT-safe bulk path,
- cancellation token passed into async DB calls,
- transactions roll back staging + DML failures,
- staged update/delete/upsert/merge reject `UseTransaction = false` before opening a connection,
- insert `UseTransaction = false` is documented and tested as a non-atomic opt-out, not as a rollback-capable mode,
- rollback failure cannot replace the primary bulk failure in public results,
- final commit uses `CancellationToken.None` and documents that cancellation rollback is guaranteed only before commit begins,
- duplicate source keys fail through the stage unique index before target DML,
- target key uniqueness is documented as a database contract, not silently assumed in examples,
- delete uses key-only stage columns and matching `SqlBulkCopy` mappings,
- insert tests include a shape whose destination column names differ from CLR member names,
- success tests assert actual target row values and missing rows, not only affected-row counts,
- `DateOnly`, `TimeOnly`, enum, `Guid`, `decimal`, `byte[]`, and nullable values are normalized or passed through before `SqlBulkCopy` reads them,
- enum conversion is selected from shape metadata and does not call `value.GetType()` or `Enum.GetUnderlyingType(...)` per row,
- `BulkShapeDataReader<T>` tracks `IsClosed`, implements idempotent `Close()`/`Dispose(bool)`, clears current row state at EOF, throws on missing `GetOrdinal` names, reports `HasRows` without skipping the first row, and disposes the underlying enumerator exactly once,
- `CheckConstraints` is enabled by default and mapped into `SqlBulkCopyOptions`,
- non-cancellation errors return redacted failed `DbResult<T>` instead of escaping after rollback,
- cancellation attempts rollback before rethrow,
- `BulkMergeOptions.Validate()` cannot be bypassed through a `BulkWriteOptions` reference,
- `DeleteMatched` is rejected when combined with update or insert actions,
- `DeleteNotMatchedBySource` rejected,
- staged update/delete/upsert/merge failure and cancellation tests prove rollback after target DML has started,
- table and column identifiers reject malformed bracket syntax, whitespace around multipart separators, and identifier parts longer than 128 characters,
- docs distinguish legacy reflection bulk from AOT-safe bulk.

Run:

```powershell
rg -n "MERGE|GetProperties|value\\.GetType\\(|RequiresUnreferencedCode|RequiresDynamicCode|IL3050|MakeGenericType|Expression\\.Compile|DynamicMethod|Reflection\\.Emit|DeleteNotMatchedBySource|WriteToServerAsync|CREATE UNIQUE INDEX|DateOnly|TimeOnly|BulkMergeOptions|stageKeysOnly|DbResult<long>|CheckConstraints|Dispose\\(|Enum.GetUnderlyingType" Lib.Db Verification docs
```

Expected: findings are explainable and limited to allowed locations.

## Completion Criteria

Implementation is release-candidate ready only when:

- all tasks are checked off,
- pre-implementation AOT and release-verification baselines were captured before source changes,
- targeted unit and integration tests pass,
- shape minimum-column validation, duplicate destination-column rejection, required mutation keys, update non-key column validation, bracket escaping, SQL type rendering, nullable false null rejection, reader row-order preservation, duplicate source keys, malformed destination names, separator-whitespace rejection, 128-character identifier limits, unsupported `SqlDbType` pre-connection rejection, invalid batch/timeout options, destination-column mapping, key-only delete staging, value normalization for `DateOnly`/`TimeOnly`/enum/`Guid`/`decimal`/`byte[]`/nullable values, reader lifecycle/disposal/idempotency, EOF current clearing, missing ordinal failure, `CheckConstraints` defaulting, metadata-based enum conversion, invalid merge action combinations, AOT enum smoke, public bulk AOT reachability, staged-DML rollback/cancellation, staged-mutation `UseTransaction = false` rejection, insert non-atomic opt-out documentation/tests, rollback-on-cancellation, rollback-failure primary-error preservation, non-cancelable final commit, redacted general-error mapping, and polymorphic merge-option validation are covered by tests or explicit static gates,
- AOT verification passes with no new Lib.Db warnings,
- official release verification passes,
- durable release-verification log is captured, non-empty, post-scanned, ignored/untracked, and remains secret-safe,
- docs are updated,
- a final security/code review finds no blocking issue.

## Scope Reduction Gate

The approved v2.4.0 bulk scope is insert/update/delete/upsert/merge. If any
implementation constraint forces removing an approved operation, narrowing the
public API, or postponing part of the bulk suite to v2.5.0, stop before
continuing. Update this bulk sub-plan, the bulk sub-spec, the integrated spec and
plan, and public docs/history/API promises; rerun review on the reduced scope;
and obtain explicit user approval for the new v2.4.0 scope.
