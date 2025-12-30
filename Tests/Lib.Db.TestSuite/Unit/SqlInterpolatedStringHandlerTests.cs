using System;
using System.Collections.Generic;
using FluentAssertions;
using Lib.Db.Fluent;
using Xunit;

namespace Lib.Db.TestSuite.Unit;

public class SqlInterpolatedStringHandlerTests
{
    [Fact]
    public void Should_Generate_Correct_Sql_And_Parameters()
    {
        // Arrange
        int userId = 123;
        string userName = "TestUser";
        bool isActive = true;

        bool isValid;
        var handler = new SqlInterpolatedStringHandler(50, 3, out isValid);
        
        // Act
        handler.AppendLiteral("SELECT * FROM Users WHERE Id = ");
        handler.AppendFormatted(userId);
        handler.AppendLiteral(" AND Name = ");
        handler.AppendFormatted(userName);
        handler.AppendLiteral(" AND IsActive = ");
        handler.AppendFormatted(isActive);

        var (sql, parameters) = handler.GetResult();
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

        bool isValid;
        var handler = new SqlInterpolatedStringHandler(20, 2, out isValid);

        // Act
        handler.AppendLiteral("SELECT TOP ");
        handler.AppendFormatted(limit);
        handler.AppendLiteral(" * FROM ");
        handler.AppendLiteral(table); // Literals are appended directly

        var (sql, parameters) = handler.GetResult();
        handler.Dispose();

        // Assert
        // Note: 'table' variable is appended as literal here because we called AppendLiteral explicitly.
        // In real usage with interpolated string $"... {table}", it would be AppendFormatted unless it's a constant string.
        // Wait, if we use $"... {table}", the compiler calls AppendFormatted<string>(table).
        // If we want it as table name, we shouldn't use this handler for dynamic table names unless we treat them as parameters (which is invalid SQL for table names).
        // Use case check: The handler is for *parameters*. 
        
        sql.Should().Be("SELECT TOP @p0 * FROM Users");
        parameters.Should().HaveCount(1);
        parameters["@p0"].Should().Be(limit);
    }
}
