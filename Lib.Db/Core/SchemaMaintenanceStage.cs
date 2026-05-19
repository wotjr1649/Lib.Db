// ============================================================================
// 파일: Lib.Db/Core/SchemaMaintenanceStage.cs
// 설명: fluent 스키마 유지보수 단계 구현
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Schema;
using Lib.Db.Execution.Tvp;

namespace Lib.Db.Core;

/// <summary>
/// <see cref="IDbSession.Schema"/> 및 <see cref="IDbSession.UseSchema(string)"/>에서 반환되는
/// 스키마 유지보수 단계 구현입니다.
/// </summary>
internal sealed class SchemaMaintenanceStage(
    ITvpSchemaProvider? tvpSchemaProvider,
    ISchemaFlushCoordinator flushCoordinator,
    string instanceHash) : ISchemaMaintenanceStage
{
    /// <inheritdoc />
    public Task<TvpSchemaDescriptor> GetTvpAsync(string tvpName, CancellationToken ct = default)
    {
        TvpTypeName typeName = TvpTypeName.Parse(tvpName);
        ITvpSchemaProvider provider = tvpSchemaProvider
            ?? throw new InvalidOperationException(
                "TVP schema provider가 등록되지 않았습니다. Lib.Db.Execution.Tvp.ITvpSchemaProvider 서비스를 등록해야 합니다.");

        return provider.GetAsync(typeName, instanceHash, ct);
    }

    /// <inheritdoc />
    public Task FlushTvpAsync(string tvpName, CancellationToken ct = default)
    {
        TvpTypeName typeName = TvpTypeName.Parse(tvpName);
        return flushCoordinator.FlushTvpAsync(instanceHash, typeName.FullName, ct);
    }

    /// <inheritdoc />
    public Task FlushSchemaAsync(CancellationToken ct = default)
        => flushCoordinator.FlushAsync(instanceHash, ct);
}
