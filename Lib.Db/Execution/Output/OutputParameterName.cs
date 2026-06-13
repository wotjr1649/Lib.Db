#nullable enable

using System.Text;

namespace Lib.Db.Execution.Output;

/// <summary>
/// Output 파라미터 이름을 표시/비교에 안전한 형태로 정규화합니다.
/// </summary>
internal readonly record struct OutputParameterName(string Raw, string Canonical)
{
    public static OutputParameterName From(string raw)
    {
        string canonical = raw.TrimStart('@');
        if (string.IsNullOrWhiteSpace(canonical))
            throw new InvalidOperationException("Output parameter name is empty.");

        return new OutputParameterName(raw, canonical);
    }

    public bool Matches(string candidate)
        => string.Equals(Canonical, candidate.TrimStart('@'), StringComparison.OrdinalIgnoreCase);

    public string SafeDisplay()
    {
        ReadOnlySpan<char> source = Canonical.AsSpan(0, Math.Min(Canonical.Length, 128));
        StringBuilder builder = new(source.Length);

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (char.IsControl(c) || IsBidiControl(c))
            {
                builder.Append("\\u");
                builder.Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static bool IsBidiControl(char c)
        => c is '\u061C' or '\u200E' or '\u200F'
            or >= '\u202A' and <= '\u202E'
            or >= '\u2066' and <= '\u2069';
}
