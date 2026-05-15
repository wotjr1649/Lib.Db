// ============================================================================
// 파일: Caching/SharedMemorySecurityTests.cs
// 설명: SharedMemoryMappedCache 동시성 및 무결성 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using Lib.Db.Caching;
using Microsoft.Extensions.Caching.Distributed;

namespace Lib.Db.IntegrationTests.Caching;

public sealed class SharedMemorySecurityTests : IDisposable
{
    private const string CacheKey = "SecurityTestKey";
    private readonly SharedMemoryCache _cache;
    private readonly string _mapName;

    public SharedMemorySecurityTests()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"LibDb_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        _mapName = tempPath;

        SharedMemoryCacheOptions options = new()
        {
            BasePath = tempPath,
            Scope = CacheScope.User,
            MaxCacheSizeBytes = 10 * 1024 * 1024,
            IsolationKey = "SecKey"
        };
        _cache = new SharedMemoryCache(
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SharedMemoryCache>.Instance);
    }

    [Fact]
    public void Concurrent_Write_Should_Maintain_Integrity()
    {
        int threadCount = 4;
        int iterations = 1000;
        Barrier barrier = new(threadCount);
        ConcurrentBag<Exception> errors = new();

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                barrier.SignalAndWait();
                for (int j = 0; j < iterations; j++)
                {
                    string value = $"Thread_{i}_Iter_{j}";
                    byte[] data = Encoding.UTF8.GetBytes(value);

                    _cache.Set(CacheKey, data, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        Assert.Empty(errors);

        byte[]? finalData = _cache.Get(CacheKey);
        Assert.NotNull(finalData);
        string finalString = Encoding.UTF8.GetString(finalData);
        Assert.Contains("Thread_", finalString);
    }

    [Fact]
    public void Chaos_Test_Read_While_Writing()
    {
        CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;
        int writerCount = 2;
        int readerCount = 2;

        List<Task> tasks = [];
        ConcurrentBag<Exception> errors = new();

        for (int i = 0; i < writerCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                int j = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        byte[] data = BitConverter.GetBytes(j++);
                        _cache.Set(CacheKey, data, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
                        Thread.Sleep(1);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                        cts.Cancel();
                    }
                }
            }));
        }

        for (int i = 0; i < readerCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        byte[]? data = _cache.Get(CacheKey);
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

        Thread.Sleep(2000);
        cts.Cancel();

        try { Task.WaitAll([.. tasks]); } catch { }

        Assert.Empty(errors);
    }

    [Fact]
    public void Activity_Tags_Should_Not_Expose_Raw_Cache_Key()
    {
        const string sensitiveKey = "SecurityTestKey:tenant=alpha;email=secret@example.com";
        List<Activity> activities = [];

        using ActivityListener listener = new()
        {
            ShouldListenTo = static source => source.Name == "Lib.Db.SharedMemoryCache",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity)
        };

        ActivitySource.AddActivityListener(listener);

        _cache.Set(
            sensitiveKey,
            Encoding.UTF8.GetBytes("value"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        _ = _cache.Get(sensitiveKey);

        Activity[] cacheActivities = activities
            .Where(activity => activity.OperationName is "CacheSet" or "CacheGet")
            .ToArray();

        Assert.Contains(cacheActivities, activity => activity.OperationName == "CacheSet");
        Assert.Contains(cacheActivities, activity => activity.OperationName == "CacheGet");

        foreach (Activity activity in cacheActivities)
        {
            KeyValuePair<string, string?>[] tags = activity.Tags.ToArray();

            Assert.Contains(tags, tag => tag.Key == "db.cache.key.summary" && tag.Value is { Length: > 0 });
            Assert.Contains(tags, tag => tag.Key == "libdb.cache.key.hash" && tag.Value is { Length: > 0 });
            Assert.DoesNotContain(tags, tag => tag.Key == "db.cache.key");
            Assert.DoesNotContain(tags, tag => tag.Value is { } value &&
                value.Contains(sensitiveKey, StringComparison.Ordinal));
            Assert.DoesNotContain(tags, tag => tag.Value is { } value &&
                value.Contains("secret@example.com", StringComparison.Ordinal));
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}
