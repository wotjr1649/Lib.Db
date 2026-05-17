// ============================================================================
// 파일: Unit/DbRequestBuilderTests.cs
// 설명: Fluent DbRequestBuilder 단위 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Entry;
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
}
