// ============================================================================
// 파일: Execution/Tvp/TvpShape.cs
// 설명: NativeAOT 친화적인 정적 TVP row shape 등록 API
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 명시 wrapper API에서 재사용할 정적 TVP row shape를 생성합니다.
/// </summary>
public static class TvpShape
{
    /// <summary>
    /// 지정된 row 타입에 대한 TVP shape builder를 생성합니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <returns>TVP shape builder입니다.</returns>
    public static TvpShapeBuilder<T> For<T>() => new();
}

/// <summary>
/// 정적 컬럼 shape와 accessor delegate를 담는 AOT 친화 TVP row shape입니다.
/// </summary>
/// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
public sealed class TvpShape<T>
{
    internal TvpShape(RuntimeTvpRowShape runtimeShape)
        => RuntimeShape = runtimeShape;

    internal RuntimeTvpRowShape RuntimeShape { get; }

    /// <summary>
    /// 등록된 TVP 컬럼 목록입니다.
    /// </summary>
    public IReadOnlyList<TvpColumnShape> Columns => RuntimeShape.Columns;
}

/// <summary>
/// 정적 TVP row shape를 구성하는 fluent builder입니다.
/// </summary>
/// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
public sealed class TvpShapeBuilder<T>
{
    private readonly List<TvpColumnShape> _columns = [];
    private readonly List<Func<object, object?>> _accessors = [];

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
    public TvpShapeBuilder<T> Column<TValue>(
        string name,
        SqlDbType dbType,
        Func<T, TValue> accessor,
        int size = 0,
        byte precision = 0,
        byte scale = 0,
        bool allowNull = false)
    {
        AddColumn(name, dbType, accessor, size, precision, scale, allowNull);
        return this;
    }

    /// <summary>
    /// 등록된 컬럼으로 재사용 가능한 TVP shape를 생성합니다.
    /// </summary>
    /// <returns>TVP shape입니다.</returns>
    public TvpShape<T> Build()
    {
        if (_columns.Count == 0)
            throw new InvalidOperationException("At least one TVP column must be registered.");

        TvpColumnShape[] columns = _columns.ToArray();
        Func<object, object?>[] accessors = _accessors.ToArray();
        Dictionary<string, int> ordinals = BuildOrdinals(columns);

        return new TvpShape<T>(
            new RuntimeTvpRowShape(
                typeof(T),
                columns,
                accessors,
                ordinals,
                RuntimeTvpDataReader.BuildSchemaTable(columns)));
    }

    internal void AddColumn<TValue>(
        string name,
        SqlDbType dbType,
        Func<T, TValue> accessor,
        int size,
        byte precision,
        byte scale,
        bool allowNull)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        TvpColumnShape column = TvpColumnShape.FromSql<TValue>(
            name,
            dbType,
            allowNull,
            size,
            precision,
            scale);

        if (_columns.Any(existing => string.Equals(existing.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Duplicate TVP column name: {column.Name}");

        _columns.Add(column);
        _accessors.Add(row => accessor((T)row));
    }

    private static Dictionary<string, int> BuildOrdinals(TvpColumnShape[] columns)
    {
        var ordinals = new Dictionary<string, int>(columns.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Length; i++)
            ordinals.Add(columns[i].Name, i);

        return ordinals;
    }
}
