using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Collections;
using System.Collections.Concurrent;

namespace Lib.Db.Execution.Binding;

/// <summary>
/// Source Generator가 생성한 TVP Reader 팩토리를 등록하는 레지스트리입니다.
/// <para>이 클래스는 내부 인프라용이며 직접 사용해서는 안 됩니다.</para>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// Source Generator가 생성한 코드를 런타임에 검색할 수 있도록 연결 고리를 제공합니다.
/// 리플렉션을 통한 타입 스캔을 피하고, 정적 초기화 시점에 팩토리를 등록하여 AOT 호환성을 확보합니다.
/// Concrete Type에 대한 캐싱(Smart Cache)을 통해 Generic Interface 조회 비용을 최소화합니다.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TvpFactoryRegistry
{
    #region 레지스트리 및 캐시

    // Key: DTO Type
    // Value: (Factory Delegate, SQL Type Name)
    private static readonly Dictionary<Type, (Func<object, IDataReader> Factory, string TypeName)> s_registry = new();

    // Cache: Concrete Type -> (Factory, TypeName)
    // Positive hits allow fast O(1) access.
    // Negative misses are also cached (Factory = null) to avoid repeat reflection scanning.
    private static readonly ConcurrentDictionary<Type, (Func<object, IDataReader>? Factory, string? TypeName)> s_cache = new();

    #endregion

    #region 공개 API (Registration & Lookup)

    /// <summary>
    /// TVP Reader 팩토리를 등록합니다. (ModuleInitializer에서 호출)
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(Type type, Func<object, IDataReader> factory, string typeName)
    {
        lock (s_registry)
        {
            s_registry[type] = (factory, typeName);
        }
    }

    /// <summary>
    /// 등록된 TVP Reader 팩토리를 조회합니다. (Smart Cache)
    /// </summary>
    internal static bool TryGet(Type concreteType, out Func<object, IDataReader>? factory, out string? typeName)
    {
        // 1. Fast Cache Lookup
        if (s_cache.TryGetValue(concreteType, out var entry))
        {
            factory = entry.Factory;
            typeName = entry.TypeName;
            return factory is not null;
        }

        // 2. Slow Scan (Registry Lookup)
        return TryResolveAndCache(concreteType, out factory, out typeName);
    }

    #endregion

    #region 내부 로직 (Resolve & Cache)

    private static bool TryResolveAndCache(Type concreteType, out Func<object, IDataReader>? factory, out string? typeName)
    {
        // Check direct registry match (rare for List<T>)
        lock (s_registry)
        {
            if (s_registry.TryGetValue(concreteType, out var regEntry))
            {
                factory = regEntry.Factory;
                typeName = regEntry.TypeName;
                s_cache[concreteType] = (factory, typeName);
                return true;
            }
        }

        // Interface Scan (IEnumerable<T>)
        foreach (var iface in concreteType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                lock (s_registry)
                {
                    if (s_registry.TryGetValue(iface, out var regEntry))
                    {
                        factory = regEntry.Factory;
                        typeName = regEntry.TypeName;
                        s_cache[concreteType] = (factory, typeName);
                        return true;
                    }
                }
            }
        }

        // Not Found (Negative Cache)
        s_cache[concreteType] = (null, null);
        factory = null;
        typeName = null;
        return false;
    }

    #endregion
}
