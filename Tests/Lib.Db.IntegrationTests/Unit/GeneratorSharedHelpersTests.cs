// ============================================================================
// 파일: Unit/GeneratorSharedHelpersTests.cs
// 설명: Source Generator 공유 헬퍼 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.TvpGen;
using Lib.Db.Schema;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class GeneratorSharedHelpersTests
{
    [Theory]
    [InlineData("class", "@class")]
    [InlineData("record", "@record")]
    [InlineData("order-id", "order_id")]
    [InlineData("123name", "_123name")]
    public void EscapeIdentifier_ShouldReturnValidCSharpIdentifier(string input, string expected)
    {
        SharedHashUtils.EscapeIdentifier(input).Should().Be(expected);
    }

    [Fact]
    public void EscapeStringLiteral_ShouldEscapeQuotesAndBackslashes()
    {
        SharedHashUtils.EscapeStringLiteral("dbo.Type\"Name\\Suffix")
            .Should()
            .Be("dbo.Type\\\"Name\\\\Suffix");
    }

    [Fact]
    public void EscapeStringLiteral_ShouldEscapeUnicodeLineSeparators()
    {
        SharedHashUtils.EscapeStringLiteral("Line\u2028Next\u2029End\u0085")
            .Should()
            .Be("Line\\u2028Next\\u2029End\\u0085");
    }

    [Fact]
    public void EscapeXmlText_ShouldEscapeXmlSpecialCharacters()
    {
        SharedHashUtils.EscapeXmlText("A < B & C > D")
            .Should()
            .Be("A &lt; B &amp; C &gt; D");
    }

    [Fact]
    public void EscapeXmlText_ShouldEscapeLineBreaksAndInvalidXmlCharacters()
    {
        SharedHashUtils.EscapeXmlText("A\npublic int Injected\rB\u2028C\u2029D\u0085E\u0001")
            .Should()
            .Be("A&#xA;public int Injected&#xD;B&#x2028;C&#x2029;D&#x85;E&#xFFFD;");
    }

    [Fact]
    public void HashOrdinalFnv1a_ShouldDistinguishSanitizedNameCollisions()
    {
        SharedHashUtils.HashOrdinalFnv1a("A.B_C.Row")
            .Should()
            .NotBe(SharedHashUtils.HashOrdinalFnv1a("A_B.C.Row"));
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("customerCode")]
    [InlineData("ORDER_ID")]
    public void HashAsciiIgnoreCaseFnv1a_ShouldMatchRuntimeTvpNameHash(string columnName)
    {
        unchecked((int)SharedHashUtils.HashAsciiIgnoreCaseFnv1a(columnName))
            .Should()
            .Be(TvpNameHash.Compute(columnName));
    }
}
