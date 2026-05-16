// ============================================================================
// 파일: Unit/SqlDbExecutorSecurityPolicyTests.cs
// 설명: SqlDbExecutor 보안 정책 단위 회귀 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Schema;
using Lib.Db.Execution;
using Lib.Db.Execution.Executors;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Unit;

[Trait("Category", "Unit")]
public sealed class SqlDbExecutorSecurityPolicyTests
{
    [Fact]
    public async Task RawSqlPolicy_DenyAllText_ShouldBlockTextCommandBeforeStrategy()
    {
        // Arrange
        Mock<IDbExecutionStrategy> strategy = CreateStrategy();
        SqlDbExecutor executor = CreateExecutor(strategy.Object, RawSqlPolicy.DenyAllText);

        // Act
        Func<Task> act = () => executor.ExecuteNonQueryAsync(
            "SELECT 1",
            new object(),
            "Verification",
            CommandType.Text,
            new DbExecutionOptions(),
            TestContext.Current.CancellationToken);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*RawSqlPolicy*");

        strategy.Verify(x => x.ExecuteAsync<int, object>(
            It.IsAny<DbRequest<object>>(),
            It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("UPDATE dbo.Users SET Name = @Name")]
    [InlineData("  /* audit */ DELETE FROM dbo.Users WHERE Id = @Id")]
    [InlineData("-- setup\r\nTRUNCATE TABLE dbo.Users")]
    [InlineData("; DELETE FROM dbo.Users WHERE Id = @Id")]
    [InlineData(";; \t-- separator\r\nDELETE FROM dbo.Users WHERE Id = @Id")]
    [InlineData(";\r\n/* audit */ DROP TABLE dbo.Users")]
    [InlineData("/* outer /* inner */ SELECT */ DELETE FROM dbo.Users")]
    [InlineData("; /* outer /* inner */ SELECT */ DROP TABLE dbo.Users")]
    [InlineData("EXEC dbo.usp_DoWork")]
    [InlineData("WITH cte AS (SELECT Id FROM dbo.Users) DELETE FROM cte")]
    [InlineData("SELECT * INTO #Users FROM dbo.Users")]
    [InlineData("DECLARE @sql nvarchar(max) = N'DELETE FROM dbo.Users'; EXEC(@sql)")]
    [InlineData("BACKUP DATABASE AppDb TO DISK = N'C:\\temp\\app.bak'")]
    [InlineData("RESTORE DATABASE AppDb FROM DISK = N'C:\\temp\\app.bak'")]
    [InlineData("DBCC CHECKDB")]
    [InlineData("USE master")]
    [InlineData("BULK INSERT dbo.Users FROM N'C:\\temp\\users.csv'")]
    public async Task RawSqlPolicy_DenyWriteText_ShouldBlockMutatingText(string sql)
    {
        // Arrange
        Mock<IDbExecutionStrategy> strategy = CreateStrategy();
        SqlDbExecutor executor = CreateExecutor(strategy.Object, RawSqlPolicy.DenyWriteText);

        // Act
        Func<Task> act = () => executor.ExecuteNonQueryAsync(
            sql,
            new object(),
            "Verification",
            CommandType.Text,
            new DbExecutionOptions(),
            TestContext.Current.CancellationToken);

        // Assert
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*RawSqlPolicy*");

        strategy.Verify(x => x.ExecuteAsync<int, object>(
            It.IsAny<DbRequest<object>>(),
            It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("SELECT 'DELETE FROM dbo.Users' AS LiteralValue")]
    [InlineData("SELECT [DROP] FROM dbo.AuditLog")]
    [InlineData("SELECT * FROM dbo.IntoTable WHERE Name = @Name")]
    public async Task RawSqlPolicy_DenyWriteText_ShouldAllowReadOnlyTextWithUnsafeWordsInLiteralsOrIdentifiers(string sql)
    {
        // Arrange
        Mock<IDbExecutionStrategy> strategy = CreateStrategy();
        strategy
            .Setup(x => x.ExecuteAsync<int, object>(
                It.IsAny<DbRequest<object>>(),
                It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        SqlDbExecutor executor = CreateExecutor(strategy.Object, RawSqlPolicy.DenyWriteText);

        // Act
        int result = await executor.ExecuteNonQueryAsync(
            sql,
            new object(),
            "Verification",
            CommandType.Text,
            new DbExecutionOptions(),
            TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(1);
        strategy.Verify(x => x.ExecuteAsync<int, object>(
            It.IsAny<DbRequest<object>>(),
            It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserInterceptorContext_ShouldExposeRedactedDiagnosticCommandText()
    {
        // Arrange
        Mock<IDbExecutionStrategy> strategy = CreateStrategy();
        CapturingSuppressInterceptor interceptor = new();
        SqlDbExecutor executor = CreateExecutor(
            strategy.Object,
            RawSqlPolicy.Allow,
            options => options.IncludeParametersInTrace = false,
            [interceptor]);

        // Act
        _ = await executor.ExecuteScalarAsync<object, int>(
            "SELECT * FROM dbo.Users WHERE Secret = 'literal'",
            new object(),
            "Verification",
            CommandType.Text,
            new DbExecutionOptions(),
            TestContext.Current.CancellationToken);

        // Assert
        interceptor.Context.Should().NotBeNull();
        interceptor.Context!.CommandText.Should().Contain("literal");
        interceptor.Context.DiagnosticCommandText.Should().Be("Text");

        strategy.Verify(x => x.ExecuteAsync<int, object>(
            It.IsAny<DbRequest<object>>(),
            It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IDbExecutionStrategy> CreateStrategy()
    {
        Mock<IDbExecutionStrategy> strategy = new();
        strategy.SetupGet(x => x.IsTransactional).Returns(false);
        strategy.SetupGet(x => x.DefaultSchemaMode).Returns(SchemaResolutionMode.None);
        return strategy;
    }

    private static SqlDbExecutor CreateExecutor(
        IDbExecutionStrategy strategy,
        RawSqlPolicy rawSqlPolicy,
        Action<LibDbOptions>? configure = null,
        IEnumerable<IDbInterceptor>? userInterceptors = null)
    {
        LibDbOptions options = TestOptionsFactory.CreateMinimal();
        options.RawSqlPolicy = rawSqlPolicy;
        configure?.Invoke(options);

        return new SqlDbExecutor(
            strategy,
            Mock.Of<ISchemaService>(),
            Mock.Of<IMapperFactory>(),
            new InterceptorChain([]),
            userInterceptors ?? Array.Empty<IDbInterceptor>(),
            options,
            Mock.Of<ILogger<SqlDbExecutor>>());
    }

    private sealed class CapturingSuppressInterceptor : IDbInterceptor
    {
        public DbInterceptionContext? Context { get; private set; }

        public ValueTask<DbInterceptionResult> OnExecutingAsync(
            DbInterceptionContext context,
            CancellationToken ct)
        {
            Context = context;
            return ValueTask.FromResult(DbInterceptionResult.Suppress);
        }

        public ValueTask OnExecutedAsync(DbInterceptionContext context, CancellationToken ct)
            => ValueTask.CompletedTask;

        public ValueTask OnErrorAsync(DbInterceptionContext context, CancellationToken ct)
            => ValueTask.CompletedTask;
    }
}
