// ============================================================================
// 파일: Lib.Db.TvpGen/GeneratorSharedHelpers.cs
// 설명: TvpAccessorGenerator와 ResultAccessorGenerator 공유 헬퍼
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Microsoft.CodeAnalysis;

namespace Lib.Db.TvpGen;

/// <summary>
/// Source Generator 간 공유 유틸리티 메서드입니다.
/// <para><b>[설계 의도]</b> 두 Generator에 중복되어 있던 코드를 단일 진실 원천으로 통합합니다.</para>
/// </summary>
internal static class GeneratorSharedHelpers
{
    #region 상수

    /// <summary>Track 4/5 하이브리드 라우팅 임계값 (이 값 이하: else-if / 초과: FNV-1a + switch)</summary>
    internal const int SmallMemberThreshold = 12;

    #endregion

    #region 접근성 검사

    /// <summary>
    /// 타입이 생성 코드에서 접근 가능한지 확인합니다.
    /// <para>중첩 타입의 ContainingType 체인까지 검사합니다.</para>
    /// </summary>
    internal static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
    {
        INamedTypeSymbol? cur = type;
        while (cur is not null)
        {
            if (cur.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected)
                return false;
            cur = cur.ContainingType;
        }
        return true;
    }

    #endregion

    #region 힌트명 생성

    /// <summary>
    /// 소스 제너레이터 AddSource에 사용될 안전한 파일 힌트명을 생성합니다.
    /// <para>완전 수식 이름 기반으로 생성하며, <see cref="SharedHashUtils.SanitizeIdentifier"/>로 파일명에 부적합한 문자를 치환합니다.</para>
    /// </summary>
    internal static string BuildSafeHintName(INamedTypeSymbol type, string suffix)
    {
        string fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
        return SharedHashUtils.SanitizeIdentifier(fqn) + suffix;
    }

    #endregion
}
