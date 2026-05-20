// ============================================================================
// 파일: Lib.Db/Diagnostics/DbDiagnosticRedactor.cs
// 설명: 진단/로그/메트릭 경계에서 민감한 식별자를 치환하는 내부 유틸리티
// ============================================================================

#nullable enable

using Microsoft.Data.SqlClient;

namespace Lib.Db.Diagnostics;

internal static class DbDiagnosticRedactor
{
    public const string RedactedRawInstance = "Raw:[redacted]";
    public const string RedactedConnectionStringInstance = "ConnectionString:[redacted]";

    public static string? RedactInstanceId(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return instanceId;

        if (instanceId.StartsWith("Raw:", StringComparison.OrdinalIgnoreCase))
            return RedactedRawInstance;

        return LooksLikeConnectionString(instanceId)
            ? RedactedConnectionStringInstance
            : instanceId;
    }

    public static bool IsSensitiveInstanceId(string? instanceId)
        => !string.IsNullOrWhiteSpace(instanceId) &&
           (instanceId.StartsWith("Raw:", StringComparison.OrdinalIgnoreCase) ||
            LooksLikeConnectionString(instanceId));

    public static bool LooksLikeConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.Contains('=', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            SqlConnectionStringBuilder builder = new(value);
            return builder.Count > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
