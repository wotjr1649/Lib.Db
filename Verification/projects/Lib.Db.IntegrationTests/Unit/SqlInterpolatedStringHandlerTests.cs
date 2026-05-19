// ============================================================================
// 파일: Unit/SqlInterpolatedStringHandlerTests.cs
// 설명: SqlInterpolatedStringHandler 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using Lib.Db.Fluent;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SqlInterpolatedStringHandlerTests
{
    [Fact]
    public void Should_Generate_Correct_Sql_And_Parameters()
    {
        // Arrange
        int userId = 123;
        string userName = "TestUser";
        bool isActive = true;

        SqlInterpolatedStringHandler handler = new(50, 3, out bool isValid);

        // Act
        handler.AppendLiteral("SELECT * FROM Users WHERE Id = ");
        handler.AppendFormatted(userId);
        handler.AppendLiteral(" AND Name = ");
        handler.AppendFormatted(userName);
        handler.AppendLiteral(" AND IsActive = ");
        handler.AppendFormatted(isActive);

        (string sql, Dictionary<string, object?> parameters) = handler.GetResult();
        handler.Dispose();

        // Assert
        sql.Should().Be("SELECT * FROM Users WHERE Id = @p0 AND Name = @p1 AND IsActive = @p2");
        parameters.Should().HaveCount(3);
        parameters["@p0"].Should().Be(userId);
        parameters["@p1"].Should().Be(userName);
        parameters["@p2"].Should().Be(isActive);
    }

    [Fact]
    public void Should_Handle_Mixed_Literals_And_Parameters()
    {
        // Arrange
        string table = "Users";
        int limit = 10;

        SqlInterpolatedStringHandler handler = new(20, 2, out bool isValid);

        // Act
        handler.AppendLiteral("SELECT TOP ");
        handler.AppendFormatted(limit);
        handler.AppendLiteral(" * FROM ");
        handler.AppendLiteral(table);

        (string sql, Dictionary<string, object?> parameters) = handler.GetResult();
        handler.Dispose();

        // Assert
        sql.Should().Be("SELECT TOP @p0 * FROM Users");
        parameters.Should().HaveCount(1);
        parameters["@p0"].Should().Be(limit);
    }
}
