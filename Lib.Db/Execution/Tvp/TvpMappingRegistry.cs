// ============================================================================
// 파일: Execution/Tvp/TvpMappingRegistry.cs
// 설명: CLR row type과 SQL Server TVP type name 매핑 레지스트리
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 자동 TVP 바인딩을 위한 CLR row type 매핑을 관리합니다.
/// </summary>
public sealed class TvpMappingRegistry
{
    private readonly ConcurrentDictionary<Type, TvpMapping> _mappings = new();

    /// <summary>
    /// CLR row type과 SQL Server TVP type name을 매핑합니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="typeName">SQL Server TVP type name입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    public TvpMappingBuilder<T> Map<T>(
        string typeName,
        TvpBindingPolicy policy = TvpBindingPolicy.Strict)
    {
        _mappings[typeof(T)] = new TvpMapping(TvpTypeName.Parse(typeName), policy, Shape: null);
        return new TvpMappingBuilder<T>(this);
    }

    /// <summary>
    /// 등록된 CLR row type 매핑을 조회합니다.
    /// </summary>
    /// <param name="rowType">TVP row CLR 타입입니다.</param>
    /// <param name="typeName">조회된 SQL Server TVP type name입니다.</param>
    /// <param name="policy">조회된 schema drift 처리 정책입니다.</param>
    /// <returns>매핑이 등록되어 있으면 <c>true</c>입니다.</returns>
    public bool TryResolve(
        Type rowType,
        out TvpTypeName typeName,
        out TvpBindingPolicy policy)
        => TryResolve(rowType, out typeName, out policy, out _);

    internal bool TryResolve(
        Type rowType,
        out TvpTypeName typeName,
        out TvpBindingPolicy policy,
        out RuntimeTvpRowShape? shape)
    {
        ArgumentNullException.ThrowIfNull(rowType);

        if (_mappings.TryGetValue(rowType, out TvpMapping mapping))
        {
            typeName = mapping.TypeName;
            policy = mapping.Policy;
            shape = mapping.Shape;
            return true;
        }

        typeName = default;
        policy = TvpBindingPolicy.Strict;
        shape = null;
        return false;
    }

    internal void SetShape<T>(RuntimeTvpRowShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        Type rowType = typeof(T);
        if (!_mappings.TryGetValue(rowType, out TvpMapping mapping))
            throw new InvalidOperationException($"TVP row type '{rowType.Name}' is not registered.");

        _mappings[rowType] = mapping with { Shape = shape };
    }

    private readonly record struct TvpMapping(
        TvpTypeName TypeName,
        TvpBindingPolicy Policy,
        RuntimeTvpRowShape? Shape);

}
