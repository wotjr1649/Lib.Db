// ============================================================================
// 파일: Executors/SqlDbExecutorTests.cs
// 설명: SqlDbExecutor 단위 테스트 (DryRun, 예외 래핑)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Contracts.Mapping;
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
}
