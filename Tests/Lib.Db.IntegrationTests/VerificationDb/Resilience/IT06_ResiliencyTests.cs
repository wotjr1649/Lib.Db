// ============================================================================
// 파일: VerificationDb/Resilience/IT06_ResiliencyTests.cs
// 설명: Resilience 재시도 통합 테스트 (독자 DI 구성)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Core;
using Lib.Db.Infrastructure.Resilience;
using Lib.Db.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lib.Db.IntegrationTests.VerificationDb.Resilience;

public sealed class IT06_ResiliencyTests
{
    private readonly ITestOutputHelper _output;

    public IT06_ResiliencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Phase", "6")]
    public async Task IT06_Resiliency_Retry_Should_Attempt_Configured_Times_On_UserDefined_TransientError_RealDb()
    {
        // 1. Setup Isolated Service Container
        ServiceCollection services = new();
        RetryCountingInterceptor interceptor = new();
        IConfiguration configuration = TestConnectionStrings.CreateConfiguration();
        string connString = TestConnectionStrings.Require(configuration, TestConnectionStrings.Verification);

        services.AddSingleton<ITransientSqlErrorDetector, TestCustomTransientErrorDetector>();
        services.AddSingleton<IDbCommandInterceptor>(interceptor);
        services.AddLogging();

        services.AddHighPerformanceDb(options =>
        {
            options.ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = connString
            };

            options.EnableResilience = true;
            options.Resilience = new LibDbOptions.ResilienceOptions
            {
                MaxRetryCount = 2,
                BaseRetryDelayMs = 10,
                RetryBackoffType = LibDbOptions.RetryBackoffType.Constant
            };
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IDbExecutor executor = provider.GetRequiredService<IDbExecutor>();

        // 2. Execution (Deterministic Failure)
        string sql = "RAISERROR('Intentional Transient Error (50000)', 16, 1) WITH NOWAIT;";

        _output.WriteLine($"[Execution] Running SQL: {sql}");

        SqlException exception = await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await executor.ExecuteNonQueryAsync(
                commandText: sql,
                parameters: new { },
                instanceHash: "Default",
                commandType: CommandType.Text,
                options: DbExecutionOptions.Default,
                ct: CancellationToken.None
            );
        });

        // 3. Assertions
        _output.WriteLine($"[Result] Exception Message: {exception.Message}");
        _output.WriteLine($"[Result] Error Number: {exception.Number}");

        Assert.Equal(50000, exception.Number);

        _output.WriteLine($"[Result] Interceptor Execution Count: {interceptor.ExecutionCount}");

        Assert.Equal(3, interceptor.ExecutionCount);

        _output.WriteLine("Confirmed: Retry logic attempted exactly 3 times (Initial + 2 Retries) for custom transient error 50000.");
    }
}

/// <summary>
/// Custom Detector that adds Error 50000 to the transient list.
/// </summary>
file sealed class TestCustomTransientErrorDetector : ITransientSqlErrorDetector
{
    private readonly DefaultTransientSqlErrorDetector _defaultDetector = new();

    public bool IsTransient(Exception ex)
    {
        if (ex is SqlException sqlEx)
        {
            foreach (SqlError error in sqlEx.Errors)
            {
                if (error.Number == 50000) return true;
            }
        }

        return _defaultDetector.IsTransient(ex);
    }
}

/// <summary>
/// Interceptor to count execution attempts.
/// </summary>
file sealed class RetryCountingInterceptor : IDbCommandInterceptor
{
    private int _executionCount;

    public int ExecutionCount => _executionCount;

    public void Reset() => Interlocked.Exchange(ref _executionCount, 0);

    public ValueTask ReaderExecutingAsync(DbCommand command, DbCommandInterceptionContext context)
    {
        Interlocked.Increment(ref _executionCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReaderExecutedAsync(DbCommand command, DbCommandExecutedEventData eventData)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask CommandFailedAsync(DbCommand command, DbCommandFailedEventData eventData)
    {
        return ValueTask.CompletedTask;
    }
}
