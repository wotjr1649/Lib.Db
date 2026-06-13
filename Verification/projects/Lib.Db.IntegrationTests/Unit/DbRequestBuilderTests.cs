// ============================================================================
// 파일: Unit/DbRequestBuilderTests.cs
// 설명: Fluent DbRequestBuilder 단위 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Entry;
using Lib.Db.Execution.Tvp;
using Lib.Db.Fluent;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class DbRequestBuilderTests
{
    [Fact]
    public async Task FormattableSql_ExecuteAsync_ShouldPreserveGeneratedParameters()
    {
        // Arrange
        int id = 42;
        Dictionary<string, object?>? capturedParameters = null;

        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.ExecuteNonQueryAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object?>, string, CommandType, DbExecutionOptions, CancellationToken>(
                (_, parameters, _, _, _, _) => capturedParameters = parameters)
            .ReturnsAsync(1);

        DbRequestBuilder builder = new(executor.Object, "verification");

        // Act
        DbResult<int> result = await builder
            .Sql((FormattableString)$"SELECT * FROM Users WHERE Id = {id}")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedParameters.Should().NotBeNull();
        capturedParameters!.Should().ContainKey("@p0");
        capturedParameters["@p0"].Should().Be(id);
    }

    [Fact]
    public async Task SqlInterpolated_ExecuteAsync_ShouldPreserveGeneratedParameters()
    {
        // Arrange
        int id = 42;
        Dictionary<string, object?>? capturedParameters = null;

        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.ExecuteNonQueryAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object?>, string, CommandType, DbExecutionOptions, CancellationToken>(
                (_, parameters, _, _, _, _) => capturedParameters = parameters)
            .ReturnsAsync(1);

        DbRequestBuilder builder = new(executor.Object, "verification");

        // Act
        DbResult<int> result = await builder
            .SqlInterpolated($"SELECT * FROM Users WHERE Id = {id}")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedParameters.Should().NotBeNull();
        capturedParameters!.Should().ContainKey("@p0");
        capturedParameters["@p0"].Should().Be(id);
    }

    [Fact]
    public async Task FormattableSql_WithAdditionalParameters_ShouldMergeGeneratedAndExplicitParameters()
    {
        // Arrange
        int id = 42;
        Dictionary<string, object?>? capturedParameters = null;

        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.ExecuteNonQueryAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object?>, string, CommandType, DbExecutionOptions, CancellationToken>(
                (_, parameters, _, _, _, _) => capturedParameters = parameters)
            .ReturnsAsync(1);

        DbRequestBuilder builder = new(executor.Object, "verification");

        // Act
        DbResult<int> result = await builder
            .Sql((FormattableString)$"SELECT * FROM Users WHERE Id = {id} AND Status = @Status")
            .With(new { Status = "A" })
            .ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedParameters.Should().NotBeNull();
        capturedParameters!.Should().ContainKey("@p0");
        capturedParameters["@p0"].Should().Be(id);
        capturedParameters.Should().ContainKey("Status");
        capturedParameters["Status"].Should().Be("A");
    }

    [Fact]
    public async Task Procedure_WithMixedScalarAndTvp_ShouldPreserveSingleFluentParameterObject()
    {
        Dictionary<string, object?>? capturedParameters = null;
        CommandType? capturedCommandType = null;
        var rows = new[] { new OrderItemRow(1, "A100", 2) };
        var parameters = new Dictionary<string, object?>
        {
            ["OrderId"] = 123,
            ["Rows"] = LibDb.Tvp("dbo.T_OrderItem", rows),
            ["RequestedBy"] = "system"
        };

        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.ExecuteNonQueryAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, object?>>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object?>, string, CommandType, DbExecutionOptions, CancellationToken>(
                (_, captured, _, commandType, _, _) =>
                {
                    capturedParameters = captured;
                    capturedCommandType = commandType;
                })
            .ReturnsAsync(1);

        DbRequestBuilder builder = new(executor.Object, "verification");

        DbResult<int> result = await builder
            .Procedure("dbo.SaveOrder")
            .With(parameters)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        capturedCommandType.Should().Be(CommandType.StoredProcedure);
        capturedParameters.Should().NotBeNull();
        capturedParameters!["OrderId"].Should().Be(123);
        capturedParameters["RequestedBy"].Should().Be("system");
        capturedParameters["Rows"].Should().BeOfType<LibDbTvpValue>();
    }

    [Theory]
    [InlineData("p0")]
    [InlineData("@p0")]
    [InlineData("P0")]
    public void FormattableSql_WithGeneratedParameterNameCollision_ShouldFailFast(string duplicateName)
    {
        // Arrange
        int id = 42;

        Mock<IDbExecutor> executor = new();
        DbRequestBuilder builder = new(executor.Object, "verification");

        IParameterStage stage = builder
            .Sql((FormattableString)$"SELECT * FROM Users WHERE Id = {id}");

        // Act
        Action act = () => stage.With(new Dictionary<string, object?> { [duplicateName] = 99 });

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*충돌*");
    }

    [Fact]
    public void FormattableSql_WithBlankParameterName_ShouldFailFast()
    {
        // Arrange
        int id = 42;

        Mock<IDbExecutor> executor = new();
        DbRequestBuilder builder = new(executor.Object, "verification");

        IParameterStage stage = builder
            .Sql((FormattableString)$"SELECT * FROM Users WHERE Id = {id}");

        // Act
        Action act = () => stage.With(new Dictionary<string, object?> { [" "] = 99 });

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*비어 있을 수 없습니다*");
    }

    [Theory]
    [InlineData(42)]
    [InlineData("active")]
    [InlineData(true)]
    public void FormattableSql_WithScalarAdditionalParameters_ShouldFailFast(object scalarParameters)
    {
        // Arrange
        int id = 42;

        Mock<IDbExecutor> executor = new();
        DbRequestBuilder builder = new(executor.Object, "verification");

        IParameterStage stage = builder
            .Sql((FormattableString)$"SELECT * FROM Users WHERE Id = {id}");

        // Act
        Action act = () => stage.With(scalarParameters);

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*이름 있는 파라미터 객체*");
    }

    [Fact]
    public async Task RawSql_GeneralFailure_ShouldReturnRedactedDbResultError()
    {
        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.ExecuteNonQueryAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SELECT SecretTable WHERE Password = 'plain-text'"));

        DbRequestBuilder builder = new(executor.Object, "verification");

        DbResult<int> result = await builder
            .Sql("SELECT * FROM SecretTable WHERE Password = 'plain-text'")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        DbError error = result.Error!.Value;
        error.Message.Should().Be("명령 실행 중 오류가 발생했습니다.");
        error.ObjectName.Should().Be("SQL command");
        error.InnerException.Should().BeNull();
        error.Message.Should().NotContain("SecretTable");
        error.ObjectName.Should().NotContain("SecretTable");
    }

    [Fact]
    public async Task Procedure_GeneralFailure_ShouldReturnRedactedDbResultError()
    {
        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.ExecuteNonQueryAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dbo.SecretProcedure failed"));

        DbRequestBuilder builder = new(executor.Object, "verification");

        DbResult<int> result = await builder
            .Procedure("dbo.SecretProcedure")
            .ExecuteAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        DbError error = result.Error!.Value;
        error.Message.Should().Be("명령 실행 중 오류가 발생했습니다.");
        error.ObjectName.Should().Be("stored procedure");
        error.InnerException.Should().BeNull();
        error.Message.Should().NotContain("SecretProcedure");
        error.ObjectName.Should().NotContain("SecretProcedure");
    }

    private sealed record OrderItemRow(int Id, string Sku, int Qty);
}
