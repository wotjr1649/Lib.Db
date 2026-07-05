// ============================================================================
// 파일: Caching/SharedMemoryMappedCacheTests.cs
// 설명: SharedMemoryMappedCache 단위 테스트 (Set/Get, 만료, CRC, 정리, 폴백)
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Buffers.Binary;
using System.IO;
using System.Diagnostics;
using System.IO.Hashing;
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

    private SharedMemoryCache CreateCache(
        string? path = null,
        bool enableObservability = false,
        string? isolationKey = "TestKey",
        CacheScope scope = CacheScope.User,
        long? maxCacheSizeBytes = null,
        IDistributedCache? fallbackCache = null,
        bool useFallbackCache = true)
    {
        SharedMemoryCacheOptions options = new()
        {
            BasePath = path ?? _basePath,
            Scope = scope,
            IsolationKey = isolationKey,
            MaxCacheSizeBytes = maxCacheSizeBytes ?? 1024L * 1024L * 1024L,
            EnableObservability = enableObservability,
            FallbackCache = useFallbackCache
                ? fallbackCache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
                : null
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

        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Null(cache.Get(key));
    }

    [Fact]
    public void SM03_Corruption_ShouldDetected_By_CRC()
    {
        using SharedMemoryCache cache = CreateCache();
        string key = "sm03-key";
        byte[] value = [0xAA, 0xBB, 0xCC];

        cache.Set(key, value, new DistributedCacheEntryOptions());

        string file = Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)[0];

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

        string keyExpired = "sm04-expired";
        cache.Set(keyExpired, new byte[1], new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1)
        });
        Thread.Sleep(50);

        string keyValid = "sm04-valid";
        cache.Set(keyValid, new byte[1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        string activeDirectory = Path.GetDirectoryName(
            Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)[0])!;
        File.WriteAllBytes(Path.Combine(activeDirectory, "corrupt.cache"), new byte[10]);

        cache.Compact();

        string[] files = Directory.GetFiles(activeDirectory, "*.cache");
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

    [Fact]
    public void SM08_IsolationKey_ShouldPartitionStorageWithinSameBasePath()
    {
        using SharedMemoryCache first = CreateCache(isolationKey: "tenant-a");
        using SharedMemoryCache second = CreateCache(isolationKey: "tenant-b");
        string key = "sm08-key";
        byte[] value = Encoding.UTF8.GetBytes("tenant-a-value");

        first.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        second.Get(key).Should().BeNull();
    }

    [Fact]
    public void SM09_Scope_ShouldPartitionStorageWithinSameBasePath()
    {
        using SharedMemoryCache userScoped = CreateCache(isolationKey: "tenant-scope", scope: CacheScope.User);
        using SharedMemoryCache machineScoped = CreateCache(isolationKey: "tenant-scope", scope: CacheScope.Machine);
        string key = "sm09-scope-key";
        byte[] value = Encoding.UTF8.GetBytes("user-scope-value");

        userScoped.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        machineScoped.Get(key).Should().BeNull();
    }

    [Fact]
    public void SM10_SameIsolationKey_ShouldShareStorageWithinSameBasePath()
    {
        using SharedMemoryCache first = CreateCache(isolationKey: "tenant-shared");
        using SharedMemoryCache second = CreateCache(isolationKey: "tenant-shared");
        string key = "sm10-key";
        byte[] value = Encoding.UTF8.GetBytes("shared-value");

        first.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        byte[]? result = second.Get(key);
        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public void SM11_StoragePath_ShouldUseSafeNamespaceDirectory()
    {
        const string isolationKey = "tenant-a-sensitive-marker";
        using SharedMemoryCache cache = CreateCache(isolationKey: isolationKey);

        cache.Set("sm11-key", [7, 8, 9], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        string file = Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Should()
            .ContainSingle()
            .Subject;
        string relativePath = Path.GetRelativePath(_basePath, file);

        Path.GetDirectoryName(relativePath).Should().NotBeNullOrWhiteSpace();
        relativePath.Should().NotContain("tenant-a-sensitive-marker");
        relativePath.Should().NotContain("sensitive-marker");
    }

    [Fact]
    public void SM12_StoragePath_ShouldUseLongIsolationHash()
    {
        using SharedMemoryCache cache = CreateCache(isolationKey: "tenant-long-hash-check");

        cache.Set("sm12-key", [1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        string file = Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Should()
            .ContainSingle()
            .Subject;
        string directoryName = Path.GetFileName(Path.GetDirectoryName(file))!;

        directoryName.Should().NotBeNullOrWhiteSpace();
        directoryName!.Split('_').Last().Should().HaveLength(32);
    }

    [Fact]
    public void SM13_OversizedEntry_ShouldDropWithoutCreatingSharedOrFallbackEntry()
    {
        var fallback = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using SharedMemoryCache cache = CreateCache(maxCacheSizeBytes: 1024 * 1024, fallbackCache: fallback);
        string key = "sm13-oversized";
        byte[] value = new byte[1024 * 1024];

        cache.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories).Should().BeEmpty();
        fallback.Get(key).Should().BeNull();
        cache.Get(key).Should().BeNull();
    }

    [Fact]
    public void SM14_ManyEntries_ShouldNotGrowNamespaceBeyondMaxCacheSize()
    {
        using SharedMemoryCache cache = CreateCache(
            maxCacheSizeBytes: 1024 * 1024,
            useFallbackCache: false);
        byte[] value = new byte[400 * 1024];

        cache.Set("sm14-a", value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        cache.Set("sm14-b", value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        cache.Set("sm14-c", value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length)
            .Should()
            .BeLessThanOrEqualTo(1024 * 1024);
        cache.Get("sm14-a").Should().NotBeNull();
        cache.Get("sm14-b").Should().NotBeNull();
        cache.Get("sm14-c").Should().BeNull();
    }

    [Fact]
    public void SM15_AggregateQuotaRejection_ShouldNotBypassToFallback()
    {
        var fallback = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using SharedMemoryCache cache = CreateCache(maxCacheSizeBytes: 1024 * 1024, fallbackCache: fallback);
        byte[] value = new byte[400 * 1024];

        cache.Set("sm15-a", value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        cache.Set("sm15-b", value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        cache.Set("sm15-c", value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        fallback.Get("sm15-c").Should().BeNull();
        cache.Get("sm15-a").Should().NotBeNull();
        cache.Get("sm15-b").Should().NotBeNull();
        cache.Get("sm15-c").Should().BeNull();
        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length)
            .Should()
            .BeLessThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public async Task SM16_ConcurrentMultiKeyWrites_ShouldNotGrowNamespaceBeyondMaxCacheSize()
    {
        using SharedMemoryCache cache = CreateCache(
            maxCacheSizeBytes: 1024 * 1024,
            useFallbackCache: false);
        byte[] value = new byte[400 * 1024];
        CancellationToken token = TestContext.Current.CancellationToken;
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] writes = Enumerable.Range(0, 8)
            .Select(index => Task.Run(async () =>
            {
                await start.Task.WaitAsync(token);
                cache.Set($"sm16-concurrent-{index}", value, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                });
            }, token))
            .ToArray();

        start.SetResult();
        await Task.WhenAll(writes);

        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length)
            .Should()
            .BeLessThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public void SM17_QuotaRejectedReplacement_ShouldRemoveExistingSharedEntry()
    {
        var fallback = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using SharedMemoryCache cache = CreateCache(maxCacheSizeBytes: 1024 * 1024, fallbackCache: fallback);
        string key = "sm17-replace";
        byte[] original = Encoding.UTF8.GetBytes("existing-shared-value");
        byte[] oversized = new byte[1024 * 1024];
        cache.Set(key, original, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        fallback.Set(key, Encoding.UTF8.GetBytes("stale-fallback-value"), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });

        cache.Set(key, oversized, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        fallback.Get(key).Should().BeNull();
        cache.Get(key).Should().BeNull();
        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void SM18_ExpiredOrCorruptEntries_ShouldBeCompactedBeforeQuotaDrop()
    {
        using SharedMemoryCache cache = CreateCache(maxCacheSizeBytes: 1024 * 1024, useFallbackCache: false);
        cache.Set("sm18-expired", new byte[700 * 1024], new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(-1)
        });
        string activeDirectory = Path.GetDirectoryName(
            Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)[0])!;
        string corruptFile = Path.Combine(activeDirectory, "quota-corrupt.cache");
        File.WriteAllBytes(corruptFile, new byte[400 * 1024]);

        cache.Set("sm18-fresh", new byte[700 * 1024], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        cache.Get("sm18-fresh").Should().NotBeNull();
        cache.Get("sm18-expired").Should().BeNull();
        File.Exists(corruptFile).Should().BeFalse();
        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length)
            .Should()
            .BeLessThanOrEqualTo(1024 * 1024);
    }

    [Fact]
    public void SM19_HeaderValidCorruptEntry_ShouldBeCompactedBeforeQuotaDrop()
    {
        using SharedMemoryCache cache = CreateCache(maxCacheSizeBytes: 1024 * 1024, useFallbackCache: false);
        cache.Set("sm19-corrupt", new byte[700 * 1024], new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        });
        string corruptFile = Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories).Single();
        byte[] bytes = File.ReadAllBytes(corruptFile);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(corruptFile, bytes);

        cache.Set("sm19-fresh", new byte[700 * 1024], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        cache.Get("sm19-fresh").Should().NotBeNull();
        cache.Get("sm19-corrupt").Should().BeNull();
        File.Exists(corruptFile).Should().BeFalse();
        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length)
            .Should()
            .BeLessThanOrEqualTo(1024 * 1024);
    }
    [Fact]
    public void SM20_OversizedEntryWithoutFallback_ShouldDropSafely()
    {
        using SharedMemoryCache cache = CreateCache(
            maxCacheSizeBytes: 1024 * 1024,
            useFallbackCache: false);
        string key = "sm20-drop";
        byte[] value = new byte[1024 * 1024];

        cache.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        Directory.GetFiles(_basePath, "*.cache", SearchOption.AllDirectories).Should().BeEmpty();
        cache.Get(key).Should().BeNull();
    }

    [Fact]
    public void SM21_LegacyFlatCacheFile_ShouldBeTreatedAsMiss()
    {
        string key = "sm21-legacy-flat";
        WriteLegacyFlatCacheFile(_basePath, key, [0x42, 0x24]);
        using SharedMemoryCache cache = CreateCache(isolationKey: "tenant-new-storage");

        cache.Get(key).Should().BeNull();
    }

    private static void WriteLegacyFlatCacheFile(string basePath, string key, byte[] value)
    {
        Directory.CreateDirectory(basePath);
        string filePath = Path.Combine(basePath, GetLegacyFileName(key));
        Span<byte> header = stackalloc byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], 0x4244424C);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..6], 1);
        header[6] = 1;
        BinaryPrimitives.WriteInt64LittleEndian(header[8..16], DateTime.UtcNow.AddMinutes(5).Ticks);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..24], value.LongLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..28], Crc32.HashToUInt32(value));
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..32], Crc32.HashToUInt32(Encoding.UTF8.GetBytes(key)));

        using FileStream stream = new(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(header);
        stream.Write(value);
    }

    private static string GetLegacyFileName(string key)
    {
        byte[] hashBytes = XxHash128.Hash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes).ToLowerInvariant() + ".cache";
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
