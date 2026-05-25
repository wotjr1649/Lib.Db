using System.Text.RegularExpressions;

namespace Lib.Db.Tools.Reporting;

public static partial class ContractOutputRedactor
{
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return SecretLikePattern().IsMatch(value) || LooksLikeConnectionString(value)
            ? "<redacted>"
            : value;
    }

    public static string EscapeMarkdown(string value)
    {
        string redacted = Redact(value);
        if (StringComparer.Ordinal.Equals(redacted, "<redacted>"))
            return redacted;

        return redacted
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("&lt;redacted&gt;", "<redacted>", StringComparison.Ordinal);
    }

    private static bool LooksLikeConnectionString(string value)
    {
        if (!value.Contains('=', StringComparison.Ordinal) ||
            !value.Contains(';', StringComparison.Ordinal))
        {
            return false;
        }

        bool hasEndpointKey = false;
        bool hasNonEndpointKey = false;
        int keyCount = 0;

        foreach (Match match in ConnectionStringKeyPattern().Matches(value))
        {
            keyCount++;
            string key = NormalizeKey(match.Groups["key"].Value);
            if (IsEndpointKey(key))
            {
                hasEndpointKey = true;
            }
            else
            {
                hasNonEndpointKey = true;
            }
        }

        return keyCount >= 2 && hasEndpointKey && hasNonEndpointKey;
    }

    private static string NormalizeKey(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static bool IsEndpointKey(string key) =>
        key is "server" or "datasource" or "address" or "addr" or "networkaddress";

    [GeneratedRegex("(?i)(password|pwd|token|secret|api[_-]?key|connection\\s*string|credential|authorization|sas[_-]?token)\\s*[:=]|(?i)(connectionstrings|connectionstring|access[_-]?token|refreshtoken|refresh[_-]?token|clientsecret|client[_-]?secret|secretkey|secret[_-]?key|apikey|api[_-]?key|sas[_-]?token|authorization|credential)")]
    private static partial Regex SecretLikePattern();

    [GeneratedRegex("(?i)(?<![a-z0-9])(?<key>server|data\\s+source|address|addr|network\\s+address|database|initial\\s+catalog|user\\s+id|uid|password|pwd|encrypt|trustservercertificate|application\\s+name|integrated\\s+security|authentication)\\s*=")]
    private static partial Regex ConnectionStringKeyPattern();
}
