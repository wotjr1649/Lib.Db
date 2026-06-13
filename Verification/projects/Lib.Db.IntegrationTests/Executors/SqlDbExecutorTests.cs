// ============================================================================
// 파일: Executors/SqlDbExecutorTests.cs
// 설명: SqlDbExecutor 단위 테스트 (DryRun, 예외 래핑)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using System.Reflection;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Execution;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Executors;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.Executors;

public sealed class SqlDbExecutorTests
{
    private readonly Mock<IDbExecutionStrategy> _mockStrategy;
    private readonly Mock<ISchemaService> _mockSchemaService;
    private readonly Mock<IMapperFactory> _mockMapperFactory;
    private readonly Mock<ILogger<SqlDbExecutor>> _mockLogger;
    private readonly InterceptorChain _interceptorChain;

    private readonly LibDbOptions _options;

    public SqlDbExecutorTests()
    {
        _mockStrategy = new Mock<IDbExecutionStrategy>();
        _mockSchemaService = new Mock<ISchemaService>();
        _mockMapperFactory = new Mock<IMapperFactory>();
        _mockLogger = new Mock<ILogger<SqlDbExecutor>>();

        _interceptorChain = new InterceptorChain(Enumerable.Empty<IDbCommandInterceptor>());

        _options = new LibDbOptions();
    }

    private SqlDbExecutor CreateExecutor(params IDbCommandInterceptor[] interceptors)
    {
        return new SqlDbExecutor(
            _mockStrategy.Object,
            _mockSchemaService.Object,
            _mockMapperFactory.Object,
            new InterceptorChain(interceptors),
            Enumerable.Empty<IDbInterceptor>(),
            _options,
            _mockLogger.Object
        );
    }

    private SqlDbExecutor CreateExecutor()
    {
        return new SqlDbExecutor(
            _mockStrategy.Object,
            _mockSchemaService.Object,
            _mockMapperFactory.Object,
            _interceptorChain,
            Enumerable.Empty<IDbInterceptor>(),
            _options,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task SQ05_QueryMultiple_DryRun_ShouldReturnEmptyGrid()
    {
        _options.EnableDryRun = true;
        SqlDbExecutor executor = CreateExecutor();

        await using Lib.Db.Contracts.Execution.IMultipleResultReader reader = await executor.QueryMultipleAsync(
            "SP_Test",
            (object?)null,
            "hash",
            CommandType.StoredProcedure,
            DbExecutionOptions.Default,
            CancellationToken.None);

        Assert.NotNull(reader);
        Assert.IsType<EmptyGridReader>(reader);

        _mockStrategy.Verify(x => x.ExecuteStreamAsync(
            It.IsAny<DbRequest<object?>>(),
            It.IsAny<Func<SqlConnection, CancellationToken, Task<SqlDataReader>>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SQ06_QueryMultiple_Exception_ShouldWrapInLibDbException()
    {
        _options.EnableDryRun = false;
        SqlDbExecutor executor = CreateExecutor();
        Exception exOriginal = new("Native Error");

        _mockStrategy.Setup(x => x.ExecuteStreamAsync(
             It.IsAny<DbRequest<object?>>(),
             It.IsAny<Func<SqlConnection, CancellationToken, Task<SqlDataReader>>>(),
             It.IsAny<CancellationToken>()))
             .ThrowsAsync(exOriginal);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.QueryMultipleAsync(
            "SP_Test",
            (object?)null,
            "hash",
            CommandType.StoredProcedure,
            DbExecutionOptions.Default,
            CancellationToken.None));

        Assert.Same(exOriginal, ex.InnerException);
    }

    [Fact]
    public async Task SQ07_QueryMultiple_ShouldUse_CommandTimeout_FromExecutionOptions()
    {
        // Arrange
        _options.EnableDryRun = false;
        _options.DefaultCommandTimeoutSeconds = 30;

        CaptureCommandTimeoutInterceptor interceptor = new();
        SqlDbExecutor executor = CreateExecutor(interceptor);

        Mock<ISqlMapper<object?>> mapper = new();
        _mockMapperFactory
            .Setup(x => x.GetMapper<object?>())
            .Returns(mapper.Object);

        _mockStrategy
            .SetupGet(x => x.DefaultSchemaMode)
            .Returns(SchemaResolutionMode.None);
        _mockStrategy
            .SetupGet(x => x.IsTransactional)
            .Returns(false);
        _mockStrategy
            .Setup(x => x.ExecuteStreamAsync(
                It.IsAny<DbRequest<object?>>(),
                It.IsAny<Func<SqlConnection, CancellationToken, Task<SqlDataReader>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DbRequest<object?>, Func<SqlConnection, CancellationToken, Task<SqlDataReader>>, CancellationToken>(
                async (_, operation, token) =>
                {
                    await using SqlConnection conn = CreateClosedTestConnection(multipleActiveResultSets: true);
                    return await operation(conn, token);
                });

        // Act
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.QueryMultipleAsync(
            "SELECT 1",
            (object?)null,
            "hash",
            CommandType.Text,
            new DbExecutionOptions(null, 7),
            TestContext.Current.CancellationToken));

        // Assert
        ex.Message.Should().Contain("QueryMultipleAsync 실행 결과가 null입니다");
        interceptor.CommandTimeout.Should().Be(7);
    }

    [Fact]
    public async Task SQ08_ExecutePipeline_ShouldUse_CommandTimeout_FromExecutionOptions()
    {
        _options.EnableDryRun = false;
        _options.DefaultCommandTimeoutSeconds = 30;

        CaptureCommandTimeoutInterceptor interceptor = new();
        SqlDbExecutor executor = CreateExecutor(interceptor);

        Mock<ISqlMapper<object?>> mapper = new();
        _mockMapperFactory
            .Setup(x => x.GetMapper<object?>())
            .Returns(mapper.Object);

        _mockStrategy
            .SetupGet(x => x.DefaultSchemaMode)
            .Returns(SchemaResolutionMode.None);
        _mockStrategy
            .SetupGet(x => x.IsTransactional)
            .Returns(false);
        _mockStrategy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<DbRequest<object?>>(),
                It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DbRequest<object?>, Func<SqlConnection, CancellationToken, Task<int>>, CancellationToken>(
                async (_, operation, token) =>
                {
                    await using SqlConnection conn = new();
                    return await operation(conn, token);
                });

        int affected = await executor.ExecuteNonQueryAsync(
            "UPDATE dbo.Sample SET Value = @value",
            (object?)null,
            "hash",
            CommandType.Text,
            new DbExecutionOptions(null, 9),
            TestContext.Current.CancellationToken);

        affected.Should().Be(0);
        interceptor.CommandTimeout.Should().Be(9);
    }

    [Fact]
    public async Task SQ10_ExecutePipeline_ShouldLeaveOutputsUnchangedWhenExecutedInterceptorFails()
    {
        _options.EnableDryRun = false;

        OutputThenFailExecutedInterceptor interceptor = new();
        SqlDbExecutor executor = CreateExecutor(interceptor);
        var output = new SqlParameter("@OutputVal", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
            Value = 1
        };
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["OutputVal"] = output
        };

        _mockMapperFactory
            .Setup(x => x.GetMapper<Dictionary<string, object?>>())
            .Returns(new DictionarySqlMapper(strict: true));

        SetupPipelineStrategyInvocation();

        var request = new DbRequest<Dictionary<string, object?>>(
            "hash",
            "dbo.usp_Output",
            CommandType.StoredProcedure,
            parameters,
            TestContext.Current.CancellationToken,
            IsTransactional: false);
        Func<SqlCommand, CancellationToken, Task<int>> operation = (command, _) =>
        {
            ((SqlParameter)command.Parameters["@OutputVal"]).Value = 42;
            return Task.FromResult(1);
        };

        Task<int> task = InvokeExecutePipelineAsync(executor, request, operation);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await task);

        exception.Message.Should().Contain("명령 실행 중 오류");
        output.Value.Should().Be(1);
        interceptor.ExecutedCalled.Should().BeTrue();
    }

    [Fact]
    public async Task SQ09_QueryStream_ShouldUse_CommandTimeout_FromExecutionOptions()
    {
        _options.EnableDryRun = false;
        _options.DefaultCommandTimeoutSeconds = 30;

        CaptureCommandTimeoutInterceptor interceptor = new();
        SqlDbExecutor executor = CreateExecutor(interceptor);

        Mock<ISqlMapper<object?>> mapper = new();
        _mockMapperFactory
            .Setup(x => x.GetMapper<object?>())
            .Returns(mapper.Object);

        _mockStrategy
            .SetupGet(x => x.DefaultSchemaMode)
            .Returns(SchemaResolutionMode.None);
        _mockStrategy
            .SetupGet(x => x.IsTransactional)
            .Returns(false);
        _mockStrategy
            .Setup(x => x.ExecuteStreamAsync(
                It.IsAny<DbRequest<object?>>(),
                It.IsAny<Func<SqlConnection, CancellationToken, Task<SqlDataReader>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DbRequest<object?>, Func<SqlConnection, CancellationToken, Task<SqlDataReader>>, CancellationToken>(
                async (_, operation, token) =>
                {
                    await using SqlConnection conn = new();
                    return await operation(conn, token);
                });

        await foreach (object? _ in executor.QueryAsync<object?, object?>(
            "SELECT 1",
            (object?)null,
            "hash",
            CommandType.Text,
            new DbExecutionOptions(null, 11),
            TestContext.Current.CancellationToken))
        {
        }

        interceptor.CommandTimeout.Should().Be(11);
    }

    private sealed class CaptureCommandTimeoutInterceptor : IDbCommandInterceptor
    {
        public int? CommandTimeout { get; private set; }

        public ValueTask ReaderExecutingAsync(
            DbCommand command,
            DbCommandInterceptionContext context)
        {
            CommandTimeout = command.CommandTimeout;
            context.SetResult(null);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReaderExecutedAsync(
            DbCommand command,
            DbCommandExecutedEventData eventData)
            => ValueTask.CompletedTask;

        public ValueTask CommandFailedAsync(
            DbCommand command,
            DbCommandFailedEventData eventData)
            => ValueTask.CompletedTask;
    }

    private void SetupPipelineStrategyInvocation()
    {
        _mockStrategy
            .SetupGet(x => x.DefaultSchemaMode)
            .Returns(SchemaResolutionMode.None);
        _mockStrategy
            .SetupGet(x => x.IsTransactional)
            .Returns(false);
        _mockStrategy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<DbRequest<Dictionary<string, object?>>>(),
                It.IsAny<Func<SqlConnection, CancellationToken, Task<int>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DbRequest<Dictionary<string, object?>>, Func<SqlConnection, CancellationToken, Task<int>>, CancellationToken>(
                async (_, operation, token) =>
                {
                    await using SqlConnection conn = new();
                    return await operation(conn, token);
                });
    }

    private static SqlConnection CreateClosedTestConnection(bool multipleActiveResultSets = false)
    {
        SqlConnectionStringBuilder builder = new()
        {
            DataSource = ".",
            InitialCatalog = "master",
            IntegratedSecurity = true,
            MultipleActiveResultSets = multipleActiveResultSets,
            Encrypt = false,
            TrustServerCertificate = true
        };

        return new SqlConnection(builder.ConnectionString);
    }

    private static Task<int> InvokeExecutePipelineAsync(
        SqlDbExecutor executor,
        DbRequest<Dictionary<string, object?>> request,
        Func<SqlCommand, CancellationToken, Task<int>> operation)
    {
        MethodInfo method = typeof(SqlDbExecutor).GetMethod(
            "ExecutePipelineAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo genericMethod = method.MakeGenericMethod(typeof(Dictionary<string, object?>), typeof(int));
        return (Task<int>)genericMethod.Invoke(executor, [request, DbExecutionOptions.Default, operation])!;
    }

    private sealed class OutputThenFailExecutedInterceptor : IDbCommandInterceptor
    {
        public bool ExecutedCalled { get; private set; }

        public ValueTask ReaderExecutingAsync(
            DbCommand command,
            DbCommandInterceptionContext context)
            => ValueTask.CompletedTask;

        public ValueTask ReaderExecutedAsync(
            DbCommand command,
            DbCommandExecutedEventData eventData)
        {
            ExecutedCalled = true;
            throw new InvalidOperationException("executed interceptor failure");
        }

        public ValueTask CommandFailedAsync(
            DbCommand command,
            DbCommandFailedEventData eventData)
            => ValueTask.CompletedTask;
    }
}
