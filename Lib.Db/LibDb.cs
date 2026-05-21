// ============================================================================
// 파일: LibDb.cs
// 설명: Lib.Db public convenience facade
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Diagnostics.CodeAnalysis;
using Lib.Db.Execution.Tvp;

namespace Lib.Db;

/// <summary>
/// Lib.Db의 간결한 public helper API를 제공합니다.
/// </summary>
public static class LibDb
{
    private const string ReflectionTvpRequiresUnreferencedCodeMessage =
        "Reflection-based TVP row discovery inspects row metadata. Use LibDb.Tvp(..., shape) or options.Tvp.Map<T>().Column(...) for Native AOT.";

    private const string ReflectionTvpRequiresDynamicCodeMessage =
        "Reflection-based TVP row discovery compiles runtime accessors. Use static TVP shape mapping for Native AOT.";

    /// <summary>
    /// row sequence를 SQL Server table-valued parameter 값으로 감쌉니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="typeName">SQL Server TVP type name입니다.</param>
    /// <param name="rows">TVP로 전달할 row sequence입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>명시 TVP 바인딩 값입니다.</returns>
    [RequiresUnreferencedCode(ReflectionTvpRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionTvpRequiresDynamicCodeMessage)]
    public static LibDbTvpValue Tvp<T>(
        string typeName,
        IEnumerable<T> rows,
        TvpBindingPolicy policy = TvpBindingPolicy.Strict)
        => LibDbTvpValue.Create(typeName, rows, policy);

    /// <summary>
    /// row sequence와 정적 row shape를 SQL Server table-valued parameter 값으로 감쌉니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="typeName">SQL Server TVP type name입니다.</param>
    /// <param name="rows">TVP로 전달할 row sequence입니다.</param>
    /// <param name="shape">AOT 친화 정적 row shape입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>명시 TVP 바인딩 값입니다.</returns>
    public static LibDbTvpValue Tvp<T>(
        string typeName,
        IEnumerable<T> rows,
        TvpShape<T> shape,
        TvpBindingPolicy policy = TvpBindingPolicy.Strict)
        => LibDbTvpValue.Create(typeName, rows, shape, policy);

    /// <summary>
    /// DB 스키마 descriptor와 row sequence를 SQL Server table-valued parameter 값으로 감쌉니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="descriptor">DB에서 조회한 TVP 스키마 descriptor입니다.</param>
    /// <param name="rows">TVP로 전달할 row sequence입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>명시 TVP 바인딩 값입니다.</returns>
    [RequiresUnreferencedCode(ReflectionTvpRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode(ReflectionTvpRequiresDynamicCodeMessage)]
    public static LibDbTvpValue Tvp<T>(
        TvpSchemaDescriptor descriptor,
        IEnumerable<T> rows,
        TvpBindingPolicy policy = TvpBindingPolicy.Adaptive)
        => LibDbTvpValue.Create(descriptor, rows, policy);
}
