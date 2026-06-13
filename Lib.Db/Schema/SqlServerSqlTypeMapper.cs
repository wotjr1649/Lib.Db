// ============================================================================
// 파일명: Lib.Db/Schema/SqlServerSqlTypeMapper.cs
// 역할  : SQL Server 메타데이터 타입명을 SqlDbType으로 변환하고 비지원 타입을 차단
// ============================================================================

#nullable enable

using System.Data;

namespace Lib.Db.Schema;

internal static class SqlServerSqlTypeMapper
{
    public static SqlDbType MapToSqlDbType(string typeName)
        => MapToSqlDbType(typeName, isCursorRef: false);

    public static SqlDbType MapToSqlDbType(string typeName, bool isCursorRef)
    {
        string normalized = typeName.Trim();

        if (isCursorRef || IsCursorTypeName(normalized))
            return SqlDbType.Variant;

        return Enum.TryParse<SqlDbType>(normalized, ignoreCase: true, out SqlDbType parsed)
            ? parsed
            : normalized.ToLowerInvariant() switch
            {
                "numeric" => SqlDbType.Decimal,
                "rowversion" => SqlDbType.Timestamp,
                "sysname" => SqlDbType.NVarChar,
                _ => SqlDbType.Variant
            };
    }

    public static bool IsCursorTypeName(string typeName)
        => typeName.Trim().Equals("cursor", StringComparison.OrdinalIgnoreCase);
}
