// ============================================================================
// 파일: Execution/Tvp/TvpSchemaFingerprint.cs
// 설명: TVP 런타임 스키마 descriptor 지문 생성
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lib.Db.Contracts.Models;

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// TVP 스키마 descriptor의 결정적 지문을 생성합니다.
/// </summary>
public static class TvpSchemaFingerprint
{
    /// <summary>
    /// TVP 전체 이름, 버전 토큰, 컬럼 수, 컬럼 메타데이터를 기반으로 SHA-256 지문을 생성합니다.
    /// </summary>
    public static string Compute(TvpSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return Compute(TvpTypeName.Parse(schema.Name), schema.VersionToken, schema.Columns);
    }

    /// <summary>
    /// TVP 전체 이름, 버전 토큰, 컬럼 수, 컬럼 메타데이터를 기반으로 SHA-256 지문을 생성합니다.
    /// </summary>
    public static string Compute(
        TvpTypeName typeName,
        long versionToken,
        IEnumerable<TvpColumnMetadata> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        TvpColumnMetadata[] orderedColumns = columns
            .OrderBy(static column => column.Ordinal)
            .ThenBy(static column => column.Name, StringComparer.Ordinal)
            .ToArray();

        StringBuilder payload = new(orderedColumns.Length * 96);
        AppendPart(payload, "tvp-schema-v1");
        AppendPart(payload, typeName.FullName);
        AppendPart(payload, versionToken.ToString(CultureInfo.InvariantCulture));
        AppendPart(payload, orderedColumns.Length.ToString(CultureInfo.InvariantCulture));

        foreach (TvpColumnMetadata column in orderedColumns)
        {
            AppendPart(payload, column.Name);
            AppendPart(payload, column.Ordinal.ToString(CultureInfo.InvariantCulture));
            AppendPart(payload, ((int)column.SqlDbType).ToString(CultureInfo.InvariantCulture));
            AppendPart(payload, column.MaxLength.ToString(CultureInfo.InvariantCulture));
            AppendPart(payload, column.Precision.ToString(CultureInfo.InvariantCulture));
            AppendPart(payload, column.Scale.ToString(CultureInfo.InvariantCulture));
            AppendPart(payload, column.IsNullable ? "1" : "0");
            AppendPart(payload, column.IsIdentity ? "1" : "0");
            AppendPart(payload, column.IsComputed ? "1" : "0");
        }

        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString().Normalize(NormalizationForm.FormC));
        byte[] hash = SHA256.HashData(bytes);

        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AppendPart(StringBuilder builder, string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormC);
        builder
            .Append(normalized.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(normalized)
            .Append('|');
    }
}
