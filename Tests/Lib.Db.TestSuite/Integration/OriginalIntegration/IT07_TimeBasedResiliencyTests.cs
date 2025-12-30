using System.Collections.Concurrent;
using System.Data;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Lib.Db.Verification.Tests.Integration;

public class IT07_TimeBasedResiliencyTests
{
    private readonly ITestOutputHelper _output;

    public IT07_TimeBasedResiliencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Optional_Resiliency_TimeBased")]
    [Trait("Phase", "7")]
    public async Task IT07_TimeBasedResiliency_Should_Retry_On_Timeout_RealDb_Optional()
    {
        // ---------------------------------------------------------------------
        // 1. Setup Isolated Service Container
        // ---------------------------------------------------------------------
        var services = new ServiceCollection();
        var interceptor = new RetryCountingInterceptor_IT07();

        // 1-1. Configuration
        var dbName = "LIBDB_VERIFICATION_TEST";
        var connString = $"Server=127.0.0.1,1433;Database={dbName};User ID=sa;Password=123456;Integrated Security=false;TrustServerCertificate=true;MultipleActiveResultSets=true";

        // 1-2. Register Interceptor & Logging
        services.AddSingleton<IDbCommandInterceptor>(interceptor);
        services.AddLogging();

        // 1-3. Add Lib.Db
        services.AddHighPerformanceDb(options =>
        {
            options.ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = connString
            };
            
            // Enabled Resilience
            options.EnableResilience = true;
            options.Resilience = new LibDbOptions.ResilienceOptions
            {
                MaxRetryCount = 1, // Total 2 attempts (1 initial + 1 retry)
                BaseRetryDelayMs = 10,
                RetryBackoffType = LibDbOptions.RetryBackoffType.Constant
            };
        });

        // 1-4. Build Provider
        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IDbExecutor>();

        // ---------------------------------------------------------------------
        // 2. Execution (Time-based Failure)
        // ---------------------------------------------------------------------
        // WAITFOR DELAY 3s, but Timeout is 1s.
        // This guarantees a Timeout Exception (-2).
        var sql = "WAITFOR DELAY '00:00:03';";
        
        _output.WriteLine($"[Execution] Running SQL: {sql} with Timeout 1s");
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        var exception = await Assert.ThrowsAsync<SqlException>(async () =>
        {
             await executor.ExecuteNonQueryAsync(
                 commandText: sql,
                 parameters: new { },
                 instanceHash: "Default",
                 commandType: CommandType.Text,
                 options: DbExecutionOptions.WithTimeout(1), // Force 1s Timeout
                 ct: CancellationToken.None
             );
        });
        
        sw.Stop();
        _output.WriteLine($"[Result] Total Elapsed Time: {sw.ElapsedMilliseconds}ms");

        // ---------------------------------------------------------------------
        // 3. Assertions
        // ---------------------------------------------------------------------
        // Verify Exception (Client Timeout is usually -2)
        _output.WriteLine($"[Result] Exception Message: {exception.Message}");
        _output.WriteLine($"[Result] Error Number: {exception.Number}");
        
        Assert.Equal(-2, exception.Number);

        // Verify Retry Count
        // Expect: Initial (1) + Retry (1) = 2 calls
        _output.WriteLine($"[Result] Interceptor Execution Count: {interceptor.ExecutionCount}");
        
        // Assert >= 2 because sometimes timeout handling might trigger extra checks, but strictly it should be 2.
        Assert.Equal(2, interceptor.ExecutionCount);
        
        _output.WriteLine("Confirmed: Timeout (-2) triggered retry correctly.");
    }
}

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

public class RetryCountingInterceptor_IT07 : IDbCommandInterceptor
{
    private int _executionCount;

    public int ExecutionCount => _executionCount;

    public ValueTask ReaderExecutingAsync(System.Data.Common.DbCommand command, DbCommandInterceptionContext context)
    {
        Interlocked.Increment(ref _executionCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReaderExecutedAsync(System.Data.Common.DbCommand command, DbCommandExecutedEventData eventData) // Fixed: Added missing method
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask CommandFailedAsync(System.Data.Common.DbCommand command, DbCommandFailedEventData eventData) // Fixed: Added missing method
    {
        return ValueTask.CompletedTask;
    }
}
