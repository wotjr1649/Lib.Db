// ============================================================================
// 파일: Execution/Tvp/TvpOptions.cs
// 설명: 런타임 TVP 옵션
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// 런타임 TVP 바인딩 옵션입니다.
/// </summary>
public sealed class TvpOptions
{
    /// <summary>
    /// 등록된 CLR row type의 <see cref="IEnumerable{T}"/> 값을 자동 TVP로 바인딩할지 여부입니다.
    /// </summary>
    public bool EnableAutoTvpBinding { get; set; } = true;

    /// <summary>
    /// CLR row type과 SQL Server TVP type name 매핑 레지스트리입니다.
    /// </summary>
    public TvpMappingRegistry Registry { get; } = new();

    /// <summary>
    /// CLR row type과 SQL Server TVP type name을 매핑합니다.
    /// </summary>
    /// <typeparam name="T">TVP row CLR 타입입니다.</typeparam>
    /// <param name="typeName">SQL Server TVP type name입니다.</param>
    /// <param name="policy">schema drift 처리 정책입니다.</param>
    /// <returns>등록된 row type의 정적 컬럼 shape builder입니다.</returns>
    public TvpMappingBuilder<T> Map<T>(
        string typeName,
        TvpBindingPolicy policy = TvpBindingPolicy.Strict)
        => Registry.Map<T>(typeName, policy);
}
