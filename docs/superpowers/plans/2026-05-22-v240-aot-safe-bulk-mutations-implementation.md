# Lib.Db v2.4.0 AOT-Safe Bulk Mutations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add AOT-safe bulk insert, update, delete, upsert, and merge APIs without using reflection, runtime code generation, or SQL Server `MERGE` as the default engine.

**Architecture:** Add a static shape model, AOT-safe data reader, identifier-safe SQL builder, and staged bulk executor. Insert uses `SqlBulkCopy` directly; update/delete/upsert/merge bulk-copy into a local temp table and execute deterministic set-based DML inside one local SQL transaction.

**Tech Stack:** .NET 10, C# 14 preview syntax already used by the repo, Microsoft.Data.SqlClient, SQL Server local verification DB, xUnit v3, FluentAssertions, existing `DbResult<T>` and `IDbSession` patterns.

---

## Implementation Hold

The user approved the staged-DML design but explicitly requested this session to stop before implementation. This plan is ready for the next implementation session. Do not modify production or test code until the user asks to continue implementation.

## Reviewed Spec

Spec: `docs/superpowers/specs/2026-05-22-v240-aot-safe-bulk-mutations-design.md`

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

## File Structure

Create:

- `Lib.Db/Execution/Bulk/BulkShape.cs`
  Public shape entry point, immutable `BulkShape<T>`, and `BulkShapeBuilder<T>`.

- `Lib.Db/Execution/Bulk/BulkColumn.cs`
  Immutable column metadata and getter delegate wrapper.

- `Lib.Db/Execution/Bulk/BulkShapeDataReader.cs`
  Internal `DbDataReader` that streams `IEnumerable<T>` through `BulkShape<T>` without reflection.

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

    private sealed record BulkShapeRow(int Id, string Name, decimal Price);
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeTests"
```

Expected: build fails because `Lib.Db.Execution.Bulk.BulkShape` does not exist.

- [ ] **Step 3: Add minimal shape model**

Create `Lib.Db/Execution/Bulk/BulkColumn.cs`:

```csharp
using System.Data;

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
    private readonly List<BulkColumn<T>> _columns = [];

    public BulkShapeBuilder<T> Key<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool nullable = false,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
        => Add(destinationName, sqlDbType, row => getter(row), isKey: true, nullable, size, precision, scale);

    public BulkShapeBuilder<T> Column<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool nullable = true,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
        => Add(destinationName, sqlDbType, row => getter(row), isKey: false, nullable, size, precision, scale);

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
        if (string.IsNullOrWhiteSpace(destinationName))
            throw new ArgumentException("Destination column name cannot be empty.", nameof(destinationName));

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
}
```

- [ ] **Step 4: Run the tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeTests"
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
}

[Fact]
public void BulkMergeOptions_ShouldRejectDeleteNotMatchedBySourceInV240()
{
    BulkMergeOptions options = new()
    {
        Actions = BulkMergeActions.DeleteNotMatchedBySource
    };

    Action act = () => options.Validate();

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*DeleteNotMatchedBySource*not supported*v2.4.0*");
}
```

- [ ] **Step 2: Run the tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeTests"
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
    public bool CheckConstraints { get; init; }
    public bool KeepIdentity { get; init; }

    public void Validate()
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

    public new void Validate()
    {
        base.Validate();

        if (Actions == BulkMergeActions.None)
            throw new InvalidOperationException("Bulk merge actions cannot be empty.");

        if ((Actions & BulkMergeActions.DeleteNotMatchedBySource) != 0)
            throw new InvalidOperationException("DeleteNotMatchedBySource is not supported by Lib.Db v2.4.0 bulk merge.");
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
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeTests"
```

Expected: tests pass. Public `IDbSession` methods are added only in the task that implements each operation.

### Task 3: Add AOT-Safe Reader

**Files:**
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkShapeDataReaderTests.cs`
- Create: `Lib.Db/Execution/Bulk/BulkShapeDataReader.cs`

- [ ] **Step 1: Write reader tests**

Create `BulkShapeDataReaderTests.cs`:

```csharp
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
        reader.GetValue(2).Should().Be(new DateOnly(2026, 5, 22));
        reader.Read().Should().BeFalse();
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

    private sealed record BulkReaderRow(int Id, string Sku, DateOnly CreatedOn);
    private sealed record BulkNullableRow(int Id, string? Name);
}
```

- [ ] **Step 2: Run reader tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeDataReaderTests"
```

Expected: build fails because `BulkShapeDataReader<T>` does not exist.

- [ ] **Step 3: Implement minimal reader**

Create `Lib.Db/Execution/Bulk/BulkShapeDataReader.cs`:

```csharp
using System.Collections;
using System.Data;
using System.Data.Common;

namespace Lib.Db.Execution.Bulk;

public sealed class BulkShapeDataReader<T> : DbDataReader where T : notnull
{
    private readonly IEnumerator<T> _enumerator;
    private readonly BulkShape<T> _shape;
    private T? _current;

    public BulkShapeDataReader(IEnumerable<T> rows, BulkShape<T> shape)
    {
        _enumerator = rows.GetEnumerator();
        _shape = shape;
    }

    public override int FieldCount => _shape.Columns.Count;
    public long RowsRead { get; private set; }
    public override bool HasRows => true;
    public override bool IsClosed { get; protected set; }
    public override int RecordsAffected => -1;
    public override int Depth => 0;

    public override bool Read()
    {
        if (!_enumerator.MoveNext())
            return false;

        _current = _enumerator.Current;
        RowsRead++;
        return true;
    }

    public override string GetName(int ordinal) => _shape.Columns[ordinal].DestinationName;
    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _shape.Columns.Count; i++)
        {
            if (string.Equals(_shape.Columns[i].DestinationName, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public override object GetValue(int ordinal)
    {
        if (_current is null)
            throw new InvalidOperationException("Read must be called before reading values.");

        BulkColumn<T> column = _shape.Columns[ordinal];
        object? value = column.Getter(_current);
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

    public override string GetDataTypeName(int ordinal) => _shape.Columns[ordinal].SqlDbType.ToString();
    public override Type GetFieldType(int ordinal) => typeof(object);
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
}
```

- [ ] **Step 4: Run reader tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeDataReaderTests"
```

Expected: tests pass.

### Task 4: Add Identifier and SQL Builder Tests

**Files:**
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/BulkSqlBuilderTests.cs`
- Create: `Lib.Db/Execution/Bulk/BulkIdentifier.cs`
- Create: `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs`
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
    public void ParseTableName_ShouldRejectUnsafeNames(string input)
    {
        Action act = () => BulkIdentifier.ParseTableName(input);

        act.Should().Throw<ArgumentException>();
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

    private sealed record BulkSqlRow(int Id, string Sku, decimal Price);
}
```

- [ ] **Step 2: Run SQL builder tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkSqlBuilderTests"
```

Expected: build fails because SQL builder classes do not exist.

- [ ] **Step 3: Implement safe identifier parser**

Create `Lib.Db/Execution/Bulk/BulkIdentifier.cs`:

```csharp
namespace Lib.Db.Execution.Bulk;

internal readonly record struct BulkIdentifier(string Schema, string Name)
{
    public static BulkIdentifier ParseTableName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Destination table name cannot be empty.", nameof(input));

        string value = input.Trim();
        if (value.Contains(';') || value.Contains("--") || value.Contains("/*") || value.Contains("*/"))
            throw new ArgumentException("Destination table name contains unsupported SQL syntax.", nameof(input));

        if (value.Count(ch => ch == '[') != value.Count(ch => ch == ']'))
            throw new ArgumentException("Destination table name contains unbalanced brackets.", nameof(input));

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
        => value.Replace("].[", ".", StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizePart(string part, string original)
    {
        if (string.IsNullOrWhiteSpace(part))
            throw new ArgumentException($"Invalid destination table name '{original}'.");

        if (part.Any(char.IsWhiteSpace))
            throw new ArgumentException($"Invalid destination table name '{original}'.");

        return part;
    }
}
```

- [ ] **Step 4: Implement SQL type renderer and staging SQL builder**

Create `Lib.Db/Execution/Bulk/BulkSqlTypeRenderer.cs`:

```csharp
using System.Data;

namespace Lib.Db.Execution.Bulk;

internal static class BulkSqlTypeRenderer
{
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
}
```

- [ ] **Step 5: Run SQL builder tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkSqlBuilderTests"
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
using Xunit;

namespace Lib.Db.IntegrationTests.VerificationDb;

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

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(2);
        long count = await CountTargetAsync();
        count.Should().Be(2);
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

    private Task ClearTargetAsync()
        => _session.Use(TestDatabaseNames.Default)
            .Sql("DELETE FROM [gap].[BulkMutationTarget]")
            .ExecuteAsync(TestContext.Current.CancellationToken);

    private async Task<long> CountTargetAsync()
    {
        DbResult<long> result = await _session.Use(TestDatabaseNames.Default)
            .Sql("SELECT COUNT_BIG(*) FROM [gap].[BulkMutationTarget]")
            .QuerySingleAsync<long>(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value;
    }

    private sealed record BulkMutationRow(
        int Id,
        string Sku,
        string Name,
        int Qty,
        decimal Price,
        DateTime UpdatedAtUtc);
}
```

Before committing this test, replace `Sql(...).ExecuteAsync(...)` and `QuerySingleAsync<long>(...)` with the exact ad-hoc SQL method names used by existing tests in `Verification/projects/Lib.Db.IntegrationTests`.

- [ ] **Step 4: Run insert integration test and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests.BulkInsertAsync_WithStaticShape_ShouldInsertRows"
```

Expected: fails because the public insert overload or executor insert path is not implemented yet.

- [ ] **Step 5: Implement `SqlBulkCopy` insert path**

In `BulkWriteExecutor`, implement:

- instance connection resolution through existing `DbSession` infrastructure,
- destination validation through `BulkIdentifier.ParseTableName`,
- option validation,
- `SqlBulkCopyOptions` mapping,
- local transaction when `UseTransaction = true`,
- `SqlBulkCopy.ColumnMappings` from shape column names,
- `BulkShapeDataReader<T>` as the data source,
- `WriteToServerAsync(reader, ct)`,
- return `reader.RowsRead`.

Use this core pattern inside the existing repository error-handling style:

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
        await transaction.CommitAsync(ct).ConfigureAwait(false);

    return DbResult<long>.Ok(reader.RowsRead);
}
catch
{
    if (transaction is not null)
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
    throw;
}
finally
{
    transaction?.Dispose();
}
```

Use the exact local connection-opening and success-result APIs identified in Task 5. Do not introduce a second connection resolver.

- [ ] **Step 6: Run insert integration test and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests.BulkInsertAsync_WithStaticShape_ShouldInsertRows"
```

Expected: test passes.

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

    result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    result.Value.Should().Be(1);
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

    result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    result.Value.Should().Be(1);
    (await CountTargetAsync()).Should().Be(1);
}
```

Add `SeedRowsAsync()` using the new AOT-safe insert method from Task 6.

- [ ] **Step 3: Run update/delete tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests.BulkUpdateAsync|FullyQualifiedName~BulkMutationTests.BulkDeleteAsync"
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
- open connection,
- begin transaction when configured,
- create stage table,
- bulk copy rows into stage,
- execute action SQL,
- capture affected row count,
- drop stage table,
- commit transaction,
- roll back on failure.

- [ ] **Step 6: Run update/delete tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests.BulkUpdateAsync|FullyQualifiedName~BulkMutationTests.BulkDeleteAsync"
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

    result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    result.Value.Updated.Should().Be(1);
    result.Value.Inserted.Should().Be(1);
    result.Value.TotalAffected.Should().Be(2);
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

    result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    result.Value.Deleted.Should().Be(1);
    result.Value.TotalAffected.Should().Be(1);
    (await CountTargetAsync()).Should().Be(1);
}
```

- [ ] **Step 3: Run upsert/merge tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests.BulkUpsertAsync|FullyQualifiedName~BulkMutationTests.BulkMergeAsync"
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
- runs update when selected,
- runs insert-missing when selected,
- runs delete-matched when selected,
- rejects `DeleteNotMatchedBySource`,
- returns separated counts.

Use `ExecuteNonQueryAsync(ct)` for each action and store the returned affected rows immediately.

- [ ] **Step 6: Run upsert/merge tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests.BulkUpsertAsync|FullyQualifiedName~BulkMutationTests.BulkMergeAsync"
```

Expected: tests pass.

### Task 9: Add AOT Smoke Coverage

**Files:**
- Modify: `Verification/projects/Lib.Db.AotVerification/Program.cs`
- Modify: `Verification/scripts/Invoke-Aot.ps1` only if warning baseline behavior needs adjustment

- [ ] **Step 1: Add AOT smoke step**

In `Program.cs`, add:

```csharp
RunStep("AotSafeBulkShape", VerifyAotSafeBulkShape);
```

Add:

```csharp
static void VerifyAotSafeBulkShape()
{
    BulkShape<AotBulkRow> shape = BulkShape.For<AotBulkRow>()
        .Key("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64, nullable: false)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Build();

    using BulkShapeDataReader<AotBulkRow> reader = new(
        [new AotBulkRow(1, "AOT-SKU", 3)],
        shape);

    if (!reader.Read())
        throw new InvalidOperationException("AOT bulk reader did not read the smoke row.");

    if (!Equals(reader.GetValue(1), "AOT-SKU"))
        throw new InvalidOperationException("AOT bulk reader returned an unexpected value.");
}

internal readonly record struct AotBulkRow(int Id, string Sku, int Qty);
```

- [ ] **Step 2: Run AOT verification and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: `AotWarningCount=0` or the existing baseline count with no new Lib.Db warning. The output must show the `AotSafeBulkShape` step completed.

### Task 10: Update Documentation

**Files:**
- Modify: `docs/02_advanced.md`
- Modify: `docs/03_api_reference.md`
- Modify: `docs/05_fluent_api_reference.md`
- Modify: `docs/06_cookbook.md`
- Modify: `docs/history.md`

- [ ] **Step 1: Update advanced docs**

Add a section explaining:

- legacy reflection `BulkInsertAsync<T>` is still available,
- new `BulkShape<T>` overloads are AOT-safe,
- staged DML is used for update/delete/upsert/merge,
- SQL Server `MERGE` is not the default engine.

- [ ] **Step 2: Update API reference**

Add public signatures and result types exactly as implemented.

- [ ] **Step 3: Update cookbook**

Add examples for:

- insert,
- update,
- delete by key,
- upsert,
- merge delete matched.

- [ ] **Step 4: Update history**

Add a v2.4.0 entry:

```markdown
- Added AOT-safe `BulkShape<T>` bulk mutation APIs for insert, update, delete, upsert, and merge. These overloads avoid reflection and use `SqlBulkCopy` plus staged set-based DML. Existing reflection-based `BulkInsertAsync<T>` remains for compatibility.
```

### Task 11: Final Verification and Review Gates

**Files:**
- No planned source changes unless verification identifies an issue.

- [ ] **Step 1: Run targeted bulk unit tests**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeTests|FullyQualifiedName~BulkShapeDataReaderTests|FullyQualifiedName~BulkSqlBuilderTests"
```

Expected: all targeted unit tests pass.

- [ ] **Step 2: Run targeted bulk integration tests**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkMutationTests"
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
pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short
```

Expected: release-grade verification completes successfully.

- [ ] **Step 5: Run review gates**

Review checklist:

- no SQL Server `MERGE` statement in generated default SQL,
- no row value interpolation into SQL text,
- no reflection in new AOT-safe path,
- no `RequiresUnreferencedCode` on new shape overloads,
- cancellation token passed into async DB calls,
- transactions roll back staging + DML failures,
- `DeleteNotMatchedBySource` rejected,
- docs distinguish legacy reflection bulk from AOT-safe bulk.

Run:

```powershell
rg -n "MERGE|GetProperties|RequiresUnreferencedCode|DeleteNotMatchedBySource|WriteToServerAsync" Lib.Db Verification docs
```

Expected: findings are explainable and limited to allowed locations.

## Completion Criteria

Implementation is release-candidate ready only when:

- all tasks are checked off,
- targeted unit and integration tests pass,
- AOT verification passes with no new Lib.Db warnings,
- official release verification passes,
- docs are updated,
- a final security/code review finds no blocking issue.
