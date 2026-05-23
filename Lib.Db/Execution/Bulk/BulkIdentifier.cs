using System.Diagnostics.CodeAnalysis;

namespace Lib.Db.Execution.Bulk;

internal readonly record struct BulkIdentifier(string Schema, string Name)
{
    private const int MaxSqlIdentifierLength = 128;

    public static BulkIdentifier ParseTableName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Destination table name cannot be empty.", nameof(input));

        if (!string.Equals(input, input.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Destination table name cannot contain leading or trailing whitespace.", nameof(input));

        if (TryParseBracketedTwoPartName(input, out BulkIdentifier bracketed))
            return bracketed;

        string[] parts = input.Split('.');
        if (parts.Length is < 1 or > 2)
            throw new ArgumentException("Destination table name must be one or two parts.", nameof(input));

        if (parts.Any(static part => !string.Equals(part, part.Trim(), StringComparison.Ordinal)))
            throw new ArgumentException("Destination table name cannot contain whitespace around separators.", nameof(input));

        return parts.Length == 1
            ? new BulkIdentifier("dbo", NormalizePart(parts[0], nameof(input)))
            : new BulkIdentifier(NormalizePart(parts[0], nameof(input)), NormalizePart(parts[1], nameof(input)));
    }

    public string ToSql() => $"{Quote(Schema)}.{Quote(Name)}";

    internal static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string NormalizePart(string part, string paramName)
    {
        if (string.IsNullOrWhiteSpace(part))
            throw new ArgumentException("Destination table name contains an empty identifier part.", paramName);

        if (part.Length > MaxSqlIdentifierLength)
            throw new ArgumentException("Destination identifier parts cannot exceed 128 characters.", paramName);

        RejectSqlSyntax(part, paramName);

        if (part.Any(char.IsWhiteSpace))
            throw new ArgumentException("Destination table name contains unsupported SQL identifier whitespace.", paramName);

        if (!IsNarrowIdentifier(part))
            throw new ArgumentException("Destination table name contains malformed identifier characters.", paramName);

        return part;
    }

    private static void RejectSqlSyntax(string value, string paramName)
    {
        if (value.Contains(';', StringComparison.Ordinal)
            || value.Contains("--", StringComparison.Ordinal)
            || value.Contains("/*", StringComparison.Ordinal)
            || value.Contains("*/", StringComparison.Ordinal)
            || value.Contains('[', StringComparison.Ordinal)
            || value.Contains(']', StringComparison.Ordinal))
        {
            throw new ArgumentException("Destination table name contains unsupported SQL identifier syntax.", paramName);
        }
    }

    private static bool TryParseBracketedTwoPartName(string input, out BulkIdentifier identifier)
    {
        identifier = default;

        if (!input.Contains('[', StringComparison.Ordinal)
            && !input.Contains(']', StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = input.Split('.');
        if (parts.Length != 2)
            return false;

        if (!TryUnwrapBracketedPart(parts[0], out string? schema)
            || !TryUnwrapBracketedPart(parts[1], out string? name))
        {
            return false;
        }

        identifier = new BulkIdentifier(
            NormalizePart(schema, nameof(input)),
            NormalizePart(name, nameof(input)));
        return true;
    }

    private static bool TryUnwrapBracketedPart(string part, [NotNullWhen(true)] out string? value)
    {
        value = null;

        if (part.Length < 3
            || part[0] != '['
            || part[^1] != ']'
            || part.IndexOf('[', 1) >= 0
            || part.IndexOf(']', 1, part.Length - 2) >= 0)
        {
            return false;
        }

        value = part[1..^1];
        return true;
    }

    private static bool IsNarrowIdentifier(string value)
    {
        char first = value[0];
        if (!IsIdentifierStart(first))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            if (!IsIdentifierPart(value[i]))
                return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char value)
        => char.IsAsciiLetter(value) || value is '_' or '@' or '#';

    private static bool IsIdentifierPart(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '_' or '@' or '#' or '$';
}
