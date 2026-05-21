// ============================================================================
// 파일: Unit/SqlServerPlanCacheAnalyzerTests.cs
// 설명: SQL Server plan cache analyzer 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using Lib.Db.Contracts.Diagnostics;
using Lib.Db.Contracts.Execution;
using Lib.Db.Diagnostics;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class SqlServerPlanCacheAnalyzerTests
{
    [Fact]
    public async Task AnalyzeSlowQueriesAsync_ShouldReturnExecutorRowsAndUsePlanCacheQuery()
    {
        QueryPerformanceInfo[] rows =
        [
            new("SELECT 1", 3, 30, 90, 30, 40, new DateTime(2026, 5, 18)),
            new("SELECT 2", 2, 20, 50, 25, 30, new DateTime(2026, 5, 18, 1, 0, 0))
        ];
        using CancellationTokenSource cts = new();
        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.QueryAsync<object, QueryPerformanceInfo>(
                It.Is<string>(sql => sql.Contains("sys.dm_exec_query_stats") && sql.Contains("TOP (@Top)")),
                It.Is<object>(p => HasTop(p, 2)),
                "Default",
                CommandType.Text,
                DbExecutionOptions.Default,
                cts.Token))
            .Returns(ToAsyncEnumerable(rows, cts.Token));

        var analyzer = new SqlServerPlanCacheAnalyzer(executor.Object);

        QueryPerformanceInfo[] result = (await analyzer.AnalyzeSlowQueriesAsync(2, cts.Token)).ToArray();

        result.Should().Equal(rows);
        executor.VerifyAll();
    }

    [Fact]
    public async Task AnalyzeSlowQueriesAsync_ShouldReturnEmptyCollectionWhenExecutorYieldsNoRows()
    {
        Mock<IDbExecutor> executor = new();
        executor
            .Setup(x => x.QueryAsync<object, QueryPerformanceInfo>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<CommandType>(),
                It.IsAny<DbExecutionOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(Array.Empty<QueryPerformanceInfo>()));

        var analyzer = new SqlServerPlanCacheAnalyzer(executor.Object);

        IEnumerable<QueryPerformanceInfo> result = await analyzer.AnalyzeSlowQueriesAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldRejectNullExecutor()
    {
        Action act = () => new SqlServerPlanCacheAnalyzer(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("executor");
    }

    private static bool HasTop(object parameters, int expected)
    {
        object? value = parameters.GetType().GetProperty("Top")?.GetValue(parameters);
        return value is int top && top == expected;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (T item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }
}
