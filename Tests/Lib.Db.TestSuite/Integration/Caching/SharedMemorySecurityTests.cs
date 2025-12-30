// ============================================================================
// SharedMemorySecurityTests.cs
// 목적: 공유 메모리(SharedMemoryMappedCache)의 동시성 및 무결성 검증
// 시나리오: 다중 스레드/태스크 경합 상황에서의 읽기/쓰기 안전성 확인
// ============================================================================

using Lib.Db.Caching;
using System.Security.Cryptography;
using System.Text;

namespace Lib.Db.Verification.Tests.Caching;

public class SharedMemorySecurityTests : IDisposable
{
    private const string CacheKey = "SecurityTestKey";
    private readonly SharedMemoryCache _cache;
    private readonly string _mapName;

    public SharedMemorySecurityTests()
    {
        // Use a unique temp path for isolation
        var tempPath = Path.Combine(Path.GetTempPath(), $"LibDb_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        _mapName = tempPath; // Using _mapName field to store path for Dispose/Cleanup if needed

        var options = new SharedMemoryCacheOptions 
        { 
            BasePath = tempPath,
            Scope = CacheScope.User,
            MaxCacheSizeBytes = 10 * 1024 * 1024, // 10MB
            IsolationKey = "SecKey"
        };
        _cache = new SharedMemoryCache(
            Microsoft.Extensions.Options.Options.Create(options), 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SharedMemoryCache>.Instance);
    }

    [Fact]
    public void Concurrent_Write_Should_Maintain_Integrity()
    {
        // Arrange
        int threadCount = 4; // User requested 4 "processes" - simulating with threads for Unit Test
        int iterations = 1000;
        var barrier = new Barrier(threadCount);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act
        Parallel.For(0, threadCount, i =>
        {
            try
            {
                barrier.SignalAndWait(); // Start simultaneously
                for (int j = 0; j < iterations; j++)
                {
                    string value = $"Thread_{i}_Iter_{j}";
                    byte[] data = Encoding.UTF8.GetBytes(value);
                    
                    _cache.Set(CacheKey, data, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
                    
                    // Immediate Verification not possible due to race, but structure must remain valid
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        // Assert
        Assert.Empty(errors);
        
        // Final logical check
        var finalData = _cache.Get(CacheKey);
        Assert.NotNull(finalData);
        string finalString = Encoding.UTF8.GetString(finalData);
        Assert.Contains("Thread_", finalString); // Should contain data from one of the threads
    }

    [Fact]
    public void Chaos_Test_Read_While_Writing()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        int writerCount = 2;
        int readerCount = 2; // Total 4 tasks
        
        var tasks = new List<Task>();
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act
        // Writers
        for (int i = 0; i < writerCount; i++)
        {
            int writerId = i;
            tasks.Add(Task.Run(() =>
            {
                int j = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        byte[] data = BitConverter.GetBytes(j++);
                        _cache.Set(CacheKey, data, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
                        Thread.Sleep(1); // Yield
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        cts.Cancel();
                    }
                }
            }));
        }

        // Readers
        for (int i = 0; i < readerCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var data = _cache.Get(CacheKey);
                        if (data != null && data.Length != 4)
                        {
                            throw new Exception("Data Corruption Detected: Length mismatch");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        cts.Cancel();
                    }
                }
            }));
        }

        // Run for a duration
        Thread.Sleep(2000);
        cts.Cancel();
        
        try { Task.WaitAll(tasks.ToArray()); } catch { /* Ignore cancellation */ }

        // Assert
        Assert.Empty(errors);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
