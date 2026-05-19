// ============================================================================
// 파일: Execution/Tvp/RuntimeTvpDataReader.cs
// 설명: 런타임 TVP row source를 DbDataReader로 스트리밍
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 런타임 TVP row source를 <see cref="DbDataReader"/> 형태로 노출합니다.
/// </summary>
#pragma warning disable CS1591 // DbDataReader members mirror framework semantics.
public sealed class RuntimeTvpDataReader : DbDataReader
{
    private const string ReflectionAccessorRequiresUnreferencedCodeMessage =
        "Runtime TVP accessors inspect public row properties. Use RuntimeTvpRowShape or TvpShape for Native AOT.";
    private const string ReflectionAccessorRequiresDynamicCodeMessage =
        "Runtime TVP accessors compile expression getters. Use RuntimeTvpRowShape or TvpShape for Native AOT.";

    private readonly IEnumerator _enumerator;
    private readonly TvpColumnShape[] _columns;
    private readonly Func<object, object?>[] _accessors;
    private readonly IReadOnlyDictionary<string, int> _ordinals;
    private readonly DataTable _schemaTable;
    private bool _closed;

    private RuntimeTvpDataReader(
        IEnumerator enumerator,
        TvpColumnShape[] columns,
        Func<object, object?>[] accessors)
        : this(enumerator, columns, accessors, BuildOrdinals(columns), BuildSchemaTable(columns))
    {
    }

    private RuntimeTvpDataReader(
        IEnumerator enumerator,
        TvpColumnShape[] columns,
        Func<object, object?>[] accessors,
        IReadOnlyDictionary<string, int> ordinals,
        DataTable schemaTable)
    {
        _enumerator = enumerator;
        _columns = columns;
        _accessors = accessors;
        _ordinals = ordinals;
        _schemaTable = schemaTable;
    }

    /// <summary>
    /// row sequence와 컬럼 정의를 기반으로 런타임 TVP reader를 생성합니다.
    /// </summary>
    /// <typeparam name="T">row CLR 타입입니다.</typeparam>
    /// <param name="rows">row sequence입니다.</param>
    /// <param name="columns">TVP 컬럼 정의입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>TVP 전송에 사용할 reader입니다.</returns>
    [RequiresUnreferencedCode(ReflectionAccessorRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionAccessorRequiresDynamicCodeMessage)]
    public static RuntimeTvpDataReader Create<T>(
        IEnumerable<T> rows,
        IReadOnlyList<TvpColumnShape> columns,
        TvpBindingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return Create(rows, typeof(T), columns, policy);
    }

    [RequiresUnreferencedCode(ReflectionAccessorRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionAccessorRequiresDynamicCodeMessage)]
    internal static RuntimeTvpDataReader Create(
        IEnumerable rows,
        Type rowType,
        IReadOnlyList<TvpColumnShape> columns,
        TvpBindingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(rowType);
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Count == 0)
            throw new ArgumentException("At least one TVP column is required.", nameof(columns));

        TvpColumnShape[] columnArray = columns.ToArray();
        Func<object, object?>[] accessors = BuildAccessors(rowType, columnArray, policy);

        return new RuntimeTvpDataReader(rows.GetEnumerator(), columnArray, accessors);
    }

    internal static RuntimeTvpDataReader Create(
        IEnumerable rows,
        RuntimeTvpRowShape shape)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);

        return new RuntimeTvpDataReader(
            rows.GetEnumerator(),
            shape.Columns,
            shape.Accessors,
            shape.Ordinals,
            shape.SchemaTable);
    }

    public override bool Read() => !_closed && _enumerator.MoveNext();

    public override int FieldCount => _columns.Length;

    public override bool HasRows => !_closed;

    public override bool IsClosed => _closed;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override object GetValue(int ordinal)
    {
        object current = _enumerator.Current
            ?? throw new InvalidOperationException("No current TVP row is available.");

        object? value = _accessors[ordinal](current);
        if (value is null or DBNull)
            return DBNull.Value;

        return NormalizeValue(value, _columns[ordinal].FieldType);
    }

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        int count = Math.Min(values.Length, _columns.Length);
        for (int i = 0; i < count; i++)
            values[i] = GetValue(i);

        return count;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override string GetName(int ordinal) => _columns[ordinal].Name;

    public override string GetDataTypeName(int ordinal) => _columns[ordinal].FieldType.Name;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2093",
        Justification = "DbDataReader exposes schema Type values only; Lib.Db does not reflect over returned members here.")]
    public override Type GetFieldType(int ordinal) => _columns[ordinal].FieldType;

    public override int GetOrdinal(string name)
    {
        if (_ordinals.TryGetValue(name, out int ordinal))
            return ordinal;

        throw new IndexOutOfRangeException($"Column '{name}' not found in TVP data.");
    }

    public override DataTable GetSchemaTable() => _schemaTable;

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(
        int ordinal,
        long dataOffset,
        byte[]? buffer,
        int bufferOffset,
        int length)
        => throw new NotSupportedException();

    public override long GetChars(
        int ordinal,
        long dataOffset,
        char[]? buffer,
        int bufferOffset,
        int length)
        => throw new NotSupportedException();

    public override void Close()
    {
        if (!_closed)
        {
            _closed = true;

            if (_enumerator is IDisposable disposable)
                disposable.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Close();

        base.Dispose(disposing);
    }

    [RequiresUnreferencedCode(ReflectionAccessorRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionAccessorRequiresDynamicCodeMessage)]
    private static Func<object, object?>[] BuildAccessors(
        Type rowType,
        TvpColumnShape[] columns,
        TvpBindingPolicy policy)
    {
        if (typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(rowType))
            return BuildDictionaryAccessors(columns, policy);

        Dictionary<string, PropertyInfo> props = rowType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
            .ToDictionary(static prop => prop.Name, StringComparer.OrdinalIgnoreCase);

        Func<object, object?>[] accessors = new Func<object, object?>[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            TvpColumnShape column = columns[i];
            if (!props.TryGetValue(column.Name, out PropertyInfo? prop))
            {
                if (policy == TvpBindingPolicy.Adaptive && column.AllowNull)
                {
                    accessors[i] = static _ => DBNull.Value;
                    continue;
                }

                throw new InvalidOperationException(
                    $"TVP row type '{rowType.Name}' does not expose required column '{column.Name}'.");
            }

            accessors[i] = CreateObjectGetter(rowType, prop);
        }

        return accessors;
    }

    [RequiresDynamicCode(ReflectionAccessorRequiresDynamicCodeMessage)]
    private static Func<object, object?> CreateObjectGetter(Type rowType, PropertyInfo property)
    {
        ParameterExpression row = Expression.Parameter(typeof(object), "row");
        UnaryExpression typedRow = Expression.Convert(row, rowType);
        MemberExpression propertyValue = Expression.Property(typedRow, property);
        UnaryExpression boxedValue = Expression.Convert(propertyValue, typeof(object));
        return Expression.Lambda<Func<object, object?>>(boxedValue, row).Compile();
    }

    private static Func<object, object?>[] BuildDictionaryAccessors(
        TvpColumnShape[] columns,
        TvpBindingPolicy policy)
    {
        Func<object, object?>[] accessors = new Func<object, object?>[columns.Length];

        for (int i = 0; i < columns.Length; i++)
        {
            TvpColumnShape column = columns[i];
            accessors[i] = row =>
            {
                var dictionary = (IReadOnlyDictionary<string, object?>)row;
                if (dictionary.TryGetValue(column.Name, out object? value))
                    return value;

                if (policy == TvpBindingPolicy.Adaptive && column.AllowNull)
                    return DBNull.Value;

                throw new InvalidOperationException(
                    $"TVP dictionary row does not contain required column '{column.Name}'.");
            };
        }

        return accessors;
    }

    private static Dictionary<string, int> BuildOrdinals(TvpColumnShape[] columns)
    {
        var ordinals = new Dictionary<string, int>(columns.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Length; i++)
            ordinals.Add(columns[i].Name, i);

        return ordinals;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2111",
        Justification = "SchemaTable stores Type metadata for SqlClient schema inspection only; Lib.Db does not reflect over returned Type members here.")]
    internal static DataTable BuildSchemaTable(TvpColumnShape[] columns)
    {
        var schema = new DataTable("SchemaTable");
        schema.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        schema.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        schema.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        schema.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        schema.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));
        schema.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        schema.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
        schema.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));

        for (int i = 0; i < columns.Length; i++)
        {
            TvpColumnShape column = columns[i];
            DataRow row = schema.NewRow();
            row[SchemaTableColumn.ColumnName] = column.Name;
            row[SchemaTableColumn.ColumnOrdinal] = i;
            row[SchemaTableColumn.ColumnSize] = column.Size == 0 ? DBNull.Value : column.Size;
            row[SchemaTableColumn.NumericPrecision] = column.Precision == 0 ? DBNull.Value : (short)column.Precision;
            row[SchemaTableColumn.NumericScale] = column.Scale == 0 ? DBNull.Value : (short)column.Scale;
            row[SchemaTableColumn.DataType] = column.FieldType;
            row[SchemaTableColumn.AllowDBNull] = column.AllowNull;
            row[SchemaTableColumn.IsKey] = false;
            schema.Rows.Add(row);
        }

        return schema;
    }

    internal static object NormalizeValue(object value, Type fieldType)
        => value switch
        {
            Half half when fieldType == typeof(float) => (float)half,
            DateOnly dateOnly when fieldType == typeof(DateTime) => dateOnly.ToDateTime(TimeOnly.MinValue),
            TimeOnly timeOnly when fieldType == typeof(TimeSpan) => timeOnly.ToTimeSpan(),
            _ => value
        };
}
#pragma warning restore CS1591
