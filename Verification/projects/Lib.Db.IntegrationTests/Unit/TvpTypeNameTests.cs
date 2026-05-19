// ============================================================================
// 파일: Unit/TvpTypeNameTests.cs
// 설명: 런타임 TVP 타입명 검증 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Execution.Tvp;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class TvpTypeNameTests
{
    [Theory]
    [InlineData("dbo.T_OrderItem", "dbo", "T_OrderItem")]
    [InlineData("[dbo].[T_OrderItem]", "dbo", "T_OrderItem")]
    [InlineData("T_OrderItem", "dbo", "T_OrderItem")]
    public void Parse_ShouldAcceptSafeTypeNames(string input, string expectedSchema, string expectedName)
    {
        TvpTypeName parsed = TvpTypeName.Parse(input);

        parsed.Schema.Should().Be(expectedSchema);
        parsed.Name.Should().Be(expectedName);
        parsed.FullName.Should().Be($"{expectedSchema}.{expectedName}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("dbo.")]
    [InlineData("dbo.T;DROP TABLE X")]
    [InlineData("dbo.T_OrderItem --")]
    [InlineData("db.other.dbo.T")]
    [InlineData("dbo.T Order")]
    [InlineData("dbo.#TempLikeType")]
    [InlineData("dbo.@VariableLikeType")]
    public void Parse_ShouldRejectUnsafeTypeNames(string input)
    {
        Action act = () => TvpTypeName.Parse(input);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*TVP type name*");
    }
}
