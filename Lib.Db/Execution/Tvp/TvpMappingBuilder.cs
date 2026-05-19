// ============================================================================
// 파일: Execution/Tvp/TvpMappingBuilder.cs
// 설명: 등록형 TVP static-shape fluent API
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 등록된 TVP row type에 정적 컬럼 shape를 추가하는 builder입니다.
/// </summary>
/// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
public sealed class TvpMappingBuilder<T>
{
    private readonly TvpMappingRegistry _registry;
    private readonly TvpShapeBuilder<T> _shapeBuilder = new();

    internal TvpMappingBuilder(TvpMappingRegistry registry)
        => _registry = registry;

    /// <summary>
    /// TVP 컬럼과 해당 row accessor를 등록합니다.
    /// </summary>
    /// <typeparam name="TValue">컬럼 값 CLR 타입입니다.</typeparam>
    /// <param name="name">SQL Server TVP 컬럼 이름입니다.</param>
    /// <param name="dbType">SQL Server 컬럼 타입입니다.</param>
    /// <param name="accessor">row에서 컬럼 값을 읽는 delegate입니다. AOT fast-path에서는 static lambda 사용을 권장합니다.</param>
    /// <param name="size">문자열/바이너리 컬럼 크기입니다.</param>
    /// <param name="precision">decimal precision입니다.</param>
    /// <param name="scale">decimal 또는 time scale입니다.</param>
    /// <param name="allowNull">TVP 컬럼 null 허용 여부입니다.</param>
    /// <returns>현재 builder입니다.</returns>
    public TvpMappingBuilder<T> Column<TValue>(
        string name,
        SqlDbType dbType,
        Func<T, TValue> accessor,
        int size = 0,
        byte precision = 0,
        byte scale = 0,
        bool allowNull = false)
    {
        _shapeBuilder.AddColumn(name, dbType, accessor, size, precision, scale, allowNull);
        _registry.SetShape<T>(_shapeBuilder.Build().RuntimeShape);
        return this;
    }
}
