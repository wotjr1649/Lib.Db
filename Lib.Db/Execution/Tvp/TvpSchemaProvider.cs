// ============================================================================
// 파일: Execution/Tvp/TvpSchemaProvider.cs
// 설명: ISchemaService 기반 TVP 런타임 스키마 descriptor provider
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// TVP 타입 이름과 DB 인스턴스 식별자를 기준으로 런타임 TVP 스키마 descriptor를 조회합니다.
/// </summary>
public interface ITvpSchemaProvider
{
    /// <summary>
    /// TVP 런타임 스키마 descriptor를 조회합니다.
    /// </summary>
    Task<TvpSchemaDescriptor> GetAsync(
        TvpTypeName typeName,
        string instanceHash,
        CancellationToken ct = default);

    /// <summary>
    /// TVP 런타임 스키마 descriptor를 조회합니다.
    /// </summary>
    Task<TvpSchemaDescriptor> GetSchemaAsync(
        TvpTypeName typeName,
        string instanceHash,
        CancellationToken ct = default)
        => GetAsync(typeName, instanceHash, ct);
}

/// <summary>
/// TVP 런타임 바인딩에 필요한 DB 스키마 descriptor입니다.
/// </summary>
/// <param name="TypeName">TVP SQL 타입 이름입니다.</param>
/// <param name="VersionToken">DB 스키마 버전 토큰입니다.</param>
/// <param name="Columns">DB TVP 컬럼 메타데이터입니다.</param>
/// <param name="Fingerprint">스키마 지문입니다.</param>
public sealed record TvpSchemaDescriptor(
    TvpTypeName TypeName,
    long VersionToken,
    IReadOnlyList<TvpColumnMetadata> Columns,
    string Fingerprint);

internal sealed class TvpSchemaProvider(ISchemaService schemaService) : ITvpSchemaProvider
{
    public async Task<TvpSchemaDescriptor> GetAsync(
        TvpTypeName typeName,
        string instanceHash,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceHash);

        TvpSchema schema = await schemaService
            .GetTvpSchemaAsync(typeName.FullName, instanceHash, ct)
            .ConfigureAwait(false);

        TvpColumnMetadata[] columns = schema.Columns
            .OrderBy(static column => column.Ordinal)
            .ThenBy(static column => column.Name, StringComparer.Ordinal)
            .ToArray();
        string fingerprint = TvpSchemaFingerprint.Compute(typeName, schema.VersionToken, columns);

        return new TvpSchemaDescriptor(typeName, schema.VersionToken, columns, fingerprint);
    }
}
