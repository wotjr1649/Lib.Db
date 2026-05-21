// ============================================================================
// 파일: Caching/SharedMemoryMappedCacheTests.cs
// 설명: SharedMemoryMappedCache 단위 테스트 (Set/Get, 만료, CRC, 정리, 폴백)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.IO;
using System.Diagnostics;
using System.Text;
using Lib.Db.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lib.Db.IntegrationTests.Caching;

public sealed class SharedMemoryMappedCacheTests : IDisposable
{
    private readonly string _basePath;

    public SharedMemoryMappedCacheTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "LibDb_Tests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            try { Directory.Delete(_basePath, true); } catch { }
        }
    }

    private SharedMemoryCache CreateCache(string? path = null, bool enableObservability = false)
    {
        SharedMemoryCacheOptions options = new()
        {
            BasePath = path ?? _basePath,
            Scope = CacheScope.User,
            IsolationKey = "TestKey",
            EnableObservability = enableObservability,
            FallbackCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
        };
        return new SharedMemoryCache(
            Options.Create(options),
            NullLogger<SharedMemoryCache>.Instance);
    }

    [Fact]
    public void SM01_Basic_Set_And_Get_ShouldWork()
    {
        using SharedMemoryCache cache = CreateCache();
        string key = "sm01-key";
        byte[] value = Encoding.UTF8.GetBytes("Hello World");

        cache.Set(key, value, new DistributedCacheEntryOptions());
        byte[]? result = cache.Get(key);

        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task SM02_Expiry_ShouldReturnNull_AfterTimePassed()
    {
        using SharedMemoryCache cache = CreateCache();
        string key = "sm02-key";
        byte[] value = [1, 2, 3];

        DistributedCacheEntryOptions options = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100)
        };
        cache.Set(key, value, options);

        Assert.NotNull(cache.Get(key));

        await Task.Delay(200);

        Assert.Null(cache.Get(key));
    }

    [Fact]
    public void SM03_Corruption_ShouldDetected_By_CRC()
    {
        using SharedMemoryCache cache = CreateCache();
        string key = "sm03-key";
        byte[] value = [0xAA, 0xBB, 0xCC];

        cache.Set(key, value, new DistributedCacheEntryOptions());

        string file = Directory.GetFiles(_basePath, "*.cache")[0];

        using (FileStream fs = new(file, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Seek(32, SeekOrigin.Begin);
            fs.WriteByte(0xFF);
        }

        byte[]? result = cache.Get(key);
        Assert.Null(result);
    }

    [Fact]
    public void SM04_Cleanup_ShouldRemove_Expired_Or_Corrupt_Files()
    {
        using SharedMemoryCache cache = CreateCache();

        File.WriteAllBytes(Path.Combine(_basePath, "corrupt.cache"), new byte[10]);

        string keyExpired = "sm04-expired";
        cache.Set(keyExpired, new byte[1], new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1)
        });
        Thread.Sleep(50);

        string keyValid = "sm04-valid";
        cache.Set(keyValid, new byte[1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        cache.Compact();

        string[] files = Directory.GetFiles(_basePath, "*.cache");
        Assert.Single(files);
        Assert.NotNull(cache.Get(keyValid));
        Assert.Null(cache.Get(keyExpired));
    }

    [Fact]
    public void SM05_Fallback_ShouldActivate_On_InitFailure()
    {
        Directory.CreateDirectory(_basePath);
        string invalidPath = Path.Combine(_basePath, "not-a-directory");
        File.WriteAllText(invalidPath, "occupied");

        SharedMemoryCacheOptions options = new()
        {
            BasePath = invalidPath,
            FallbackCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
        };

        using SharedMemoryCache cache = new(
            Options.Create(options),
            NullLogger<SharedMemoryCache>.Instance);

        Assert.True(cache.IsFallbackMode);
        Assert.Equal("fallback", cache.CacheMode);

        string key = "fallback-key";
        byte[] val = [0x99];

        cache.Set(key, val, new DistributedCacheEntryOptions());
        byte[]? res = cache.Get(key);

        Assert.NotNull(res);
        Assert.Equal(val, res);
    }

    [Fact]
    public async Task SM06_AsyncMethods_ShouldReturnCanceledTask_ForPreCanceledToken()
    {
        using SharedMemoryCache cache = CreateCache();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        CancellationToken token = cts.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.GetAsync("sm06-key", token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.SetAsync(
                "sm06-key",
                [1, 2, 3],
                new DistributedCacheEntryOptions(),
                token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.RemoveAsync("sm06-key", token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.RefreshAsync("sm06-key", token));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void SM07_ShouldHonorEnableObservabilityAndNeverTagRawCacheKey(bool enabled, int expectedActivityCount)
    {
        using SharedMemoryCache cache = CreateCache(enableObservability: enabled);
        using ActivityCapture capture = new("Lib.Db");
        string key = "tenant:secret-sensitive-cache-key";

        cache.Set(key, [1, 2, 3], new DistributedCacheEntryOptions());
        _ = cache.Get(key);

        capture.StartedCount.Should().Be(expectedActivityCount);
        capture.RawKeyTagCount.Should().Be(0);
        if (enabled)
            capture.KeyHashTagCount.Should().Be(expectedActivityCount);
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public int StartedCount { get; private set; }
        public int RawKeyTagCount { get; private set; }
        public int KeyHashTagCount { get; private set; }

        public ActivityCapture(string sourceName)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    if (activity.OperationName is "CacheGet" or "CacheSet")
                        StartedCount++;
                },
                ActivityStopped = activity =>
                {
                    if (activity.OperationName is not ("CacheGet" or "CacheSet"))
                        return;

                    foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
                    {
                        if (tag.Key == "db.cache.key")
                            RawKeyTagCount++;
                        if (tag.Key == "db.cache.key_hash")
                            KeyHashTagCount++;
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}
