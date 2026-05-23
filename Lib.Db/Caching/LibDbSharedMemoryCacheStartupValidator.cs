// ============================================================================
// File: Lib.Db/Caching/LibDbSharedMemoryCacheStartupValidator.cs
// Purpose: Startup validation for explicit Lib.Db SharedMemoryCache opt-in
// ============================================================================

#nullable enable

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lib.Db.Caching;

internal sealed class LibDbSharedMemoryCacheStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public LibDbSharedMemoryCacheStartupValidator(
        IServiceProvider serviceProvider,
        LibDbSharedMemoryOptInMarker marker)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(marker);

        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IDistributedCache[] registeredCaches = _serviceProvider
            .GetServices<IDistributedCache>()
            .ToArray();

        bool hasExactlyOneSharedMemoryCache =
            registeredCaches.Length == 1 &&
            registeredCaches[0] is SharedMemoryCache;

        if (!hasExactlyOneSharedMemoryCache)
        {
            throw new InvalidOperationException(
                "Lib.Db: IDistributedCache registrations changed after AddLibDbSharedMemoryCache(). " +
                "Use exactly one L2 provider. For Redis, SQL Server, Postgres, or another provider-backed L2, " +
                "do not call AddLibDbSharedMemoryCache().");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
