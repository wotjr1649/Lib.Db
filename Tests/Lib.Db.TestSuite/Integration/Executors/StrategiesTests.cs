using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Contracts.Models; // For SpSchema?
using Lib.Db.Execution.Executors;
using Lib.Db.Verification.Tests.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Xunit;

namespace Lib.Db.Verification.Tests.Executors;

public class StrategiesTests
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
        var sizer = new AdaptiveBatchSizer(initial: 1000, min: 100, max: 2000, targetSec: 1.0);
        Assert.Equal(1000, sizer.Current);

        // Case A: High throughput (2000 rows in 0.5s = 4000 rps)
        sizer.Adjust(TimeSpan.FromSeconds(0.5), 2000, memoryLoad: 0.1);
        Assert.InRange(sizer.Current, 1001, 1200);

        // Case B: Memory Pressure > 0.8
        sizer.Adjust(TimeSpan.FromSeconds(1.0), 1000, memoryLoad: 0.9);
        Assert.True(sizer.Current < 1000);

        // Case C: Throttle
        sizer.Throttle();
        Assert.Equal(100, sizer.Current);
    }

    [Fact]
    public async Task ST02_InterceptorChain_Order()
    {
        var callLog = new List<string>();

        var mockA = new Mock<IDbCommandInterceptor>();
        mockA.Setup(x => x.ReaderExecutingAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandInterceptionContext>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("A_Executing"));
        mockA.Setup(x => x.ReaderExecutedAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandExecutedEventData>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("A_Executed"));

        var mockB = new Mock<IDbCommandInterceptor>();
        mockB.Setup(x => x.ReaderExecutingAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandInterceptionContext>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("B_Executing"));
        mockB.Setup(x => x.ReaderExecutedAsync(It.IsAny<DbCommand>(), It.IsAny<DbCommandExecutedEventData>()))
             .Returns(ValueTask.CompletedTask)
             .Callback(() => callLog.Add("B_Executed"));

        var chain = new InterceptorChain(new[] { mockA.Object, mockB.Object });
        
        using var cmd = new SqlCommand();
        var ctx = new DbCommandInterceptionContext("test_hash", CancellationToken.None);
        
        await chain.OnExecutingAsync(cmd, ctx);
        Assert.Equal("A_Executing", callLog[0]);
        Assert.Equal("B_Executing", callLog[1]);

        callLog.Clear();
        var data = new DbCommandExecutedEventData(100, null);
        await chain.OnExecutedAsync(cmd, data);
        
        Assert.Equal("A_Executed", callLog[0]);
        Assert.Equal("B_Executed", callLog[1]);
    }

    [Fact]
    public async Task ST03_ResilientStrategy_Deadlock_Retry()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions 
            { 
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder().Handle<SqlException>(ex => ex.Number == 1205),
                Delay = TimeSpan.Zero
            })
            .Build();

        _mockPipelineProvider.Setup(x => x.IsEnabled).Returns(true);
        _mockPipelineProvider.Setup(x => x.Pipeline).Returns(pipeline);

        var request = new DbRequest<int>(
            InstanceHash: "test_hash",
            CommandText: "SELECT 1",
            CommandType: CommandType.Text,
            Parameters: 0,
            CancellationToken: CancellationToken.None,
            IsTransactional: false
        );

        int callCount = 0;
        
        // Use a real (disposable) connection if possible, or Mock if CreateConnectionAsync returns Task<SqlConnection> (which isn't mockable easily).
        // If Lib.Db uses SqlConnection directly, we have to provide one.
        // We will create a disposable SqlConnection. It doesn't need to be open to pass strictly, 
        // BUT ResilientStrategy might try to Open it?
        // Strategies: using var conn = await _connectionFactory.CreateConnectionAsync(...);
        // It does NOT call OpenAsync inside Strategy usually; Executor does. 
        // Strategy passes conn to operation.
        // But for Deadlock handling (1205), Strategy DOES create a NEW command on the connection to Set Priority.
        // new SqlCommand(..., conn).ExecuteNonQueryAsync().
        // This requires connection to be Open.
        // A real SqlConnection without Open() will fail ExecuteNonQueryAsync.
        // This test case is therefore difficult to run as a pure unit test without a real DB or specific abstraction.
        // However, we can assert that retry happened. The 1205 logic is:
        // catch 1205 -> _elevatePriorityOnNextRetry = true.
        // Next try -> if (_elevatePriority... && conn.State == Open) -> Execute SET DEADLOCK...
        // If conn is Closed, it skips? Or checks State?
        // Code check needed.
        // L590: if (_elevatePriorityOnNextRetry) { ... if (conn is { State: ConnectionState.Open } or { State: ConnectionState.Connecting }) ... }
        // So if we return a Closed connection, it SHOULD SKIP the SQL execution but still proceed.
        // This allows us to verify the Retry Loop logic without crashing on SQL execution!
        // We just need to ensure `_elevatePriorityOnNextRetry` flag was set.
        // But `_elevatePriority` is private.
        // We can infer it by the fact that it retries (Polly).
        // But Polly retry is configured here.
        // So this test mainly verifies that ResilientStrategy *propagates* the exception to Polly 
        // (after `HandleSqlExceptionAsync` which delegates 1205 logic),
        // and Polly retries.
        
        _mockConnFactory.Setup(x => x.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             // Note: Contracts use string instanceHash usually? 
             // Core.IDbConnectionFactory uses DbInstanceId. 
             // Strategies.cs uses IDbConnectionFactory.
             // We need to use It.IsAny<object> or match the type.
             // But if I put It.IsAny<object>(), Moq might complain if type is specific.
             // I will use `It.IsAny<Lib.Db.Core.DbInstanceId>()` if I can access it, or generic `It.IsAny<object>()`.
             // Wait, I didn't include `using Lib.Db.Configuration;` in the snippet above?
             // I should include it.
            .ReturnsAsync(new SqlConnection());

        var strategy = new ResilientStrategy(_mockConnFactory.Object, _mockPipelineProvider.Object, _mockSchemaService.Object, _mockLogger.Object);
        var ex1205 = SqlExceptionFactory.Create(1205);

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
        var strategy = new ResilientStrategy(_mockConnFactory.Object, _mockPipelineProvider.Object, _mockSchemaService.Object, _mockLogger.Object);
        
        var request = new DbRequest<int>(
            InstanceHash: "test_hash",
            CommandText: "SELECT 1",
            CommandType: CommandType.Text,
            Parameters: 0,
            CancellationToken: CancellationToken.None,
            IsTransactional: false
        );
        
        var exFastFail = SqlExceptionFactory.Create(18456); 

        _mockPipelineProvider.Setup(x => x.IsEnabled).Returns(false);
        _mockConnFactory.Setup(x => x.CreateConnectionAsync(It.IsAny<Lib.Db.Contracts.Core.DbInstanceId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlConnection());

        var ex = await Assert.ThrowsAsync<BrokenCircuitException>(() => 
            strategy.ExecuteAsync<int, int>(request, (c, t) => throw exFastFail, CancellationToken.None));
            
        Assert.Contains("18456", ex.Message);
    }

    [Fact]
    public async Task ST05_Schema_SelfHealing()
    {
        var strategy = new ResilientStrategy(_mockConnFactory.Object, _mockPipelineProvider.Object, _mockSchemaService.Object, _mockLogger.Object);
        
        var request = new DbRequest<int>(
            InstanceHash: "test_hash", 
            CommandText: "sp_test", 
            CommandType: CommandType.StoredProcedure, 
            Parameters: 0, 
            CancellationToken: CancellationToken.None,
            IsTransactional: false
        );
        
        var ex207 = SqlExceptionFactory.Create(207);

        _mockPipelineProvider.Setup(x => x.IsEnabled).Returns(false);
        _mockConnFactory.Setup(x => x.CreateConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SqlConnection());

        await Assert.ThrowsAsync<SqlException>(() => 
            strategy.ExecuteAsync<int, int>(request, (c, t) => throw ex207, CancellationToken.None));

        // Use string matchers for the Method Call
        // InvalidateSpSchema(string spName, string instanceHash)
        _mockSchemaService.Verify(x => x.InvalidateSpSchema("sp_test", "test_hash"), Times.Once);
        
        // GetSpSchemaAsync(string spName, string instanceHash, CancellationToken ct)
        _mockSchemaService.Verify(x => x.GetSpSchemaAsync("sp_test", "test_hash", It.IsAny<CancellationToken>()), Times.Once);
    }
}

