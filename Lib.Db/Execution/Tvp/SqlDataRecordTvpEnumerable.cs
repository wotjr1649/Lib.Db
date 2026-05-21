// ============================================================================
// 파일: Execution/Tvp/SqlDataRecordTvpEnumerable.cs
// 설명: 정적 TVP shape를 SqlDataRecord 스트림으로 전송하는 fast-path
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Data;
using Microsoft.Data.SqlClient.Server;

namespace Lib.Db.Execution.Tvp;

internal sealed class SqlDataRecordTvpEnumerable : IEnumerable<SqlDataRecord>
{
    private readonly IEnumerable _rows;
    private readonly RuntimeTvpRowShape _shape;
    private readonly SqlMetaData[] _metadata;

    internal SqlDataRecordTvpEnumerable(IEnumerable rows, RuntimeTvpRowShape shape)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _shape = shape ?? throw new ArgumentNullException(nameof(shape));
        _metadata = BuildMetadata(shape.Columns);
    }

    public IEnumerator<SqlDataRecord> GetEnumerator()
    {
        var record = new SqlDataRecord(_metadata);

        foreach (object? row in _rows)
        {
            if (row is null)
                throw new InvalidOperationException("TVP row source contains a null row.");

            for (int i = 0; i < _shape.Columns.Length; i++)
            {
                TvpColumnShape column = _shape.Columns[i];
                object? value = _shape.Accessors[i](row);

                if (value is null or DBNull)
                {
                    record.SetDBNull(i);
                    continue;
                }

                record.SetValue(i, RuntimeTvpDataReader.NormalizeValue(value, column.FieldType));
            }

            yield return record;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static SqlMetaData[] BuildMetadata(TvpColumnShape[] columns)
    {
        SqlMetaData[] metadata = new SqlMetaData[columns.Length];
        for (int i = 0; i < columns.Length; i++)
            metadata[i] = CreateMetadata(columns[i]);

        return metadata;
    }

    private static SqlMetaData CreateMetadata(TvpColumnShape column)
    {
        SqlDbType dbType = column.DbType ?? InferDbType(column.FieldType);
        return dbType switch
        {
            SqlDbType.Char or SqlDbType.NChar or SqlDbType.NText or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.VarChar
                => new SqlMetaData(column.Name, dbType, column.Size > 0 ? column.Size : SqlMetaData.Max),

            SqlDbType.Binary or SqlDbType.Image or SqlDbType.Timestamp or SqlDbType.VarBinary
                => new SqlMetaData(column.Name, dbType, column.Size > 0 ? column.Size : SqlMetaData.Max),

            SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney
                => new SqlMetaData(
                    column.Name,
                    dbType,
                    column.Precision == 0 ? (byte)18 : column.Precision,
                    column.Scale),

            _ => new SqlMetaData(column.Name, dbType)
        };
    }

    private static SqlDbType InferDbType(Type fieldType)
    {
        Type type = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        if (type == typeof(string))
            return SqlDbType.NVarChar;
        if (type == typeof(int))
            return SqlDbType.Int;
        if (type == typeof(long))
            return SqlDbType.BigInt;
        if (type == typeof(short))
            return SqlDbType.SmallInt;
        if (type == typeof(byte))
            return SqlDbType.TinyInt;
        if (type == typeof(bool))
            return SqlDbType.Bit;
        if (type == typeof(decimal))
            return SqlDbType.Decimal;
        if (type == typeof(double))
            return SqlDbType.Float;
        if (type == typeof(float))
            return SqlDbType.Real;
        if (type == typeof(DateTime))
            return SqlDbType.DateTime2;
        if (type == typeof(DateTimeOffset))
            return SqlDbType.DateTimeOffset;
        if (type == typeof(TimeSpan))
            return SqlDbType.Time;
        if (type == typeof(Guid))
            return SqlDbType.UniqueIdentifier;
        if (type == typeof(byte[]))
            return SqlDbType.VarBinary;

        return SqlDbType.Variant;
    }
}
