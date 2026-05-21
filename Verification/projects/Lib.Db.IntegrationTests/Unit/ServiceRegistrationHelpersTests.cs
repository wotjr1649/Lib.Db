// ============================================================================
// 파일: Unit/ServiceRegistrationHelpersTests.cs
// 설명: 서비스 등록 경로의 multi-instance fail-closed 회귀 테스트
// ============================================================================

using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Extensions;
using Lib.Db.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class ServiceRegistrationHelpersTests
{
    [Fact]
    public void RegisterConditionalSharedMemoryCache_ShouldNotRegisterDistributedCacheByDefault()
    {
        ServiceCollection services = BuildServices(CreateOptions(enableSharedMemoryCache: null));

        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<IDistributedCache>().Should().BeNull();
    }

    [Fact]
    public void RegisterConditionalSharedMemoryCache_ShouldUsePassiveSlotAllocatorByDefault()
    {
        ServiceCollection services = BuildServices(CreateOptions(enableSharedMemoryCache: null));

        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);

        using ServiceProvider provider = services.BuildServiceProvider();

        IProcessSlotAllocator allocator = provider.GetRequiredService<IProcessSlotAllocator>();

        allocator.HasSlot.Should().BeFalse();
        allocator.SlotId.Should().Be(-1);
    }

    [Fact]
    public void ProcessSlotAllocator_ShouldRequireSharedMemoryOptIn_WhenSharedMemoryFlagIsEnabled()
    {
        using ServiceProvider provider = BuildCoreProvider(CreateOptions(enableSharedMemoryCache: true));

        Action act = () => provider.GetRequiredService<IProcessSlotAllocator>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*EnableSharedMemoryCache=true*AddLibDbSharedMemoryCache*");
    }

    [Fact]
    public void AddLibDbSharedMemoryCache_ShouldFailClosed_WhenPrimaryConnectionNameIsMissing()
    {
        using ServiceProvider provider = BuildSharedMemoryOptInProvider(CreateOptionsWithMissingPrimary());

        Action act = () => provider.GetRequiredService<IDistributedCache>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SharedMemoryCache*Primary*ConnectionStrings*");
    }

    [Fact]
    public void AddLibDbSharedMemoryCache_ShouldFailClosedForProcessSlot_WhenPrimaryConnectionNameIsMissing()
    {
        using ServiceProvider provider = BuildSharedMemoryOptInProvider(CreateOptionsWithMissingPrimary());

        Action act = () => provider.GetRequiredService<IProcessSlotAllocator>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ProcessSlotAllocator*Primary*ConnectionStrings*");
    }

    [Fact]
    public void AddLibDbSharedMemoryCache_ShouldRejectExistingDistributedCacheProvider()
    {
        ServiceCollection services = BuildServices(CreateOptions(enableSharedMemoryCache: true));
        services.AddSingleton<IDistributedCache>(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        Action act = () => services.AddLibDbSharedMemoryCache();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddLibDbSharedMemoryCache*IDistributedCache*");
    }

    [Fact]
    public void AddLibDbSharedMemoryCache_ShouldRegisterSharedMemoryServices()
    {
        ServiceCollection services = BuildServices(CreateOptions(enableSharedMemoryCache: true));

        services.AddLibDbSharedMemoryCache();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IDistributedCache));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IProcessSlotAllocator));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(LibDbSharedMemoryOptInMarker));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(CacheMaintenanceService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(LibDbSharedMemoryCacheStartupValidator));
    }

    [Fact]
    public async Task SharedMemoryCacheStartupValidator_ShouldRejectProviderAddedAfterOptIn()
    {
        ServiceCollection services = BuildServices(CreateOptions(enableSharedMemoryCache: true));
        services.AddLibDbSharedMemoryCache();
        services.AddSingleton<IDistributedCache>(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService validator = provider
            .GetServices<IHostedService>()
            .OfType<LibDbSharedMemoryCacheStartupValidator>()
            .Single();

        Func<Task> act = () => validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IDistributedCache*after AddLibDbSharedMemoryCache*");
    }

    [Fact]
    public void AddLibDb_ShouldNotRegisterDistributedCache_WhenProviderIsNotConfigured()
    {
        ServiceCollection services = new();

        services.AddLibDb(options =>
        {
            LibDbOptions configured = CreateOptions(enableSharedMemoryCache: null);
            options.ConnectionStringNames = configured.ConnectionStringNames;
            options.ConnectionStrings = configured.ConnectionStrings;
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<IDistributedCache>().Should().BeNull();
    }

    [Fact]
    public void AddLibDbSharedMemoryCache_ShouldForceSharedMemoryFlagForDiagnostics()
    {
        ServiceCollection services = new();

        services.AddLibDb(options =>
        {
            LibDbOptions configured = CreateOptions(enableSharedMemoryCache: false);
            options.ConnectionStringNames = configured.ConnectionStringNames;
            options.ConnectionStrings = configured.ConnectionStrings;
            options.EnableSharedMemoryCache = configured.EnableSharedMemoryCache;
        });
        services.AddLibDbSharedMemoryCache();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<LibDbOptions>>()
            .Value
            .EnableSharedMemoryCache
            .Should()
            .BeTrue();
    }

    private static ServiceProvider BuildCoreProvider(LibDbOptions options)
    {
        ServiceCollection services = BuildServices(options);

        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildSharedMemoryOptInProvider(LibDbOptions options)
    {
        ServiceCollection services = BuildServices(options);

        ServiceRegistrationHelpers.RegisterConditionalSharedMemoryCache(services);
        services.AddLibDbSharedMemoryCache();

        return services.BuildServiceProvider();
    }

    private static ServiceCollection BuildServices(LibDbOptions options)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IOptions<LibDbOptions>>(Options.Create(options));
        services.AddSingleton(options);

        return services;
    }

    private static LibDbOptions CreateOptions(bool? enableSharedMemoryCache)
    {
        LibDbOptions options = new()
        {
            ConnectionStringNames = ["Primary"],
            EnableSharedMemoryCache = enableSharedMemoryCache
        };

        options.ConnectionStrings["Primary"] =
            "Server=(localdb)\\MSSQLLocalDB;Database=PrimaryDb;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        return options;
    }

    private static LibDbOptions CreateOptionsWithMissingPrimary()
    {
        LibDbOptions options = new()
        {
            ConnectionStringNames = ["Primary"],
            EnableSharedMemoryCache = true
        };

        options.ConnectionStrings["Secondary"] =
            "Server=(localdb)\\MSSQLLocalDB;Database=SecondaryDb;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

        return options;
    }
}
