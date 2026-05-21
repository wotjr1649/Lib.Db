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
- Microsoft `HybridCache` key/tag invalidation affects the current server and secondary out-of-process storage. Other servers' in-memory L1 entries are not affected by that invalidation alone.
- Microsoft distributed cache guidance treats Redis, SQL Server, Postgres, NCache, and distributed memory cache as provider choices behind `IDistributedCache`, but explicitly says `Distributed Memory Cache` is not an actual distributed cache.
- The current Lib.Db shared-memory implementation uses file-backed `MemoryMappedFile.CreateFromFile` and cache files. Microsoft memory-mapped file docs distinguish persisted file-backed maps from non-persisted named maps; named `MemoryMappedFile.CreateOrOpen` overloads carry Windows platform support annotations and should not be used as the basis for a universal cross-platform design.
- Microsoft `Mutex` docs warn that named mutexes are filesystem-backed on Unix-like systems, can be interfered with by other users, and currently cannot be access-restricted there in the same way as Windows.

Reference links:

- HybridCache library in ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid
- Distributed caching in ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed
- `IDistributedCache` API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.idistributedcache
- `MemoryDistributedCache` API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.memorydistributedcache
- `MemoryMappedFile` API: https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile
- Memory-mapped files overview: https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files
- `Mutex` API: https://learn.microsoft.com/en-us/dotnet/api/system.threading.mutex

## Problem

Lib.Db wants to be a general-purpose library that offers the same API and semantics on Windows, Linux, and macOS. The current shared-memory cache conflicts with that goal in six ways.

First, OS behavior differs. `EnableSharedMemoryCache = null` enables shared memory on Windows but disables it on Linux/macOS. This is operationally honest, but it means a default Lib.Db install does not have the same cache topology on every OS.

Second, security responsibility is too broad. Shared-memory caching stores payloads as files under a configurable base path, uses named mutexes, deletes expired files, and hashes connection strings for isolation. These are not merely library implementation details; they are local host security decisions.

Third, failure modes are subtle. If named mutex creation fails and the implementation falls back to unnamed mutexes, the code may still use the same shared cache files while synchronization becomes process-local. That can corrupt cache data or produce stale reads.

Fourth, implicit fallback can misrepresent topology. Registering `MemoryDistributedCache` as the default `IDistributedCache` makes APIs see an L2-like service, but it is only in-process memory. That can create duplicate local caching and can mislead users into believing they have cross-process or cross-host L2.

Fifth, a partial shared-memory mode is possible if configuration sets `EnableSharedMemoryCache = true` but the application does not call an explicit shared-memory registration method. The library can then enable slot allocation or epoch coordination without a matching `IDistributedCache` adapter. That mixed state is harder to diagnose than a startup failure.

Sixth, treating every unknown `IDistributedCache` implementation as production L2 is too optimistic. A test double, local wrapper, or in-memory adapter can implement the interface without providing cross-process or cross-host semantics. Provider detection must distinguish verified provider-backed L2 from unverified distributed-cache-shaped services.

## Goals

- Make Lib.Db core OS-neutral by default.
- Keep high-value in-process schema caching.
- Use `HybridCache` as the primary cache API.
- Use provider-backed `IDistributedCache` only when the application explicitly registers a real provider.
- Avoid registering `MemoryDistributedCache` as an implicit L2 for production-style defaults.
- Do not report unknown `IDistributedCache` implementations as verified production L2 unless the application explicitly marks them trusted or they match a known provider family.
- Keep query result caching opt-in and caller-owned.
- Preserve a migration path for Windows users who benefit from local shared-memory cache.
- Make cache topology observable without printing secrets or connection strings.
- Fail fast on contradictory cache configuration instead of silently composing partial topology.

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
3. Provider-backed L2 is available only when a verified or explicitly trusted `IDistributedCache` provider is registered by the host application.
4. If no provider exists, Lib.Db uses L1-only `HybridCache` behavior and reports topology as `LocalOnly`.
5. `SharedMemoryCache` is removed from the default core registration path and moved behind explicit API opt-in.
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

### VerifiedProviderBackedL2

Used when the application explicitly registers a supported or explicitly trusted `IDistributedCache` provider before Lib.Db cache registration.

Examples:

- Redis via `Microsoft.Extensions.Caching.StackExchangeRedis`
- SQL Server via `Microsoft.Extensions.Caching.SqlServer`
- Postgres via `Microsoft.Extensions.Caching.Postgres`
- NCache or another provider that the host explicitly marks as trusted

Behavior:

- `HybridCache` uses local L1 and provider-backed L2.
- Schema cache payloads can use L2 when serialization is configured safely.
- Query result caching remains opt-in.
- Provider configuration, network security, credentials, eviction, persistence, and monitoring are application/operator responsibilities.
- Diagnostics expose `LibDbCacheTopology.VerifiedProviderBackedL2`.

### UnverifiedDistributedCache

Used when an `IDistributedCache` service is present but Lib.Db cannot prove it is a real shared L2 provider.

Examples:

- test doubles
- local wrappers over `MemoryDistributedCache`
- custom implementations with unknown deployment semantics

Behavior:

- `HybridCache` may still use the registered `IDistributedCache` because this is how Microsoft `HybridCache` composes with DI.
- Lib.Db diagnostics must not claim production L2 or cross-host coherence.
- Schema cache code must treat corrupt or version-incompatible provider payloads as misses.
- The application can opt into trusted-provider classification through an explicit Lib.Db API or option if the custom provider is known to be shared and secure in that deployment.
- Diagnostics expose `LibDbCacheTopology.UnverifiedDistributedCache`.

### SharedMemoryOptIn

Used only when the application explicitly opts into Lib.Db's shared-memory adapter by calling `AddLibDbSharedMemoryCache()`.

Behavior:

- Not registered by default.
- Must not be advertised as universal L2.
- `EnableSharedMemoryCache = true` is not sufficient by itself. If the option is set without the explicit registration marker, startup must fail with a non-secret diagnostic that names `AddLibDbSharedMemoryCache()`.
- `AddLibDbSharedMemoryCache()` must reject any other `IDistributedCache` registration by default, including providers registered before or after the opt-in call. Silent `TryAddSingleton<IDistributedCache>` would create a mixed state where the provider stays active while shared-memory slot allocation replaces core coordination.
- Should be packaged separately or guarded by an explicit option and explicit API.
- Should surface a startup warning on non-Windows until Linux/macOS file locking and permissions are verified in CI.

## Provider Detection and Defensive L2 Behavior

Provider absence must be an explicit runtime state, not an accident.

Recommended behavior:

- Do not auto-register `MemoryDistributedCache` as Lib.Db's default `IDistributedCache`.
- Do not treat `MemoryDistributedCache` as provider-backed L2 unless the user explicitly asks for development/test memory L2.
- Do not treat unknown `IDistributedCache` implementations as verified provider L2 by default.
- Add an internal cache topology detector:
  - `NoDistributedCache`: no `IDistributedCache` registered
  - `LocalMemoryDistributedCache`: `MemoryDistributedCache` registered
  - `VerifiedProviderBackedDistributedCache`: Redis, SQL Server, Postgres, NCache, or an explicitly trusted custom provider
  - `UnverifiedDistributedCache`: an `IDistributedCache` exists, but Lib.Db cannot prove production L2 semantics
  - `SharedMemoryDistributedCache`: explicit Lib.Db adapter
- Make topology observable through diagnostics without exposing provider connection strings.

Defensive rules:

- If there is no provider-backed `IDistributedCache`, Lib.Db must run as L1-only and must not claim distributed or L2 semantics.
- If a provider is present but unverified, Lib.Db may interoperate with it through `HybridCache`, but diagnostics must report `UnverifiedDistributedCache` and `HasVerifiedProviderBackedL2 = false`.
- If `EnableSharedMemoryCache = true` is set without `AddLibDbSharedMemoryCache()`, Lib.Db must fail fast. This prevents slot allocation, epoch coordination, or cleanup services from running without the cache adapter they coordinate.
- If `AddLibDbSharedMemoryCache()` is combined with another `IDistributedCache` registration in either order, Lib.Db must fail fast by default. The caller must choose either host-provided L2 or Lib.Db shared memory.
- If `EnableEpochCoordination = true` is set without shared-memory opt-in, Lib.Db must fail fast. It must not auto-enable file epoch coordination merely because the OS is Windows.

This avoids a common false confidence failure: local testing appears to have L2, but production scale-out lacks coherent shared cache behavior.

## Diagnostics Contract

Cache topology must be visible because provider-neutral behavior is otherwise easy to misread.

Lib.Db should expose a small topology snapshot through diagnostics, health checks, or startup logging:

- topology kind
- whether verified provider-backed L2 is active
- provider type name only, never provider options or connection strings
- shared-memory opt-in state
- epoch coordination state
- warnings for unverified providers, local memory providers, or disabled L2

Diagnostics must not include:

- raw connection strings
- provider credentials
- raw cache keys
- tenant/user identifiers unless already redacted or hashed
- serialized cache payloads

This snapshot is for operational clarity, not authorization. It must not be documented as a security boundary.

## Provider Responsibilities

Provider-backed caching should follow these rules.

### Registration

The host application registers the provider before Lib.Db cache registration and before Lib.Db cache helpers are used:

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

Lib.Db should also avoid multiple competing `HybridCache` configuration paths. If `AddLibDb()` internally registers baseline `HybridCache` serializers and the application later calls `AddLibDbHybridCache()`, the implementation must compose options predictably and must not erase caller settings. The public guidance should show one clear order:

1. register the host `IDistributedCache` provider, if any
2. register Lib.Db
3. configure Lib.Db `HybridCache` options once
4. optionally call `AddLibDbSharedMemoryCache()` instead of a host provider, not in addition to one

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

### VerifiedProviderBackedL2 Risks

Moving L2 to providers does not remove all risk. It moves risk to a clearer operational boundary:

- Redis/Postgres/SQL cache credentials must be protected
- transport encryption must be configured by the host
- cache payloads may contain sensitive data unless callers key and scope carefully
- eviction and stale-data behavior must be understood
- provider outages must degrade to DB reads or local cache misses without corrupting results

Lib.Db should provide safe defaults, clear diagnostics, and opt-in APIs rather than trying to own every provider's security model.

### Unverified Provider Risks

An `IDistributedCache` interface alone does not prove distributed behavior. From a security and correctness perspective, an unknown implementation can be:

- a test double with no persistence
- an in-process memory cache wrapped behind the interface
- a provider with weak transport security or broad tenant visibility
- a provider that serializes values differently from Lib.Db expectations

Lib.Db should therefore classify unknown providers as unverified until the host explicitly trusts them. This avoids overstating guarantees in diagnostics and reduces the chance that operators rely on a cache topology that the library did not actually validate.

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
- cache topology health/diagnostics snapshot

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
3. Add fail-fast guards for `EnableSharedMemoryCache = true` without explicit opt-in.
4. Add fail-fast guards for `AddLibDbSharedMemoryCache()` plus an existing `IDistributedCache` provider.
5. Classify unknown `IDistributedCache` implementations as unverified unless explicitly trusted.
6. Add docs that no provider means local-only L1.
7. Mark `SharedMemoryCache` as advanced opt-in.
8. Move shared-memory implementation to optional package or isolate it behind a compatibility registration method.
9. Update examples to show Redis/SQL/Postgres provider registration and tenant/auth-aware query cache keys.
10. Add Linux CI verification that core works without shared memory.
11. Add provider-backed integration tests using one portable provider where feasible.

## Testing Strategy

Required tests:

- no provider registers local-only topology
- no provider does not register implicit `MemoryDistributedCache`
- explicit `MemoryDistributedCache` is reported as local-memory development provider, not production L2
- Redis/SQL/Postgres provider registration is detected as verified provider-backed L2
- unknown `IDistributedCache` is reported as unverified and `HasVerifiedProviderBackedL2 = false`
- explicit trusted provider override changes an unknown provider to verified provider-backed L2
- HybridCache works without `IDistributedCache`
- `EnableSharedMemoryCache = true` without `AddLibDbSharedMemoryCache()` fails fast with a non-secret error
- `AddLibDbSharedMemoryCache()` fails fast when another `IDistributedCache` provider is registered before or after shared-memory opt-in
- `AddLibDbSharedMemoryCache()` exercises the real options pipeline and `PostConfigure` path, not only manually injected `IOptions<LibDbOptions>`
- invalid provider payload is treated as miss
- provider outage is treated as miss or fallback to DB/local cache, not as corrupted result
- schema flush invalidates local snapshot and provider entry
- topology diagnostics/health output is redacted and contains no provider secrets or raw cache keys
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

- verified provider-backed L2 through registered and recognized/trusted `IDistributedCache`
- shared-memory adapter only as advanced/optional compatibility feature
- unverified `IDistributedCache` interop is allowed, but diagnostics must not claim production L2

This design reduces OS-specific code in the core package, lowers local security risk, makes cache topology honest, and keeps the important performance wins where they matter most: in-process schema lookup and HybridCache read-through behavior.
