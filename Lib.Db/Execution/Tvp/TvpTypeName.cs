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

        string[] parts = SplitNameParts(value.Trim(), nameof(value));

        string schema;
        string name;

        if (parts.Length == 1)
        {
            schema = "dbo";
            name = parts[0];
        }
        else
        {
            schema = parts[0];
            name = parts[1];
        }

        if (!IsSafeIdentifier(schema) || !IsSafeIdentifier(name))
            throw new ArgumentException("Invalid TVP type name.", nameof(value));

        return new TvpTypeName(schema, name);
    }

    private static string[] SplitNameParts(string value, string paramName)
    {
        List<string> parts = new(2);
        int index = 0;

        while (index < value.Length)
        {
            SkipWhitespace(value, ref index);
            if (parts.Count == 2)
                throw new ArgumentException("Invalid TVP type name.", paramName);

            parts.Add(ReadNamePart(value, ref index, paramName));
            SkipWhitespace(value, ref index);

            if (index >= value.Length)
                break;

            index++;
            if (index >= value.Length)
                throw new ArgumentException("Invalid TVP type name.", paramName);
        }

        return parts.ToArray();
    }

    private static string ReadNamePart(string value, ref int index, string paramName)
    {
        if (value[index] == '[')
            return ReadBracketedNamePart(value, ref index, paramName);

        int start = index;
        while (index < value.Length && value[index] != '.')
            index++;

        string part = value[start..index].Trim();
        if (part.Length == 0)
            throw new ArgumentException("Invalid TVP type name.", paramName);

        return part;
    }

    private static string ReadBracketedNamePart(string value, ref int index, string paramName)
    {
        int start = ++index;
        int end = value.IndexOf(']', start);
        if (end < 0 || end == start)
            throw new ArgumentException("Invalid TVP type name.", paramName);

        string part = value[start..end];
        index = end + 1;
        SkipWhitespace(value, ref index);

        if (index < value.Length && value[index] != '.')
            throw new ArgumentException("Invalid TVP type name.", paramName);

        return part;
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
            index++;
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
