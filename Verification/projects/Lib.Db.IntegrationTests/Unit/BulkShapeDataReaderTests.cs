using System.Collections;
using System.Data;
using Lib.Db.Execution.Bulk;

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
        reader.GetOrdinal("sku").Should().Be(1);
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
        BulkShape<BulkReaderRow> shape = CreateReaderShape();
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
        BulkShape<BulkReaderRow> shape = CreateReaderShape();
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
        BulkShape<BulkReaderRow> shape = CreateReaderShape();

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
        BulkShape<BulkReaderRow> shape = CreateReaderShape();

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
        BulkShape<BulkReaderRow> shape = CreateReaderShape();

        using BulkShapeDataReader<BulkReaderRow> reader = new([], shape);

        Action act = () => reader.GetOrdinal("MissingColumn");

        act.Should().Throw<IndexOutOfRangeException>()
            .WithMessage("*MissingColumn*");
    }

    [Fact]
    public void HasRows_ShouldReturnFalseForEmptyRows()
    {
        BulkShape<BulkReaderRow> shape = CreateReaderShape();

        using BulkShapeDataReader<BulkReaderRow> reader = new([], shape);

        reader.HasRows.Should().BeFalse();
        reader.Read().Should().BeFalse();
    }

    private static BulkShape<BulkReaderRow> CreateReaderShape()
        => BulkShape.For<BulkReaderRow>()
            .Key("Id", SqlDbType.Int, static row => row.Id)
            .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 32, nullable: false)
            .Column("CreatedOn", SqlDbType.Date, static row => row.CreatedOn)
            .Build();

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
