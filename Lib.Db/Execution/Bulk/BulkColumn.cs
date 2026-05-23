using System.Data;
using System.Globalization;

namespace Lib.Db.Execution.Bulk;

/// <summary>
/// AOT-safe bulk 작업에서 사용할 단일 destination column metadata입니다.
/// </summary>
/// <typeparam name="T">원본 행 타입입니다.</typeparam>
public sealed class BulkColumn<T>
    where T : notnull
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

    /// <summary>shape 안에서의 원본 column 순서입니다.</summary>
    public int Ordinal { get; }

    /// <summary>shape 안에서의 원본 column 순서입니다.</summary>
    public int SourceOrdinal => Ordinal;

    /// <summary>대상 테이블 column 이름입니다.</summary>
    public string DestinationName { get; }

    /// <summary>대상 SQL Server type입니다.</summary>
    public SqlDbType SqlDbType { get; }

    /// <summary>mutation key column 여부입니다.</summary>
    public bool IsKey { get; }

    /// <summary>null 허용 여부입니다.</summary>
    public bool Nullable { get; }

    /// <summary>문자열/바이너리 column size입니다. null은 max 요청입니다.</summary>
    public int? Size { get; }

    /// <summary>decimal precision입니다.</summary>
    public byte? Precision { get; }

    /// <summary>decimal 또는 temporal scale입니다.</summary>
    public byte? Scale { get; }

    internal Func<T, object?> Getter { get; }

    /// <summary>shape build 시 선택된 converter를 적용해 provider-facing 값을 반환합니다.</summary>
    public object? GetValue(T row) => Getter(row);
}

internal static class BulkValueConverter
{
    public static Func<TValue, object?> Create<TValue>(SqlDbType sqlDbType)
    {
        Type valueType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        ValidateClrSqlTypeCompatibility(valueType, sqlDbType);

        if (valueType == typeof(DateOnly) && sqlDbType == SqlDbType.Date)
            return static value => value is null ? null : ((DateOnly)(object)value).ToDateTime(TimeOnly.MinValue);

        if (valueType == typeof(TimeOnly) && sqlDbType == SqlDbType.Time)
            return static value => value is null ? null : ((TimeOnly)(object)value).ToTimeSpan();

        if (valueType.IsEnum)
            return CreateEnumConverter<TValue>(valueType);

        return static value => value;
    }

    private static Func<TValue, object?> CreateEnumConverter<TValue>(Type valueType)
        => Type.GetTypeCode(valueType) switch
        {
            TypeCode.Byte => static value => value is null ? null : Convert.ToByte(value, CultureInfo.InvariantCulture),
            TypeCode.Int16 => static value => value is null ? null : Convert.ToInt16(value, CultureInfo.InvariantCulture),
            TypeCode.Int32 => static value => value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture),
            TypeCode.Int64 => static value => value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException($"Enum underlying type '{valueType.Name}' is not supported by AOT-safe bulk shapes.")
        };

    private static void ValidateClrSqlTypeCompatibility(Type valueType, SqlDbType sqlDbType)
    {
        if (valueType.IsEnum)
        {
            SqlDbType expectedSqlType = Type.GetTypeCode(valueType) switch
            {
                TypeCode.Byte => SqlDbType.TinyInt,
                TypeCode.Int16 => SqlDbType.SmallInt,
                TypeCode.Int32 => SqlDbType.Int,
                TypeCode.Int64 => SqlDbType.BigInt,
                _ => throw new ArgumentException($"Enum underlying type '{valueType.Name}' is not supported by AOT-safe bulk shapes.")
            };

            if (sqlDbType != expectedSqlType)
                throw new ArgumentException($"CLR enum underlying type '{TypeCodeName(valueType)}' must be mapped to SqlDbType.{expectedSqlType}, not SqlDbType.{sqlDbType}.");

            return;
        }

        bool compatible = sqlDbType switch
        {
            SqlDbType.Bit => valueType == typeof(bool),
            SqlDbType.TinyInt => valueType == typeof(byte),
            SqlDbType.SmallInt => valueType == typeof(short),
            SqlDbType.Int => valueType == typeof(int),
            SqlDbType.BigInt => valueType == typeof(long),
            SqlDbType.UniqueIdentifier => valueType == typeof(Guid),
            SqlDbType.Date => valueType == typeof(DateOnly) || valueType == typeof(DateTime),
            SqlDbType.Time => valueType == typeof(TimeOnly) || valueType == typeof(TimeSpan),
            SqlDbType.DateTime or SqlDbType.SmallDateTime or SqlDbType.DateTime2 => valueType == typeof(DateTime),
            SqlDbType.DateTimeOffset => valueType == typeof(DateTimeOffset),
            SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => valueType == typeof(decimal),
            SqlDbType.NVarChar or SqlDbType.VarChar => valueType == typeof(string),
            SqlDbType.VarBinary => valueType == typeof(byte[]),
            _ => false
        };

        if (!compatible)
            throw new ArgumentException($"CLR type '{TypeName(valueType)}' is not compatible with SqlDbType.{sqlDbType} for AOT-safe bulk shapes.");
    }

    private static string TypeName(Type type)
        => type == typeof(byte[]) ? "Byte[]" : type.Name;

    private static string TypeCodeName(Type enumType)
        => Type.GetTypeCode(enumType) switch
        {
            TypeCode.Byte => nameof(Byte),
            TypeCode.Int16 => nameof(Int16),
            TypeCode.Int32 => nameof(Int32),
            TypeCode.Int64 => nameof(Int64),
            _ => enumType.Name
        };
}
