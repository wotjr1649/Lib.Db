using System.Data;

namespace Lib.Db.Execution.Bulk;

internal static class BulkSqlTypeRenderer
{
    public static string Render<T>(BulkColumn<T> column)
        where T : notnull
        => column.SqlDbType switch
        {
            SqlDbType.Bit => "bit",
            SqlDbType.TinyInt => "tinyint",
            SqlDbType.SmallInt => "smallint",
            SqlDbType.Int => "int",
            SqlDbType.BigInt => "bigint",
            SqlDbType.UniqueIdentifier => "uniqueidentifier",
            SqlDbType.Date => "date",
            SqlDbType.Time => column.Scale is null ? "time" : $"time({column.Scale.Value})",
            SqlDbType.DateTime => "datetime",
            SqlDbType.SmallDateTime => "smalldatetime",
            SqlDbType.DateTime2 => column.Scale is null ? "datetime2" : $"datetime2({column.Scale.Value})",
            SqlDbType.DateTimeOffset => column.Scale is null ? "datetimeoffset" : $"datetimeoffset({column.Scale.Value})",
            SqlDbType.Decimal => RenderDecimal(column),
            SqlDbType.Money => "money",
            SqlDbType.SmallMoney => "smallmoney",
            SqlDbType.NVarChar => column.Size is null ? "nvarchar(max)" : $"nvarchar({column.Size.Value})",
            SqlDbType.VarChar => column.Size is null ? "varchar(max)" : $"varchar({column.Size.Value})",
            SqlDbType.VarBinary => column.Size is null ? "varbinary(max)" : $"varbinary({column.Size.Value})",
            _ => throw new NotSupportedException($"SqlDbType '{column.SqlDbType}' is not supported by AOT-safe bulk operations.")
        };

    public static Type GetFieldType<T>(BulkColumn<T> column)
        where T : notnull
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
            SqlDbType.DateTime or SqlDbType.SmallDateTime or SqlDbType.DateTime2 => typeof(DateTime),
            SqlDbType.DateTimeOffset => typeof(DateTimeOffset),
            SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => typeof(decimal),
            SqlDbType.NVarChar or SqlDbType.VarChar => typeof(string),
            SqlDbType.VarBinary => typeof(byte[]),
            _ => throw new NotSupportedException($"SqlDbType '{column.SqlDbType}' is not supported by AOT-safe bulk shapes.")
        };

    private static string RenderDecimal<T>(BulkColumn<T> column)
        where T : notnull
    {
        if (column.Precision is null || column.Scale is null)
            throw new InvalidOperationException("Decimal bulk columns require explicit precision and scale.");

        return $"decimal({column.Precision.Value},{column.Scale.Value})";
    }
}
