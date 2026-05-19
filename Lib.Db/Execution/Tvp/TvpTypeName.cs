// ============================================================================
// 파일: Execution/Tvp/TvpTypeName.cs
// 설명: SQL Server TVP 타입명 검증 및 정규화
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.Execution.Tvp;

/// <summary>
/// SQL Server TVP 타입명을 안전하게 검증하고 two-part name으로 정규화합니다.
/// </summary>
public readonly record struct TvpTypeName
{
    /// <summary>
    /// 검증된 TVP type name을 생성합니다.
    /// </summary>
    public TvpTypeName(string schema, string name)
    {
        if (!IsSafeIdentifier(schema) || !IsSafeIdentifier(name))
            throw new ArgumentException("Invalid TVP type name.");

        Schema = schema;
        Name = name;
    }

    /// <summary>
    /// SQL Server schema 이름입니다.
    /// </summary>
    public string Schema { get; }

    /// <summary>
    /// SQL Server TVP type 이름입니다.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// SQL Server schema-qualified TVP type name입니다.
    /// </summary>
    public string FullName => $"{Schema}.{Name}";

    /// <summary>
    /// 입력 문자열을 검증하고 schema-qualified TVP type name으로 변환합니다.
    /// </summary>
    /// <param name="value">one-part 또는 two-part SQL Server type name입니다.</param>
    /// <returns>검증된 TVP type name입니다.</returns>
    /// <exception cref="ArgumentException">TVP type name이 안전한 식별자 형태가 아니면 발생합니다.</exception>
    public static TvpTypeName Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Invalid TVP type name.", nameof(value));

        string normalized = value.Trim()
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        string[] parts = normalized.Split(
            '.',
            StringSplitOptions.TrimEntries);

        if (parts.Any(static part => part.Length == 0))
            throw new ArgumentException("Invalid TVP type name.", nameof(value));

        string schema;
        string name;

        if (parts.Length == 1)
        {
            schema = "dbo";
            name = parts[0];
        }
        else if (parts.Length == 2)
        {
            schema = parts[0];
            name = parts[1];
        }
        else
        {
            throw new ArgumentException("Invalid TVP type name.", nameof(value));
        }

        if (!IsSafeIdentifier(schema) || !IsSafeIdentifier(name))
            throw new ArgumentException("Invalid TVP type name.", nameof(value));

        return new TvpTypeName(schema, name);
    }

    internal static bool IsSafeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128)
            return false;

        char first = value[0];
        if (!(char.IsLetter(first) || first == '_'))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            char ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                return false;
        }

        return true;
    }
}
