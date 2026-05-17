// ============================================================================
// 파일: Lib.Db/Diagnostics/DbDiagnosticRedactor.cs
// 설명: 진단/로그/메트릭 경계에서 민감한 식별자를 치환하는 내부 유틸리티
// ============================================================================

#nullable enable

namespace Lib.Db.Diagnostics;

internal static class DbDiagnosticRedactor
{
    public const string RedactedRawInstance = "Raw:[redacted]";

    public static string? RedactInstanceId(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return instanceId;

        return instanceId.StartsWith("Raw:", StringComparison.Ordinal)
            ? RedactedRawInstance
            : instanceId;
    }
}
