// ============================================================================
// 파일: Executors/SqlDbExecutorTests.cs
// 설명: SqlDbExecutor 단위 테스트 (DryRun, 예외 래핑)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Contracts.Schema;
using Lib.Db.Execution;
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
        InterceptorChain interceptorChain = interceptors.Length == 0
            ? _interceptorChain
            : new InterceptorChain(interceptors);

        return new SqlDbExecutor(
            _mockStrategy.Object,
            _mockSchemaService.Object,
            _mockMapperFactory.Object,
            interceptorChain,
            Enumerable.Empty<IDbInterceptor>(),
            _options,
            _mockLogger.Object
        );
    }

    private void SetupExecuteAsync<TResult, TParams>()
    {
        _mockStrategy
            .Setup(x => x.ExecuteAsync(
                It.IsAny<DbRequest<TParams>>(),
                It.IsAny<Func<SqlConnection, CancellationToken, Task<TResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<DbRequest<TParams>, Func<SqlConnection, CancellationToken, Task<TResult>>, CancellationToken>(
                static (_, operation, token) => operation(new SqlConnection(), token));
    }

    private Mock<ISqlMapper<SchemaFallbackParams>> SetupSchemaFallbackMapper()
    {
        Mock<ISqlMapper<SchemaFallbackParams>> mapper = new();
        _mockMapperFactory
            .Setup(x => x.GetMapper<SchemaFallbackParams>())
            .Returns(mapper.Object);

        return mapper;
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
    public async Task SQ07_StoredProcedureSchemaLookupFailure_ShouldThrow_ByDefault()
    {
        Exception schemaFailure = new InvalidOperationException("schema lookup failed");
        SetupExecuteAsync<int, SchemaFallbackParams>();
        SetupSchemaFallbackMapper();

        _mockStrategy
            .SetupGet(x => x.DefaultSchemaMode)
            .Returns(SchemaResolutionMode.ServiceOnly);
        _mockSchemaService
            .Setup(x => x.GetSpSchemaAsync("SP_Test", "hash", It.IsAny<CancellationToken>()))
            .ThrowsAsync(schemaFailure);

        SqlDbExecutor executor = CreateExecutor(new SuppressResultInterceptor(42));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteNonQueryAsync(
                "SP_Test",
                new SchemaFallbackParams(),
                "hash",
                CommandType.StoredProcedure,
                DbExecutionOptions.Default,
                CancellationToken.None));

        Assert.Same(schemaFailure, ex.InnerException);
    }

    [Fact]
    public async Task SQ08_StoredProcedureSchemaLookupFailure_ShouldFallback_WhenExplicitlyAllowed()
    {
        _options.AllowStoredProcedureSchemaFallback = true;
        Exception schemaFailure = new InvalidOperationException("schema lookup failed");
        SetupExecuteAsync<int, SchemaFallbackParams>();
        Mock<ISqlMapper<SchemaFallbackParams>> mapper = SetupSchemaFallbackMapper();

        _mockStrategy
            .SetupGet(x => x.DefaultSchemaMode)
            .Returns(SchemaResolutionMode.ServiceOnly);
        _mockSchemaService
            .Setup(x => x.GetSpSchemaAsync("SP_Test", "hash", It.IsAny<CancellationToken>()))
            .ThrowsAsync(schemaFailure);

        SqlDbExecutor executor = CreateExecutor(new SuppressResultInterceptor(42));

        int result = await executor.ExecuteNonQueryAsync(
            "SP_Test",
            new SchemaFallbackParams(),
            "hash",
            CommandType.StoredProcedure,
            DbExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(42, result);
        mapper.Verify(x => x.MapParameters(
                It.IsAny<SqlCommand>(),
                It.IsAny<SchemaFallbackParams>(),
                It.Is<SpSchema?>(schema => schema == null)),
            Times.Once);
    }

    public sealed class SchemaFallbackParams
    {
        public int Id { get; set; } = 1;
    }

    private sealed class SuppressResultInterceptor(object result) : IDbCommandInterceptor
    {
        public ValueTask ReaderExecutingAsync(DbCommand command, DbCommandInterceptionContext context)
        {
            context.SetResult(result);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReaderExecutedAsync(DbCommand command, DbCommandExecutedEventData eventData)
            => ValueTask.CompletedTask;

        public ValueTask CommandFailedAsync(DbCommand command, DbCommandFailedEventData eventData)
            => ValueTask.CompletedTask;
    }
}
