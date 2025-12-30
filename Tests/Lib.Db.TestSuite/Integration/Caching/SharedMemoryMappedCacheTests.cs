using System;
using System.IO;
using System.IO.Hashing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lib.Db.Caching;
using Lib.Db.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lib.Db.Verification.Tests.Caching;

public class SharedMemoryMappedCacheTests : IDisposable
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

    private SharedMemoryCache CreateCache(string? path = null)
    {
        var options = new SharedMemoryCacheOptions
        {
            BasePath = path ?? _basePath,
            Scope = CacheScope.User,
            IsolationKey = "TestKey",
            FallbackCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
        };
        return new SharedMemoryCache(
            Options.Create(options),
            NullLogger<SharedMemoryCache>.Instance);
    }

    [Fact]
    public void SM01_Basic_Set_And_Get_ShouldWork()
    {
        using var cache = CreateCache();
        var key = "sm01-key";
        var value = Encoding.UTF8.GetBytes("Hello World");
        
        cache.Set(key, value, new DistributedCacheEntryOptions());
        var result = cache.Get(key);

        Assert.NotNull(result);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task SM02_Expiry_ShouldReturnNull_AfterTimePassed()
    {
        using var cache = CreateCache();
        var key = "sm02-key";
        var value = new byte[] { 1, 2, 3 };

        // 1. Set with short expiry (100ms)
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100)
        };
        cache.Set(key, value, options);

        // 2. Verify immediate retrieval
        Assert.NotNull(cache.Get(key));

        // 3. Wait 200ms
        await Task.Delay(200);

        // 4. Verify miss
        Assert.Null(cache.Get(key));
    }

    [Fact]
    public void SM03_Corruption_ShouldDetected_By_CRC()
    {
        using var cache = CreateCache();
        var key = "sm03-key";
        var value = new byte[] { 0xAA, 0xBB, 0xCC };

        cache.Set(key, value, new DistributedCacheEntryOptions());

        // Locate the file and corrupt it
        // We rely on GetInternal implementation detail: "cache_{hex}.mmf"
        // But simpler: just find ANY .mmf in the folder
        var file = Directory.GetFiles(_basePath, "*.cache")[0];

        using (var fs = new FileStream(file, FileMode.Open, FileAccess.ReadWrite))
        {
            // Skip Header (32 bytes) and corrupt data
            // Header Layout: 32 bytes.
            fs.Seek(32, SeekOrigin.Begin);
            fs.WriteByte(0xFF); // Corrupt first byte of data
        }

        // Verify Get returns null due to CRC mismatch
        var result = cache.Get(key);
        Assert.Null(result);
    }

    [Fact]
    public void SM04_Cleanup_ShouldRemove_Expired_Or_Corrupt_Files()
    {
        using var cache = CreateCache();
        
        // 1. Create a corrupt/empty file
        File.WriteAllBytes(Path.Combine(_basePath, "corrupt.cache"), new byte[10]); 

        // 2. Create an expired file (Manual Header construction required or use Cache with 1ms expiry and wait)
        var keyExpired = "sm04-expired";
        cache.Set(keyExpired, new byte[1], new DistributedCacheEntryOptions
        {
             AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1)
        });
        Thread.Sleep(50); // Ensure expired

        // 3. Creates a valid file
        var keyValid = "sm04-valid";
        cache.Set(keyValid, new byte[1], new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });

        // 4. Run Cleanup
        cache.Compact();

        // 5. Verify
        var files = Directory.GetFiles(_basePath, "*.cache");
        // Should contain only VALID file. The valid file name is hash based.
        // We just assert that we have 1 file (the valid one) and NOT the corrupt one.
        // But actual file count check is safer.
        // corrupt.mmf should be gone (IsExpired logic: length < 32 -> true)
        // expired file should be gone.
        
        Assert.Single(files);
        Assert.NotNull(cache.Get(keyValid));
        Assert.Null(cache.Get(keyExpired)); 
    }

    [Fact]
    public void SM05_Fallback_ShouldActivate_On_InitFailure()
    {
        // Use invalid path to trigger exception in Directory.CreateDirectory
        var invalidPath = Path.Combine(_basePath, "in<valid>name"); 
        
 
        
        // Pass FallbackCache explicitly for this test
        var options = new SharedMemoryCacheOptions
        {
            BasePath = invalidPath,
            FallbackCache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))
        };
        
        using var cache = new SharedMemoryCache(
            Options.Create(options), 
            NullLogger<SharedMemoryCache>.Instance);
        
        // Should not throw, should log error and use memory fallback
        var key = "fallback-key";
        var val = new byte[] { 0x99 };
        
        cache.Set(key, val, new DistributedCacheEntryOptions());
        var res = cache.Get(key);
        
        Assert.NotNull(res);
        Assert.Equal(val, res);
        
        // Verify NO file created in invalid path (obviously) or base path
        // BasePath itself might not exist if creation failed.
    }
}

