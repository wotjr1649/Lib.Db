using System.Collections.Concurrent;
using System.Data;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Execution;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Core; // Added for LibDbOptions
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Lib.Db.Verification.Tests.Integration;

public class IT06_Resiliency_Tests
{
    private readonly ITestOutputHelper _output;

    public IT06_Resiliency_Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Phase", "6")]
    public async Task IT06_Resiliency_Retry_Should_Attempt_Configured_Times_On_UserDefined_TransientError_RealDb()
    {
        // ---------------------------------------------------------------------
        // 1. Setup Isolated Service Container
        // ---------------------------------------------------------------------
        var services = new ServiceCollection();
        var interceptor = new RetryCountingInterceptor();

        // 1-1. Configuration
        var dbName = "LIBDB_VERIFICATION_TEST";
        var connString = $"Server=127.0.0.1,1433;Database={dbName};User ID=sa;Password=123456;Integrated Security=false;TrustServerCertificate=true;MultipleActiveResultSets=true";

        // 1-2. Register Custom Detector BEFORE Lib.Db to override Default
        // We want Error 50000 to be transient
        services.AddSingleton<ITransientSqlErrorDetector, TestCustomTransientErrorDetector>();
        
        // 1-3. Register Interceptor
        services.AddSingleton<IDbCommandInterceptor>(interceptor);
        
        // 1-3.5 Add Logging (Required by Lib.Db)
        services.AddLogging();

        // 1-4. Add Lib.Db
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
                MaxRetryCount = 2, // Total 3 attempts (1 initial + 2 retries)
                BaseRetryDelayMs = 10, // Fast fail
                RetryBackoffType = LibDbOptions.RetryBackoffType.Constant
            };
        });

        // 1-5. Build Provider
        await using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IDbExecutor>();

        // ---------------------------------------------------------------------
        // 2. Execution (Deterministic Failure)
        // ---------------------------------------------------------------------
        // RAISERROR with 50000. 
        // We use Severity 16 (User Error) but our Custom Detector treats 50000 as Transient.
        var sql = "RAISERROR('Intentional Transient Error (50000)', 16, 1) WITH NOWAIT;";
        
        _output.WriteLine($"[Execution] Running SQL: {sql}");
        
        var exception = await Assert.ThrowsAsync<SqlException>(async () =>
        {
             // Use Raw Interface Method
             await executor.ExecuteNonQueryAsync(
                 commandText: sql,
                 parameters: new { },
                 instanceHash: "Default",
                 commandType: CommandType.Text,
                 options: DbExecutionOptions.Default,
                 ct: CancellationToken.None
             );
        });

        // ---------------------------------------------------------------------
        // 3. Assertions
        // ---------------------------------------------------------------------
        // Verify Exception
        _output.WriteLine($"[Result] Exception Message: {exception.Message}");
        _output.WriteLine($"[Result] Error Number: {exception.Number}");
        
        Assert.Equal(50000, exception.Number);

        // Verify Retry Count
        // Expect: Initial (1) + Retry (2) = 3 calls
        _output.WriteLine($"[Result] Interceptor Execution Count: {interceptor.ExecutionCount}");
        
        Assert.Equal(3, interceptor.ExecutionCount);
        
        _output.WriteLine("Confirmed: Retry logic attempted exactly 3 times (Initial + 2 Retries) for custom transient error 50000.");
    }
}

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

/// <summary>
/// Custom Detector that adds Error 50000 to the transient list.
/// </summary>
public class TestCustomTransientErrorDetector : ITransientSqlErrorDetector
{
    // Use the default logic for fallback
    private readonly Lib.Db.Infrastructure.Resilience.DefaultTransientSqlErrorDetector _defaultDetector 
        = new Lib.Db.Infrastructure.Resilience.DefaultTransientSqlErrorDetector();

    public bool IsTransient(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            foreach (SqlError error in sqlEx.Errors)
            {
                // Custom User-Defined Transient Error
                if (error.Number == 50000) return true;
            }
        }
        
        return _defaultDetector.IsTransient(ex);
    }
}

/// <summary>
/// Interceptor to count execution attempts.
/// </summary>
public class RetryCountingInterceptor : IDbCommandInterceptor
{
    private int _executionCount;

    public int ExecutionCount => _executionCount;

    public void Reset() => Interlocked.Exchange(ref _executionCount, 0);

    public ValueTask ReaderExecutingAsync(System.Data.Common.DbCommand command, DbCommandInterceptionContext context)
    {
        Interlocked.Increment(ref _executionCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReaderExecutedAsync(System.Data.Common.DbCommand command, DbCommandExecutedEventData eventData)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask CommandFailedAsync(System.Data.Common.DbCommand command, DbCommandFailedEventData eventData)
    {
        return ValueTask.CompletedTask;
    }
}
