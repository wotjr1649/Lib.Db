// ============================================================================
// 파일: Unit/QueryCacheExtensionsCoverageTests.cs
// 설명: QueryCacheExtensions 캐시 hit/miss/failure 경로 단위 테스트
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Lib.Db.Caching;
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
    public async Task WithHybridCacheAsync_ShouldPreserveDefaultLiteralCancellationTokenCall()
    {
        using ServiceProvider provider = CreateHybridCacheProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();

#pragma warning disable xUnit1051 // This regression test intentionally preserves the default literal cancellation token call.
        DbResult<CachedUser?> result = await Task
            .FromResult(DbResult<CachedUser?>.Ok(new CachedUser(11, "default-ct")))
            .WithHybridCacheAsync(cache, "hybrid:default-ct", TimeSpan.FromMinutes(1), default);
#pragma warning restore xUnit1051

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new CachedUser(11, "default-ct"));
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldStoreHybridCacheEntryWithTags()
    {
        var cache = new LibDbAotHybridCache();
        int queryCalls = 0;

        Task<DbResult<string?>> Query()
        {
            queryCalls++;
            return Task.FromResult(DbResult<string?>.Ok("before"));
        }

        DbResult<string?> first = await Query()
            .WithHybridCacheAsync(
                cache,
                "hybrid:tagged",
                TimeSpan.FromMinutes(5),
                tags: ["product", "tenant:hash"],
                TestContext.Current.CancellationToken);

        await cache.RemoveByTagAsync("product", TestContext.Current.CancellationToken);

        DbResult<string?> second = await Task.FromResult(DbResult<string?>.Ok("after"))
            .WithHybridCacheAsync(
                cache,
                "hybrid:tagged",
                TimeSpan.FromMinutes(5),
                tags: ["product", "tenant:hash"],
                TestContext.Current.CancellationToken);

        first.Value.Should().Be("before");
        second.Value.Should().Be("after");
        queryCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" tag")]
    [InlineData("tag ")]
    [InlineData("*")]
    public async Task WithHybridCacheAsync_ShouldRejectInvalidTags(string? tag)
    {
        var cache = new LibDbAotHybridCache();
        string[] tags = tag is null ? [null!] : [tag];

        Func<Task> act = () => Task.FromResult(DbResult<string?>.Ok("value"))
            .WithHybridCacheAsync(
                cache,
                "hybrid:invalid-tag",
                TimeSpan.FromMinutes(1),
                tags,
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldTreatNullTagsAsNoTags()
    {
        var cache = new LibDbAotHybridCache();

        DbResult<string?> result = await Task.FromResult(DbResult<string?>.Ok("value"))
            .WithHybridCacheAsync(
                cache,
                "hybrid:null-tags",
                TimeSpan.FromMinutes(1),
                tags: null,
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("value");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldRejectTooManyDistinctTags()
    {
        var cache = new LibDbAotHybridCache();
        string[] tags = Enumerable.Range(0, 33)
            .Select(static index => $"tag:{index}")
            .ToArray();

        Func<Task> act = () => Task.FromResult(DbResult<string?>.Ok("value"))
            .WithHybridCacheAsync(
                cache,
                "hybrid:too-many-tags",
                TimeSpan.FromMinutes(1),
                tags,
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*32*tags*");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldCountDistinctTagsWhenEnforcingLimit()
    {
        var cache = new LibDbAotHybridCache();
        string[] tags = Enumerable.Range(0, 32)
            .Select(static index => $"tag:{index}")
            .Concat(["tag:0", "tag:0"])
            .ToArray();

        DbResult<string?> result = await Task.FromResult(DbResult<string?>.Ok("value"))
            .WithHybridCacheAsync(
                cache,
                "hybrid:duplicate-tags",
                TimeSpan.FromMinutes(1),
                tags,
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldThrowGenericMessageWhenResultFails()
    {
        var cache = new LibDbAotHybridCache();

        Func<Task> act = () => Task
            .FromResult(DbResult<CachedUser?>.Fail(CreateError("hybrid failed: SELECT * FROM dbo.SecretTenant")))
            .WithHybridCacheAsync(
                cache,
                "hybrid:failure",
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        InvalidOperationException exception = (await act.Should()
            .ThrowAsync<InvalidOperationException>()).Which;

        exception.Message.Should().Be("DB query failed.");
        exception.Message.Should().NotContain("hybrid failed");
        exception.Message.Should().NotContain("SecretTenant");
        exception.Message.Should().NotContain("SELECT");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldThrowGenericMessageWhenDefaultHybridCacheResultFails()
    {
        using ServiceProvider provider = CreateHybridCacheProvider();
        HybridCache cache = provider.GetRequiredService<HybridCache>();

        Func<Task> act = () => Task
            .FromResult(DbResult<CachedUser?>.Fail(CreateError("raw row value: SELECT * FROM dbo.SecretTenant")))
            .WithHybridCacheAsync(
                cache,
                "hybrid:default-provider-failure",
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        InvalidOperationException exception = (await act.Should()
            .ThrowAsync<InvalidOperationException>()).Which;

        exception.Message.Should().Be("DB query failed.");
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain("raw row value");
        exception.ToString().Should().NotContain("SecretTenant");
        exception.ToString().Should().NotContain("SELECT");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldThrowGenericMessageWhenResultTaskFaults()
    {
        var cache = new LibDbAotHybridCache();
        InvalidOperationException rawFailure = new("raw provider failure: SELECT * FROM dbo.SecretTenant");

        Func<Task> act = () => Task
            .FromException<DbResult<CachedUser?>>(rawFailure)
            .WithHybridCacheAsync(
                cache,
                "hybrid:faulted",
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        InvalidOperationException exception = (await act.Should()
            .ThrowAsync<InvalidOperationException>()).Which;

        exception.Message.Should().Be("DB query failed.");
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain("raw provider failure");
        exception.ToString().Should().NotContain("SecretTenant");
        exception.ToString().Should().NotContain("SELECT");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldThrowGenericMessageWhenHybridCacheProviderFails()
    {
        HybridCache cache = new ThrowingHybridCache(
            new InvalidOperationException("cache payload leak for UserId=123 in dbo.SecretTenant"));

        Func<Task> act = () => Task
            .FromResult(DbResult<CachedUser?>.Ok(new CachedUser(12, "provider-failure")))
            .WithHybridCacheAsync(
                cache,
                "hybrid:provider-failure",
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        InvalidOperationException exception = (await act.Should()
            .ThrowAsync<InvalidOperationException>()).Which;

        exception.Message.Should().Be("DB query failed.");
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain("cache payload");
        exception.ToString().Should().NotContain("UserId=123");
        exception.ToString().Should().NotContain("SecretTenant");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldPreserveCancellationWhenResultTaskCancels()
    {
        var cache = new LibDbAotHybridCache();
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => Task
            .FromCanceled<DbResult<CachedUser?>>(cts.Token)
            .WithHybridCacheAsync(
                cache,
                "hybrid:canceled",
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldUseFallbackMessageWhenErrorMessageIsMissing()
    {
        var cache = new LibDbAotHybridCache();

        Func<Task> act = () => Task
            .FromResult(DbResult<CachedUser?>.Fail(new DbError { Kind = DbErrorKind.Unknown, Message = null! }))
            .WithHybridCacheAsync(cache, "hybrid:failure:fallback", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB query failed.");
    }

    [Fact]
    public async Task WithHybridCacheAsync_ShouldUseFallbackMessageWhenErrorObjectIsMissing()
    {
        var cache = new LibDbAotHybridCache();
        DbResult<CachedUser?> failureWithoutError = default;

        Func<Task> act = () => Task
            .FromResult(failureWithoutError)
            .WithHybridCacheAsync(cache, "hybrid:failure:null-error", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("DB query failed.");
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

    private sealed class ThrowingHybridCache(Exception exception) : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> underlyingDataCallback,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
            => throw exception;

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
            => throw exception;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

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
