using System.Collections.ObjectModel;
using System.Data;

namespace Lib.Db.Execution.Bulk;

/// <summary>
/// AOT-safe bulk shape builder 진입점입니다.
/// </summary>
public static class BulkShape
{
    /// <summary>지정한 행 타입의 bulk shape builder를 만듭니다.</summary>
    /// <typeparam name="T">원본 행 타입입니다.</typeparam>
    public static BulkShapeBuilder<T> For<T>()
        where T : notnull
        => new();
}

/// <summary>
/// AOT-safe bulk 작업에 사용할 immutable column shape입니다.
/// </summary>
/// <typeparam name="T">원본 행 타입입니다.</typeparam>
public sealed class BulkShape<T>
    where T : notnull
{
    internal BulkShape(IReadOnlyList<BulkColumn<T>> columns)
    {
        Columns = new ReadOnlyCollection<BulkColumn<T>>(columns.ToArray());
        KeyColumns = new ReadOnlyCollection<BulkColumn<T>>(columns.Where(static column => column.IsKey).ToArray());
        WritableColumns = new ReadOnlyCollection<BulkColumn<T>>(columns.Where(static column => !column.IsKey).ToArray());
    }

    /// <summary>모든 destination column metadata입니다.</summary>
    public IReadOnlyList<BulkColumn<T>> Columns { get; }

    /// <summary>mutation key column metadata입니다.</summary>
    public IReadOnlyList<BulkColumn<T>> KeyColumns { get; }

    /// <summary>insert/update 대상 writable column metadata입니다.</summary>
    public IReadOnlyList<BulkColumn<T>> WritableColumns { get; }

    /// <summary>staged mutation 작업에서 필요한 key 계약을 검증합니다.</summary>
    public void ValidateForMutation()
    {
        if (KeyColumns.Count == 0)
            throw new InvalidOperationException("Bulk mutation shapes require at least one key column.");
    }
}

/// <summary>
/// AOT-safe bulk shape를 구성하는 builder입니다.
/// </summary>
/// <typeparam name="T">원본 행 타입입니다.</typeparam>
public sealed class BulkShapeBuilder<T>
    where T : notnull
{
    private const int MaxSqlIdentifierLength = 128;
    private readonly List<BulkColumn<T>> _columns = [];

    /// <summary>mutation key column을 추가합니다.</summary>
    public BulkShapeBuilder<T> Key<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool nullable = false,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
        => Add(destinationName, sqlDbType, getter, isKey: true, nullable, size, precision, scale);

    /// <summary>writable destination column을 추가합니다.</summary>
    public BulkShapeBuilder<T> Column<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool nullable = true,
        int? size = null,
        byte? precision = null,
        byte? scale = null)
        => Add(destinationName, sqlDbType, getter, isKey: false, nullable, size, precision, scale);

    /// <summary>검증된 immutable bulk shape를 만듭니다.</summary>
    public BulkShape<T> Build()
    {
        if (_columns.Count == 0)
            throw new InvalidOperationException("Bulk shape must contain at least one column.");

        string? duplicate = _columns
            .GroupBy(static column => column.DestinationName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .FirstOrDefault();

        if (duplicate is not null)
            throw new InvalidOperationException($"Bulk shape contains duplicate destination column '{duplicate}'.");

        BulkColumn<T>? nullableKey = _columns.FirstOrDefault(static column => column.IsKey && column.Nullable);
        if (nullableKey is not null)
            throw new InvalidOperationException($"Bulk key column '{nullableKey.DestinationName}' must be non-null.");

        ValidateStageKeyIndexShape(_columns.Where(static column => column.IsKey));

        return new BulkShape<T>(_columns);
    }

    private BulkShapeBuilder<T> Add<TValue>(
        string destinationName,
        SqlDbType sqlDbType,
        Func<T, TValue> getter,
        bool isKey,
        bool nullable,
        int? size,
        byte? precision,
        byte? scale)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ValidateDestinationColumnName(destinationName);
        ValidateSqlDbType(sqlDbType);
        ValidateSqlMetadata(sqlDbType, size, precision, scale);

        Func<T, object?> convertedGetter = CreateGetter(getter, sqlDbType);

        _columns.Add(new BulkColumn<T>(
            _columns.Count,
            destinationName,
            sqlDbType,
            convertedGetter,
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
            || destinationName.Contains('[', StringComparison.Ordinal)
            || destinationName.Contains(']', StringComparison.Ordinal))
        {
            throw new ArgumentException("Destination column name contains unsupported SQL identifier syntax.", nameof(destinationName));
        }
    }

    private static void ValidateSqlDbType(SqlDbType sqlDbType)
    {
        if (!IsSupportedSqlDbType(sqlDbType))
            throw new ArgumentOutOfRangeException(nameof(sqlDbType), sqlDbType, $"SqlDbType '{sqlDbType}' is not supported by AOT-safe bulk shapes.");
    }

    private static void ValidateSqlMetadata(SqlDbType sqlDbType, int? size, byte? precision, byte? scale)
    {
        if (sqlDbType == SqlDbType.Decimal)
        {
            if (precision is null || scale is null)
                throw new ArgumentException("Decimal bulk columns require explicit precision and scale.");

            if (precision is < 1 or > 38)
                throw new ArgumentOutOfRangeException(nameof(precision), precision, "Decimal precision must be between 1 and 38.");

            if (scale > precision)
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "Decimal scale cannot exceed precision.");
        }

        if (sqlDbType is SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.VarBinary)
        {
            int maxSize = sqlDbType == SqlDbType.NVarChar ? 4_000 : 8_000;
            if (size is < 1)
                throw new ArgumentOutOfRangeException(nameof(size), size, $"{sqlDbType} size must be positive. Use null to request max.");

            if (size > maxSize)
                throw new ArgumentOutOfRangeException(nameof(size), size, $"{sqlDbType} size cannot exceed {maxSize}. Use null to request max.");
        }

        if ((sqlDbType is SqlDbType.Time or SqlDbType.DateTime2 or SqlDbType.DateTimeOffset)
            && scale is > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, $"{sqlDbType} scale must be between 0 and 7.");
        }
    }

    private static void ValidateStageKeyIndexShape(IEnumerable<BulkColumn<T>> keyColumns)
    {
        const int MaxIndexKeyColumns = 32;
        const int MaxIndexKeyBytes = 900;

        int keyCount = 0;
        int declaredBytes = 0;
        foreach (BulkColumn<T> keyColumn in keyColumns)
        {
            keyCount++;
            if (keyCount > MaxIndexKeyColumns)
                throw new InvalidOperationException("Bulk mutation key columns cannot exceed SQL Server's 32-column index-key limit.");

            declaredBytes = checked(declaredBytes + GetDeclaredIndexKeyBytes(keyColumn));
        }

        if (declaredBytes > MaxIndexKeyBytes)
            throw new InvalidOperationException($"Bulk mutation key columns cannot exceed SQL Server's index key {MaxIndexKeyBytes}-byte limit.");
    }

    private static int GetDeclaredIndexKeyBytes(BulkColumn<T> column)
        => column.SqlDbType switch
        {
            SqlDbType.Bit or SqlDbType.TinyInt => 1,
            SqlDbType.SmallInt => 2,
            SqlDbType.Int or SqlDbType.Real or SqlDbType.SmallMoney => 4,
            SqlDbType.BigInt or SqlDbType.DateTime or SqlDbType.Money => 8,
            SqlDbType.UniqueIdentifier => 16,
            SqlDbType.Date => 3,
            SqlDbType.Time => column.Scale is null ? 5 : column.Scale is <= 2 ? 3 : column.Scale is <= 4 ? 4 : 5,
            SqlDbType.SmallDateTime => 4,
            SqlDbType.DateTime2 => column.Scale is <= 2 ? 6 : column.Scale is <= 4 ? 7 : 8,
            SqlDbType.DateTimeOffset => column.Scale is <= 2 ? 8 : column.Scale is <= 4 ? 9 : 10,
            SqlDbType.Decimal => column.Precision is <= 9 ? 5 : column.Precision is <= 19 ? 9 : column.Precision is <= 28 ? 13 : 17,
            SqlDbType.NVarChar => column.Size is null ? throw new InvalidOperationException("Bulk mutation key columns cannot use nvarchar(max).") : checked(column.Size.Value * 2),
            SqlDbType.VarChar => column.Size ?? throw new InvalidOperationException("Bulk mutation key columns cannot use varchar(max)."),
            SqlDbType.VarBinary => column.Size ?? throw new InvalidOperationException("Bulk mutation key columns cannot use varbinary(max)."),
            _ => throw new NotSupportedException($"SqlDbType '{column.SqlDbType}' is not supported by AOT-safe bulk shapes.")
        };

    private static bool IsSupportedSqlDbType(SqlDbType sqlDbType)
        => sqlDbType is SqlDbType.Bit
            or SqlDbType.TinyInt
            or SqlDbType.SmallInt
            or SqlDbType.Int
            or SqlDbType.BigInt
            or SqlDbType.UniqueIdentifier
            or SqlDbType.Date
            or SqlDbType.Time
            or SqlDbType.DateTime
            or SqlDbType.SmallDateTime
            or SqlDbType.DateTime2
            or SqlDbType.DateTimeOffset
            or SqlDbType.Decimal
            or SqlDbType.Money
            or SqlDbType.SmallMoney
            or SqlDbType.NVarChar
            or SqlDbType.VarChar
            or SqlDbType.VarBinary;
}
