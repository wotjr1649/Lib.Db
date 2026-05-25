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
string userProfileCacheKey = cacheKeys.UserProfile(userId); // opaque app-owned label, not the raw identifier

DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct)
    .WithCacheAsync(cache, userProfileCacheKey, TimeSpan.FromMinutes(5), ct: ct);
```

`WithCacheAsync` is chained after a task has already been created. Use `GetOrQueryAsync` when cache hits must avoid starting the DB call.

## Cache A List From A Stream

```csharp
string customerOrdersCacheKey = cacheKeys.CustomerOrders(customerId); // opaque app-owned label

DbResult<List<OrderDto>> result = await db.Default
    .Procedure("dbo.usp_ListOrders")
    .With(new { CustomerId = customerId })
    .QueryAsync<OrderDto>(ct)
    .WithCacheListAsync(cache, customerOrdersCacheKey, TimeSpan.FromMinutes(2), ct: ct);
```

## Cache-Aside Without Starting DB On Hit

```csharp
DbResult<UserDto?> result = await QueryCacheExtensions.GetOrQueryAsync(
    cache,
    userProfileCacheKey,
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
    .WithHybridCacheAsync(hybridCache, userProfileCacheKey, TimeSpan.FromMinutes(5), ct);
```

Use the tag overload for grouped logical invalidation:

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>(ct)
    .WithHybridCacheAsync(
        hybridCache,
        userProfileCacheKey,
        TimeSpan.FromMinutes(5),
        tags: ["entity:user-profile", "schema:user-profile"],
        ct);
```

## Invalidate

```csharp
await cache.InvalidateCacheAsync(userProfileCacheKey, ct);
await hybridCache.RemoveByTagAsync("entity:user-profile", ct);
```

## Cache Key Safety

- Do not put secrets, connection strings, SQL text, row values, cache payloads, or raw tenant/user identifiers in cache keys or tags.
- Include tenant, user, culture, and authorization dimensions only through opaque application-owned labels, such as a keyed hash or lookup key that cannot reveal the original identifier.
- HybridCache entry tags reject null, empty/whitespace, leading/trailing whitespace, wildcard `*`, and more than 32 distinct ordinal tags.
- `RemoveByTagAsync("*")` is a cache-wide logical invalidation operation. Do not use `*` as an entry tag.
- `WithHybridCacheAsync` is chained after a task has already been created. A cache hit cannot prevent task creation side effects or a later task fault.
- HybridCache lookup/provider/serializer/query failures are exposed as generic `DB query failed.` exceptions.
- Be careful with machine-wide shared cache scope; read `connection-security.md`.

## Shared Memory Options

`LibDbOptions.SharedMemoryCache` includes `BasePath`, `Scope`, `MaxCacheSizeBytes`, `FallbackCache`, and `IsolationKey`.

`CacheScope.User` isolates per OS user. `CacheScope.Machine` is machine-wide and needs stronger operational review.
