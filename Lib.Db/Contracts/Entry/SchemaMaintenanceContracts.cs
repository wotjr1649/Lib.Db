// ============================================================================
// 파일명 : Lib.Db/Contracts/Entry/SchemaMaintenanceContracts.cs
// 설명   : fluent 스키마 유지보수 진입점 계약
// 대상   : .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Execution.Tvp;

namespace Lib.Db.Contracts.Entry;

/// <summary>
/// 런타임 스키마 조회와 캐시 플러시를 fluent API에서 수행하는 유지보수 단계입니다.
/// </summary>
public interface ISchemaMaintenanceStage
{
    /// <summary>
    /// 지정한 TVP 타입의 런타임 descriptor를 조회합니다.
    /// </summary>
    /// <param name="tvpName">one-part 또는 two-part TVP 타입명입니다.</param>
    /// <param name="ct">취소 토큰입니다.</param>
    /// <returns>TVP 런타임 descriptor입니다.</returns>
    Task<TvpSchemaDescriptor> GetTvpAsync(string tvpName, CancellationToken ct = default);

    /// <summary>
    /// 지정한 TVP 타입의 스키마 캐시만 플러시합니다.
    /// </summary>
    /// <param name="tvpName">one-part 또는 two-part TVP 타입명입니다.</param>
    /// <param name="ct">취소 토큰입니다.</param>
    Task FlushTvpAsync(string tvpName, CancellationToken ct = default);

    /// <summary>
    /// 현재 인스턴스의 전체 스키마 캐시를 플러시합니다.
    /// </summary>
    /// <param name="ct">취소 토큰입니다.</param>
    Task FlushSchemaAsync(CancellationToken ct = default);
}
