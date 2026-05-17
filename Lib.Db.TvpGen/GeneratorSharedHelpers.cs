// ============================================================================
// 파일: Lib.Db.TvpGen/GeneratorSharedHelpers.cs
// 설명: TvpAccessorGenerator와 ResultAccessorGenerator 공유 헬퍼
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    /// <summary>
    /// 타입 또는 상위 타입 선언에 <c>file</c> modifier가 포함되어 생성 파일에서 접근할 수 없는지 확인합니다.
    /// </summary>
    internal static bool IsFileLocalType(
        INamedTypeSymbol type,
        System.Threading.CancellationToken cancellationToken)
    {
        INamedTypeSymbol? current = type;
        while (current is not null)
        {
            if (HasFileModifier(current, cancellationToken))
            {
                return true;
            }

            current = current.ContainingType;
        }

        return false;
    }

    private static bool HasFileModifier(
        INamedTypeSymbol type,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (SyntaxReference syntaxReference in type.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (syntaxReference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration)
                continue;

            foreach (SyntaxToken modifier in declaration.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.FileKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region 힌트명 생성

    /// <summary>
    /// 소스 제너레이터 AddSource에 사용될 안전한 파일 힌트명을 생성합니다.
    /// <para>완전 수식 이름 기반으로 생성하며, sanitize 후 원본 이름의 ordinal hash를 붙여 충돌을 방지합니다.</para>
    /// </summary>
    internal static string BuildSafeHintName(INamedTypeSymbol type, string suffix)
        => BuildSafeTypeSuffix(type) + suffix;

    /// <summary>
    /// 생성 타입명 suffix에 사용할 안전하고 결정론적인 식별자를 생성합니다.
    /// </summary>
    internal static string BuildSafeTypeSuffix(INamedTypeSymbol type)
    {
        string fqn = GetFullyQualifiedName(type);
        string hash = SharedHashUtils.HashOrdinalFnv1a(fqn).ToString("x8");
        return SharedHashUtils.SanitizeIdentifier(fqn) + "_" + hash;
    }

    private static string GetFullyQualifiedName(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");

    #endregion
}
