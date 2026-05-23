// ============================================================================
// File: Unit/TvpCoreCoverageTests.cs
// Role: Focused coverage for Lib.Db.Execution.Tvp core readers and caches
// ============================================================================

using System.Collections;
using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Tvp;
using Microsoft.Data.SqlClient.Server;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TvpCoreCoverageTests
{
    [Fact]
    public void TypedColumnBuffer_ShouldResizeConvertSpecialTypesAndRejectUseAfterDispose()
    {
        var numbers = new TypedColumnBuffer<int>(initialCapacity: 1);
        for (int i = 0; i < 40; i++)
            numbers.Add(i);

        numbers.GetTypedValue(39).Should().Be(39);
        numbers.GetValue(7).Should().Be(7);
        numbers.IsNull(7).Should().BeFalse();

        var date = Buffer(new DateOnly(2026, 5, 18));
        var time = Buffer(new TimeOnly(14, 30, 15));
        var half = Buffer((Half)1.5f);
        var nullableText = NullStringBuffer();

        date.GetValue(0).Should().Be(new DateTime(2026, 5, 18));
        time.GetValue(0).Should().Be(new TimeSpan(14, 30, 15));
        half.GetValue(0).Should().Be(1.5f);
        nullableText.IsNull(0).Should().BeTrue();
        nullableText.GetValue(0).Should().BeNull();

        numbers.Dispose();
        numbers.Invoking(static buffer => buffer.Add(41))
            .Should()
            .Throw<ObjectDisposedException>();

        numbers.Invoking(static buffer => buffer.Dispose())
            .Should()
            .NotThrow();
    }

    [Fact]
    public async Task ColumnarTvpReader_ShouldExposeTypedAccessorsMetadataAndSlices()
    {
        Guid guid = Guid.Parse("ba48c932-57f9-4f77-8fc6-748bc30243df");
        DateOnly shipDate = new(2026, 5, 18);
        TimeOnly startsAt = new(9, 15, 30);
        byte[] payload = [1, 2, 3, 4, 5];

        TvpColumnShape[] shapes =
        [
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Optional("MaybeId", typeof(int)),
            TvpColumnShape.Required("Name", typeof(string), size: 24),
            TvpColumnShape.Required("Payload", typeof(byte[]), size: 5),
            TvpColumnShape.Required("Big", typeof(long)),
            TvpColumnShape.Required("Flag", typeof(bool)),
            TvpColumnShape.Required("Amount", typeof(decimal), precision: 18, scale: 2),
            TvpColumnShape.Required("Ratio", typeof(double)),
            TvpColumnShape.Required("ShipDate", typeof(DateTime)),
            TvpColumnShape.Required("StartsAt", typeof(DateTime)),
            TvpColumnShape.Required("Tiny", typeof(byte)),
            TvpColumnShape.Required("Letter", typeof(char)),
            TvpColumnShape.Required("HalfValue", typeof(float)),
            TvpColumnShape.Required("TraceId", typeof(Guid)),
            TvpColumnShape.Required("Small", typeof(short)),
            TvpColumnShape.Optional("OptionalText", typeof(string))
        ];

        ColumnBuffer[] buffers =
        [
            Buffer(42),
            Buffer<int?>(43),
            Buffer("abcdef"),
            Buffer<byte[]>(payload),
            Buffer<long?>(9876543210L),
            Buffer<bool?>(true),
            Buffer<decimal?>(123.45m),
            Buffer<double?>(3.25d),
            Buffer(shipDate),
            Buffer<TimeOnly?>(startsAt),
            Buffer<byte?>(8),
            Buffer<char?>('Z'),
            Buffer<Half?>((Half)2.5f),
            Buffer<Guid?>(guid),
            Buffer<short?>(12),
            NullStringBuffer()
        ];

        DataTable schema = RuntimeTvpDataReader.BuildSchemaTable(shapes);
        await using var reader = new ColumnarTvpReader(buffers, rowCount: 1, Ordinals(shapes), schema);

        reader.FieldCount.Should().Be(shapes.Length);
        reader.HasRows.Should().BeTrue();
        reader.IsClosed.Should().BeFalse();
        reader.Depth.Should().Be(0);
        reader.RecordsAffected.Should().Be(-1);
        reader.GetSchemaTable().Should().BeSameAs(schema);
        reader.GetOrdinal("name").Should().Be(2);
        reader.Invoking(static r => r.GetOrdinal("Missing"))
            .Should()
            .Throw<IndexOutOfRangeException>();

        reader.Read().Should().BeTrue();
        reader[0].Should().Be(42);
        reader["Name"].Should().Be("abcdef");
        reader.GetInt32(0).Should().Be(42);
        reader.GetInt32(1).Should().Be(43);
        reader.GetString(2).Should().Be("abcdef");
        reader.GetInt64(4).Should().Be(9876543210L);
        reader.GetBoolean(5).Should().BeTrue();
        reader.GetDecimal(6).Should().Be(123.45m);
        reader.GetDouble(7).Should().Be(3.25d);
        reader.GetDateTime(8).Should().Be(shipDate.ToDateTime(TimeOnly.MinValue));
        reader.GetDateTime(9).Should().Be(DateTime.Today.Add(startsAt.ToTimeSpan()));
        reader.GetByte(10).Should().Be(8);
        reader.GetChar(11).Should().Be('Z');
        reader.GetFloat(12).Should().Be(2.5f);
        reader.GetGuid(13).Should().Be(guid);
        reader.GetInt16(14).Should().Be(12);
        reader.IsDBNull(15).Should().BeTrue();
        reader.GetValue(8).Should().Be(shipDate.ToDateTime(TimeOnly.MinValue));
        reader.GetValue(9).Should().Be(startsAt.ToTimeSpan());
        reader.GetValue(12).Should().Be(2.5f);
        reader.GetName(2).Should().Be("Name");
        reader.GetFieldType(3).Should().Be(typeof(byte[]));
        reader.GetDataTypeName(0).Should().Be(nameof(Int32));
        reader.NextResult().Should().BeFalse();

        object[] values = new object[4];
        reader.GetValues(values).Should().Be(4);
        values[0].Should().Be(42);
        values[1].Should().Be(43);
        values[2].Should().Be("abcdef");
        ((byte[])values[3]).Should().Equal(payload);

        reader.GetBytes(3, 0, null, 0, 0).Should().Be(5);
        byte[] byteSlice = new byte[3];
        reader.GetBytes(3, 1, byteSlice, 0, byteSlice.Length).Should().Be(3);
        byteSlice.Should().Equal(2, 3, 4);
        reader.GetBytes(3, 99, byteSlice, 0, byteSlice.Length).Should().Be(0);
        reader.GetBytes(3, -1, byteSlice, 0, byteSlice.Length).Should().Be(0);
        reader.Invoking(r => r.GetBytes(3, 0, byteSlice, 0, -1))
            .Should()
            .Throw<IndexOutOfRangeException>();

        reader.GetChars(2, 0, null, 0, 0).Should().Be(6);
        char[] charSlice = new char[3];
        reader.GetChars(2, 2, charSlice, 0, charSlice.Length).Should().Be(3);
        charSlice.Should().Equal('c', 'd', 'e');
        reader.GetChars(2, 99, charSlice, 0, charSlice.Length).Should().Be(0);
        reader.GetChars(2, -1, charSlice, 0, charSlice.Length).Should().Be(0);
        reader.Invoking(r => r.GetChars(2, 0, charSlice, 0, -1))
            .Should()
            .Throw<IndexOutOfRangeException>();

        reader.Read().Should().BeFalse();
        await reader.DisposeAsync();
        reader.IsClosed.Should().BeTrue();
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void ColumnarTvpReader_DisposeFalse_ShouldCloseReaderWithoutDisposingColumnBuffers()
    {
        TvpColumnShape[] shapes = [TvpColumnShape.Required("Id", typeof(int))];
        TypedColumnBuffer<int> buffer = Buffer(1);
        var reader = new ColumnarTvpReader([buffer], rowCount: 1, Ordinals(shapes));

        MethodInfo dispose = typeof(ColumnarTvpReader).GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        dispose.Invoke(reader, [false]);

        reader.IsClosed.Should().BeTrue();
        buffer.Invoking(static column => column.Add(2))
            .Should()
            .NotThrow();

        buffer.Dispose();
        reader.Dispose();
    }

    [Fact]
    public void ColumnarTvpReader_ShouldRejectMetadataAccessWhenSchemaIsMissing()
    {
        ColumnBuffer[] buffers = [Buffer(1)];
        using var reader = new ColumnarTvpReader(
            buffers,
            rowCount: 1,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 0
            });

        reader.GetSchemaTable().Should().BeNull();
        reader.Read().Should().BeTrue();
        reader.GetOrdinal("Id").Should().Be(0);
        reader.Invoking(static r => r.GetName(0))
            .Should()
            .Throw<NotSupportedException>();
        reader.Invoking(static r => r.GetFieldType(0))
            .Should()
            .Throw<NotSupportedException>();
        reader.Invoking(static r => r.GetDataTypeName(0))
            .Should()
            .Throw<NotSupportedException>();
        reader.Invoking(static r => r.GetString(0))
            .Should()
            .Throw<InvalidCastException>();
    }

    [Fact]
    public void ColumnarTvpReader_ShouldCoverNullableNonNullableAndInvalidGetterBranches()
    {
        Guid traceId = Guid.Parse("a6019e51-8480-4b1d-aaf4-1cbbe953c583");
        using var reader = new ColumnarTvpReader(
            [
                Buffer(1),
                Buffer("wrong"),
                Buffer(2L),
                Buffer(true),
                Buffer(1.5d),
                Buffer(new DateTime(2026, 5, 18, 1, 2, 3)),
                Buffer<DateTime?>(new DateTime(2026, 5, 19)),
                Buffer<DateOnly?>(new DateOnly(2026, 5, 20)),
                Buffer(new TimeOnly(7, 8, 9)),
                Buffer((byte)9),
                Buffer('Q'),
                Buffer<float?>(3.5f),
                Buffer(traceId),
                Buffer((short)4),
                NullByteArrayBuffer(),
                NullNonNullableStringBuffer()
            ],
            rowCount: 1,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 0
            });

        reader.Read().Should().BeTrue();
        reader.Invoking(static r => r.GetInt32(1)).Should().Throw<InvalidCastException>();
        reader.GetInt64(2).Should().Be(2L);
        reader.Invoking(static r => r.GetInt64(1)).Should().Throw<InvalidCastException>();
        reader.GetBoolean(3).Should().BeTrue();
        reader.Invoking(static r => r.GetBoolean(1)).Should().Throw<InvalidCastException>();
        reader.Invoking(static r => r.GetDecimal(1)).Should().Throw<InvalidCastException>();
        reader.GetDouble(4).Should().Be(1.5d);
        reader.Invoking(static r => r.GetDouble(1)).Should().Throw<InvalidCastException>();
        reader.GetDateTime(5).Should().Be(new DateTime(2026, 5, 18, 1, 2, 3));
        reader.GetDateTime(6).Should().Be(new DateTime(2026, 5, 19));
        reader.GetDateTime(7).Should().Be(new DateTime(2026, 5, 20));
        reader.GetDateTime(8).Should().Be(DateTime.Today.Add(new TimeOnly(7, 8, 9).ToTimeSpan()));
        reader.Invoking(static r => r.GetDateTime(1)).Should().Throw<InvalidCastException>();
        reader.GetByte(9).Should().Be(9);
        reader.Invoking(static r => r.GetByte(1)).Should().Throw<InvalidCastException>();
        reader.GetChar(10).Should().Be('Q');
        reader.Invoking(static r => r.GetChar(1)).Should().Throw<InvalidCastException>();
        reader.GetFloat(11).Should().Be(3.5f);
        reader.Invoking(static r => r.GetFloat(1)).Should().Throw<InvalidCastException>();
        reader.GetGuid(12).Should().Be(traceId);
        reader.Invoking(static r => r.GetGuid(1)).Should().Throw<InvalidCastException>();
        reader.GetInt16(13).Should().Be(4);
        reader.Invoking(static r => r.GetInt16(1)).Should().Throw<InvalidCastException>();
        reader.GetBytes(14, 0, null, 0, 0).Should().Be(0);
        reader.GetChars(15, 0, null, 0, 0).Should().Be(0);
        Action enumerate = () => reader.GetEnumerator().MoveNext();
        enumerate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldExposeStateNormalizeValuesAndCloseEnumerator()
    {
        var rows = new[]
        {
            new RuntimeRow(
                Id: 7,
                Name: null,
                ShipDate: new DateOnly(2026, 5, 18),
                StartsAt: new TimeOnly(10, 11, 12),
                HalfValue: (Half)3.5f,
                TraceId: Guid.Parse("7c7ec7cb-574b-4b2f-b73c-8bc2ef0102ff"),
                Small: 4,
                Big: 9876543210L,
                Flag: true,
                Amount: 42.42m,
                Ratio: 1.25d,
                Real: 2.25f,
                Tiny: 9,
                Letter: 'Q')
        };

        TvpColumnShape[] columns =
        [
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Optional("Name", typeof(string)),
            TvpColumnShape.Required("ShipDate", typeof(DateTime)),
            TvpColumnShape.Required("StartsAt", typeof(TimeSpan)),
            TvpColumnShape.Required("HalfValue", typeof(float)),
            TvpColumnShape.Required("TraceId", typeof(Guid)),
            TvpColumnShape.Required("Small", typeof(short)),
            TvpColumnShape.Required("Big", typeof(long)),
            TvpColumnShape.Required("Flag", typeof(bool)),
            TvpColumnShape.Required("Amount", typeof(decimal)),
            TvpColumnShape.Required("Ratio", typeof(double)),
            TvpColumnShape.Required("Real", typeof(float)),
            TvpColumnShape.Required("Tiny", typeof(byte)),
            TvpColumnShape.Required("Letter", typeof(char))
        ];

        using var reader = RuntimeTvpDataReader.Create(rows, columns, TvpBindingPolicy.Strict);

        reader.FieldCount.Should().Be(columns.Length);
        reader.HasRows.Should().BeTrue();
        reader.IsClosed.Should().BeFalse();
        reader.Depth.Should().Be(0);
        reader.RecordsAffected.Should().Be(-1);
        reader.Invoking(static r => r.GetValue(0))
            .Should()
            .Throw<InvalidOperationException>();
        reader.Invoking(static r => r.GetValues(null!))
            .Should()
            .Throw<ArgumentNullException>();

        reader.Read().Should().BeTrue();
        reader[0].Should().Be(7);
        reader["Name"].Should().Be(DBNull.Value);
        reader.IsDBNull(1).Should().BeTrue();
        reader.GetOrdinal("halfvalue").Should().Be(4);
        reader.GetName(0).Should().Be("Id");
        reader.GetDataTypeName(2).Should().Be(nameof(DateTime));
        reader.GetFieldType(4).Should().Be(typeof(float));
        reader.GetDateTime(2).Should().Be(new DateTime(2026, 5, 18));
        reader.GetValue(3).Should().Be(new TimeSpan(10, 11, 12));
        reader.GetFloat(4).Should().Be(3.5f);
        reader.GetGuid(5).Should().Be(rows[0].TraceId);
        reader.GetInt16(6).Should().Be(4);
        reader.GetInt64(7).Should().Be(9876543210L);
        reader.GetBoolean(8).Should().BeTrue();
        reader.GetDecimal(9).Should().Be(42.42m);
        reader.GetDouble(10).Should().Be(1.25d);
        reader.GetFloat(11).Should().Be(2.25f);
        reader.GetByte(12).Should().Be(9);
        reader.GetChar(13).Should().Be('Q');
        reader.NextResult().Should().BeFalse();
        reader.Invoking(static r => r.GetOrdinal("Missing"))
            .Should()
            .Throw<IndexOutOfRangeException>();
        reader.Invoking(r => r.GetBytes(0, 0, null, 0, 0))
            .Should()
            .Throw<NotSupportedException>();
        reader.Invoking(r => r.GetChars(0, 0, null, 0, 0))
            .Should()
            .Throw<NotSupportedException>();

        object[] values = new object[5];
        reader.GetValues(values).Should().Be(5);
        values.Should().Equal(7, DBNull.Value, new DateTime(2026, 5, 18), new TimeSpan(10, 11, 12), 3.5f);

        DataTable schema = reader.GetSchemaTable();
        schema.Rows[4][SchemaTableColumn.DataType].Should().Be(typeof(float));
        schema.Rows[4][SchemaTableColumn.AllowDBNull].Should().Be(false);

        reader.Close();
        reader.IsClosed.Should().BeTrue();
        reader.HasRows.Should().BeFalse();
        reader.Read().Should().BeFalse();
        reader.Invoking(static r => r.Close())
            .Should()
            .NotThrow();
    }

    [Fact]
    public void RuntimeTvpDataReader_NormalizeValue_ShouldPreserveSpecialTypesWhenFieldTypeDoesNotMatch()
    {
        object half = (Half)1.5f;
        object dateOnly = new DateOnly(2026, 5, 18);
        object timeOnly = new TimeOnly(10, 11, 12);

        RuntimeTvpDataReader.NormalizeValue(half, typeof(double)).Should().Be(half);
        RuntimeTvpDataReader.NormalizeValue(dateOnly, typeof(DateOnly)).Should().Be(dateOnly);
        RuntimeTvpDataReader.NormalizeValue(timeOnly, typeof(TimeOnly)).Should().Be(timeOnly);
    }

    [Fact]
    public void RuntimeTvpDataReader_DisposeFalse_ShouldNotCloseOrDisposeEnumerator()
    {
        var rows = new DisposableEnumerable<RuntimeRow>(
        [
            new RuntimeRow(
                Id: 7,
                Name: "runtime",
                ShipDate: new DateOnly(2026, 5, 18),
                StartsAt: new TimeOnly(10, 11, 12),
                HalfValue: (Half)3.5f,
                TraceId: Guid.Parse("7c7ec7cb-574b-4b2f-b73c-8bc2ef0102ff"),
                Small: 4,
                Big: 9876543210L,
                Flag: true,
                Amount: 42.42m,
                Ratio: 1.25d,
                Real: 2.25f,
                Tiny: 9,
                Letter: 'Q')
        ]);
        TvpColumnShape[] columns = [TvpColumnShape.Required("Id", typeof(int))];
        using RuntimeTvpDataReader reader = RuntimeTvpDataReader.Create(
            rows,
            columns,
            TvpBindingPolicy.Strict);
        MethodInfo dispose = typeof(RuntimeTvpDataReader).GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        dispose.Invoke(reader, [false]);

        reader.IsClosed.Should().BeFalse();
        rows.Disposed.Should().BeFalse();
        reader.Read().Should().BeTrue();
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldHandleDictionaryAdaptiveAndStrictMissingColumns()
    {
        TvpColumnShape[] columns =
        [
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Optional("Note", typeof(string))
        ];
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 5
            }
        };

        using RuntimeTvpDataReader adaptive = RuntimeTvpDataReader.Create(
            rows,
            typeof(IReadOnlyDictionary<string, object?>),
            columns,
            TvpBindingPolicy.Adaptive);

        adaptive.Read().Should().BeTrue();
        adaptive.GetInt32(0).Should().Be(5);
        adaptive.GetValue(1).Should().Be(DBNull.Value);

        using RuntimeTvpDataReader strict = RuntimeTvpDataReader.Create(
            rows,
            typeof(IReadOnlyDictionary<string, object?>),
            columns,
            TvpBindingPolicy.Strict);

        strict.Read().Should().BeTrue();
        strict.Invoking(static r => r.GetValue(1))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*required column 'Note'*");

        Action emptyColumns = () => RuntimeTvpDataReader.Create(
            rows,
            typeof(IReadOnlyDictionary<string, object?>),
            Array.Empty<TvpColumnShape>(),
            TvpBindingPolicy.Strict);

        emptyColumns.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SqlDataRecordTvpEnumerable_ShouldCreateMetadataSetValuesAndRejectNullRows()
    {
        TvpSchemaDescriptor descriptor = Descriptor(
            "dbo.T_RecordCoverage",
            Column("Name", 0, SqlDbType.NVarChar, maxLength: 80, isNullable: true),
            Column("Payload", 1, SqlDbType.VarBinary, maxLength: 4),
            Column("Amount", 2, SqlDbType.Decimal, precision: 12, scale: 3),
            Column("ShipDate", 3, SqlDbType.Date),
            Column("StartsAt", 4, SqlDbType.Time),
            Column("TraceId", 5, SqlDbType.UniqueIdentifier),
            Column("Maybe", 6, SqlDbType.NVarChar, maxLength: -1, isNullable: true),
            Column("Quantity", 7, SqlDbType.Int));
        RuntimeTvpRowShape shape = TvpRowAccessorCache.GetOrAdd(typeof(SqlRecordRow), descriptor, TvpBindingPolicy.Strict);
        var rows = new[]
        {
            new SqlRecordRow(
                "sku-1",
                [9, 8, 7, 6],
                123.456m,
                new DateOnly(2026, 5, 18),
                new TimeOnly(8, 45, 0),
                Guid.Parse("3a62a1ce-51bc-4839-a49f-3cc8a2d80583"),
                null,
                4)
        };

        var enumerable = new SqlDataRecordTvpEnumerable(rows, shape);

        using IEnumerator<SqlDataRecord> enumerator = enumerable.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();
        SqlDataRecord record = enumerator.Current;
        record.FieldCount.Should().Be(8);
        record.GetName(0).Should().Be("Name");
        record.GetValue(0).Should().Be("sku-1");
        record.GetValue(1).Should().BeEquivalentTo(new byte[] { 9, 8, 7, 6 });
        record.GetValue(2).Should().Be(123.456m);
        record.GetValue(3).Should().Be(new DateTime(2026, 5, 18));
        record.GetValue(4).Should().Be(new TimeSpan(8, 45, 0));
        record.GetValue(5).Should().Be(rows[0].TraceId);
        record.IsDBNull(6).Should().BeTrue();
        record.GetValue(7).Should().Be(4);
        enumerator.MoveNext().Should().BeFalse();

        IEnumerator nonGeneric = ((IEnumerable)enumerable).GetEnumerator();
        using (nonGeneric as IDisposable)
        {
            nonGeneric.MoveNext().Should().BeTrue();
            nonGeneric.Current.Should().BeOfType<SqlDataRecord>();
        }

        Action nullRows = () => _ = new SqlDataRecordTvpEnumerable(null!, shape);
        nullRows.Should().Throw<ArgumentNullException>();

        Action nullShape = () => _ = new SqlDataRecordTvpEnumerable(rows, null!);
        nullShape.Should().Throw<ArgumentNullException>();

        var nullRowEnumerable = new SqlDataRecordTvpEnumerable(new object?[] { null }, shape);
        nullRowEnumerable.Invoking(static e => e.ToList())
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*null row*");
    }

    [Fact]
    public void SqlDataRecordTvpEnumerable_ShouldInferMetadataFromClrTypes()
    {
        DateTimeOffset timestamp = new(2026, 5, 18, 9, 30, 0, TimeSpan.Zero);
        Guid traceId = Guid.Parse("57a94264-d150-4020-b536-7870d29a6ca3");
        byte[] payload = [5, 4, 3];
        TvpColumnShape[] columns =
        [
            TvpColumnShape.Optional("TextValue", typeof(string), size: 12),
            TvpColumnShape.Required("IntValue", typeof(int)),
            TvpColumnShape.Required("LongValue", typeof(long)),
            TvpColumnShape.Required("ShortValue", typeof(short)),
            TvpColumnShape.Required("ByteValue", typeof(byte)),
            TvpColumnShape.Required("Flag", typeof(bool)),
            TvpColumnShape.Required("Amount", typeof(decimal), precision: 10, scale: 2),
            TvpColumnShape.Required("DoubleValue", typeof(double)),
            TvpColumnShape.Required("FloatValue", typeof(float)),
            TvpColumnShape.Required("CreatedAt", typeof(DateTime)),
            TvpColumnShape.Required("Timestamp", typeof(DateTimeOffset)),
            TvpColumnShape.Required("Duration", typeof(TimeSpan)),
            TvpColumnShape.Required("TraceId", typeof(Guid)),
            TvpColumnShape.Required("Payload", typeof(byte[]), size: 3),
            TvpColumnShape.FromSql<byte[]>("PayloadMax", SqlDbType.VarBinary, allowNull: false, size: 0, precision: 0, scale: 0),
            TvpColumnShape.FromSql<decimal>("DefaultDecimal", SqlDbType.Decimal, allowNull: false, size: 0, precision: 0, scale: 0),
            TvpColumnShape.Optional("VariantValue", typeof(Uri))
        ];
        Func<object, object?>[] accessors =
        [
            _ => "abc",
            _ => 1,
            _ => 2L,
            _ => (short)3,
            _ => (byte)4,
            _ => true,
            _ => 5.25m,
            _ => 6.5d,
            _ => 7.5f,
            _ => new DateTime(2026, 5, 18, 1, 2, 3),
            _ => timestamp,
            _ => new TimeSpan(1, 2, 3),
            _ => traceId,
            _ => payload,
            _ => payload,
            _ => 8.25m,
            _ => null
        ];
        var shape = new RuntimeTvpRowShape(
            typeof(object),
            columns,
            accessors,
            Ordinals(columns),
            RuntimeTvpDataReader.BuildSchemaTable(columns));
        var enumerable = new SqlDataRecordTvpEnumerable(new object[] { new() }, shape);

        using IEnumerator<SqlDataRecord> enumerator = enumerable.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();
        SqlDataRecord record = enumerator.Current;

        record.FieldCount.Should().Be(columns.Length);
        record.GetValue(0).Should().Be("abc");
        record.GetValue(1).Should().Be(1);
        record.GetValue(2).Should().Be(2L);
        record.GetValue(3).Should().Be((short)3);
        record.GetValue(4).Should().Be((byte)4);
        record.GetValue(5).Should().Be(true);
        record.GetValue(6).Should().Be(5.25m);
        record.GetValue(7).Should().Be(6.5d);
        record.GetValue(8).Should().Be(7.5f);
        record.GetValue(9).Should().Be(new DateTime(2026, 5, 18, 1, 2, 3));
        record.GetValue(10).Should().Be(timestamp);
        record.GetValue(11).Should().Be(new TimeSpan(1, 2, 3));
        record.GetValue(12).Should().Be(traceId);
        record.GetValue(13).Should().BeEquivalentTo(payload);
        record.GetValue(14).Should().BeEquivalentTo(payload);
        record.GetValue(15).Should().Be(8.25m);
        record.IsDBNull(16).Should().BeTrue();
    }

    [Fact]
    public void TvpAccessorRegistry_ShouldRegisterFallbackAccessorsAndResolveByType()
    {
        TvpAccessorRegistry.TryGet<RegistryInitialCoverageRow>(out TvpAccessors<RegistryInitialCoverageRow>? missing)
            .Should()
            .BeFalse();
        missing.Should().BeNull();

        TvpAccessors<RegistryInitialCoverageRow> accessors = TvpAccessorCache.GetTypedAccessors<RegistryInitialCoverageRow>();

        TvpAccessorRegistry.Register(accessors);

        TvpAccessorRegistry.TryGet<RegistryInitialCoverageRow>(out TvpAccessors<RegistryInitialCoverageRow>? resolved)
            .Should()
            .BeTrue();
        resolved.Should().BeSameAs(accessors);

        var badAccessors = new TvpAccessors<RegistryMismatchRow>
        {
            Properties = Array.Empty<PropertyInfo>(),
            Accessors = Array.Empty<Func<object, object?>>(),
            TypedAccessors = Array.Empty<Func<RegistryMismatchRow, object?>>(),
            OrdinalMap = Array.Empty<KeyValuePair<string, int>>()
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            SchemaTable = new DataTable()
        };

        Action registerBad = () => TvpAccessorRegistry.Register(badAccessors);

#if DEBUG
        registerBad.Should().Throw<InvalidOperationException>()
            .WithMessage("*등록 접근자*");
#else
        registerBad.Should().NotThrow();
#endif
    }

    [Fact]
    public void TvpRowAccessorCache_ShouldResolveEverySqlDbTypeFromDescriptor()
    {
        TvpRowAccessorCache.Clear();

        foreach (SqlDbType dbType in Enum.GetValues<SqlDbType>())
        {
            TvpSchemaDescriptor descriptor = Descriptor(
                $"dbo.T_RowAccessor_{dbType}",
                Column("Value", ordinal: 0, dbType));

            RuntimeTvpRowShape shape = TvpRowAccessorCache.GetOrAdd(
                typeof(RowAccessorAllSqlTypesRow),
                descriptor,
                TvpBindingPolicy.Strict);

            shape.Columns.Should().ContainSingle();
            shape.Columns[0].DbType.Should().Be(dbType);
        }
    }

    [Fact]
    public void TvpColumnShape_ShouldNormalizeSqlTypesNamesAndNullability()
    {
        TvpColumnShape.Required(" Id ", typeof(int)).Should().Match<TvpColumnShape>(c =>
            c.Name == "Id" && c.FieldType == typeof(int) && !c.AllowNull);
        TvpColumnShape.Optional(" Note ", typeof(string), size: 32).Should().Match<TvpColumnShape>(c =>
            c.Name == "Note" && c.FieldType == typeof(string) && c.AllowNull && c.Size == 32);

        AssertSqlShape<long>(SqlDbType.BigInt, typeof(long));
        AssertSqlShape<byte[]>(SqlDbType.Binary, typeof(byte[]));
        AssertSqlShape<byte[]>(SqlDbType.Image, typeof(byte[]));
        AssertSqlShape<byte[]>(SqlDbType.Timestamp, typeof(byte[]));
        AssertSqlShape<byte[]>(SqlDbType.VarBinary, typeof(byte[]));
        AssertSqlShape<bool>(SqlDbType.Bit, typeof(bool));
        AssertSqlShape<string>(SqlDbType.Char, typeof(string));
        AssertSqlShape<string>(SqlDbType.NChar, typeof(string));
        AssertSqlShape<string>(SqlDbType.NText, typeof(string));
        AssertSqlShape<string>(SqlDbType.NVarChar, typeof(string));
        AssertSqlShape<string>(SqlDbType.Text, typeof(string));
        AssertSqlShape<string>(SqlDbType.VarChar, typeof(string));
        AssertSqlShape<string>(SqlDbType.Xml, typeof(string));
        AssertSqlShape<DateOnly>(SqlDbType.Date, typeof(DateTime));
        AssertSqlShape<DateTime>(SqlDbType.DateTime, typeof(DateTime));
        AssertSqlShape<DateTime>(SqlDbType.DateTime2, typeof(DateTime));
        AssertSqlShape<DateTime>(SqlDbType.SmallDateTime, typeof(DateTime));
        AssertSqlShape<DateTimeOffset>(SqlDbType.DateTimeOffset, typeof(DateTimeOffset));
        AssertSqlShape<decimal>(SqlDbType.Decimal, typeof(decimal));
        AssertSqlShape<decimal>(SqlDbType.Money, typeof(decimal));
        AssertSqlShape<decimal>(SqlDbType.SmallMoney, typeof(decimal));
        AssertSqlShape<double>(SqlDbType.Float, typeof(double));
        AssertSqlShape<int>(SqlDbType.Int, typeof(int));
        AssertSqlShape<float>(SqlDbType.Real, typeof(float));
        AssertSqlShape<short>(SqlDbType.SmallInt, typeof(short));
        AssertSqlShape<TimeOnly>(SqlDbType.Time, typeof(TimeSpan));
        AssertSqlShape<byte>(SqlDbType.TinyInt, typeof(byte));
        AssertSqlShape<Guid>(SqlDbType.UniqueIdentifier, typeof(Guid));

        TvpColumnShape.FromSql<RegistryCoverageRow>(
                "StructuredValue",
                SqlDbType.Structured,
                allowNull: false,
                size: 0,
                precision: 0,
                scale: 0)
            .FieldType
            .Should()
            .Be(typeof(RegistryCoverageRow));

        TvpColumnShape.FromSql<int?>(
                "NullableId",
                SqlDbType.Int,
                allowNull: false,
                size: 0,
                precision: 0,
                scale: 0)
            .AllowNull
            .Should()
            .BeTrue();
        TvpColumnShape.FromSql<int>(
                "OptionalId",
                SqlDbType.Int,
                allowNull: true,
                size: 0,
                precision: 0,
                scale: 0)
            .AllowNull
            .Should()
            .BeTrue();
        TvpColumnShape.FromSql<Uri>(
                "VariantValue",
                SqlDbType.Variant,
                allowNull: false,
                size: 0,
                precision: 0,
                scale: 0)
            .FieldType
            .Should()
            .Be(typeof(Uri));

        foreach (SqlDbType dbType in Enum.GetValues<SqlDbType>())
        {
            TvpColumnShape.FromSql<object>(
                    $"All{dbType}Value",
                    dbType,
                    allowNull: false,
                    size: 0,
                    precision: 0,
                    scale: 0)
                .DbType
                .Should()
                .Be(dbType);
        }

        Action nullName = () => TvpColumnShape.Required(null!, typeof(int));
        nullName.Should().Throw<ArgumentException>();

        Action whiteSpaceName = () => TvpColumnShape.Optional("   ", typeof(string));
        whiteSpaceName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TvpRowAccessorCache_ShouldBuildDictionaryShapesClearEntriesAndRejectEmptyDescriptors()
    {
        TvpSchemaDescriptor descriptor = Descriptor(
            "dbo.T_DictionaryCoverage",
            Column("OptionalText", 1, SqlDbType.NVarChar, maxLength: 40, isNullable: true),
            Column("Id", 0, SqlDbType.Int));
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 11
            }
        };

        RuntimeTvpRowShape adaptiveShape = TvpRowAccessorCache.GetOrAdd(
            typeof(IReadOnlyDictionary<string, object?>),
            descriptor,
            TvpBindingPolicy.Adaptive);
        RuntimeTvpRowShape sameShape = TvpRowAccessorCache.GetOrAdd(
            typeof(IReadOnlyDictionary<string, object?>),
            descriptor,
            TvpBindingPolicy.Adaptive);

        sameShape.Should().BeSameAs(adaptiveShape);
        adaptiveShape.Columns.Select(static c => c.Name).Should().Equal("Id", "OptionalText");
        using (RuntimeTvpDataReader reader = RuntimeTvpDataReader.Create(rows, adaptiveShape))
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(11);
            reader.GetValue(1).Should().Be(DBNull.Value);
        }

        RuntimeTvpRowShape strictShape = TvpRowAccessorCache.GetOrAdd(
            typeof(IReadOnlyDictionary<string, object?>),
            descriptor,
            TvpBindingPolicy.Strict);
        using (RuntimeTvpDataReader reader = RuntimeTvpDataReader.Create(rows, strictShape))
        {
            reader.Read().Should().BeTrue();
            reader.Invoking(static r => r.GetValue(1))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*required column 'OptionalText'*");
        }

        TvpRowAccessorCache.Clear(TvpTypeName.Parse("dbo.T_DictionaryCoverage"));
        RuntimeTvpRowShape afterTargetedClear = TvpRowAccessorCache.GetOrAdd(
            typeof(IReadOnlyDictionary<string, object?>),
            descriptor,
            TvpBindingPolicy.Adaptive);
        afterTargetedClear.Should().NotBeSameAs(adaptiveShape);

        TvpRowAccessorCache.Clear();
        RuntimeTvpRowShape afterFullClear = TvpRowAccessorCache.GetOrAdd(
            typeof(IReadOnlyDictionary<string, object?>),
            descriptor,
            TvpBindingPolicy.Adaptive);
        afterFullClear.Should().NotBeSameAs(afterTargetedClear);

        TvpSchemaDescriptor empty = Descriptor("dbo.T_EmptyCoverage");
        Action buildEmpty = () => TvpRowAccessorCache.GetOrAdd(typeof(RegistryCoverageRow), empty, TvpBindingPolicy.Strict);
        buildEmpty.Should().Throw<InvalidOperationException>().WithMessage("*does not expose columns*");
    }

    [Fact]
    public void TvpRowBinding_ShouldSelectReaderKindsAndInferFallbackShape()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        using DbDataReader existingReader = table.CreateDataReader();
        var existingReaderValue = new LibDbTvpValue(
            TvpTypeName.Parse("dbo.T_Table"),
            existingReader,
            RowType: null,
            TvpBindingPolicy.Strict);

        TvpRowBinding.CreateReader(existingReaderValue)
            .Should()
            .BeSameAs(existingReader);
        TvpRowBinding.CreateParameterValue(existingReaderValue)
            .Should()
            .BeSameAs(existingReader);

        var tableValue = new LibDbTvpValue(
            TvpTypeName.Parse("dbo.T_Table"),
            table,
            RowType: null,
            TvpBindingPolicy.Strict);

        using DbDataReader tableReader = TvpRowBinding.CreateReader(tableValue);
        tableReader.Read().Should().BeTrue();
        tableReader.GetInt32(0).Should().Be(1);
        using DbDataReader parameterTableReader = ((DataTableReader)TvpRowBinding.CreateParameterValue(tableValue));
        parameterTableReader.Read().Should().BeTrue();
        parameterTableReader.GetInt32(0).Should().Be(1);

        TvpShape<StaticShapeRow> shape = TvpShape
            .For<StaticShapeRow>()
            .Column("Id", SqlDbType.Int, static row => row.Id)
            .Build();
        object parameterValue = TvpRowBinding.CreateParameterValue(
            LibDb.Tvp<StaticShapeRow>("dbo.T_Static", new[] { new StaticShapeRow(2) }, shape));

        parameterValue.Should().BeAssignableTo<IEnumerable<SqlDataRecord>>();
        using DbDataReader staticShapeReader = TvpRowBinding.CreateReader(
            LibDb.Tvp<StaticShapeRow>("dbo.T_Static", new[] { new StaticShapeRow(22) }, shape));
        staticShapeReader.Read().Should().BeTrue();
        staticShapeReader.GetInt32(0).Should().Be(22);

        var scalarWithShape = new LibDbTvpValue(
            TvpTypeName.Parse("dbo.T_Static"),
            Rows: 123,
            RowType: typeof(StaticShapeRow),
            TvpBindingPolicy.Strict)
        {
            RowShape = shape.RuntimeShape
        };
        Action scalarShapedRows = () => TvpRowBinding.CreateParameterValue(scalarWithShape);
        scalarShapedRows.Should().Throw<InvalidOperationException>()
            .WithMessage("*enumerable row source*");

        using DbDataReader inferred = TvpRowBinding.CreateReader(
            LibDb.Tvp<InferredShapeRow>("dbo.T_Inferred", new[] { new InferredShapeRow(3, new DateOnly(2026, 5, 18), (Half)1.25f, new TimeOnly(6, 7, 8)) }));
        inferred.Read().Should().BeTrue();
        inferred.GetInt32(0).Should().Be(3);
        inferred.GetValue(1).Should().Be(new DateTime(2026, 5, 18));
        inferred.GetValue(2).Should().Be(1.25f);
        inferred.GetValue(3).Should().Be(new TimeSpan(6, 7, 8));

        using DbDataReader nullableInferred = TvpRowBinding.CreateReader(
            LibDb.Tvp<NullableInferredShapeRow>("dbo.T_NullableInferred", new[]
            {
                new NullableInferredShapeRow(7, new DateOnly(2026, 5, 19), (Half)2.5f, new TimeOnly(7, 8, 9))
            }));
        nullableInferred.Read().Should().BeTrue();
        nullableInferred.GetInt32(0).Should().Be(7);
        nullableInferred.GetValue(1).Should().Be(new DateTime(2026, 5, 19));
        nullableInferred.GetValue(2).Should().Be(2.5f);
        nullableInferred.GetValue(3).Should().Be(new TimeSpan(7, 8, 9));

        using DbDataReader attributed = TvpRowBinding.CreateReader(
            LibDb.Tvp<AttributedInferenceRow>("dbo.T_Attributed", new[]
            {
                new AttributedInferenceRow("abc", 12.34m)
            }));
        DataTable attributedSchema = attributed.GetSchemaTable()!;
        attributedSchema.Rows[0][SchemaTableColumn.ColumnSize].Should().Be(24);
        attributedSchema.Rows[1][SchemaTableColumn.NumericPrecision].Should().Be((short)9);
        attributedSchema.Rows[1][SchemaTableColumn.NumericScale].Should().Be((short)3);

        TvpSchemaDescriptor descriptor = Descriptor(
            "dbo.T_BindingDescriptor",
            Column("Id", 0, SqlDbType.Int));
        using DbDataReader descriptorReader = TvpRowBinding.CreateReader(
            LibDb.Tvp<StaticShapeRow>(descriptor, new[] { new StaticShapeRow(4) }));
        descriptorReader.Read().Should().BeTrue();
        descriptorReader.GetInt32(0).Should().Be(4);

        var descriptorWithoutRowType = new LibDbTvpValue(
            descriptor.TypeName,
            new[] { new StaticShapeRow(5) },
            RowType: null,
            TvpBindingPolicy.Strict)
        {
            SchemaDescriptor = descriptor
        };
        using DbDataReader inferredDescriptorReader = TvpRowBinding.CreateReader(descriptorWithoutRowType);
        inferredDescriptorReader.Read().Should().BeTrue();
        inferredDescriptorReader.GetInt32(0).Should().Be(5);

        var descriptorNonGenericValue = new LibDbTvpValue(
            descriptor.TypeName,
            new ArrayList { new object() },
            RowType: null,
            TvpBindingPolicy.Strict)
        {
            SchemaDescriptor = descriptor
        };
        Action descriptorCannotInfer = () => TvpRowBinding.CreateReader(descriptorNonGenericValue);
        descriptorCannotInfer.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be inferred*");

        Action dictionaryWithoutDescriptor = () => TvpRowBinding.CreateReader(
            LibDb.Tvp<Dictionary<string, object?>>("dbo.T_Dictionary", new[]
            {
                new Dictionary<string, object?>
                {
                    ["Id"] = 1
                }
            }));
        dictionaryWithoutDescriptor.Should().Throw<InvalidOperationException>()
            .WithMessage("*explicit schema*");

        var scalarValue = new LibDbTvpValue(
            TvpTypeName.Parse("dbo.T_Scalar"),
            Rows: 123,
            RowType: typeof(int),
            TvpBindingPolicy.Strict);
        Action scalarRows = () => TvpRowBinding.CreateReader(scalarValue);
        scalarRows.Should().Throw<InvalidOperationException>()
            .WithMessage("*enumerable row source*");

        Action emptyRow = () => TvpRowBinding.CreateReader(
            LibDb.Tvp<EmptyRow>("dbo.T_Empty", new[] { new EmptyRow() }));
        emptyRow.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not expose public readable columns*");

        using DbDataReader edgeInferred = TvpRowBinding.CreateReader(
            LibDb.Tvp<AccessorEdgeRow>("dbo.T_EdgeInference", new[] { new AccessorEdgeRow { Id = 6, Name = "edge" } }));
        edgeInferred.Read().Should().BeTrue();
        edgeInferred.GetInt32(0).Should().Be(6);

        using DbDataReader lengthPrecisionInferred = TvpRowBinding.CreateReader(
            LibDb.Tvp<LengthPrecisionInferenceRow>("dbo.T_LengthPrecision", new[]
            {
                new LengthPrecisionInferenceRow("abc", 1.23m)
            }));
        DataTable lengthPrecisionSchema = lengthPrecisionInferred.GetSchemaTable()!;
        lengthPrecisionSchema.Rows[0][SchemaTableColumn.ColumnSize].Should().Be(12);
        lengthPrecisionSchema.Rows[1][SchemaTableColumn.NumericPrecision].Should().Be((short)8);
        lengthPrecisionSchema.Rows[1][SchemaTableColumn.NumericScale].Should().Be((short)2);

        Action duplicateInference = () => InvokePrivateBuildShape(CreateDuplicatePropertyType());
        duplicateInference.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*Duplicate TVP property name*");

        var nonGenericRows = new ArrayList { new object() };
        var nonGenericValue = new LibDbTvpValue(
            TvpTypeName.Parse("dbo.T_NonGeneric"),
            nonGenericRows,
            RowType: null,
            TvpBindingPolicy.Strict);
        Action nonGenericCannotInfer = () => TvpRowBinding.CreateReader(nonGenericValue);
        nonGenericCannotInfer.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be inferred*");

        InvokePrivateTryGetElementType(typeof(InferredShapeRow[]))
            .Should()
            .Be(typeof(InferredShapeRow));
        InvokePrivateTryGetElementType(typeof(IEnumerable<InferredShapeRow>))
            .Should()
            .Be(typeof(InferredShapeRow));
        InvokePrivateTryGetElementType(typeof(List<InferredShapeRow>))
            .Should()
            .Be(typeof(InferredShapeRow));
        InvokePrivateTryGetElementType(typeof(NonGenericEnumerableOnly))
            .Should()
            .BeNull();
        InvokePrivateTryGetElementType(typeof(object))
            .Should()
            .BeNull();

        Action privateNonEnumerable = () => InvokePrivateCreateReaderWithInferredShape(scalarValue);
        privateNonEnumerable.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*enumerable row source*");

        TvpRowBinding.ClearCache();
    }

    [Fact]
    public void SqlDataRecordTvpEnumerable_ShouldInferNullableColumnDbType()
    {
        TvpColumnShape[] columns = [TvpColumnShape.Optional("MaybeId", typeof(int?))];
        RuntimeTvpRowShape shape = new(
            typeof(NullableFastPathRow),
            columns,
            [row => ((NullableFastPathRow)row).MaybeId],
            Ordinals(columns),
            RuntimeTvpDataReader.BuildSchemaTable(columns));

        SqlDataRecord record = new SqlDataRecordTvpEnumerable(
            new[] { new NullableFastPathRow(7) },
            shape).Single();

        record.GetInt32(0).Should().Be(7);
    }

    [Fact]
    public void RuntimeTvpDataReader_ShouldCoverEnumeratorDisposalAdaptivePocoAndShapeProperties()
    {
        var rows = new DisposableEnumerable<AccessorEdgeRow>(
        [
            new AccessorEdgeRow { Id = 17, Name = "edge" }
        ]);
        TvpColumnShape[] columns =
        [
            TvpColumnShape.Required("Id", typeof(int)),
            TvpColumnShape.Optional("Missing", typeof(string))
        ];

        using RuntimeTvpDataReader reader = RuntimeTvpDataReader.Create(
            rows,
            typeof(AccessorEdgeRow),
            columns,
            TvpBindingPolicy.Adaptive);

        IEnumerator enumerator = reader.GetEnumerator();
        enumerator.MoveNext().Should().BeTrue();
        reader.Read().Should().BeFalse();
        reader.Close();
        rows.Disposed.Should().BeTrue();

        RuntimeTvpRowShape nullRowShape = new(
            typeof(object),
            [TvpColumnShape.Required("Id", typeof(int))],
            [_ => 1],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = 0
            },
            RuntimeTvpDataReader.BuildSchemaTable([TvpColumnShape.Required("Id", typeof(int))]));
        using RuntimeTvpDataReader nullRowReader = RuntimeTvpDataReader.Create(new object?[] { null }, nullRowShape);
        nullRowReader.Read().Should().BeTrue();
        nullRowReader.Invoking(static r => r.GetValue(0))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No current TVP row*");

        RuntimeTvpRowShape shape = new(
            typeof(AccessorEdgeRow),
            columns,
            [_ => 1, _ => DBNull.Value],
            Ordinals(columns),
            RuntimeTvpDataReader.BuildSchemaTable(columns));

        shape.RowType.Should().Be(typeof(AccessorEdgeRow));
        shape.Columns.Should().BeSameAs(columns);
        shape.Accessors[0](new AccessorEdgeRow()).Should().Be(1);
        shape.Ordinals["missing"].Should().Be(1);
        shape.SchemaTable.Rows.Count.Should().Be(2);
    }

    [Fact]
    public void TvpAccessorCache_ShouldCoverRegistryCacheCapacityDelegatesAndComparerEdges()
    {
        TvpAccessorCache.Clear();
        TvpAccessorCache.Configure(new LibDbOptions { MaxCacheSize = 1000 });
        TvpAccessorCache.Configure((LibDbOptions)RuntimeHelpers.GetUninitializedObject(typeof(LibDbOptions)));

        TvpAccessorCache.Clear();
        TvpAccessorCache.GetTypedAccessors<DeclarationOrderCoverageRow>()
            .Properties
            .Select(static property => property.Name)
            .Should()
            .Equal(
                nameof(DeclarationOrderCoverageRow.UserName),
                nameof(DeclarationOrderCoverageRow.Email),
                nameof(DeclarationOrderCoverageRow.Age));

        TvpAccessorCache.GetTypedAccessors<DelegateCacheCoverageRow>()
            .Properties
            .Should()
            .ContainSingle(static property => property.Name == nameof(DelegateCacheCoverageRow.Id));
        TvpAccessorCache.Clear();
        TvpAccessorCache.GetTypedAccessors<DelegateCacheCoverageRow>()
            .Properties
            .Should()
            .ContainSingle(static property => property.Name == nameof(DelegateCacheCoverageRow.Id));

        TvpAccessors<RegistryCoverageRow> classAccessors = TvpAccessorCache.GetTypedAccessors<RegistryCoverageRow>();
        classAccessors.TypedAccessors[0](null!).Should().BeNull();
        classAccessors.Accessors[0](null!).Should().BeNull();
        TvpAccessorRegistry.Register(classAccessors);
        TvpAccessorCache.GetTypedAccessors<RegistryCoverageRow>()
            .Should()
            .BeSameAs(classAccessors);
        TvpAccessorCache.GetAccessors<RegistryCoverageRow>()
            .Should()
            .BeSameAs(classAccessors);

        TvpAccessorCache.CompileAccessors<AccessorEdgeRow>()
            .Properties
            .Select(static property => property.Name)
            .Should()
            .Equal("Id", "Name");
        TvpAccessorCache.CompileAccessors<IInterfaceAccessorRow>()
            .Properties
            .Should()
            .ContainSingle(static property => property.Name == nameof(IInterfaceAccessorRow.Id));

        TvpAccessors<StructAccessorRow> structAccessors = TvpAccessorCache.GetTypedAccessors<StructAccessorRow>();
        structAccessors.TypedAccessors[0](new StructAccessorRow(31)).Should().Be(31);
        structAccessors.Accessors[0](new StructAccessorRow(32)).Should().Be(32);

        var reorderedAccessors = new TvpAccessors<RegistryOrderRow>
        {
            Properties =
            [
                typeof(RegistryOrderRow).GetProperty(nameof(RegistryOrderRow.Name))!,
                typeof(RegistryOrderRow).GetProperty(nameof(RegistryOrderRow.Id))!
            ],
            Accessors = [_ => "name", _ => 1],
            TypedAccessors = [_ => "name", _ => 1],
            OrdinalMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(RegistryOrderRow.Name)] = 0,
                [nameof(RegistryOrderRow.Id)] = 1
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            SchemaTable = new DataTable()
        };
        Action registerReordered = () => TvpAccessorRegistry.Register(reorderedAccessors);
#if DEBUG
        registerReordered.Should().Throw<InvalidOperationException>()
            .WithMessage("*프로퍼티 불일치*");
#else
        registerReordered.Should().NotThrow();
#endif

        FieldInfo maxCacheSize = typeof(TvpAccessorCache).GetField(
            "s_maxCacheSize",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        object? original = maxCacheSize.GetValue(null);
        try
        {
            maxCacheSize.SetValue(null, 1);
            TvpAccessorCache.Clear();
            TvpAccessorCache.GetTypedAccessors<CapacityRowA>().Properties.Should().HaveCount(1);
            TvpAccessorCache.GetTypedAccessors<CapacityRowB>().Properties.Should().HaveCount(1);
        }
        finally
        {
            maxCacheSize.SetValue(null, original);
            TvpAccessorCache.Clear();
        }

        Action duplicateFallback = () => TvpAccessorCache.CompileAccessors<DuplicateAccessorRow>();
        duplicateFallback.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate TVP property name*");

        Type comparerType = typeof(TvpAccessorCache).GetNestedType(
            "StableRuntimePropertyComparer",
            BindingFlags.NonPublic)!;
        object comparer = comparerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        MethodInfo compare = comparerType.GetMethod("Compare")!;
        PropertyInfo idProperty = typeof(RegistryCoverageRow).GetProperty(nameof(RegistryCoverageRow.Id))!;

        ((int)compare.Invoke(comparer, [null, idProperty])!).Should().BeLessThan(0);
        ((int)compare.Invoke(comparer, [idProperty, null])!).Should().BeGreaterThan(0);
        ((int)compare.Invoke(comparer, [null, null])!).Should().Be(0);
    }

    [Fact]
    public void TvpRowAccessorCache_ShouldCoverPocoPoliciesAndSqlMetadataMapping()
    {
        TvpSchemaDescriptor optionalDescriptor = Descriptor(
            "dbo.T_PocoOptionalCoverage",
            Column("Id", 0, SqlDbType.Int),
            Column("Missing", 1, SqlDbType.NVarChar, maxLength: 80, isNullable: true));
        TvpSchemaDescriptor computedFingerprintDescriptor = optionalDescriptor with { Fingerprint = string.Empty };
        TvpRowAccessorCache.GetOrAdd(
                typeof(AccessorEdgeRow),
                computedFingerprintDescriptor,
                TvpBindingPolicy.Adaptive)
            .Columns
            .Should()
            .HaveCount(2);

        RuntimeTvpRowShape adaptive = TvpRowAccessorCache.GetOrAdd(
            typeof(AccessorEdgeRow),
            optionalDescriptor,
            TvpBindingPolicy.Adaptive);
        using (RuntimeTvpDataReader reader = RuntimeTvpDataReader.Create(
            new[] { new AccessorEdgeRow { Id = 9 } },
            adaptive))
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(9);
            reader.GetValue(1).Should().Be(DBNull.Value);
        }

        TvpSchemaDescriptor strictDescriptor = Descriptor(
            "dbo.T_PocoStrictMissingCoverage",
            Column("Id", 0, SqlDbType.Int),
            Column("Missing", 1, SqlDbType.NVarChar, maxLength: 80, isNullable: true));
        Action strictMissing = () => TvpRowAccessorCache.GetOrAdd(
            typeof(AccessorEdgeRow),
            strictDescriptor,
            TvpBindingPolicy.Strict);
        strictMissing.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not expose required column 'Missing'*");

        TvpSchemaDescriptor metadataDescriptor = Descriptor(
            "dbo.T_MetadataCoverage",
            Column("Big", 0, SqlDbType.BigInt),
            Column("Payload", 1, SqlDbType.VarBinary, maxLength: 5),
            Column("Flag", 2, SqlDbType.Bit),
            Column("Text", 3, SqlDbType.NVarChar, maxLength: 80),
            Column("Offset", 4, SqlDbType.DateTimeOffset),
            Column("Money", 5, SqlDbType.Money),
            Column("DoubleValue", 6, SqlDbType.Float),
            Column("RealValue", 7, SqlDbType.Real),
            Column("Small", 8, SqlDbType.SmallInt),
            Column("Duration", 9, SqlDbType.Time),
            Column("Tiny", 10, SqlDbType.TinyInt),
            Column("TraceId", 11, SqlDbType.UniqueIdentifier),
            Column("Variant", 12, SqlDbType.Variant));

        RuntimeTvpRowShape metadataShape = TvpRowAccessorCache.GetOrAdd(
            typeof(MetadataCoverageRow),
            metadataDescriptor,
            TvpBindingPolicy.Strict);

        metadataShape.Columns.Select(static column => column.FieldType).Should().Equal(
            typeof(long),
            typeof(byte[]),
            typeof(bool),
            typeof(string),
            typeof(DateTimeOffset),
            typeof(decimal),
            typeof(double),
            typeof(float),
            typeof(short),
            typeof(TimeSpan),
            typeof(byte),
            typeof(Guid),
            typeof(object));
        metadataShape.Columns[3].Size.Should().Be(40);
        metadataShape.Columns[1].Size.Should().Be(5);

        Type cacheKeyType = typeof(TvpRowAccessorCache).GetNestedType(
            "CacheKey",
            BindingFlags.NonPublic)!;
        object cacheKey = Activator.CreateInstance(
            cacheKeyType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [typeof(MetadataCoverageRow), "dbo.T_MetadataCoverage", "fingerprint", TvpBindingPolicy.Strict],
            culture: null)!;
        cacheKeyType.GetProperty("Fingerprint")!
            .GetValue(cacheKey)
            .Should()
            .Be("fingerprint");
    }

    [Fact]
    public void TvpShapeAndNameAndFingerprint_ShouldCoverBoundaryBranches()
    {
        Action emptyBuild = () => TvpShape.For<StaticShapeRow>().Build();
        emptyBuild.Should().Throw<InvalidOperationException>()
            .WithMessage("*At least one TVP column*");

        TvpShapeBuilder<StaticShapeRow> builder = TvpShape.For<StaticShapeRow>();
        Action nullAccessor = () => builder.AddColumn<int>(
            "Id",
            SqlDbType.Int,
            null!,
            size: 0,
            precision: 0,
            scale: 0,
            allowNull: false);
        nullAccessor.Should().Throw<ArgumentNullException>();

        builder.Column("Id", SqlDbType.Int, static row => row.Id);
        Action duplicate = () => builder.Column("id", SqlDbType.Int, static row => row.Id);
        duplicate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate TVP column name*");

        TvpShape<StaticShapeRow> shape = builder.Build();
        shape.Columns.Should().ContainSingle(column => column.Name == "Id");

        string tooLong = "dbo." + new string('A', 129);
        Action longName = () => TvpTypeName.Parse(tooLong);
        longName.Should().Throw<ArgumentException>();
        Action badSchema = () => TvpTypeName.Parse("1bad.Valid");
        badSchema.Should().Throw<ArgumentException>();
        Action badName = () => TvpTypeName.Parse("dbo.1bad");
        badName.Should().Throw<ArgumentException>();
        Action badColumn = () => TvpColumnShape.Required("Id]; DROP TABLE Users;--", typeof(int));
        badColumn.Should().Throw<ArgumentException>();
        Action longColumn = () => TvpColumnShape.Required(new string('C', 129), typeof(int));
        longColumn.Should().Throw<ArgumentException>();
        Action badColumnCtor = () => _ = new TvpColumnShape("Id;DROP", typeof(int), allowNull: false);
        badColumnCtor.Should().Throw<ArgumentException>();
        Action nullColumnType = () => _ = new TvpColumnShape("Id", null!, allowNull: false);
        nullColumnType.Should().Throw<ArgumentNullException>();
        Action badColumnWith = () => _ = TvpColumnShape.Required("Id", typeof(int)) with { Name = "Id;DROP" };
        badColumnWith.Should().Throw<ArgumentException>();
        Action badTypeCtor = () => _ = new TvpTypeName("dbo", "Bad;DROP");
        badTypeCtor.Should().Throw<ArgumentException>();

        TvpTypeName invalidDescriptorTypeName = TvpTypeName.Parse("dbo.T_InvalidDescriptorColumn");
        TvpColumnMetadata[] invalidDescriptorColumns = [Column("Bad;DROP", 0, SqlDbType.Int, isNullable: false)];
        TvpSchemaDescriptor invalidColumnDescriptor = new(
            invalidDescriptorTypeName,
            VersionToken: 1,
            invalidDescriptorColumns,
            TvpSchemaFingerprint.Compute(invalidDescriptorTypeName, 1, invalidDescriptorColumns));
        Action invalidDescriptorBinding = () => TvpRowAccessorCache.GetOrAdd(
            typeof(StaticShapeRow),
            invalidColumnDescriptor,
            TvpBindingPolicy.Strict);
        invalidDescriptorBinding.Should().Throw<ArgumentException>();

        TvpSchemaDescriptor fingerprintMismatch = Descriptor(
            "dbo.T_FingerprintMismatch",
            Column("Id", 0, SqlDbType.Int, isNullable: false)) with
        {
            Fingerprint = "stale"
        };
        Action staleFingerprint = () => TvpRowAccessorCache.GetOrAdd(
            typeof(StaticShapeRow),
            fingerprintMismatch,
            TvpBindingPolicy.Strict);
        staleFingerprint.Should().Throw<InvalidOperationException>()
            .WithMessage("*fingerprint mismatch*");

        var schema = new TvpSchema
        {
            Name = "dbo.T_FingerprintCoverage",
            VersionToken = 99,
            Columns =
            [
                Column("Id", 0, SqlDbType.Int, isNullable: false),
                new(
                    Name: "ComputedIdentity",
                    NameHash: 0,
                    MaxLength: 0,
                    Ordinal: 1,
                    SqlDbType: SqlDbType.Int,
                    Precision: 0,
                    Scale: 0,
                    IsIdentity: true,
                    IsComputed: true,
                    IsNullable: true)
            ]
        };
        TvpSchemaFingerprint.Compute(schema).Should().NotBeNullOrWhiteSpace();
        Action nullSchema = () => TvpSchemaFingerprint.Compute((TvpSchema)null!);
        nullSchema.Should().Throw<ArgumentNullException>();
        Action nullColumns = () => TvpSchemaFingerprint.Compute(
            TvpTypeName.Parse("dbo.T_NullColumns"),
            1,
            null!);
        nullColumns.Should().Throw<ArgumentNullException>();

        var descriptor = new TvpSchemaDescriptor(
            TvpTypeName.Parse("dbo.T_DescriptorCoverage"),
            VersionToken: 3,
            schema.Columns,
            Fingerprint: "fp");
        descriptor.TypeName.FullName.Should().Be("dbo.T_DescriptorCoverage");
        descriptor.VersionToken.Should().Be(3);
        descriptor.Columns.Should().BeSameAs(schema.Columns);
        descriptor.Fingerprint.Should().Be("fp");

        var registry = new TvpMappingRegistry();
        Action setShapeBeforeMap = () => registry.SetShape<StaticShapeRow>(shape.RuntimeShape);
        setShapeBeforeMap.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not registered*");

        var buffer = new TypedColumnBuffer<int>(initialCapacity: 1);
        buffer.Add(1);
        buffer.Dispose();
        MethodInfo resize = typeof(TypedColumnBuffer<int>).GetMethod(
            "Resize",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        Action resizeAfterDispose = () => resize.Invoke(buffer, null);
        resizeAfterDispose.Should().Throw<TargetInvocationException>()
            .WithInnerException<ObjectDisposedException>();
    }

    private static TypedColumnBuffer<T> Buffer<T>(params T[] values)
    {
        var buffer = new TypedColumnBuffer<T>(initialCapacity: 1);
        foreach (T value in values)
            buffer.Add(value);

        return buffer;
    }

    private static TypedColumnBuffer<string?> NullStringBuffer()
    {
        var buffer = new TypedColumnBuffer<string?>(initialCapacity: 1);
        buffer.Add(null);
        return buffer;
    }

    private static TypedColumnBuffer<byte[]> NullByteArrayBuffer()
    {
        var buffer = new TypedColumnBuffer<byte[]>(initialCapacity: 1);
        buffer.Add(null!);
        return buffer;
    }

    private static TypedColumnBuffer<string> NullNonNullableStringBuffer()
    {
        var buffer = new TypedColumnBuffer<string>(initialCapacity: 1);
        buffer.Add(null!);
        return buffer;
    }

    private static IReadOnlyDictionary<string, int> Ordinals(IReadOnlyList<TvpColumnShape> columns)
        => columns
            .Select(static (column, index) => new KeyValuePair<string, int>(column.Name, index))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static TvpSchemaDescriptor Descriptor(string typeNameValue, params TvpColumnMetadata[] columns)
    {
        TvpTypeName typeName = TvpTypeName.Parse(typeNameValue);
        string fingerprint = TvpSchemaFingerprint.Compute(typeName, versionToken: 23, columns);

        return new TvpSchemaDescriptor(typeName, VersionToken: 23, columns, fingerprint);
    }

    private static TvpColumnMetadata Column(
        string name,
        int ordinal,
        SqlDbType sqlDbType,
        long maxLength = 0,
        byte precision = 0,
        byte scale = 0,
        bool isNullable = false)
        => new(
            Name: name,
            NameHash: 0,
            MaxLength: maxLength,
            Ordinal: ordinal,
            SqlDbType: sqlDbType,
            Precision: precision,
            Scale: scale,
            IsIdentity: false,
            IsComputed: false,
            IsNullable: isNullable);

    private static void AssertSqlShape<TValue>(SqlDbType dbType, Type expectedFieldType)
        => TvpColumnShape.FromSql<TValue>(
                $"{dbType}Value",
                dbType,
                allowNull: false,
                size: 7,
                precision: 6,
                scale: 2)
            .Should()
            .Match<TvpColumnShape>(shape =>
                shape.FieldType == expectedFieldType &&
                shape.DbType == dbType &&
                shape.Size == 7 &&
                shape.Precision == 6 &&
                shape.Scale == 2);

    private static Type? InvokePrivateTryGetElementType(Type type)
        => (Type?)typeof(TvpRowBinding)
            .GetMethod("TryGetElementType", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [type]);

    private static void InvokePrivateCreateReaderWithInferredShape(LibDbTvpValue tvp)
        => typeof(TvpRowBinding)
            .GetMethod("CreateReaderWithInferredShape", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [tvp]);

    private static void InvokePrivateBuildShape(Type rowType)
        => typeof(TvpRowBinding)
            .GetMethod("BuildShape", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [rowType]);

    private static Type CreateDuplicatePropertyType()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("LibDbCoverageDynamic" + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("Main");
        TypeBuilder type = module.DefineType(
            "DuplicateTvpRow" + Guid.NewGuid().ToString("N"),
            TypeAttributes.Public | TypeAttributes.Class);

        DefineConstantProperty(type, "Id", "get_Id_Int32", typeof(int), il => il.Emit(OpCodes.Ldc_I4_1));
        DefineConstantProperty(type, "Id", "get_Id_String", typeof(string), il => il.Emit(OpCodes.Ldstr, "duplicate"));

        return type.CreateType()!;
    }

    private static void DefineConstantProperty(
        TypeBuilder type,
        string propertyName,
        string getterName,
        Type propertyType,
        Action<ILGenerator> emitValue)
    {
        MethodBuilder getter = type.DefineMethod(
            getterName,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            propertyType,
            Type.EmptyTypes);
        ILGenerator il = getter.GetILGenerator();
        emitValue(il);
        il.Emit(OpCodes.Ret);

        PropertyBuilder property = type.DefineProperty(
            propertyName,
            PropertyAttributes.None,
            propertyType,
            Type.EmptyTypes);
        property.SetGetMethod(getter);
    }

    private sealed record RuntimeRow(
        int Id,
        string? Name,
        DateOnly ShipDate,
        TimeOnly StartsAt,
        Half HalfValue,
        Guid TraceId,
        short Small,
        long Big,
        bool Flag,
        decimal Amount,
        double Ratio,
        float Real,
        byte Tiny,
        char Letter);

    private sealed record SqlRecordRow(
        string Name,
        byte[] Payload,
        decimal Amount,
        DateOnly ShipDate,
        TimeOnly StartsAt,
        Guid TraceId,
        string? Maybe,
        int Quantity);

    private sealed record RegistryCoverageRow(int Id, string Name);

    private sealed record RegistryInitialCoverageRow(int Id, string Name);

    private sealed record DelegateCacheCoverageRow(int Id);

    private sealed record DeclarationOrderCoverageRow(string UserName, string Email, int? Age);

    private sealed record RegistryOrderRow(int Id, string Name);

    private sealed record RegistryMismatchRow(int Id);

    private sealed record RowAccessorAllSqlTypesRow(object? Value);

    private sealed record StaticShapeRow(int Id);

    private sealed record NullableFastPathRow(int? MaybeId);

    private sealed record InferredShapeRow(int Id, DateOnly ShipDate, Half HalfValue, TimeOnly StartsAt);

    private sealed record NullableInferredShapeRow(int? Id, DateOnly? ShipDate, Half? HalfValue, TimeOnly? StartsAt);

    private sealed record AttributedInferenceRow(
        [property: DbParameter(Size = 24)] string Name,
        [property: DbParameter(Precision = 9, Scale = 3)] decimal Amount);

    private sealed record LengthPrecisionInferenceRow(
        [property: TvpLength(12)] string Name,
        [property: TvpPrecision(8, 2)] decimal Amount);

    private sealed class EmptyRow;

    private sealed class NonGenericEnumerableOnly : IEnumerable
    {
        public IEnumerator GetEnumerator()
            => Array.Empty<object>().GetEnumerator();
    }

    private interface IInterfaceAccessorRow
    {
        int Id { get; }
    }

    private sealed class AccessorEdgeRow
    {
        public int Id { get; init; }

        public string? Name { get; init; }

        public int this[int index] => index;

        public int WriteOnly
        {
            set { }
        }
    }

    private readonly record struct StructAccessorRow(int Id);

    private sealed record CapacityRowA(int Id);

    private sealed record CapacityRowB(int Id);

    private record DuplicateAccessorBase(int Id);

    private sealed record DuplicateAccessorRow(int Id, int Other) : DuplicateAccessorBase(Other)
    {
        public new int Id { get; init; } = Id;
    }

    private sealed record MetadataCoverageRow(
        long Big,
        byte[] Payload,
        bool Flag,
        string Text,
        DateTimeOffset Offset,
        decimal Money,
        double DoubleValue,
        float RealValue,
        short Small,
        TimeSpan Duration,
        byte Tiny,
        Guid TraceId,
        object Variant);

    private sealed class DisposableEnumerable<T>(IReadOnlyList<T> items) : IEnumerable<T>
    {
        public bool Disposed { get; private set; }

        public IEnumerator<T> GetEnumerator()
            => new DisposableEnumerator<T>(items.GetEnumerator(), () => Disposed = true);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DisposableEnumerator<T>(IEnumerator<T> inner, Action onDispose) : IEnumerator<T>
    {
        public T Current => inner.Current;

        object? IEnumerator.Current => Current;

        public bool MoveNext() => inner.MoveNext();

        public void Reset() => inner.Reset();

        public void Dispose()
        {
            inner.Dispose();
            onDispose();
        }
    }
}
