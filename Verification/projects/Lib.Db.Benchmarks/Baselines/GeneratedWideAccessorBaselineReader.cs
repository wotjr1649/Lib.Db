// ============================================================================
// File: Benchmarks/Lib.Db.Benchmarks/Baselines/GeneratedWideAccessorBaselineReader.cs
// Description: Benchmark-only generated accessor baseline for the 16-column TVP.
// Target: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Data;
using System.Data.Common;

namespace Lib.Db.Benchmarks.Baselines;

public sealed class GeneratedWideAccessorBaselineReader : DbDataReader
{
    private static readonly DataTable s_schemaTable = BuildSchemaTable();
    private readonly IReadOnlyList<BenchmarkWideOrderItemRow> _rows;
    private int _index = -1;
    private bool _closed;

    private GeneratedWideAccessorBaselineReader(IReadOnlyList<BenchmarkWideOrderItemRow> rows)
    {
        _rows = rows;
    }

    public static DbDataReader Create(IReadOnlyList<BenchmarkWideOrderItemRow> rows)
        => new GeneratedWideAccessorBaselineReader(rows);

    public override bool Read() => !_closed && ++_index < _rows.Count;
    public override int FieldCount => 16;
    public override bool HasRows => _rows.Count > 0;
    public override bool IsClosed => _closed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override object GetValue(int ordinal)
    {
        BenchmarkWideOrderItemRow row = _rows[_index];
        return ordinal switch
        {
            0 => row.Id,
            1 => row.Sku,
            2 => row.Qty,
            3 => row.Price,
            4 => row.Discount,
            5 => row.Tax,
            6 => row.LineTotal,
            7 => row.IsGift,
            8 => row.WarehouseId,
            9 => row.Region,
            10 => row.BatchId,
            11 => row.RequestedAt,
            12 => row.SequenceNumber,
            13 => row.Priority,
            14 => row.Status,
            15 => row.Note,
            _ => throw new IndexOutOfRangeException()
        };
    }

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
            values[i] = GetValue(i);

        return count;
    }

    public override string GetName(int ordinal) => ordinal switch
    {
        0 => "Id",
        1 => "Sku",
        2 => "Qty",
        3 => "Price",
        4 => "Discount",
        5 => "Tax",
        6 => "LineTotal",
        7 => "IsGift",
        8 => "WarehouseId",
        9 => "Region",
        10 => "BatchId",
        11 => "RequestedAt",
        12 => "SequenceNumber",
        13 => "Priority",
        14 => "Status",
        15 => "Note",
        _ => throw new IndexOutOfRangeException()
    };

    public override int GetOrdinal(string name) => name switch
    {
        "Id" => 0,
        "Sku" => 1,
        "Qty" => 2,
        "Price" => 3,
        "Discount" => 4,
        "Tax" => 5,
        "LineTotal" => 6,
        "IsGift" => 7,
        "WarehouseId" => 8,
        "Region" => 9,
        "BatchId" => 10,
        "RequestedAt" => 11,
        "SequenceNumber" => 12,
        "Priority" => 13,
        "Status" => 14,
        "Note" => 15,
        _ => throw new IndexOutOfRangeException($"Column '{name}' not found.")
    };

    public override Type GetFieldType(int ordinal) => ordinal switch
    {
        0 => typeof(int),
        1 => typeof(string),
        2 => typeof(int),
        3 => typeof(decimal),
        4 => typeof(decimal),
        5 => typeof(decimal),
        6 => typeof(decimal),
        7 => typeof(bool),
        8 => typeof(int),
        9 => typeof(string),
        10 => typeof(Guid),
        11 => typeof(DateTime),
        12 => typeof(long),
        13 => typeof(short),
        14 => typeof(byte),
        15 => typeof(string),
        _ => throw new IndexOutOfRangeException()
    };

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
    public override bool IsDBNull(int ordinal) => false;
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override DataTable GetSchemaTable() => s_schemaTable;
    public override bool NextResult() => false;
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();

    public override void Close()
    {
        _closed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();

        base.Dispose(disposing);
    }

    private static DataTable BuildSchemaTable()
    {
        DataTable schema = new("SchemaTable");
        schema.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        schema.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        schema.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        schema.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        schema.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));
        schema.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        schema.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
        schema.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));

        Add(schema, "Id", 0, typeof(int), allowNull: false);
        Add(schema, "Sku", 1, typeof(string), allowNull: false, size: 64);
        Add(schema, "Qty", 2, typeof(int), allowNull: false);
        Add(schema, "Price", 3, typeof(decimal), allowNull: false, precision: 18, scale: 2);
        Add(schema, "Discount", 4, typeof(decimal), allowNull: false, precision: 18, scale: 2);
        Add(schema, "Tax", 5, typeof(decimal), allowNull: false, precision: 18, scale: 2);
        Add(schema, "LineTotal", 6, typeof(decimal), allowNull: false, precision: 18, scale: 2);
        Add(schema, "IsGift", 7, typeof(bool), allowNull: false);
        Add(schema, "WarehouseId", 8, typeof(int), allowNull: false);
        Add(schema, "Region", 9, typeof(string), allowNull: false, size: 16);
        Add(schema, "BatchId", 10, typeof(Guid), allowNull: false);
        Add(schema, "RequestedAt", 11, typeof(DateTime), allowNull: false);
        Add(schema, "SequenceNumber", 12, typeof(long), allowNull: false);
        Add(schema, "Priority", 13, typeof(short), allowNull: false);
        Add(schema, "Status", 14, typeof(byte), allowNull: false);
        Add(schema, "Note", 15, typeof(string), allowNull: false, size: 128);
        return schema;
    }

    private static void Add(
        DataTable schema,
        string name,
        int ordinal,
        Type type,
        bool allowNull,
        int size = 0,
        byte precision = 0,
        byte scale = 0)
    {
        DataRow row = schema.NewRow();
        row[SchemaTableColumn.ColumnName] = name;
        row[SchemaTableColumn.ColumnOrdinal] = ordinal;
        row[SchemaTableColumn.ColumnSize] = size == 0 ? DBNull.Value : size;
        row[SchemaTableColumn.NumericPrecision] = precision == 0 ? DBNull.Value : (short)precision;
        row[SchemaTableColumn.NumericScale] = scale == 0 ? DBNull.Value : (short)scale;
        row[SchemaTableColumn.DataType] = type;
        row[SchemaTableColumn.AllowDBNull] = allowNull;
        row[SchemaTableColumn.IsKey] = false;
        schema.Rows.Add(row);
    }
}
