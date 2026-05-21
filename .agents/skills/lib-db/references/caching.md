# Caching

Use this file for query result caching, HybridCache, shared-memory cache options, and cache key safety.

## Namespaces

```csharp
using Lib.Db.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
```

## Register HybridCache

```csharp
builder.Services.AddLibDbHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});
```

When runtime dynamic code is unavailable, Lib.Db registers its AOT-compatible HybridCache implementation.

## Cache A Single Result

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct)
    .WithCacheAsync(cache, $"user:{userId}", TimeSpan.FromMinutes(5), ct: ct);
```

`WithCacheAsync` is chained after a task has already been created. Use `GetOrQueryAsync` when cache hits must avoid starting the DB call.

## Cache A List From A Stream

```csharp
DbResult<List<OrderDto>> result = await db.Default
    .Procedure("dbo.usp_ListOrders")
    .With(new { CustomerId = customerId })
    .QueryAsync<OrderDto>(ct)
    .WithCacheListAsync(cache, $"orders:{customerId}", TimeSpan.FromMinutes(2), ct: ct);
```

## Cache-Aside Without Starting DB On Hit

```csharp
DbResult<UserDto?> result = await QueryCacheExtensions.GetOrQueryAsync(
    cache,
    $"user:{userId}",
    TimeSpan.FromMinutes(5),
    () => db.Default
        .Procedure("dbo.usp_GetUser")
        .With(new { UserId = userId })
        .QuerySingleAsync<UserDto>(ct),
    ct: ct);
```

## HybridCache Result

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct)
    .WithHybridCacheAsync(hybridCache, $"user:{userId}", TimeSpan.FromMinutes(5), ct);
```

## Invalidate

```csharp
await cache.InvalidateCacheAsync($"user:{userId}", ct);
```

## Cache Key Safety

- Do not put secrets or full connection strings in cache keys.
- Include tenant, user, culture, and authorization dimensions when they affect results.
- Prefer stable IDs over raw user input.
- Be careful with machine-wide shared cache scope; read `connection-security.md`.

## Shared Memory Options

`LibDbOptions.SharedMemoryCache` includes `BasePath`, `Scope`, `MaxCacheSizeBytes`, `FallbackCache`, and `IsolationKey`.

`CacheScope.User` isolates per OS user. `CacheScope.Machine` is machine-wide and needs stronger operational review.
