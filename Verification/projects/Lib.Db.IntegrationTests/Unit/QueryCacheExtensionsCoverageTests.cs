// ============================================================================
// 파일: Unit/QueryCacheExtensionsCoverageTests.cs
// 설명: QueryCacheExtensions 캐시 hit/miss/failure 경로 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Lib.Db.Contracts.Core;
using Lib.Db.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class QueryCacheExtensionsCoverageTests
{
    [Fact]
    public async Task WithCacheAsync_ShouldReturnCachedValueWithoutAwaitingResult()
    {
        var cache = new InMemoryDistributedCache();
        await cache.SetAsync("user:1", JsonSerializer.SerializeToUtf8Bytes(new CachedUser(1, "cached")), TestContext.Current.CancellationToken);

        DbResult<CachedUser?> result = await Task
            .FromResult(DbResult<CachedUser?>.Fail(CreateError("should not be observed")))
            .WithCacheAsync(cache, "user:1", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new CachedUser(1, "cached"));
    }

    [Fact]
    public async Task WithCacheAsync_ShouldStoreSuccessfulNonNullResultOnMiss()
    {
        var cache = new InMemoryDistributedCache();

        DbResult<CachedUser?> result = await Task
            .FromResult(DbResult<CachedUser?>.Ok(new CachedUser(2, "fresh")))
            .WithCacheAsync(cache, "user:2", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        result.Value.Should().Be(new CachedUser(2, "fresh"));
        cache.Contains("user:2").Should().BeTrue();
    }

    [Fact]
    public async Task WithCacheAsync_ShouldNotStoreFailuresOrNullValues()
    {
        var cache = new InMemoryDistributedCache();

        DbResult<CachedUser?> failure = await Task
            .FromResult(DbResult<CachedUser?>.Fail(CreateError("failed")))
            .WithCacheAsync(cache, "user:failure", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);
        DbResult<CachedUser?> nullResult = await Task
            .FromResult(DbResult<CachedUser?>.Ok(null))
            .WithCacheAsync(cache, "user:null", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        failure.IsSuccess.Should().BeFalse();
        nullResult.IsSuccess.Should().BeTrue();
        cache.Contains("user:failure").Should().BeFalse();
        cache.Contains("user:null").Should().BeFalse();
    }

    [Fact]
    public async Task WithCacheListAsync_ShouldReturnCachedList()
    {
        var cache = new InMemoryDistributedCache();
        await cache.SetAsync("users", JsonSerializer.SerializeToUtf8Bytes(new List<CachedUser>
        {
            new(1, "cached")
        }), TestContext.Current.CancellationToken);

        DbResult<List<CachedUser>> result = await Task
            .FromResult(DbResult<IAsyncEnumerable<CachedUser>>.Fail(CreateError("should not be observed")))
            .WithCacheListAsync(cache, "users", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(new CachedUser(1, "cached"));
    }

    [Fact]
    public async Task WithCacheListAsync_ShouldReturnEmptyListWhenCachedPayloadIsNull()
    {
        var cache = new InMemoryDistributedCache();
        await cache.SetAsync("users:null", JsonSerializer.SerializeToUtf8Bytes<List<CachedUser>?>(null), TestContext.Current.CancellationToken);

        DbResult<List<CachedUser>> result = await Task
            .FromResult(DbResult<IAsyncEnumerable<CachedUser>>.Fail(CreateError("should not be observed")))
            .WithCacheListAsync(cache, "users:null", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task WithCacheListAsync_ShouldPropagateFailure()
    {
        var cache = new InMemoryDistributedCache();

        DbResult<List<CachedUser>> result = await Task
            .FromResult(DbResult<IAsyncEnumerable<CachedUser>>.Fail(CreateError("stream failed")))
            .WithCacheListAsync(cache, "users:failure", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        cache.Contains("users:failure").Should().BeFalse();
    }

    [Fact]
    public async Task WithCacheListAsync_ShouldMaterializeAndStoreNonEmptyStream()
    {
        var cache = new InMemoryDistributedCache();

        DbResult<List<CachedUser>> result = await Task
            .FromResult(DbResult<IAsyncEnumerable<CachedUser>>.Ok(CreateUsersAsync()))
            .WithCacheListAsync(cache, "users:fresh", TimeSpan.FromMinutes(1), ct: TestContext.Current.CancellationToken);

        result.Value.Should().HaveCount(2);
        cache.Contains("users:fresh").Should().BeTrue();
    }

    [Fact]
    public async Task GetOrQueryAsync_ShouldReturnCachedValueWithoutInvokingFactory()
    {
        var cache = new InMemoryDistributedCache();
        await cache.SetAsync("factory:hit", JsonSerializer.SerializeToUtf8Bytes(new CachedUser(7, "hit")), TestContext.Current.CancellationToken);
        int factoryCalls = 0;

        DbResult<CachedUser?> result = await QueryCacheExtensions.GetOrQueryAsync(
            cache,
            "factory:hit",
            TimeSpan.FromMinutes(1),
            () =>
            {
                factoryCalls++;
                return Task.FromResult(DbResult<CachedUser?>.Ok(new CachedUser(8, "miss")));
            },
            ct: TestContext.Current.CancellationToken);

        result.Value.Should().Be(new CachedUser(7, "hit"));
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetOrQueryAsync_ShouldStoreSuccessfulFactoryResult()
    {
        var cache = new InMemoryDistributedCache();

        DbResult<CachedUser?> result = await QueryCacheExtensions.GetOrQueryAsync(
            cache,
            "factory:miss",
            TimeSpan.FromMinutes(1),
            () => Task.FromResult(DbResult<CachedUser?>.Ok(new CachedUser(9, "fresh"))),
            ct: TestContext.Current.CancellationToken);

        result.Value.Should().Be(new CachedUser(9, "fresh"));
        cache.Contains("factory:miss").Should().BeTrue();
    }

    [Fact]
    public async Task GetOrQueryAsync_ShouldNotStoreFactoryFailuresOrNullValues()
    {
        var cache = new InMemoryDistributedCache();

        DbResult<CachedUser?> failure = await QueryCacheExtensions.GetOrQueryAsync(
            cache,
            "factory:failure",
            TimeSpan.FromMinutes(1),
            () => Task.FromResult(DbResult<CachedUser?>.Fail(CreateError("failed"))),
            ct: TestContext.Current.CancellationToken);
        DbResult<CachedUser?> nullValue = await QueryCacheExtensions.GetOrQueryAsync(
            cache,
            "factory:null",
            TimeSpan.FromMinutes(1),
            () => Task.FromResult(DbResult<CachedUser?>.Ok(null)),
            ct: TestContext.Current.CancellationToken);

        failure.IsSuccess.Should().BeFalse();
        nullValue.IsSuccess.Should().BeTrue();
        cache.Contains("factory:failure").Should().BeFalse();
        cache.Contains("factory:null").Should().BeFalse();
    }

    [Fact]
    public async Task InvalidateCacheAsync_ShouldRemoveKey()
    {
        var cache = new InMemoryDistributedCache();
        await cache.SetAsync("remove-me", [1, 2, 3], TestContext.Current.CancellationToken);

        await cache.InvalidateCacheAsync("remove-me", TestContext.Current.CancellationToken);

        cache.Contains("remove-me").Should().BeFalse();
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldCacheSuccessfulResult()
    {
        using ServiceProvider provider = CreateHybridCacheProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();

        DbResult<CachedUser?> result = await Task
            .FromResult(DbResult<CachedUser?>.Ok(new CachedUser(10, "hybrid")))
            .WithHybridCacheAsync(cache, "hybrid:success", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new CachedUser(10, "hybrid"));
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldThrowWhenResultFails()
    {
        using ServiceProvider provider = CreateHybridCacheProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();

        Func<Task> act = () => Task
            .FromResult(DbResult<CachedUser?>.Fail(CreateError("hybrid failed")))
            .WithHybridCacheAsync(cache, "hybrid:failure", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("hybrid failed");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldUseFallbackMessageWhenErrorMessageIsMissing()
    {
        using ServiceProvider provider = CreateHybridCacheProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();

        Func<Task> act = () => Task
            .FromResult(DbResult<CachedUser?>.Fail(new DbError { Kind = DbErrorKind.Unknown, Message = null! }))
            .WithHybridCacheAsync(cache, "hybrid:failure:fallback", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB 쿼리 실패");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldUseFallbackMessageWhenErrorObjectIsMissing()
    {
        using ServiceProvider provider = CreateHybridCacheProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();
        DbResult<CachedUser?> failureWithoutError = default;

        Func<Task> act = () => Task
            .FromResult(failureWithoutError)
            .WithHybridCacheAsync(cache, "hybrid:failure:null-error", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB 쿼리 실패");
    }

    private static async IAsyncEnumerable<CachedUser> CreateUsersAsync()
    {
        await Task.Yield();
        yield return new CachedUser(1, "a");
        yield return new CachedUser(2, "b");
    }

    private static DbError CreateError(string message)
        => new()
        {
            Kind = DbErrorKind.Unknown,
            Message = message
        };

    private static ServiceProvider CreateHybridCacheProvider()
    {
        ServiceCollection services = new();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private sealed record CachedUser(int Id, string Name);

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _items = new();

        public bool Contains(string key) => _items.ContainsKey(key);

        public byte[]? Get(string key)
            => _items.TryGetValue(key, out byte[]? value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
            => Task.CompletedTask;

        public void Remove(string key)
            => _items.TryRemove(key, out _);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _items[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
