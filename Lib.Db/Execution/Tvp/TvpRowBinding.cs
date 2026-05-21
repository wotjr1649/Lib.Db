// ============================================================================
// 파일: Execution/Tvp/TvpRowBinding.cs
// 설명: 런타임 TVP wrapper를 SqlClient가 소비할 reader 값으로 변환
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;

namespace Lib.Db.Execution.Tvp;

internal static class TvpRowBinding
{
    private const string ReflectionFallbackRequiresUnreferencedCodeMessage =
        "Runtime TVP row shape inference inspects public row properties. Use a static TvpShape for Native AOT.";
    private const string ReflectionFallbackRequiresDynamicCodeMessage =
        "Runtime TVP row shape inference compiles expression accessors. Use a static TvpShape for Native AOT.";
    private static readonly ConcurrentDictionary<Type, RuntimeTvpRowShape> s_shapeCache = new();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The annotated fallback is reached only when the caller did not provide a static TVP shape; public non-shape TVP overloads bubble the warning to callers.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The annotated fallback is reached only when the caller did not provide a static TVP shape; public non-shape TVP overloads bubble the warning to callers.")]
    internal static object CreateParameterValue(LibDbTvpValue tvp)
    {
        ArgumentNullException.ThrowIfNull(tvp);

        if (tvp.Rows is DbDataReader dbDataReader)
            return dbDataReader;

        if (tvp.Rows is DataTable dataTable)
            return dataTable.CreateDataReader();

        if (tvp.RowShape is not null)
        {
            if (tvp.Rows is not IEnumerable rows)
                throw new InvalidOperationException("TVP rows must be an enumerable row source.");

            return new SqlDataRecordTvpEnumerable(rows, tvp.RowShape);
        }

        return CreateReader(tvp);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The annotated fallback is reached only when the caller did not provide a static TVP shape; public non-shape TVP overloads bubble the warning to callers.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "The annotated fallback is reached only when the caller did not provide a static TVP shape; public non-shape TVP overloads bubble the warning to callers.")]
    internal static DbDataReader CreateReader(LibDbTvpValue tvp)
    {
        ArgumentNullException.ThrowIfNull(tvp);

        if (tvp.Rows is DbDataReader dbDataReader)
            return dbDataReader;

        if (tvp.Rows is DataTable dataTable)
            return dataTable.CreateDataReader();

        if (tvp.Rows is not IEnumerable rows)
            throw new InvalidOperationException("TVP rows must be an enumerable row source.");

        if (tvp.RowShape is not null)
            return RuntimeTvpDataReader.Create(rows, tvp.RowShape);

        if (tvp.SchemaDescriptor is not null)
        {
            Type descriptorRowType = tvp.RowType ?? TryGetElementType(tvp.Rows.GetType())
                ?? throw new InvalidOperationException("TVP row type could not be inferred.");
            RuntimeTvpRowShape descriptorShape = TvpRowAccessorCache.GetOrAdd(
                descriptorRowType,
                tvp.SchemaDescriptor,
                tvp.Policy);

            return RuntimeTvpDataReader.Create(rows, descriptorShape);
        }

        return CreateReaderWithInferredShape(tvp);
    }

    internal static void ClearCache()
    {
        s_shapeCache.Clear();
        TvpRowAccessorCache.Clear();
    }

    [RequiresUnreferencedCode(ReflectionFallbackRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionFallbackRequiresDynamicCodeMessage)]
    private static DbDataReader CreateReaderWithInferredShape(LibDbTvpValue tvp)
    {
        if (tvp.Rows is not IEnumerable rows)
            throw new InvalidOperationException("TVP rows must be an enumerable row source.");

        Type rowType = tvp.RowType ?? TryGetElementType(tvp.Rows.GetType())
            ?? throw new InvalidOperationException("TVP row type could not be inferred.");

        RuntimeTvpRowShape shape = s_shapeCache.GetOrAdd(rowType, BuildShape);
        return RuntimeTvpDataReader.Create(rows, shape);
    }

    [RequiresUnreferencedCode(ReflectionFallbackRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionFallbackRequiresDynamicCodeMessage)]
    private static RuntimeTvpRowShape BuildShape(Type rowType)
    {
        if (typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(rowType))
            throw new InvalidOperationException("Dictionary TVP rows require an explicit schema before binding.");

        PropertyInfo[] properties = rowType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
            .ToArray();

        if (properties.Length == 0)
            throw new InvalidOperationException($"TVP row type '{rowType.Name}' does not expose public readable columns.");

        TvpColumnShape[] columns = new TvpColumnShape[properties.Length];
        Func<object, object?>[] accessors = new Func<object, object?>[properties.Length];
        Dictionary<string, int> ordinals = new(properties.Length, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            Type fieldType = NormalizeFieldType(property.PropertyType);
            bool allowNull = !property.PropertyType.IsValueType
                || Nullable.GetUnderlyingType(property.PropertyType) is not null;
            DbParameterAttribute? parameter = property.GetCustomAttribute<DbParameterAttribute>(inherit: true);
            TvpLengthAttribute? length = property.GetCustomAttribute<TvpLengthAttribute>(inherit: true);
            TvpPrecisionAttribute? precision = property.GetCustomAttribute<TvpPrecisionAttribute>(inherit: true);

            int size = parameter is { Size: not 0 }
                ? parameter.Size
                : length?.Length ?? 0;

            byte numericPrecision = parameter is { Precision: not 0 }
                ? parameter.Precision
                : precision?.Precision ?? 0;

            byte numericScale = parameter is { Scale: not 0 }
                ? parameter.Scale
                : precision?.Scale ?? 0;

            columns[i] = allowNull
                ? TvpColumnShape.Optional(
                    property.Name,
                    fieldType,
                    size,
                    numericPrecision,
                    numericScale)
                : TvpColumnShape.Required(
                    property.Name,
                    fieldType,
                    size,
                    numericPrecision,
                    numericScale);

            accessors[i] = CreateObjectGetter(rowType, property);

            if (!ordinals.TryAdd(property.Name, i))
                throw new InvalidOperationException($"Duplicate TVP property name: {property.Name} in {rowType.Name}");
        }

        return new RuntimeTvpRowShape(
            rowType,
            columns,
            accessors,
            ordinals,
            RuntimeTvpDataReader.BuildSchemaTable(columns));
    }

    private static Type NormalizeFieldType(Type type)
    {
        Type fieldType = Nullable.GetUnderlyingType(type) ?? type;
        if (fieldType == typeof(Half))
            return typeof(float);

        if (fieldType == typeof(DateOnly))
            return typeof(DateTime);

        if (fieldType == typeof(TimeOnly))
            return typeof(TimeSpan);

        return fieldType;
    }

    [RequiresDynamicCode(ReflectionFallbackRequiresDynamicCodeMessage)]
    private static Func<object, object?> CreateObjectGetter(Type rowType, PropertyInfo property)
    {
        ParameterExpression row = Expression.Parameter(typeof(object), "row");
        UnaryExpression typedRow = Expression.Convert(row, rowType);
        MemberExpression propertyValue = Expression.Property(typedRow, property);
        UnaryExpression boxedValue = Expression.Convert(propertyValue, typeof(object));
        return Expression.Lambda<Func<object, object?>>(boxedValue, row).Compile();
    }

    [RequiresUnreferencedCode(ReflectionFallbackRequiresUnreferencedCodeMessage)]
    private static Type? TryGetElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return type.GetGenericArguments()[0];

        foreach (Type iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }

        return null;
    }
}
