# Lib.Db v2.4.0 Provider-Neutral Caching Design

Date: 2026-05-21
Status: Draft
Scope: Lib.Db core caching, schema cache coordination, optional shared-memory cache, and provider-backed L2 behavior

## Context

Lib.Db v2.3.0 includes several cache layers:

- `HybridSchemaSnapshot`: in-process schema snapshot with L1 `FrozenDictionary` and L2 `ConcurrentDictionary`
- `HybridCache`: schema payload cache and user-facing query cache helpers
- `IDistributedCache`: query cache extension surface and optional HybridCache secondary storage
- `SharedMemoryCache`: Lib.Db-owned file/MMF/named-mutex implementation of `IDistributedCache`
- `EpochStore` / `SchemaFlushService`: process-to-process schema invalidation coordination
- `ProcessSlotAllocator`: local process slot and leader coordination for shared-memory scenarios

The current design is high-performance on Windows-local multi-process deployments, but it makes Lib.Db core responsible for OS-specific IPC, local file cache security, named mutex semantics, cache cleanup, and cross-process coordination. That responsibility is larger than a provider-neutral SQL Server data-access library should own.

Official platform notes checked on 2026-05-21:

- Microsoft `HybridCache` uses a configured `IDistributedCache` as secondary L2 storage. Without an `IDistributedCache`, it still provides in-process caching and stampede protection.
- Microsoft distributed cache guidance treats Redis, SQL Server, Postgres, NCache, and distributed memory cache as provider choices behind `IDistributedCache`.
- Named `MemoryMappedFile.CreateOrOpen` APIs carry Windows platform support annotations.
- Microsoft `Mutex` docs warn that named mutexes are filesystem-backed on Unix-like systems, can be interfered with by other users, and currently cannot be access-restricted there in the same way as Windows.

Reference links:

- HybridCache library in ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid
- Distributed caching in ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed
- `IDistributedCache` API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.idistributedcache
- `MemoryDistributedCache` API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.memorydistributedcache
- `MemoryMappedFile` API: https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile
- Memory-mapped files overview: https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files

## Problem

Lib.Db wants to be a general-purpose library that offers the same API and semantics on Windows, Linux, and macOS. The current shared-memory cache conflicts with that goal in four ways.

First, OS behavior differs. `EnableSharedMemoryCache = null` enables shared memory on Windows but disables it on Linux/macOS. This is operationally honest, but it means a default Lib.Db install does not have the same cache topology on every OS.

Second, security responsibility is too broad. Shared-memory caching stores payloads as files under a configurable base path, uses named mutexes, deletes expired files, and hashes connection strings for isolation. These are not merely library implementation details; they are local host security decisions.

Third, failure modes are subtle. If named mutex creation fails and the implementation falls back to unnamed mutexes, the code may still use the same shared cache files while synchronization becomes process-local. That can corrupt cache data or produce stale reads.

Fourth, implicit fallback can misrepresent topology. Registering `MemoryDistributedCache` as the default `IDistributedCache` makes APIs see an L2-like service, but it is only in-process memory. That can create duplicate local caching and can mislead users into believing they have cross-process or cross-host L2.

## Goals

- Make Lib.Db core OS-neutral by default.
- Keep high-value in-process schema caching.
- Use `HybridCache` as the primary cache API.
- Use provider-backed `IDistributedCache` only when the application explicitly registers a real provider.
- Avoid registering `MemoryDistributedCache` as an implicit L2 for production-style defaults.
- Keep query result caching opt-in and caller-owned.
- Preserve a migration path for Windows users who benefit from local shared-memory cache.
- Make cache topology observable without printing secrets or connection strings.

## Non-Goals

- Do not build a new cross-platform distributed cache engine inside Lib.Db.
- Do not make Redis, SQL Server, Postgres, or NCache a hard dependency.
- Do not automatically cache all query results.
- Do not promise cross-host invalidation for in-process caches.
- Do not treat shared-memory cache as a security boundary.

## Design Summary

Lib.Db v2.4.0 should move to a provider-neutral cache model:

1. Core schema caching is always local and OS-neutral.
2. `HybridCache` is always available for schema cache and user-facing helpers.
3. Provider-backed L2 is available only when a real `IDistributedCache` provider is registered by the host application.
4. If no provider exists, Lib.Db uses L1-only `HybridCache` behavior and reports topology as `LocalOnly`.
5. `SharedMemoryCache` is removed from the default core registration path and moved behind explicit opt-in.
6. Shared-memory support is documented as Windows-local optimization or experimental cross-platform adapter, not as default general-purpose L2.

## Proposed Cache Topologies

### LocalOnly

Used when no provider-backed `IDistributedCache` exists.

Behavior:

- `HybridCache` primary local cache is active.
- `HybridSchemaSnapshot` remains active.
- Query cache helpers that take `HybridCache` work with local cache and stampede protection.
- No cross-process, cross-container, or cross-host L2 is claimed.
- Diagnostics expose `LibDbCacheTopology.LocalOnly`.

This is the safest default.

### ProviderBackedL2

Used when the application explicitly registers a supported `IDistributedCache` provider before Lib.Db cache registration.

Examples:

- Redis via `Microsoft.Extensions.Caching.StackExchangeRedis`
- SQL Server via `Microsoft.Extensions.Caching.SqlServer`
- Postgres via `Microsoft.Extensions.Caching.Postgres`
- NCache or another provider implementing `IDistributedCache`

Behavior:

- `HybridCache` uses local L1 and provider-backed L2.
- Schema cache payloads can use L2 when serialization is configured safely.
- Query result caching remains opt-in.
- Provider configuration, network security, credentials, eviction, persistence, and monitoring are application/operator responsibilities.

### SharedMemoryOptIn

Used only when the application explicitly opts into Lib.Db's shared-memory adapter.

Behavior:

- Not registered by default.
- Must not be advertised as universal L2.
- Should be packaged separately or guarded by an explicit option such as `EnableSharedMemoryCache = true`.
- Should surface a startup warning on non-Windows until Linux/macOS file locking and permissions are verified in CI.

## Provider Detection and Defensive L2 Behavior

Provider absence must be an explicit runtime state, not an accident.

Recommended behavior:

- Do not auto-register `MemoryDistributedCache` as Lib.Db's default `IDistributedCache`.
- Do not treat `MemoryDistributedCache` as provider-backed L2 unless the user explicitly asks for development/test memory L2.
- Add an internal cache topology detector:
  - `NoDistributedCache`: no `IDistributedCache` registered
  - `LocalMemoryDistributedCache`: `MemoryDistributedCache` registered
  - `ProviderBackedDistributedCache`: Redis, SQL Server, Postgres, NCache, or unknown external implementation
  - `SharedMemoryDistributedCache`: explicit Lib.Db adapter
- Make topology observable through diagnostics without exposing provider connection strings.

The defensive rule:

If there is no provider-backed `IDistributedCache`, Lib.Db must run as L1-only and must not claim distributed or L2 semantics.

This avoids a common false confidence failure: local testing appears to have L2, but production scale-out lacks coherent shared cache behavior.

## Provider Responsibilities

Provider-backed caching should follow these rules.

### Registration

The host application registers the provider before Lib.Db cache registration:

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

builder.Services.AddLibDbHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});
```

Lib.Db must not read or print the provider connection string. Diagnostics should report only provider type and presence.

### Keying

Lib.Db-generated cache keys should include enough dimensions to prevent accidental cross-tenant or cross-connection reuse:

- library prefix and schema version
- package major/minor cache format version
- hashed instance identity
- schema name and object name
- result shape or static TVP shape identity when relevant

Application-generated query result cache keys remain application-owned. Documentation must require tenant, user, culture, authorization, feature flag, and data freshness dimensions when they affect results.

### Serialization

Provider L2 requires serialization. Lib.Db should:

- prefer source-generated or explicit serializers for AOT-sensitive paths
- avoid reflection-only serialization in Native AOT paths
- validate cache payload format version on read
- fail closed by treating invalid payloads as cache misses

### Expiration

Lib.Db should set conservative defaults:

- schema metadata: longer TTL with explicit invalidation support
- query result cache: caller-provided TTL only
- negative cache: short TTL with jitter

Providers own eviction policy, memory pressure behavior, persistence, and clustering.

### Invalidation

Lib.Db should expose explicit invalidation commands:

- flush schema cache for one instance
- flush one stored procedure schema
- flush one TVP schema
- flush local snapshots

Provider-backed L2 invalidation should remove the provider entry. In-process L1 entries on other nodes are not automatically invalidated unless the provider or application adds a separate pub/sub or message bus. The documentation must state this limitation plainly.

## Security Analysis

### Current SharedMemoryCache Risks

The current shared-memory design increases local attack surface:

- cache payloads are local files
- base path permissions become part of the security model
- cleanup deletes files under a cache directory
- named mutexes are global/local host objects with OS-specific semantics
- Unix-like named mutexes cannot be access-restricted in the same way as Windows
- fallback to unnamed mutexes can silently remove cross-process synchronization
- cache keys can carry sensitive dimensions if callers misuse them

These risks are not acceptable as default behavior for a provider-neutral library.

### ProviderBackedL2 Risks

Moving L2 to providers does not remove all risk. It moves risk to a clearer operational boundary:

- Redis/Postgres/SQL cache credentials must be protected
- transport encryption must be configured by the host
- cache payloads may contain sensitive data unless callers key and scope carefully
- eviction and stale-data behavior must be understood
- provider outages must degrade to DB reads or local cache misses without corrupting results

Lib.Db should provide safe defaults, clear diagnostics, and opt-in APIs rather than trying to own every provider's security model.

## Code Review Assessment

The current design is clever but too heavy for the default path.

Strengths:

- schema cache performance is a real need
- `HybridSchemaSnapshot` keeps hot reads in process
- `HybridCache` aligns with modern .NET caching APIs
- invalid cached schema payload recovery is already considered
- connection string material is hashed before isolation keys are generated

Weaknesses:

- local file cache is too much responsibility for a data-access core package
- shared-memory behavior is OS-specific and difficult to prove
- fallback behavior can degrade correctness while continuing to look enabled
- implicit `MemoryDistributedCache` can blur L1 and L2 semantics
- public surface exposes `SharedMemoryCache` and `CacheScope`, making later removal harder

Review recommendation:

Keep cache APIs and schema cache semantics. Remove implicit shared-memory registration from core.

## Structure Changes

### Core

Core should keep:

- `HybridSchemaSnapshot`
- `SchemaService`
- `HybridCache` registration helper
- query cache helper methods
- cache key and payload version helpers
- provider/topology diagnostics

Core should remove or stop default-registering:

- `SharedMemoryCache`
- `ProcessSlotAllocator`
- shared-memory epoch coordination
- implicit `MemoryDistributedCache`

### Optional Package

Create one optional package only if the optimization is still valuable:

- `Lib.Db.Caching.SharedMemory`

It should include:

- `SharedMemoryCache`
- `CacheScope`
- `ProcessSlotAllocator`
- file/MMF/lock-specific tests
- OS support matrix

The package must be explicit opt-in and should start as Windows-supported unless Linux/macOS CI proves equivalent behavior.

## Usability Changes

### Before

Users may get different cache behavior by OS:

- Windows default: shared memory can become active
- Linux/macOS default: memory fallback
- HybridCache may see an `IDistributedCache` even when the provider is only local memory

### After

Users get predictable topology:

- no provider: local-only
- provider registered: L1 plus provider-backed L2
- shared memory: explicit advanced opt-in

This is easier to explain:

> Lib.Db caches schema metadata locally by default. Add an `IDistributedCache` provider if you want L2.

## Performance Impact

### L1 Hit

Expected to stay the same or improve slightly because the hot path avoids file, MMF, and mutex overhead. `HybridSchemaSnapshot` and HybridCache local storage remain in process.

### L2 Hit

Provider-backed L2 is slower than local shared memory because it can involve network I/O and serialization. That cost buys cross-host consistency, provider observability, eviction, and operational security.

### Cache Miss

Miss cost is mostly unchanged: Lib.Db queries SQL Server metadata or executes the underlying query, then populates L1 and optionally provider-backed L2.

### Startup

Startup should become lighter by avoiding shared-memory directory creation, named mutex initialization, slot allocation, and cleanup services unless explicitly enabled.

### AOT

Native AOT behavior should become clearer:

- local cache remains available
- provider L2 requires AOT-compatible serializers
- Lib.Db's local AOT HybridCache fallback must be documented as local-only

## Migration Plan

1. Add topology diagnostics and tests.
2. Stop implicit `MemoryDistributedCache` registration in the provider-neutral path.
3. Add docs that no provider means local-only L1.
4. Mark `SharedMemoryCache` as advanced opt-in.
5. Move shared-memory implementation to optional package or isolate it behind a compatibility registration method.
6. Update examples to show Redis/SQL/Postgres provider registration.
7. Add Linux CI verification that core works without shared memory.
8. Add provider-backed integration tests using one portable provider where feasible.

## Testing Strategy

Required tests:

- no provider registers local-only topology
- no provider does not register implicit `MemoryDistributedCache`
- explicit `MemoryDistributedCache` is reported as local-memory development provider, not production L2
- Redis/SQL/Postgres provider registration is detected as provider-backed L2
- HybridCache works without `IDistributedCache`
- invalid provider payload is treated as miss
- schema flush invalidates local snapshot and provider entry
- shared-memory package is not registered by default
- shared-memory opt-in on non-Windows either fails clearly or reports experimental status

Security tests:

- diagnostics never print connection strings or provider secrets
- cache keys in logs are hashed/redacted
- query result caching examples include tenant/auth dimensions
- provider outage degrades without data corruption

## Documentation Changes

Update docs to state:

- L1 is always local and process-bound.
- L2 exists only with an explicit provider.
- `MemoryDistributedCache` is not a production distributed cache.
- Shared-memory cache is not the default and is not a security boundary.
- Provider configuration belongs to the host application.
- Provider credentials are never printed by Lib.Db.

## Decision

Adopt provider-neutral caching for v2.4.0.

Default behavior:

- L1 local schema cache and HybridCache
- no implicit L2
- no implicit shared-memory cache

Opt-in behavior:

- provider-backed L2 through registered `IDistributedCache`
- shared-memory adapter only as advanced/optional compatibility feature

This design reduces OS-specific code in the core package, lowers local security risk, makes cache topology honest, and keeps the important performance wins where they matter most: in-process schema lookup and HybridCache read-through behavior.
