// ============================================================================
// 파일: Lib.Db/Diagnostics/DbCommandTextPolicy.cs
// 설명: SQL 명령 텍스트의 로그/Telemetry 노출 정책
// ============================================================================

#nullable enable

using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lib.Db.Configuration;

namespace Lib.Db.Diagnostics;

internal readonly record struct DbCommandTextDiagnostic(
    string Summary,
    string Hash,
    string? SensitiveText);

internal static class DbCommandTextPolicy
{
    private const int MaxSummaryLength = 128;

    internal static DbCommandTextDiagnostic CreateDiagnostic(
        string? commandText,
        CommandType commandType,
        LibDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string normalized = commandText ?? string.Empty;
        string? sensitiveText = options.EnableSensitiveCommandTextLogging
            ? Truncate(normalized, options.MaxSensitiveCommandTextLength)
            : null;

        return new DbCommandTextDiagnostic(
            CreateSummary(normalized, commandType),
            CreateStableHash(normalized),
            sensitiveText);
    }

    internal static IReadOnlyList<KeyValuePair<string, object?>> BuildActivityTags(
        string? commandText,
        CommandType commandType,
        string? instanceHash,
        LibDbOptions options)
    {
        DbCommandTextDiagnostic diagnostic = CreateDiagnostic(commandText, commandType, options);
        List<KeyValuePair<string, object?>> tags =
        [
            new("db.system", "mssql"),
            new("db.operation", commandType.ToString()),
            new("db.command_type", commandType.ToString()),
            new("db.query.summary", diagnostic.Summary),
            new("libdb.command.hash", diagnostic.Hash)
        ];

        if (!string.IsNullOrWhiteSpace(instanceHash))
            tags.Add(new KeyValuePair<string, object?>("libdb.instance", instanceHash));

        if (diagnostic.SensitiveText is { Length: > 0 } sensitiveText)
        {
            tags.Add(new KeyValuePair<string, object?>("db.query.text", sensitiveText));
            tags.Add(new KeyValuePair<string, object?>("db.statement", sensitiveText));
        }

        return tags;
    }

    internal static void EnrichActivity(
        Activity? activity,
        string? commandText,
        CommandType commandType,
        string? instanceHash,
        LibDbOptions options)
    {
        if (activity is null)
            return;

        foreach (KeyValuePair<string, object?> tag in BuildActivityTags(commandText, commandType, instanceHash, options))
            activity.SetTag(tag.Key, tag.Value);
    }

    internal static string GetLogCommandText(string? commandText, CommandType commandType, LibDbOptions options)
    {
        DbCommandTextDiagnostic diagnostic = CreateDiagnostic(commandText, commandType, options);
        return diagnostic.SensitiveText ?? $"{diagnostic.Summary} #{diagnostic.Hash}";
    }

    internal static string CreateSummary(string? commandText, CommandType commandType)
    {
        string normalized = commandText?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return "Empty Command";

        return commandType switch
        {
            CommandType.StoredProcedure => CreateNamedCommandSummary("StoredProcedure", normalized),
            CommandType.TableDirect => CreateNamedCommandSummary("TableDirect", normalized),
            _ => "SQL Text"
        };
    }

    internal static string CreateStableHash(string? value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CreateNamedCommandSummary(string kind, string commandText)
    {
        if (!IsSafeIdentifierLikeText(commandText))
            return kind;

        return $"{kind} {Truncate(commandText, MaxSummaryLength)}";
    }

    private static bool IsSafeIdentifierLikeText(string value)
    {
        foreach (char ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '.' or '[' or ']' or '$' or '#')
                continue;

            return false;
        }

        return true;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }
}
