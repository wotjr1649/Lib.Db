// ============================================================================
// File: Lib.Db.Execution/Tvp/TvpMetadata.cs
// Role: TVP 접근자(Accessor)의 런타임 관리, 캐싱, 레지스트리
// Env : .NET 10 / C# 14
// Notes:
//   - TvpAccessorRegistry: Source Generator 접근자 등록소
//   - TvpAccessorCache: 런타임 리플렉션(Fallback) 및 캐시 관리
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Models;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Lib.Db.Execution.Tvp;

#region TVP 접근자 레지스트리

/// <summary>
/// Source Generator가 생성한 TVP 접근자를 등록하고 조회하는 중앙 레지스트리입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// - 컴파일 타임에 생성된 고성능 접근자(Zero-Reflection)를 런타임에 주입받아 사용합니다.<br/>
/// - DEBUG 모드에서는 런타임 생성 결과(Fallback)와 비교하여 정합성을 검증합니다.
/// </para>
/// </summary>
public static class TvpAccessorRegistry
{
    // 다양한 T에 대한 TvpAccessors<T>를 저장하기 위해 object로 저장
    private static readonly ConcurrentDictionary<Type, object> s_registry = new();

    /// <summary>
    /// 생성된 TVP 접근자를 레지스트리에 등록합니다. (ModuleInitializer 등에서 호출)
    /// </summary>
    public static void Register<T>(TvpAccessors<T> accessors)
    {
        s_registry[typeof(T)] = accessors;

#if DEBUG
        // [개발 전용] Source Generator와 Fallback(Reflection) 간의 정렬/구성 일치성 검증
        ValidateAgainstFallback(accessors);
#endif
    }

    /// <summary>
    /// 등록된 접근자가 있는지 확인하고 반환합니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGet<T>(out TvpAccessors<T>? accessors)
    {
        if (s_registry.TryGetValue(typeof(T), out var baseAccessors))
        {
            accessors = (TvpAccessors<T>)baseAccessors;
            return true;
        }
        accessors = null;
        return false;
    }

#if DEBUG
    /// <summary>
    /// [개발 전용] Source Generator 결과물과 런타임 리플렉션 결과물의 프로퍼티 순서/구성이 일치하는지 검증합니다.
    /// </summary>
    private static void ValidateAgainstFallback<T>(TvpAccessors<T> sgAccessors)
    {
        var fallbackAccessors = TvpAccessorCache.CompileAccessors<T>();

        // 1. 프로퍼티 개수 검증
        if (sgAccessors.Properties.Length != fallbackAccessors.Properties.Length)
        {
            throw new InvalidOperationException(
                $"[TVP 무결성 실패] {typeof(T).Name}: " +
                $"SG 프로퍼티 수({sgAccessors.Properties.Length}) != " +
                $"Fallback 프로퍼티 수({fallbackAccessors.Properties.Length})");
        }

        // 2. 프로퍼티 이름 및 순서 검증
        for (int i = 0; i < sgAccessors.Properties.Length; i++)
        {
            var sgProp = sgAccessors.Properties[i];
            var fbProp = fallbackAccessors.Properties[i];

            if (sgProp.Name != fbProp.Name)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[TVP 무결성 실패] {typeof(T).Name}");
                sb.AppendLine($"인덱스 [{i}]에서 프로퍼티 불일치 발생");
                sb.AppendLine($" - SG (SourceGen) : {sgProp.Name}");
                sb.AppendLine($" - FB (Reflection): {fbProp.Name}");
                sb.AppendLine();
                sb.AppendLine("== 전체 정렬 비교 ==");
                for (int j = 0; j < sgAccessors.Properties.Length; j++)
                {
                    sb.AppendLine($"[{j}] SG: {sgAccessors.Properties[j].Name.PadRight(20)} | FB: {fallbackAccessors.Properties[j].Name}");
                }

                throw new InvalidOperationException(sb.ToString());
            }
        }
    }
#endif
}

#endregion

#region TVP 접근자 캐시

/// <summary>
/// 런타임 리플렉션을 사용하여 TVP 접근자를 생성하고 캐싱하는 관리자입니다.
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 1. Source Generator가 없는 환경(JIT)에서의 Fallback 처리<br/>
/// 2. Bounded Cache(제한된 크기)를 통한 메모리 관리<br/>
/// 3. PropertyInfo 정렬 규칙(MetadataToken 기반)의 기준점 제공
/// </para>
/// </summary>
public static class TvpAccessorCache
{
    private static int s_maxCacheSize = 10_000;
    private static readonly ConcurrentDictionary<Type, object> s_fallbackCache = new();

    /// <summary>
    /// 라이브러리 옵션을 통해 캐시 정책을 설정합니다.
    /// </summary>
    public static void Configure(LibDbOptions options)
        => s_maxCacheSize = options.MaxCacheSize > 0 ? options.MaxCacheSize : 10_000;

    /// <summary>
    /// 내부 캐시를 모두 비웁니다.
    /// </summary>
    public static void Clear() => s_fallbackCache.Clear();

    /// <summary>
    /// 지정된 타입 T에 대한 TVP 접근자를 가져옵니다. (Registry 우선 -> Fallback)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TvpAccessors<T> GetTypedAccessors<T>()
    {
        // 1. Registry 확인 (Source Generator로 등록된 것이 있으면 최우선 사용)
        if (TvpAccessorRegistry.TryGet<T>(out var gen)) return gen!;

        // 2. Fallback (런타임 생성 및 캐싱)
        return (TvpAccessors<T>)GetOrAddFallback<T>();
    }

    /// <summary>
    /// 비제네릭 컨텍스트에서 접근자를 가져옵니다.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TvpAccessors GetAccessors<T>() => GetTypedAccessors<T>();

    private static object GetOrAddFallback<T>()
    {
        var type = typeof(T);
        if (s_fallbackCache.TryGetValue(type, out var accessors)) return accessors;

        // Bounded Cache: 용량 초과 시 전체 초기화 (LRU보다 저비용)
        if (s_fallbackCache.Count >= s_maxCacheSize) s_fallbackCache.Clear();

        return s_fallbackCache.GetOrAdd(type, static _ => CompileAccessors<T>());
    }

    /// <summary>
    /// [Internal] 리플렉션을 사용하여 TVP 접근자를 컴파일합니다.
    /// <para>
    /// - <see cref="TvpAccessors.BuildSchemaTable"/>을 호출하여 스키마 생성 (Half 지원 포함)<br/>
    /// - Getter 델리게이트 생성 (Typed / Object)<br/>
    /// - Ordinal 매핑 생성
    /// </para>
    /// </summary>
    internal static TvpAccessors<T> CompileAccessors<T>()
    {
        // 1. 프로퍼티 추출 및 정렬 (MetadataToken 순서 준수)
        var props = GetAllPublicReadablePropsRuntime(typeof(T)).ToArray();
        int count = props.Length;

        var typedAccessors = new Func<T, object?>[count];
        var objAccessors = new Func<object, object?>[count];
        var ordinalBuilder = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);

        // 2. 스키마 테이블 생성 (TvpModels.cs의 공통 로직 사용)
        //    [중요] 여기서 Half -> Float 변환 등 메타데이터 보정이 수행됩니다.
        var schemaTable = TvpAccessors.BuildSchemaTable(props);

        // 3. 접근자 델리게이트 생성
        for (int i = 0; i < count; i++)
        {
            var prop = props[i];
            typedAccessors[i] = CreateTypedGetterDelegate<T>(prop);
            objAccessors[i] = CreateObjectGetterDelegate<T>(prop);

            if (!ordinalBuilder.TryAdd(prop.Name, i))
                throw new InvalidOperationException($"Duplicate TVP property name: {prop.Name} in {typeof(T).Name}");
        }

        // 4. 명시적 SQL 타입명 확인 ([TvpRow(TypeName = "...")]])
        var attr = typeof(T).GetCustomAttribute<TvpRowAttribute>();
        string? sqlTypeName = attr?.TypeName;

        return new TvpAccessors<T>
        {
            Properties = props,
            Accessors = objAccessors,
            TypedAccessors = typedAccessors,
            // .NET 8+ FrozenDictionary로 조회 성능 최적화
            OrdinalMap = ordinalBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            SchemaTable = schemaTable,
            SqlTypeName = sqlTypeName,
            IsValidated = false
        };
    }

    /// <summary>
    /// 타입의 모든 읽기 가능한 Public 프로퍼티를 가져와서 안정적으로 정렬합니다.
    /// </summary>
    private static List<PropertyInfo> GetAllPublicReadablePropsRuntime(Type t)
    {
        var list = new List<PropertyInfo>();
        var current = t;

        // 상속 계층 순회
        while (current is not null && current != typeof(object))
        {
            var props = current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                               .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);
            list.AddRange(props);
            current = current.BaseType;
        }

        // [Strict Compliance] MetadataToken 기반 정렬로 컴파일러 생성 순서 보장
        list.Sort(StableRuntimePropertyComparer.Instance);
        return list;
    }

    // Expression Tree를 사용하여 Typed Getter 생성 (T -> object)
    private static Func<T, object?> CreateTypedGetterDelegate<T>(PropertyInfo prop)
    {
        var p = Expression.Parameter(typeof(T), "x");
        var propAccess = Expression.Property(p, prop);
        var convert = Expression.Convert(propAccess, typeof(object));

        if (typeof(T).IsValueType) return Expression.Lambda<Func<T, object?>>(convert, p).Compile();

        var nullCheck = Expression.Equal(p, Expression.Constant(null, typeof(T)));
        var body = Expression.Condition(nullCheck, Expression.Constant(null, typeof(object)), convert);
        return Expression.Lambda<Func<T, object?>>(body, p).Compile();
    }

    // Expression Tree를 사용하여 Object Getter 생성 (object -> object)
    private static Func<object, object?> CreateObjectGetterDelegate<T>(PropertyInfo prop)
    {
        var p = Expression.Parameter(typeof(object), "o");
        var cast = Expression.Convert(p, typeof(T));
        var propAccess = Expression.Property(cast, prop);
        var convert = Expression.Convert(propAccess, typeof(object));

        if (typeof(T).IsValueType) return Expression.Lambda<Func<object, object?>>(convert, p).Compile();

        var nullCheck = Expression.Equal(p, Expression.Constant(null));
        var body = Expression.Condition(nullCheck, Expression.Constant(null, typeof(object)), convert);
        return Expression.Lambda<Func<object, object?>>(body, p).Compile();
    }

    /// <summary>
    /// PropertyInfo 목록을 안정적으로 정렬하기 위한 Comparer입니다.
    /// <para>
    /// ✅ Source Generator (StableSymbolPropertyComparer)와 동일한 알파벳 순서 정렬을 사용합니다.
    /// ✅ 이를 통해 DEBUG 모드의 ValidateAgainstFallback 검증을 통과합니다.
    /// </para>
    /// </summary>
    /// <remarks>
    /// [Change Log 2025-12-19]
    /// - Before: MetadataToken-based complex sort (Inheritance depth → Type Token → Property Token → Name)
    /// - After: StringComparer.Ordinal simple alphabetical sort
    /// - Reason: Must match Source Generator (TvpAccessorGenerator.StableSymbolPropertyComparer)
    /// </remarks>
    private sealed class StableRuntimePropertyComparer : IComparer<PropertyInfo>
    {
        public static readonly StableRuntimePropertyComparer Instance = new();

        public int Compare(PropertyInfo? a, PropertyInfo? b)
        {
            // Source Generator와 동일한 알파벳 순서 정렬
            return StringComparer.Ordinal.Compare(a?.Name, b?.Name);
        }
    }
}

#endregion