// ============================================================================
// 파일: Executors/StrategiesTests.cs
// 설명: 실행 전략 단위 테스트 (AdaptiveBatchSizer, InterceptorChain, Resilient, FastFail, SelfHealing)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Execution.Executors;
using Lib.Db.IntegrationTests.Infrastructure;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Lib.Db.IntegrationTests.Executors;

public sealed class StrategiesTests
{
    private readonly Mock<IDbConnectionFactory> _mockConnFactory;
    private readonly Mock<ISchemaService> _mockSchemaService;
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IResiliencePipelineProvider> _mockPipelineProvider;

    public StrategiesTests()
    {
        _mockConnFactory = new Mock<IDbConnectionFactory>();
        _mockSchemaService = new Mock<ISchemaService>();
        _mockLogger = new Mock<ILogger>();
        _mockPipelineProvider = new Mock<IResiliencePipelineProvider>();
    }

    [Fact]
    public void ST01_AdaptiveBatchSizer_Logic()
    {
        AdaptiveBatchSizer sizer = new(initial: 1000, min: 100, max: 2000, targetSec: 1.0);
        Assert.Equal(1000, sizer.Current);

        sizer.Adjust(TimeSpan.FromSeconds(0.5), 2000, memoryLoad: 0.1);
        Assert.InRange(sizer.Current, 1001, 1200);

        sizer.Adjust(TimeSpan.FromSeconds(1.0), 1000, memoryLoad: 0.9);
        Assert.True(sizer.Current < 1000);

        sizer.Throttle();
        Assert.Equal(100, sizer.Current);
    }

    [Fact]
    public async Task ST02_InterceptorChain_Order()
    {
        List<string> callLog = [];

        Mock<IDbCommandInterceptor> mockA = new();
        mockA.Setup(x => x.ReaderExecutingAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandInterceptionContext>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("A_Executing"));
        mockA.Setup(x => x.ReaderExecutedAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandExecutedEventData>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("A_Executed"));

        Mock<IDbCommandInterceptor> mockB = new();
        mockB.Setup(x => x.ReaderExecutingAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandInterceptionContext>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("B_Executing"));
        mockB.Setup(x => x.ReaderExecutedAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandExecutedEventData>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("B_Executed"));

        InterceptorChain chain = new([mockA.Object, mockB.Object]);

        using SqlCommand cmd = new();
        DbCommandInterceptionContext ctx = new("test_hash", CancellationToken.None);

        await chain.OnExecutingAsync(cmd, ctx);
        Assert.Equal("A_Executing", callLog[0]);
        Assert.Equal("B_Executing", callLog[1]);

        callLog.Clear();
        DbCommandExecutedEventData data = new(100, null);
        await chain.OnExecutedAsync(cmd, data);

        Assert.Equal("A_Executed", callLog[0]);
        Assert.Equal("B_Executed", callLog[1]);
    }

    [Fact]
    public async Task ST03_ResilientStrategy_Deadlock_Retry()
    {
        ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder().Handle<SqlException>(ex => ex.Number == 1205),
                Delay = TimeSpan.Zero
            })
            .Build();

        _mockPipelineProvider.Setup(x => x.IsEnabled).Returns(true);
        _mockPipelineProvider.Setup(x => x.Pipeline).Returns(pipeline);

        DbRequest<int> request = new(
            InstanceHash: "test_hash",
            CommandText: "SELECT 1",
            CommandType: CommandType.Text,
            Parameters: 0,
            CancellationToken: CancellationToken.None,
            IsTransactional: false
        );

        int callCount = 0;

        _mockConnFactory.Setup(x => x.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlConnection());

        ResilientStrategy strategy = new(_mockConnFactory.Object, _mockPipelineProvider.Object, _mockSchemaService.Object, _mockLogger.Object);
        SqlException ex1205 = SqlExceptionFactory.Create(1205);

        await strategy.ExecuteAsync(request, async (conn, ct) =>
        {
            callCount++;
            if (callCount == 1) throw ex1205;
            return 1;
        }, CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ST04_FastFail_CircuitBreaker()
    {
        ResilientStrategy strategy = new(_mockConnFactory.Object, _mockPipelineProvider.Object, _mockSchemaService.Object, _mockLogger.Object);

        DbRequest<int> request = new(
            InstanceHash: "test_hash",
            CommandText: "SELECT 1",
            CommandType: CommandType.Text,
            Parameters: 0,
            CancellationToken: CancellationToken.None,
            IsTransactional: false
        );

        SqlException exFastFail = SqlExceptionFactory.Create(18456);

        _mockPipelineProvider.Setup(x => x.IsEnabled).Returns(false);
        _mockConnFactory.Setup(x => x.CreateConnectionAsync(It.IsAny<Lib.Db.Contracts.Core.DbInstanceId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlConnection());

        BrokenCircuitException ex = await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            strategy.ExecuteAsync<int, int>(request, (c, t) => throw exFastFail, CancellationToken.None));

        Assert.Contains("18456", ex.Message);
    }

    [Fact]
    public async Task ST05_Schema_SelfHealing()
    {
        ResilientStrategy strategy = new(_mockConnFactory.Object, _mockPipelineProvider.Object, _mockSchemaService.Object, _mockLogger.Object);

        DbRequest<int> request = new(
            InstanceHash: "test_hash",
            CommandText: "sp_test",
            CommandType: CommandType.StoredProcedure,
            Parameters: 0,
            CancellationToken: CancellationToken.None,
            IsTransactional: false
        );

        SqlException ex207 = SqlExceptionFactory.Create(207);

        _mockPipelineProvider.Setup(x => x.IsEnabled).Returns(false);
        _mockConnFactory.Setup(x => x.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlConnection());

        await Assert.ThrowsAsync<SqlException>(() =>
            strategy.ExecuteAsync<int, int>(request, (c, t) => throw ex207, CancellationToken.None));

        _mockSchemaService.Verify(x => x.InvalidateSpSchema("sp_test", "test_hash"), Times.Once);
        _mockSchemaService.Verify(x => x.GetSpSchemaAsync("sp_test", "test_hash", It.IsAny<CancellationToken>()), Times.Once);
    }
}
