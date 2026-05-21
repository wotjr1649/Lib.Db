// ============================================================================
// 파일: Benchmarks/Lib.Db.Benchmarks/Baselines/GeneratedAccessorBaselineReader.cs
// 설명: Lib.Db.TvpGen 제거 후에도 비교 가능한 benchmark-only generated accessor baseline
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Data;
using System.Data.Common;

namespace Lib.Db.Benchmarks.Baselines;

public sealed class GeneratedAccessorBaselineReader : DbDataReader
{
    private static readonly DataTable s_schemaTable = BuildSchemaTable();
    private readonly IReadOnlyList<BenchmarkOrderItemRow> _rows;
    private int _index = -1;
    private bool _closed;

    private GeneratedAccessorBaselineReader(IReadOnlyList<BenchmarkOrderItemRow> rows)
    {
        _rows = rows;
    }

    public static DbDataReader Create(IReadOnlyList<BenchmarkOrderItemRow> rows)
        => new GeneratedAccessorBaselineReader(rows);

    public override bool Read() => !_closed && ++_index < _rows.Count;
    public override int FieldCount => 4;
    public override bool HasRows => _rows.Count > 0;
    public override bool IsClosed => _closed;
    public override int RecordsAffected => -1;
    public override int Depth => 0;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override object GetValue(int ordinal)
    {
        BenchmarkOrderItemRow row = _rows[_index];
        return ordinal switch
        {
            0 => row.Id,
            1 => row.Sku,
            2 => row.Qty,
            3 => row.Price,
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
        _ => throw new IndexOutOfRangeException()
    };

    public override int GetOrdinal(string name) => name switch
    {
        "Id" => 0,
        "Sku" => 1,
        "Qty" => 2,
        "Price" => 3,
        _ => throw new IndexOutOfRangeException($"Column '{name}' not found.")
    };

    public override Type GetFieldType(int ordinal) => ordinal switch
    {
        0 => typeof(int),
        1 => typeof(string),
        2 => typeof(int),
        3 => typeof(decimal),
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
