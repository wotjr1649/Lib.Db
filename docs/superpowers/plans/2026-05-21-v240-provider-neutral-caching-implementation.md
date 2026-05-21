# Lib.Db v2.4.0 Provider-Neutral Caching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Lib.Db caching provider-neutral by default: no implicit distributed cache provider, no default shared-memory cache, and L1-only behavior when the host application has not registered a real L2 provider.

**Architecture:** Keep the existing in-process schema snapshot and HybridCache registration, but split provider detection, default registration, shared-memory opt-in, epoch coordination, and documentation into focused changes. Lib.Db core never registers `MemoryDistributedCache` as an L2 substitute; provider-backed L2 exists only when the host registers `IDistributedCache` or explicitly opts into Lib.Db shared memory.

**Tech Stack:** .NET 10, C# 14 preview syntax already used by the repo, Microsoft.Extensions.Caching.Hybrid, Microsoft.Extensions.Caching.Distributed, xUnit v3, FluentAssertions, Microsoft.Extensions.DependencyInjection.

---

## Reviewed Spec

Spec: `docs/superpowers/specs/2026-05-21-v240-provider-neutral-caching-design.md`

The implementation must satisfy these decisions:

- Provider absence means local-only behavior.
- Lib.Db must not auto-register `MemoryDistributedCache`.
- `MemoryDistributedCache` is local memory and must not be reported as production L2.
- Existing provider registrations must be preserved.
- `SharedMemoryCache` must move behind an explicit opt-in registration.
- Epoch coordination defaults to disabled unless shared memory or epoch coordination is explicitly enabled.
- Diagnostics and docs must describe topology without printing connection strings, provider credentials, or raw cache keys.

Official references were checked in the spec on 2026-05-21:

- <https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid>
- <https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed>
- <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.idistributedcache>
- <https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.memorydistributedcache>
- <https://learn.microsoft.com/en-us/dotnet/api/system.io.memorymappedfiles.memorymappedfile>
- <https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files>

## File Structure

Create:

- `Lib.Db/Caching/LibDbCacheTopology.cs`
  Internal cache topology model and detector. This file owns the vocabulary for `LocalOnly`, `LocalMemoryDistributedCache`, `ProviderBackedL2`, and `SharedMemoryOptIn`.

Modify:

- `Lib.Db/Extensions/ServiceRegistrationHelpers.cs`
  Stop implicit `MemoryDistributedCache` registration. Keep core cache coordination provider-neutral. Add a separate shared-memory opt-in helper.

- `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs`
  Keep `RegisterLibDbCoreServices` provider-neutral. Add public `AddLibDbSharedMemoryCache()` opt-in registration.

- `Lib.Db/Extensions/HybridCacheExtensions.cs`
  Update comments so `HybridCache` is described as L1-only unless a real `IDistributedCache` provider is registered by the host.

- `Lib.Db/Schema/SchemaFlushService.cs`
  Align epoch default behavior with provider-neutral caching and avoid implying cross-process L2 when shared memory is not active.

- `Lib.Db/Configuration/LibDbOptions.cs`
  Update XML comments for `EnableSharedMemoryCache` and `EnableEpochCoordination`.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`
  Add detector unit coverage and shared-memory opt-in coverage.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/ServiceRegistrationHelpersTests.cs`
  Add default registration and provider preservation coverage.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/SchemaFlushServiceTests.cs`
  Add provider-neutral epoch default coverage.

- `docs/02_configuration.md`
  Document provider-neutral defaults and the shared-memory opt-in switch.

- `docs/02_advanced.md`
  Replace shared-memory-as-default L2 wording with provider-backed L2 guidance.

- `docs/03_api_reference.md`
  Update option descriptions and add `AddLibDbSharedMemoryCache()`.

- `docs/05_fluent_api_reference.md`
  Clarify `WithHybridCacheAsync` topology.

- `docs/06_cookbook.md`
  Add provider-backed Redis and local-only examples.

- `docs/history.md`
  Record the v2.4.0 provider-neutral cache behavior.

---

### Task 1: Add Topology Detector Tests

**Files:**
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`
- Later implementation file: `Lib.Db/Caching/LibDbCacheTopology.cs`

- [ ] **Step 1: Add topology detector tests**

Add these tests near the existing cache infrastructure tests in `CacheHostingCoverageTests`:

```csharp
[Fact]
public void CacheTopologyDetector_ShouldReportMissingDistributedCacheAsLocalOnly()
{
    LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache: null);

    state.Kind.Should().Be(LibDbCacheTopologyKind.LocalOnly);
    state.HasProviderBackedL2.Should().BeFalse();
    state.ProviderTypeName.Should().BeNull();
}

[Fact]
public void CacheTopologyDetector_ShouldReportMemoryDistributedCacheAsLocalMemory()
{
    var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache);

    state.Kind.Should().Be(LibDbCacheTopologyKind.LocalMemoryDistributedCache);
    state.HasProviderBackedL2.Should().BeFalse();
    state.ProviderTypeName.Should().Contain(nameof(MemoryDistributedCache));
}

[Fact]
public void CacheTopologyDetector_ShouldReportSharedMemoryCacheAsSharedMemoryOptIn()
{
    string basePath = CreateTempDirectory();
    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
    using var cache = new SharedMemoryCache(
        Options.Create(new SharedMemoryCacheOptions { BasePath = basePath }),
        loggerFactory.CreateLogger<SharedMemoryCache>());

    LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache);

    state.Kind.Should().Be(LibDbCacheTopologyKind.SharedMemoryOptIn);
    state.HasProviderBackedL2.Should().BeFalse();
    state.ProviderTypeName.Should().Contain(nameof(SharedMemoryCache));
}

[Fact]
public void CacheTopologyDetector_ShouldReportUnknownDistributedCacheAsProviderBackedL2()
{
    var cache = new RecordingDistributedCache();

    LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(cache);

    state.Kind.Should().Be(LibDbCacheTopologyKind.ProviderBackedL2);
    state.HasProviderBackedL2.Should().BeTrue();
    state.ProviderTypeName.Should().Contain(nameof(RecordingDistributedCache));
}
```

Add this helper at the end of `CacheHostingCoverageTests`:

```csharp
private sealed class RecordingDistributedCache : IDistributedCache
{
    private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

    public byte[]? Get(string key)
    {
        return _values.TryGetValue(key, out byte[]? value) ? value : null;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return Task.FromResult(Get(key));
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        _values[key] = value;
    }

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        _values.Remove(key);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~CacheHostingCoverageTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: FAIL with missing `LibDbCacheTopologyState`, `LibDbCacheTopologyDetector`, and `LibDbCacheTopologyKind`.

- [ ] **Step 3: Commit the failing tests**

```powershell
git add .\Verification\projects\Lib.Db.IntegrationTests\Unit\CacheHostingCoverageTests.cs
git commit -m "test: cover cache topology detection"
```

### Task 2: Implement Cache Topology Detection

**Files:**
- Create: `Lib.Db/Caching/LibDbCacheTopology.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`

- [ ] **Step 1: Create the topology detector**

Create `Lib.Db/Caching/LibDbCacheTopology.cs`:

```csharp
// ============================================================================
// File: Lib.Db/Caching/LibDbCacheTopology.cs
// Purpose: Provider-neutral cache topology detection for Lib.Db registration
// ============================================================================

#nullable enable

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Lib.Db.Caching;

internal enum LibDbCacheTopologyKind
{
    LocalOnly,
    LocalMemoryDistributedCache,
    ProviderBackedL2,
    SharedMemoryOptIn
}

internal sealed record LibDbCacheTopologyState(
    LibDbCacheTopologyKind Kind,
    string? ProviderTypeName,
    bool HasProviderBackedL2)
{
    public static LibDbCacheTopologyState LocalOnly { get; } =
        new(LibDbCacheTopologyKind.LocalOnly, null, HasProviderBackedL2: false);
}

internal static class LibDbCacheTopologyDetector
{
    public static LibDbCacheTopologyState Detect(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        return Detect(serviceProvider.GetService<IDistributedCache>());
    }

    public static LibDbCacheTopologyState Detect(IDistributedCache? cache)
    {
        if (cache is null)
            return LibDbCacheTopologyState.LocalOnly;

        Type cacheType = cache.GetType();
        string? fullName = cacheType.FullName;
        string providerTypeName = string.IsNullOrWhiteSpace(fullName)
            ? cacheType.Name
            : fullName;

        return cache switch
        {
            SharedMemoryCache => new(
                LibDbCacheTopologyKind.SharedMemoryOptIn,
                providerTypeName,
                HasProviderBackedL2: false),

            MemoryDistributedCache => new(
                LibDbCacheTopologyKind.LocalMemoryDistributedCache,
                providerTypeName,
                HasProviderBackedL2: false),

            _ => new(
                LibDbCacheTopologyKind.ProviderBackedL2,
                providerTypeName,
                HasProviderBackedL2: true)
        };
    }
}
```

- [ ] **Step 2: Run topology tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~CacheHostingCoverageTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS for the new topology detector tests.

- [ ] **Step 3: Commit topology detection**

```powershell
git add .\Lib.Db\Caching\LibDbCacheTopology.cs .\Verification\projects\Lib.Db.IntegrationTests\Unit\CacheHostingCoverageTests.cs
git commit -m "feat: add provider-neutral cache topology detection"
```

### Task 3: Add Default Registration Regression Tests

**Files:**
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/ServiceRegistrationHelpersTests.cs`
- Later implementation files:
  - `Lib.Db/Extensions/ServiceRegistrationHelpers.cs`
  - `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs`

- [ ] **Step 1: Add tests for local-only default behavior**

Add these tests to `ServiceRegistrationHelpersTests`:

```csharp
[Fact]
public void RegisterLibDbCoreServices_ShouldNotRegisterDistributedCache_WhenProviderIsMissing()
{
    ServiceCollection services = CreateConfiguredServices(options =>
    {
        options.EnableSharedMemoryCache = null;
        options.EnableEpochCoordination = null;
    });

    services.RegisterLibDbCoreServices();

    using ServiceProvider provider = services.BuildServiceProvider();

    provider.GetService<IDistributedCache>().Should().BeNull();
    provider.GetRequiredService<HybridCache>().Should().NotBeNull();
    provider.GetRequiredService<IProcessSlotAllocator>().Should().BeOfType<PassiveProcessSlotAllocator>();
}

[Fact]
public void RegisterLibDbCoreServices_ShouldPreserveCallerDistributedCacheProvider()
{
    ServiceCollection services = CreateConfiguredServices(options =>
    {
        options.EnableSharedMemoryCache = null;
        options.EnableEpochCoordination = null;
    });
    services.AddSingleton<IDistributedCache, TestDistributedCache>();

    services.RegisterLibDbCoreServices();

    using ServiceProvider provider = services.BuildServiceProvider();

    provider.GetRequiredService<IDistributedCache>().Should().BeOfType<TestDistributedCache>();
    LibDbCacheTopologyState state = LibDbCacheTopologyDetector.Detect(provider);
    state.Kind.Should().Be(LibDbCacheTopologyKind.ProviderBackedL2);
    state.HasProviderBackedL2.Should().BeTrue();
}

[Fact]
public void AddLibDbSharedMemoryCache_ShouldRegisterSharedMemoryCacheOnlyWhenExplicitlyCalled()
{
    ServiceCollection services = CreateConfiguredServices(options =>
    {
        options.EnableSharedMemoryCache = null;
        options.EnableEpochCoordination = null;
        options.SharedMemoryCache.BasePath = Path.Combine(Path.GetTempPath(), "LibDbPlanTest_" + Guid.NewGuid().ToString("N"));
    });

    services.RegisterLibDbCoreServices();
    services.AddLibDbSharedMemoryCache();

    using ServiceProvider provider = services.BuildServiceProvider();

    provider.GetRequiredService<IDistributedCache>().Should().BeOfType<SharedMemoryCache>();
    provider.GetRequiredService<IProcessSlotAllocator>().Should().BeOfType<ProcessSlotAllocator>();
    LibDbCacheTopologyDetector.Detect(provider).Kind.Should().Be(LibDbCacheTopologyKind.SharedMemoryOptIn);
}
```

Add these helpers to `ServiceRegistrationHelpersTests`:

```csharp
private static ServiceCollection CreateConfiguredServices(Action<LibDbOptions> configure)
{
    LibDbOptions options = new();
    options.ConnectionStrings["Default"] =
        "Server=(localdb)\\MSSQLLocalDB;Database=LibDbPlan;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";
    configure(options);

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddLibDbOptions(_ => { });
    services.AddSingleton<IOptions<LibDbOptions>>(Options.Create(options));
    services.AddSingleton(options);
    return services;
}

private sealed class TestDistributedCache : IDistributedCache
{
    public byte[]? Get(string key) => null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        => Task.FromResult<byte[]?>(null);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
    }

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
        => Task.CompletedTask;

    public void Refresh(string key)
    {
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
        => Task.CompletedTask;

    public void Remove(string key)
    {
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
        => Task.CompletedTask;
}
```

- [ ] **Step 2: Add missing usings**

Add these usings at the top of `ServiceRegistrationHelpersTests.cs`:

```csharp
using Lib.Db.Caching;
using Lib.Db.Hosting;
using Microsoft.Extensions.Caching.Hybrid;
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~ServiceRegistrationHelpersTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: FAIL because core still registers a fallback `MemoryDistributedCache`, and `AddLibDbSharedMemoryCache` does not exist.

- [ ] **Step 4: Commit failing registration tests**

```powershell
git add .\Verification\projects\Lib.Db.IntegrationTests\Unit\ServiceRegistrationHelpersTests.cs
git commit -m "test: cover provider-neutral cache registration"
```

### Task 4: Make Core Registration Provider-Neutral

**Files:**
- Modify: `Lib.Db/Extensions/ServiceRegistrationHelpers.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/ServiceRegistrationHelpersTests.cs`

- [ ] **Step 1: Remove implicit MemoryDistributedCache registration**

In `ServiceRegistrationHelpers.cs`, remove `using System.Runtime.InteropServices;` if it is no longer used after the replacement.

Replace the body of `RegisterConditionalSharedMemoryCache` with:

```csharp
internal static void RegisterConditionalSharedMemoryCache(IServiceCollection services)
{
    services.TryAddSingleton<Lib.Db.Contracts.Cache.IIsolationKeyGenerator, Lib.Db.Caching.IsolationKeyGenerator>();

    services.TryAddSingleton<IProcessSlotAllocator>(sp =>
    {
        LibDbOptions options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
        ILogger<ProcessSlotAllocator> logger = sp.GetRequiredService<ILogger<Lib.Db.Hosting.ProcessSlotAllocator>>();
        IIsolationKeyGenerator keyGenerator = sp.GetRequiredService<IIsolationKeyGenerator>();

        if (options.EnableSharedMemoryCache is not true)
        {
            logger.LogInformation("[ProcessSlot] provider-neutral local-only mode - passive allocator");
            return new Lib.Db.Hosting.PassiveProcessSlotAllocator();
        }

        string connectionString = GetPrimaryConnectionStringOrThrow(options, "ProcessSlotAllocator");
        string? generatedKey = keyGenerator.Generate(connectionString);
        string isolationKey = string.IsNullOrWhiteSpace(generatedKey)
            ? "Shared"
            : generatedKey;
        return new Lib.Db.Hosting.ProcessSlotAllocator(isolationKey, logger);
    });
}
```

This keeps the existing method name so call sites stay stable, but changes its behavior to provider-neutral local-only by default.

- [ ] **Step 2: Add shared-memory opt-in helper**

Add this method below `RegisterConditionalSharedMemoryCache`:

```csharp
internal static void RegisterSharedMemoryCacheOptIn(IServiceCollection services)
{
    services.TryAddSingleton<Lib.Db.Contracts.Cache.IIsolationKeyGenerator, Lib.Db.Caching.IsolationKeyGenerator>();

    services.RemoveAll<IProcessSlotAllocator>();
    services.TryAddSingleton<IProcessSlotAllocator>(sp =>
    {
        LibDbOptions options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
        ILogger<ProcessSlotAllocator> logger = sp.GetRequiredService<ILogger<Lib.Db.Hosting.ProcessSlotAllocator>>();
        IIsolationKeyGenerator keyGenerator = sp.GetRequiredService<IIsolationKeyGenerator>();

        string connectionString = GetPrimaryConnectionStringOrThrow(options, "ProcessSlotAllocator");
        string? generatedKey = keyGenerator.Generate(connectionString);
        string isolationKey = string.IsNullOrWhiteSpace(generatedKey)
            ? "Shared"
            : generatedKey;
        return new Lib.Db.Hosting.ProcessSlotAllocator(isolationKey, logger);
    });

    services.TryAddSingleton<IDistributedCache>(sp =>
    {
        LibDbOptions options = sp.GetRequiredService<IOptions<LibDbOptions>>().Value;
        ILogger<SharedMemoryCache> logger = sp.GetRequiredService<ILogger<SharedMemoryCache>>();
        IIsolationKeyGenerator keyGenerator = sp.GetRequiredService<IIsolationKeyGenerator>();

        string connectionString = GetPrimaryConnectionStringOrThrow(options, "SharedMemoryCache");
        SharedMemoryCacheOptions configured = options.SharedMemoryCache;
        string? generatedIsolationKey = keyGenerator.Generate(connectionString);
        string isolationKey = !string.IsNullOrWhiteSpace(generatedIsolationKey)
            ? generatedIsolationKey
            : (!string.IsNullOrWhiteSpace(configured.IsolationKey)
                ? configured.IsolationKey
                : "Shared");

        string basePath = string.IsNullOrWhiteSpace(configured.BasePath)
            ? Path.Combine(Path.GetTempPath(), "LibDbCache")
            : configured.BasePath;

        logger.LogWarning(
            "[SharedMemoryCache] explicit opt-in active. This is a local host optimization, not universal distributed L2. Provider={ProviderType}",
            nameof(SharedMemoryCache));

        SharedMemoryCacheOptions cacheOptions = new()
        {
            BasePath = basePath,
            Scope = configured.Scope,
            MaxCacheSizeBytes = configured.MaxCacheSizeBytes,
            FallbackCache = configured.FallbackCache,
            IsolationKey = isolationKey,
            EnableObservability = options.EnableObservability
        };

        return new SharedMemoryCache(Options.Create(cacheOptions), logger);
    });

    services.AddHostedService<CacheMaintenanceService>();
}
```

- [ ] **Step 3: Run registration tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~ServiceRegistrationHelpersTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: FAIL only because `AddLibDbSharedMemoryCache` is not public yet.

- [ ] **Step 4: Commit provider-neutral helper changes**

```powershell
git add .\Lib.Db\Extensions\ServiceRegistrationHelpers.cs .\Verification\projects\Lib.Db.IntegrationTests\Unit\ServiceRegistrationHelpersTests.cs
git commit -m "feat: stop implicit distributed cache registration"
```

### Task 5: Add Explicit Shared-Memory Registration API

**Files:**
- Modify: `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs`
- Test: `Verification/projects/Lib.Db.IntegrationTests/Unit/ServiceRegistrationHelpersTests.cs`

- [ ] **Step 1: Add public opt-in extension**

Add this method near the other service registration extensions in `LibDbServiceCollectionExtensions.cs`:

```csharp
/// <summary>
/// Explicitly enables Lib.Db's local shared-memory cache adapter.
/// </summary>
/// <remarks>
/// This registration is an advanced local-host optimization. It is not the default
/// cache provider and does not provide universal cross-host L2 semantics.
/// </remarks>
public static IServiceCollection AddLibDbSharedMemoryCache(
    this IServiceCollection services)
{
    services.PostConfigure<LibDbOptions>(static options =>
    {
        options.EnableSharedMemoryCache = true;
    });

    ServiceRegistrationHelpers.RegisterSharedMemoryCacheOptIn(services);
    return services;
}
```

- [ ] **Step 2: Run registration tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~ServiceRegistrationHelpersTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS.

- [ ] **Step 3: Run cache hosting tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~CacheHostingCoverageTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS.

- [ ] **Step 4: Commit explicit shared-memory API**

```powershell
git add .\Lib.Db\Extensions\LibDbServiceCollectionExtensions.cs .\Verification\projects\Lib.Db.IntegrationTests\Unit\ServiceRegistrationHelpersTests.cs
git commit -m "feat: add explicit shared-memory cache opt-in"
```

### Task 6: Align Epoch Coordination With Local-Only Defaults

**Files:**
- Modify: `Lib.Db/Extensions/LibDbServiceCollectionExtensions.cs`
- Modify: `Lib.Db/Schema/SchemaFlushService.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/SchemaFlushServiceTests.cs`

- [ ] **Step 1: Add epoch default tests**

Add this test to `SchemaFlushServiceTests`:

```csharp
[Fact]
public async Task AddSchemaFlushCoordination_ShouldDisableEpochByDefault_WhenSharedMemoryIsNotExplicit()
{
    string basePath = Path.Combine(Path.GetTempPath(), "LibDbEpochPlan_" + Guid.NewGuid().ToString("N"));
    LibDbOptions options = new()
    {
        EnableSharedMemoryCache = null,
        EnableEpochCoordination = null
    };

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(options);
    services.AddSingleton<IOptions<LibDbOptions>>(Options.Create(options));
    services.AddSchemaFlushCoordination(basePath);

    using ServiceProvider provider = services.BuildServiceProvider();
    EpochStore store = provider.GetRequiredService<EpochStore>();

    store.IncrementEpoch("abc").Should().Be(0);
    store.GetEpoch("abc").Should().Be(0);
    Directory.Exists(basePath).Should().BeFalse();

    await provider.DisposeAsync();
}
```

- [ ] **Step 2: Run test to verify current behavior**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~SchemaFlushServiceTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: FAIL on Windows because epoch still follows auto-enabled shared memory when both options are null.

- [ ] **Step 3: Change epoch default calculation**

In `AddSchemaFlushCoordination`, replace the current OS auto-detection block with:

```csharp
bool enableSharedMemory = options.EnableSharedMemoryCache is true;
bool enableEpoch = options.EnableEpochCoordination.GetValueOrDefault(enableSharedMemory);
```

- [ ] **Step 4: Align `SchemaFlushService` constructor or execution gate**

In `SchemaFlushService.cs`, replace the local shared-memory default calculation with:

```csharp
bool enableSharedMemory = options.EnableSharedMemoryCache is true;
```

Keep existing explicit `EnableEpochCoordination = true` behavior intact.

- [ ] **Step 5: Run schema flush tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~SchemaFlushServiceTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS.

- [ ] **Step 6: Commit epoch default changes**

```powershell
git add .\Lib.Db\Extensions\LibDbServiceCollectionExtensions.cs .\Lib.Db\Schema\SchemaFlushService.cs .\Verification\projects\Lib.Db.IntegrationTests\Unit\SchemaFlushServiceTests.cs
git commit -m "feat: default epoch coordination to local-only mode"
```

### Task 7: Update XML Comments and HybridCache Guidance

**Files:**
- Modify: `Lib.Db/Configuration/LibDbOptions.cs`
- Modify: `Lib.Db/Extensions/HybridCacheExtensions.cs`
- Modify: `Lib.Db/Contracts/Schema/SchemaContracts.cs`
- Modify: `Lib.Db/Schema/SchemaService.cs`

- [ ] **Step 1: Update `EnableSharedMemoryCache` XML comments**

In `LibDbOptions.cs`, replace the comment for `EnableSharedMemoryCache` with:

```csharp
/// <summary>
/// Explicitly enables Lib.Db's local shared-memory cache adapter.
/// <para>
/// The default value <c>null</c> means Lib.Db does not register a distributed cache provider.
/// Schema caching remains local through HybridCache and HybridSchemaSnapshot.
/// </para>
/// <para>
/// Set this to <c>true</c> only together with <c>AddLibDbSharedMemoryCache()</c>.
/// Prefer a host-registered <c>IDistributedCache</c> provider such as Redis, SQL Server,
/// Postgres, or NCache when cross-process or cross-host L2 behavior is required.
/// </para>
/// </summary>
```

- [ ] **Step 2: Update `EnableEpochCoordination` XML comments**

Replace the comment for `EnableEpochCoordination` with:

```csharp
/// <summary>
/// Enables file-based epoch coordination for explicit shared-memory scenarios.
/// <para>
/// The default value <c>null</c> follows explicit shared-memory opt-in only.
/// It does not auto-enable on Windows.
/// </para>
/// </summary>
```

- [ ] **Step 3: Update `HybridCacheExtensions` comments**

Replace the L1/L2 bullet in `HybridCacheExtensions.cs` with:

```csharp
/// <item><description><strong>Provider-neutral topology</strong>: HybridCache always provides local in-process caching. It uses L2 only when the host application has registered an <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> provider.</description></item>
```

Replace the inline comments before `services.AddHybridCache` with:

```csharp
// .NET HybridCache registration.
// Without IDistributedCache this is local in-process caching with stampede protection.
// With a host-registered IDistributedCache provider, HybridCache can use provider-backed L2.
```

- [ ] **Step 4: Update schema comments that imply unconditional L2**

In `SchemaContracts.cs` and `SchemaService.cs`, replace wording that says `L2(Redis 등 분산)` or `HybridCache L2 (Distributed)` with wording that says:

```text
Provider-backed L2 is optional and exists only when the host registers IDistributedCache.
```

Use Korean for adjacent Korean XML comments and English for adjacent English comments.

- [ ] **Step 5: Run build**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~CacheHostingCoverageTests|FullyQualifiedName~ServiceRegistrationHelpersTests|FullyQualifiedName~SchemaFlushServiceTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS.

- [ ] **Step 6: Commit comment updates**

```powershell
git add .\Lib.Db\Configuration\LibDbOptions.cs .\Lib.Db\Extensions\HybridCacheExtensions.cs .\Lib.Db\Contracts\Schema\SchemaContracts.cs .\Lib.Db\Schema\SchemaService.cs
git commit -m "docs: clarify provider-neutral cache semantics in code"
```

### Task 8: Update User Documentation

**Files:**
- Modify: `docs/02_configuration.md`
- Modify: `docs/02_advanced.md`
- Modify: `docs/03_api_reference.md`
- Modify: `docs/05_fluent_api_reference.md`
- Modify: `docs/06_cookbook.md`
- Modify: `docs/history.md`

- [ ] **Step 1: Update configuration docs**

In `docs/02_configuration.md`, replace the `EnableSharedMemoryCache` description with:

```markdown
| `EnableSharedMemoryCache` | `bool?` | `null` | `null` means Lib.Db does not register a distributed cache provider. Use `AddLibDbSharedMemoryCache()` plus `true` only for explicit local shared-memory opt-in. |
```

Add this paragraph below the options table:

```markdown
Lib.Db v2.4.0 is provider-neutral by default. If the application does not register an `IDistributedCache` provider, schema caching still works locally through `HybridCache` and the in-process schema snapshot. Lib.Db does not create an implicit `MemoryDistributedCache`, because that implementation is local memory and can be mistaken for real L2.
```

- [ ] **Step 2: Update advanced caching docs**

In `docs/02_advanced.md`, replace the section that presents `SharedMemoryCache` as default L2 with:

````markdown
### Provider-backed L2 cache

Lib.Db uses local schema caching by default. Provider-backed L2 exists only when the host application registers an `IDistributedCache` implementation such as Redis, SQL Server, Postgres, or NCache before Lib.Db cache usage.

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = ["Default"];
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

Lib.Db never logs provider connection strings. Logs and diagnostics may report provider type presence only.
````

Add this shared-memory opt-in section:

````markdown
### SharedMemoryCache opt-in

`SharedMemoryCache` is a local-host optimization for advanced scenarios. It is not a universal distributed cache and is not enabled by default.

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = ["Default"];
    options.EnableSharedMemoryCache = true;
});

builder.Services.AddLibDbSharedMemoryCache();
```

Use this only when file permissions, local process trust boundaries, cleanup behavior, and OS-specific mutex behavior are acceptable for the deployment.
````

- [ ] **Step 3: Update API reference**

In `docs/03_api_reference.md`, update the options table entry:

```markdown
| `EnableSharedMemoryCache` | `bool?` | `null` | Explicit shared-memory opt-in flag. `null` does not register L2. |
```

Add `AddLibDbSharedMemoryCache` to the service registration table:

```markdown
| `AddLibDbSharedMemoryCache()` | `IServiceCollection` | Explicitly registers Lib.Db's local shared-memory cache adapter. |
```

- [ ] **Step 4: Update fluent API cache docs**

In `docs/05_fluent_api_reference.md`, replace the `WithHybridCacheAsync` topology sentence with:

```markdown
`WithHybridCacheAsync` uses local in-process caching by default. It uses L2 only when the host application has registered an `IDistributedCache` provider.
```

- [ ] **Step 5: Update cookbook**

In `docs/06_cookbook.md`, add this local-only recipe:

````markdown
## Recipe: Local-only HybridCache

Use this when the application does not need cross-process or cross-host cache reuse.

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = ["Default"];
});

builder.Services.AddLibDbHybridCache();
```

No `IDistributedCache` provider is registered. Lib.Db remains local-only and does not create an implicit `MemoryDistributedCache`.
````

Add this provider-backed recipe:

````markdown
## Recipe: Provider-backed L2 with Redis

```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = ["Default"];
});

builder.Services.AddLibDbHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(1)
    };
});
```

Redis credentials belong to the host application configuration. Lib.Db must not print them.
````

- [ ] **Step 6: Update history**

Add this entry to `docs/history.md`:

```markdown
- v2.4.0 cache registration is provider-neutral by default. Lib.Db no longer registers an implicit `MemoryDistributedCache`; missing provider means local-only `HybridCache` and schema snapshot behavior. Shared-memory cache is now an explicit local-host opt-in.
```

- [ ] **Step 7: Commit docs**

```powershell
git add .\docs\02_configuration.md .\docs\02_advanced.md .\docs\03_api_reference.md .\docs\05_fluent_api_reference.md .\docs\06_cookbook.md .\docs\history.md
git commit -m "docs: document provider-neutral cache configuration"
```

### Task 9: Final Verification

**Files:**
- Verify all changed files

- [ ] **Step 1: Run focused unit tests**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~CacheHostingCoverageTests|FullyQualifiedName~ServiceRegistrationHelpersTests|FullyQualifiedName~SchemaFlushServiceTests|FullyQualifiedName~RuntimeUtilityCoverageTests" -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS.

- [ ] **Step 2: Run full non-database guard-safe test pass**

Run:

```powershell
dotnet test .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj -p:LIBDB_SKIP_TEST_ENV_GUARD=true
```

Expected: PASS, except tests that intentionally require DB fixtures may skip or fail based on local fixture policy. If DB-backed tests fail because the verification database is not configured, run the focused tests from Step 1 and record the environment limitation in the final handoff.

- [ ] **Step 3: Run diff whitespace check**

Run:

```powershell
git diff --check
```

Expected: no output.

- [ ] **Step 4: Scan for obsolete cache claims**

Run:

```powershell
rg -n "MemoryDistributedCache 사용|Windows에서는 true|기본값.*Windows|SharedMemoryCache.*default|implicit MemoryDistributedCache|자동 활성화" Lib.Db docs Verification
```

Expected: no remaining claim that Lib.Db defaults to shared memory or implicit `MemoryDistributedCache`.

- [ ] **Step 5: Commit final cleanup if needed**

If Step 4 finds stale wording and the wording is corrected:

```powershell
git add .\Lib.Db .\Verification .\docs
git commit -m "chore: remove stale cache topology wording"
```

## Implementation Notes

- Do not remove `SharedMemoryCache`, `CacheMaintenanceService`, `EpochStore`, or `ProcessSlotAllocator` in this implementation. The v2.4.0 change is default registration behavior and documentation, not a physical package split.
- Do not add Redis, SQL Server distributed cache, Postgres, or NCache package references to `Lib.Db`. Those providers belong to the host application.
- Do not log provider connection strings or raw cache keys.
- Do not treat `MemoryDistributedCache` as production L2. It is local memory behind the `IDistributedCache` interface.
- Preserve caller-owned `IDistributedCache` registrations with `TryAdd` behavior.
- Keep `WithCacheAsync` and `WithCacheListAsync` as opt-in caller-owned query result cache helpers.

## Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-21-v240-provider-neutral-caching-implementation.md`.

Two execution options:

1. Subagent-Driven (recommended) - dispatch a fresh worker per task, review between tasks, and keep commits small.
2. Inline Execution - execute tasks in this session using `superpowers:executing-plans`, with checkpoints after each task group.
