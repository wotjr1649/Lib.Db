// ============================================================================
// 파일: Caching/SharedMemorySecurityTests.cs
// 설명: SharedMemoryMappedCache 동시성 및 무결성 검증
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Hashing;
using System.Text;
using Lib.Db.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lib.Db.IntegrationTests.Caching;

public sealed class SharedMemorySecurityTests : IDisposable
{
    private const string CacheKey = "SecurityTestKey";
    private const int CacheHeaderSize = 64;
    private const int CacheHeaderCrcOffset = 24;
    private const int ProtectedPayloadNonceSize = 12;
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
    public async Task Chaos_Test_Read_While_Writing()
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
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
            }, token));
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
            }, token));
        }

        await Task.Delay(2000, TestContext.Current.CancellationToken);
        cts.Cancel();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }

        Assert.Empty(errors);
    }

    [Fact]
    public void TamperedProtectedPayloadWithUpdatedCrc_ShouldReturnMiss()
    {
        string key = "tamper-payload";
        byte[] original = Encoding.UTF8.GetBytes("trusted-value");
        _cache.Set(key, original, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        string file = GetSingleCacheFile(_mapName);
        byte[] bytes = File.ReadAllBytes(file);
        int protectedPayloadLength = bytes.Length - CacheHeaderSize;
        int firstCiphertextByteOffset = CacheHeaderSize + ProtectedPayloadNonceSize;

        bytes[firstCiphertextByteOffset] ^= 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(CacheHeaderCrcOffset, sizeof(uint)),
            Crc32.HashToUInt32(bytes.AsSpan(CacheHeaderSize, protectedPayloadLength)));
        File.WriteAllBytes(file, bytes);

        Assert.Null(_cache.Get(key));
    }

    [Fact]
    public void TamperedHeader_ShouldReturnMiss()
    {
        string key = "tamper-header";
        byte[] value = Encoding.UTF8.GetBytes("trusted-value");
        _cache.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        string file = GetSingleCacheFile(_mapName);
        byte[] bytes = File.ReadAllBytes(file);

        bytes[7] ^= 0x01;
        File.WriteAllBytes(file, bytes);

        Assert.Null(_cache.Get(key));
    }

    [Fact]
    public void UnrecognizedHeaderVersion_ShouldReturnMiss()
    {
        string key = "tamper-version";
        byte[] value = Encoding.UTF8.GetBytes("trusted-value");
        _cache.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
        string file = GetSingleCacheFile(_mapName);
        byte[] bytes = File.ReadAllBytes(file);

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, sizeof(ushort)), ushort.MaxValue);
        File.WriteAllBytes(file, bytes);

        Assert.Null(_cache.Get(key));
    }

    [Fact]
    public void ValidFile_ShouldRoundTrip()
    {
        string key = "valid-roundtrip";
        byte[] value = Encoding.UTF8.GetBytes("trusted-value");

        _cache.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        Assert.Equal(value, _cache.Get(key));
    }

    [Fact]
    public void CopiedFileWithDifferentIsolationKey_ShouldReturnMiss()
    {
        string root = Path.Combine(Path.GetTempPath(), "LibDb_Isolation_" + Guid.NewGuid().ToString("N"));
        try
        {
            using SharedMemoryCache first = CreateCache(root, "tenant-a");
            using SharedMemoryCache second = CreateCache(root, "tenant-b");
            string key = "copied-cross-isolation";
            byte[] value = Encoding.UTF8.GetBytes("tenant-a-value");

            first.Set(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            string firstFile = GetSingleCacheFile(root);
            string firstDirectory = Path.GetDirectoryName(firstFile)!;
            string secondDirectory = Directory.GetDirectories(root)
                .Single(directory => !StringComparer.OrdinalIgnoreCase.Equals(directory, firstDirectory));
            File.Copy(firstFile, Path.Combine(secondDirectory, Path.GetFileName(firstFile)));

            Assert.Null(second.Get(key));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static SharedMemoryCache CreateCache(string basePath, string isolationKey)
    {
        SharedMemoryCacheOptions options = new()
        {
            BasePath = basePath,
            Scope = CacheScope.User,
            MaxCacheSizeBytes = 10 * 1024 * 1024,
            IsolationKey = isolationKey
        };

        return new SharedMemoryCache(
            Options.Create(options),
            NullLogger<SharedMemoryCache>.Instance);
    }

    private static string GetSingleCacheFile(string basePath)
        => Directory.GetFiles(basePath, "*.cache", SearchOption.AllDirectories).Single();

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
    public void Dispose()
    {
        _cache.Dispose();
    }
}
