// ============================================================================
// 파일: Lib.Db/Execution/Binding/TvpFactoryRegistry.cs
// 설명: TVP 팩토리 레지스트리 — 명시 등록 Fast TVP 팩토리 등록/조회 관리
// 대상: .NET 10 / C# 14
// ============================================================================

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Lib.Db.Execution.Tvp;

namespace Lib.Db.Execution.Binding;

/// <summary>
/// 명시 등록된 TVP Reader 팩토리를 관리하는 레지스트리입니다.
/// <para>이 클래스는 내부 인프라용이며 직접 사용해서는 안 됩니다.</para>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 반복 호출 경로에서 리플렉션 기반 타입 스캔을 피할 수 있도록 정적 팩토리 연결 고리를 제공합니다.
/// 수동 등록 또는 호환 레거시 등록 경로에서 팩토리를 등록하여 AOT 친화적인 fast-path를 확보합니다.
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
    /// TVP Reader 팩토리를 등록합니다.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(Type type, Func<object, IDataReader> factory, string typeName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);
        string normalizedTypeName = TvpTypeName.Parse(typeName).FullName;

        lock (s_registry)
        {
            s_registry[type] = (factory, normalizedTypeName);
        }
    }

    /// <summary>
    /// 등록된 TVP Reader 팩토리를 조회합니다. (Smart Cache)
    /// </summary>
    internal static bool TryGet(Type concreteType, out Func<object, IDataReader>? factory, out string? typeName)
    {
        // 1. Fast Cache Lookup
        if (s_cache.TryGetValue(concreteType, out (Func<object, IDataReader>? Factory, string? TypeName) entry))
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

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Source-generated TVP factories are registered explicitly; interface scanning only matches concrete IEnumerable<T> wrappers to those factories.")]
    private static bool TryResolveAndCache(Type concreteType, out Func<object, IDataReader>? factory, out string? typeName)
    {
        // Check direct registry match (rare for List<T>)
        lock (s_registry)
        {
            if (s_registry.TryGetValue(concreteType, out (Func<object, IDataReader> Factory, string TypeName) regEntry))
            {
                factory = regEntry.Factory;
                typeName = regEntry.TypeName;
                s_cache[concreteType] = (factory, typeName);
                return true;
            }
        }

        // Interface Scan (IEnumerable<T>)
        foreach (Type iface in concreteType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                lock (s_registry)
                {
                    if (s_registry.TryGetValue(iface, out (Func<object, IDataReader> Factory, string TypeName) regEntry))
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
