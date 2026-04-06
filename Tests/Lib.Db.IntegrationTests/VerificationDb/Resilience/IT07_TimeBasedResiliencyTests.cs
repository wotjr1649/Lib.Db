// ============================================================================
// 파일: VerificationDb/Resilience/IT07_TimeBasedResiliencyTests.cs
// 설명: 시간 기반 Resilience 통합 테스트 (Timeout 재시도, 독자 DI 구성)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Data;
using System.Data.Common;
using Lib.Db.Core;
using Microsoft.Data.SqlClient;

namespace Lib.Db.IntegrationTests.VerificationDb.Resilience;

public sealed class IT07_TimeBasedResiliencyTests
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
        // 1. Setup Isolated Service Container
        ServiceCollection services = new();
        RetryCountingInterceptor_IT07 interceptor = new();

        string dbName = "LIBDB_VERIFICATION_TEST";
        string connString = $"Server=127.0.0.1,1433;Database={dbName};User ID=sa;Password=123456;Integrated Security=false;TrustServerCertificate=true;MultipleActiveResultSets=true";

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
                MaxRetryCount = 1,
                BaseRetryDelayMs = 10,
                RetryBackoffType = LibDbOptions.RetryBackoffType.Constant
            };
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IDbExecutor executor = provider.GetRequiredService<IDbExecutor>();

        // 2. Execution (Time-based Failure)
        string sql = "WAITFOR DELAY '00:00:03';";

        _output.WriteLine($"[Execution] Running SQL: {sql} with Timeout 1s");

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        SqlException exception = await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await executor.ExecuteNonQueryAsync(
                commandText: sql,
                parameters: new { },
                instanceHash: "Default",
                commandType: CommandType.Text,
                options: DbExecutionOptions.WithTimeout(1),
                ct: CancellationToken.None
            );
        });

        sw.Stop();
        _output.WriteLine($"[Result] Total Elapsed Time: {sw.ElapsedMilliseconds}ms");

        // 3. Assertions
        _output.WriteLine($"[Result] Exception Message: {exception.Message}");
        _output.WriteLine($"[Result] Error Number: {exception.Number}");

        Assert.Equal(-2, exception.Number);

        _output.WriteLine($"[Result] Interceptor Execution Count: {interceptor.ExecutionCount}");

        Assert.Equal(2, interceptor.ExecutionCount);

        _output.WriteLine("Confirmed: Timeout (-2) triggered retry correctly.");
    }
}

/// <summary>
/// IT07 전용 Interceptor.
/// </summary>
file sealed class RetryCountingInterceptor_IT07 : IDbCommandInterceptor
{
    private int _executionCount;

    public int ExecutionCount => _executionCount;

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
