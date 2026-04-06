// ============================================================================
// 파일: Lib.Db.TvpGen/SharedHashUtils.cs
// 설명: TvpAccessorGenerator와 ResultAccessorGenerator가 공유하는
//       FNV-1a 해시 및 식별자 정리 유틸리티 (Generator-Side 전용)
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Text;

namespace Lib.Db.TvpGen;

#region [공유 해시 유틸리티] Generator-Side FNV-1a + 식별자 정리

/// <summary>
/// Source Generator 내부에서 사용하는 공유 유틸리티입니다.
/// <para>
/// <b>[설계 의도]</b>
/// TvpAccessorGenerator와 ResultAccessorGenerator에 중복되어 있던
/// FNV-1a 해시 함수 및 식별자 정리 함수를 단일 진실 원천으로 통합합니다.
/// </para>
/// <para>
/// ⚠️ 이 클래스는 <b>Generator-Side(컴파일 타임)</b> 전용입니다.
/// 생성 코드에 임베딩되는 런타임 FNV-1a 해시(생성 출력 내부의 __HashName)와는
/// 별개이며, 해당 런타임 코드는 각 Generator가 그대로 출력합니다.
/// </para>
/// </summary>
internal static class SharedHashUtils
{
    #region FNV-1a 해시 (ASCII IgnoreCase, Generator-Side)

    /// <summary>
    /// ASCII IgnoreCase FNV-1a 해시를 계산합니다.
    /// <para>해시 경로의 switch-case 키로 사용합니다(결정론적).</para>
    /// <para>
    /// <b>[설계 의도]</b>
    /// Generator가 생성 코드에 case 레이블(0xXXXXXXXX)을 기록할 때
    /// 컴파일 타임에 해시값을 미리 산출하는 용도입니다.
    /// </para>
    /// </summary>
    /// <param name="s">해시할 문자열 (속성/멤버 이름)</param>
    /// <returns>FNV-1a 해시값 (uint)</returns>
    public static uint HashAsciiIgnoreCaseFnv1a(string s)
    {
        unchecked
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint h = offset;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((uint)(c - 'A') <= 25u) c = (char)(c | 0x20);
                h ^= c;
                h *= prime;
            }

            return h;
        }
    }

    #endregion

    #region 식별자 정리 (SanitizeIdentifier)

    /// <summary>
    /// 멤버 이름을 C# 식별자로 안전하게 변환합니다.
    /// <para>
    /// <b>[설계 의도]</b>
    /// DB 컬럼명 등에 포함될 수 있는 특수 문자를 밑줄(_)로 치환하여
    /// 생성 코드에서 유효한 식별자로 사용할 수 있도록 합니다.
    /// </para>
    /// </summary>
    /// <param name="s">원본 이름</param>
    /// <returns>C# 식별자로 안전한 문자열</returns>
    public static string SanitizeIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "_";

        StringBuilder sb = new StringBuilder(s.Length + 1);

        char first = s[0];
        if (!(char.IsLetter(first) || first == '_'))
            sb.Append('_');

        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            else sb.Append('_');
        }

        return sb.ToString();
    }

    #endregion
}

#endregion
