// ============================================================================
// 파일: Execution/Tvp/TvpRowAccessorCache.cs
// 설명: TVP descriptor 기반 row accessor shape 캐시
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Lib.Db.Contracts.Models;

namespace Lib.Db.Execution.Tvp;

internal static class TvpRowAccessorCache
{
    private const string DescriptorAccessorRequiresUnreferencedCodeMessage =
        "Descriptor-based TVP POCO binding inspects public row properties. Use a static TvpShape for Native AOT.";

    private const string DescriptorAccessorRequiresDynamicCodeMessage =
        "Descriptor-based TVP POCO binding compiles expression accessors. Use a static TvpShape for Native AOT.";

    private static readonly ConcurrentDictionary<CacheKey, RuntimeTvpRowShape> s_cache = new();

    [RequiresUnreferencedCode(DescriptorAccessorRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(DescriptorAccessorRequiresDynamicCodeMessage)]
    internal static RuntimeTvpRowShape GetOrAdd(
        Type rowType,
        TvpSchemaDescriptor descriptor,
        TvpBindingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(rowType);
        ArgumentNullException.ThrowIfNull(descriptor);

        string computedFingerprint = TvpSchemaFingerprint.Compute(
            descriptor.TypeName,
            descriptor.VersionToken,
            descriptor.Columns);
        if (!string.IsNullOrWhiteSpace(descriptor.Fingerprint) &&
            !string.Equals(descriptor.Fingerprint, computedFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"TVP schema descriptor fingerprint mismatch for '{descriptor.TypeName.FullName}'.");
        }

        string fingerprint = computedFingerprint;
        CacheKey key = new(rowType, descriptor.TypeName.FullName, fingerprint, policy);

        return s_cache.GetOrAdd(
            key,
            static (cacheKey, state) => BuildShape(cacheKey.RowType, state.descriptor, cacheKey.Policy),
            (descriptor, policy));
    }

    internal static void Clear() => s_cache.Clear();

    internal static void Clear(TvpTypeName typeName)
    {
        foreach (CacheKey key in s_cache.Keys)
        {
            if (string.Equals(key.TypeName, typeName.FullName, StringComparison.OrdinalIgnoreCase))
                s_cache.TryRemove(key, out _);
        }
    }

    [RequiresUnreferencedCode(DescriptorAccessorRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(DescriptorAccessorRequiresDynamicCodeMessage)]
    private static RuntimeTvpRowShape BuildShape(
        Type rowType,
        TvpSchemaDescriptor descriptor,
        TvpBindingPolicy policy)
    {
        TvpColumnMetadata[] metadata = descriptor.Columns
            .OrderBy(static column => column.Ordinal)
            .ThenBy(static column => column.Name, StringComparer.Ordinal)
            .ToArray();

        if (metadata.Length == 0)
            throw new InvalidOperationException($"TVP type '{descriptor.TypeName.FullName}' does not expose columns.");

        TvpColumnShape[] columns = metadata.Select(ToColumnShape).ToArray();
        Func<object, object?>[] accessors = BuildAccessors(rowType, columns, policy);
        Dictionary<string, int> ordinals = BuildOrdinals(columns);

        return new RuntimeTvpRowShape(
            rowType,
            columns,
            accessors,
            ordinals,
            RuntimeTvpDataReader.BuildSchemaTable(columns));
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Descriptor-driven TVP accessor discovery is a runtime adaptive path. Native AOT hot paths use registered static shapes.")]
    [RequiresUnreferencedCode(DescriptorAccessorRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(DescriptorAccessorRequiresDynamicCodeMessage)]
    private static Func<object, object?>[] BuildAccessors(
        Type rowType,
        TvpColumnShape[] columns,
        TvpBindingPolicy policy)
    {
        if (typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(rowType))
            return BuildDictionaryAccessors(columns, policy);

        Dictionary<string, PropertyInfo> properties = rowType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
            .ToDictionary(static prop => prop.Name, StringComparer.OrdinalIgnoreCase);

        Func<object, object?>[] accessors = new Func<object, object?>[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            TvpColumnShape column = columns[i];
            if (!properties.TryGetValue(column.Name, out PropertyInfo? property))
            {
                if (policy == TvpBindingPolicy.Adaptive && column.AllowNull)
                {
                    accessors[i] = static _ => DBNull.Value;
                    continue;
                }

                throw new InvalidOperationException(
                    $"TVP row type '{rowType.Name}' does not expose required column '{column.Name}'.");
            }

            accessors[i] = CreateObjectGetter(rowType, property);
        }

        return accessors;
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

    [RequiresDynamicCode(DescriptorAccessorRequiresDynamicCodeMessage)]
    private static Func<object, object?> CreateObjectGetter(Type rowType, PropertyInfo property)
    {
        ParameterExpression row = Expression.Parameter(typeof(object), "row");
        UnaryExpression typedRow = Expression.Convert(row, rowType);
        MemberExpression propertyValue = Expression.Property(typedRow, property);
        UnaryExpression boxedValue = Expression.Convert(propertyValue, typeof(object));
        return Expression.Lambda<Func<object, object?>>(boxedValue, row).Compile();
    }

    private static TvpColumnShape ToColumnShape(TvpColumnMetadata column)
        => new(
            column.Name,
            ResolveFieldType(column.SqlDbType),
            column.IsNullable,
            MapSize(column),
            column.Precision,
            column.Scale,
            column.SqlDbType);

    private static int MapSize(TvpColumnMetadata column)
    {
        if (column.MaxLength <= 0)
            return checked((int)column.MaxLength);

        return column.SqlDbType switch
        {
            SqlDbType.NChar or SqlDbType.NVarChar => checked((int)(column.MaxLength / 2)),
            _ => checked((int)column.MaxLength)
        };
    }

    private static Type ResolveFieldType(SqlDbType dbType)
        => dbType switch
        {
            SqlDbType.BigInt => typeof(long),
            SqlDbType.Binary or SqlDbType.Image or SqlDbType.Timestamp or SqlDbType.VarBinary => typeof(byte[]),
            SqlDbType.Bit => typeof(bool),
            SqlDbType.Char or SqlDbType.NChar or SqlDbType.NText or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.VarChar or SqlDbType.Xml => typeof(string),
            SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime => typeof(DateTime),
            SqlDbType.DateTimeOffset => typeof(DateTimeOffset),
            SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney => typeof(decimal),
            SqlDbType.Float => typeof(double),
            SqlDbType.Int => typeof(int),
            SqlDbType.Real => typeof(float),
            SqlDbType.SmallInt => typeof(short),
            SqlDbType.Time => typeof(TimeSpan),
            SqlDbType.TinyInt => typeof(byte),
            SqlDbType.UniqueIdentifier => typeof(Guid),
            _ => typeof(object)
        };

    private static Dictionary<string, int> BuildOrdinals(TvpColumnShape[] columns)
    {
        var ordinals = new Dictionary<string, int>(columns.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Length; i++)
            ordinals.Add(columns[i].Name, i);

        return ordinals;
    }

    private readonly record struct CacheKey(
        Type RowType,
        string TypeName,
        string Fingerprint,
        TvpBindingPolicy Policy);
}
