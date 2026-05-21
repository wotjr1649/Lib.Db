// ============================================================================
// 파일: Execution/Tvp/LibDbTvpValue.cs
// 설명: 런타임 TVP 명시 바인딩 값
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Diagnostics.CodeAnalysis;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 런타임 TVP 스키마 불일치 처리 정책입니다.
/// </summary>
public enum TvpBindingPolicy
{
    /// <summary>
    /// 컬럼 누락, 타입 불일치, 필수 값 누락을 즉시 예외로 처리합니다.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// nullable/default-safe 변경만 런타임에서 보정합니다.
    /// </summary>
    Adaptive = 1
}

/// <summary>
/// SQL Server TVP 파라미터로 바인딩할 row source와 type name을 담는 명시 wrapper입니다.
/// </summary>
public sealed record LibDbTvpValue(
    TvpTypeName TypeName,
    object Rows,
    Type? RowType,
    TvpBindingPolicy Policy)
{
    private const string ReflectionTvpRequiresUnreferencedCodeMessage =
        "Reflection-based TVP row discovery inspects row metadata. Use LibDb.Tvp(..., shape) or options.Tvp.Map<T>().Column(...) for Native AOT.";

    private const string ReflectionTvpRequiresDynamicCodeMessage =
        "Reflection-based TVP row discovery compiles runtime accessors. Use static TVP shape mapping for Native AOT.";

    internal RuntimeTvpRowShape? RowShape { get; init; }

    /// <summary>
    /// DB에서 조회한 TVP 스키마 descriptor입니다. 지정되면 descriptor/fingerprint 기반 row accessor cache를 사용합니다.
    /// </summary>
    public TvpSchemaDescriptor? SchemaDescriptor { get; init; }

    /// <summary>
    /// strongly typed row sequence를 TVP 값으로 감쌉니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="typeName">SQL Server TVP type name입니다.</param>
    /// <param name="rows">TVP로 전달할 row sequence입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>명시 TVP 바인딩 값입니다.</returns>
    [RequiresUnreferencedCode(ReflectionTvpRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionTvpRequiresDynamicCodeMessage)]
    public static LibDbTvpValue Create<T>(
        string typeName,
        IEnumerable<T> rows,
        TvpBindingPolicy policy = TvpBindingPolicy.Strict)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return new LibDbTvpValue(
            TvpTypeName.Parse(typeName),
            rows,
            typeof(T),
            policy);
    }

    /// <summary>
    /// strongly typed row sequence와 정적 row shape를 TVP 값으로 감쌉니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="typeName">SQL Server TVP type name입니다.</param>
    /// <param name="rows">TVP로 전달할 row sequence입니다.</param>
    /// <param name="shape">AOT 친화 정적 row shape입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>명시 TVP 바인딩 값입니다.</returns>
    public static LibDbTvpValue Create<T>(
        string typeName,
        IEnumerable<T> rows,
        TvpShape<T> shape,
        TvpBindingPolicy policy = TvpBindingPolicy.Strict)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(shape);

        return new LibDbTvpValue(
            TvpTypeName.Parse(typeName),
            rows,
            typeof(T),
            policy)
        {
            RowShape = shape.RuntimeShape
        };
    }

    /// <summary>
    /// DB 스키마 descriptor와 strongly typed row sequence를 TVP 값으로 감쌉니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="descriptor">DB에서 조회한 TVP 스키마 descriptor입니다.</param>
    /// <param name="rows">TVP로 전달할 row sequence입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>명시 TVP 바인딩 값입니다.</returns>
    [RequiresUnreferencedCode(ReflectionTvpRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionTvpRequiresDynamicCodeMessage)]
    public static LibDbTvpValue Create<T>(
        TvpSchemaDescriptor descriptor,
        IEnumerable<T> rows,
        TvpBindingPolicy policy = TvpBindingPolicy.Adaptive)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(rows);

        return new LibDbTvpValue(
            descriptor.TypeName,
            rows,
            typeof(T),
            policy)
        {
            SchemaDescriptor = descriptor
        };
    }
}
