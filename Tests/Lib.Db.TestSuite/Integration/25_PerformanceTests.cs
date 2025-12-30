// ============================================================================
// 25_PerformanceTests.cs
// 목적: 성능 벤치마크 및 처리량 측정
// 시나리오: 4개
// 대상: .NET 10 / C# 14
// 패턴: Bulk Insert, Channel Pipeline, Concurrency, Throughput
// ============================================================================

using Lib.Db.Contracts.Entry;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Threading.Channels;
using Xunit.Abstractions;

namespace Lib.Db.Verification.Tests;

[Collection("Database Collection")]
public class PerformanceTests(TestDatabaseFixture fixture, ITestOutputHelper output)
{
    private readonly IDbContext _db = fixture.Db;
    private readonly IServiceProvider _services = fixture.Services;
    private readonly ITestOutputHelper _output = output;

    #region Bulk Insert Performance

    [Fact]
    public async Task Scenario_25_1_BulkInsert_ShouldMeetPerformanceBaseline()
    {
        // [목적] Bulk Insert 성능 기준선 검증 (10K rows < 5초)
        _output.WriteLine("=== Bulk Insert Performance Test 시작 ===");
        
        // Arrange
        const int rowCount = 10_000;
        const int batchNumber = 25001;
        
        // ToArray() 사용 (기존 테스트와 동일)
        var testData = Enumerable.Range(1, rowCount).Select(i => new PerfBulkInsertTvp
        {
            BatchNumber = batchNumber,
            Data = $"Performance Test {i}"
        }).ToArray(); // ToList() → ToArray()
        
        _output.WriteLine($"테스트 데이터: {rowCount:N0}행");
        _output.WriteLine($"데이터 타입: {testData.GetType().Name}");
        
        try
        {
            // Act - Bulk Insert 실행
            var sw = Stopwatch.StartNew();
            
            await _db.Default.BulkInsertAsync("perf.BulkTest", testData);
            
            sw.Stop();
            
            // 결과 확인
            var insertedCount = await _db.Default
                .Sql($"SELECT COUNT(*) FROM perf.BulkTest WHERE BatchNumber = {batchNumber}")
                .ExecuteScalarAsync<int>();
            
            _output.WriteLine($"\n삽입 완료: {insertedCount:N0}행");
            _output.WriteLine($"소요 시간: {sw.Elapsed.TotalSeconds:F2}초");
            
            if (insertedCount > 0)
            {
                _output.WriteLine($"처리량: {insertedCount / sw.Elapsed.TotalSeconds:F0} rows/sec");
            }
            
            // Assert - 성능 기준선 검증
            Assert.Equal(rowCount, insertedCount);
            Assert.True(sw.Elapsed.TotalSeconds < 5, 
                $"Bulk Insert가 너무 느림: {sw.Elapsed.TotalSeconds:F2}초 (기준: 5초)");
            
            _output.WriteLine("\n=== Test 완료: 성능 기준선 충족 ===");
        }
        finally
        {
            // Cleanup
            await _db.Default
                .Sql($"DELETE FROM perf.BulkTest WHERE BatchNumber = {batchNumber}")
                .ExecuteAsync();
            _output.WriteLine("테스트 데이터 정리 완료");
        }
    }

    [Fact]
    public async Task Scenario_25_2_BulkInsertPipeline_ShouldStreamEfficiently()
    {
        // [목적] Channel 기반 Bulk Insert Pipeline 성능 검증
        _output.WriteLine("=== Bulk Insert Pipeline Performance Test 시작 ===");
        
        // Arrange
        const int rowCount = 10_000;
        const int batchSize = 1_000;
        const int batchNumber = 25002;
        
        _output.WriteLine($"테스트 데이터: {rowCount:N0}행");
        _output.WriteLine($"배치 크기: {batchSize:N0}행");
        
        try
        {
            // Channel 생성
            var channel = Channel.CreateUnbounded<PerfBulkInsertTvp>();
            
            // Act - Producer: 데이터 생성
            var producerTask = Task.Run(async () =>
            {
                for (int i = 0; i < rowCount; i++)
                {
                    await channel.Writer.WriteAsync(new PerfBulkInsertTvp
                    {
                        BatchNumber = batchNumber,
                        Data = $"Pipeline Test {i + 1}"
                    });
                }
                
                channel.Writer.Complete();
                _output.WriteLine($"Producer 완료: {rowCount:N0}행 생성");
            });
            
            // Consumer: Bulk Insert Pipeline
            var sw = Stopwatch.StartNew();
            
            await _db.Default.BulkInsertPipelineAsync(
                "perf.BulkTest", 
                channel.Reader, 
                batchSize);
            
            await producerTask;
            sw.Stop();
            
            // 결과 확인
            var insertedCount = await _db.Default
                .Sql($"SELECT COUNT(*) FROM perf.BulkTest WHERE BatchNumber = {batchNumber}")
                .ExecuteScalarAsync<int>();
            
            _output.WriteLine($"\n삽입 완료: {insertedCount:N0}행");
            _output.WriteLine($"소요 시간: {sw.Elapsed.TotalSeconds:F2}초");
            
            if (insertedCount > 0)
            {
                _output.WriteLine($"처리량: {insertedCount / sw.Elapsed.TotalSeconds:F0} rows/sec");
            }
            
            // Assert
            Assert.Equal(rowCount, insertedCount);
            Assert.True(sw.Elapsed.TotalSeconds < 10, 
                $"Pipeline이 너무 느림: {sw.Elapsed.TotalSeconds:F2}초 (기준: 10초)");
            
            _output.WriteLine("\n=== Test 완료: Pipeline 성능 검증 ===");
        }
        finally
        {
            // Cleanup
            await _db.Default
                .Sql($"DELETE FROM perf.BulkTest WHERE BatchNumber = {batchNumber}")
                .ExecuteAsync();
            _output.WriteLine("테스트 데이터 정리 완료");
        }
    }

    #endregion

    #region Concurrency Performance

    [Fact]
    public async Task Scenario_25_3_HighConcurrency_ShouldHandleLoad()
    {
        // [목적] 고동시성 환경에서 안정성 검증 (1000 concurrent queries)
        _output.WriteLine("=== High Concurrency Test 시작 ===");
        
        // Arrange
        const int concurrentQueries = 1000;
        _output.WriteLine($"동시 쿼리: {concurrentQueries:N0}개");
        
        // Act
        var sw = Stopwatch.StartNew();
        var tasks = new List<Task<int>>();
        
        for (int i = 0; i < concurrentQueries; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(async () =>
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IDbContext>();
                
                return await db.Default
                    .Sql("SELECT @TaskId AS TaskId")
                    .With(new { TaskId = taskId })
                    .ExecuteScalarAsync<int>();
            }));
        }
        
        var results = await Task.WhenAll(tasks);
        sw.Stop();
        
        _output.WriteLine($"\n완료: {results.Length:N0}개 쿼리");
        _output.WriteLine($"소요 시간: {sw.Elapsed.TotalSeconds:F2}초");
        _output.WriteLine($"QPS: {results.Length / sw.Elapsed.TotalSeconds:F0} queries/sec");
        
        // Assert
        Assert.Equal(concurrentQueries, results.Length);
        Assert.All(results, (result, index) => Assert.Equal(index, result));
        
        _output.WriteLine("\n=== Test 완료: 고동시성 처리 성공 ===");
    }

    #endregion

    #region Query Throughput

    [Fact]
    public async Task Scenario_25_4_QueryThroughput_ShouldMeetBaseline()
    {
        // [목적] 쿼리 처리량 기준선 검증 (100+ queries/sec)
        _output.WriteLine("=== Query Throughput Test 시작 ===");
        
        // Arrange
        const int queryCount = 1_000;
        _output.WriteLine($"쿼리 수: {queryCount:N0}개");
        
        // Act - 순차 실행
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < queryCount; i++)
        {
            await _db.Default
                .Sql("SELECT @Value AS Value")
                .With(new { Value = i })
                .ExecuteScalarAsync<int>();
        }
        
        sw.Stop();
        
        var qps = queryCount / sw.Elapsed.TotalSeconds;
        
        _output.WriteLine($"\n완료: {queryCount:N0}개 쿼리");
        _output.WriteLine($"소요 시간: {sw.Elapsed.TotalSeconds:F2}초");
        _output.WriteLine($"처리량: {qps:F0} queries/sec");
        
        // Assert - 최소 처리량 검증
        Assert.True(qps >= 100, 
            $"처리량이 너무 낮음: {qps:F0} QPS (최소: 100 QPS)");
        
        _output.WriteLine("\n=== Test 완료: 처리량 기준선 충족 ===");
    }

    #endregion
}
