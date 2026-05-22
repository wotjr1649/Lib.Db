// ============================================================================
// File: Lib.Db/Caching/LibDbCacheTopology.cs
// Purpose: Provider-neutral cache topology detection for Lib.Db registration
// ============================================================================

#nullable enable

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lib.Db.Caching;

internal enum LibDbCacheTopologyKind
{
    LocalOnly,
    LocalMemoryDistributedCache,
    VerifiedProviderBackedL2,
    UnverifiedDistributedCache,
    SharedMemoryOptIn
}

internal sealed record LibDbCacheTopologyState(
    LibDbCacheTopologyKind Kind,
    string? ProviderTypeName,
    bool HasVerifiedProviderBackedL2)
{
    public static LibDbCacheTopologyState LocalOnly { get; } =
        new(
            LibDbCacheTopologyKind.LocalOnly,
            ProviderTypeName: null,
            HasVerifiedProviderBackedL2: false);
}

internal static class LibDbCacheTopologyDetector
{
    public static LibDbCacheTopologyState Detect(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        LibDbCacheTopologyOptions options = new();
        LibDbCacheTopologyOptions? registeredOptions =
            serviceProvider.GetService<LibDbCacheTopologyOptions>();
        if (registeredOptions is not null)
        {
            foreach (string trustedProviderTypeName in registeredOptions.TrustedProviderTypeNames)
            {
                options.TrustedProviderTypeNames.Add(trustedProviderTypeName);
            }
        }

        foreach (LibDbTrustedDistributedCacheProvider trustedProvider in
            serviceProvider.GetServices<LibDbTrustedDistributedCacheProvider>())
        {
            options.TrustedProviderTypeNames.Add(trustedProvider.ProviderTypeName);
        }

        return Detect(serviceProvider.GetService<IDistributedCache>(), options);
    }

    public static LibDbCacheTopologyState Detect(
        IDistributedCache? cache,
        LibDbCacheTopologyOptions? options = null)
    {
        if (cache is null)
            return LibDbCacheTopologyState.LocalOnly;

        options ??= new LibDbCacheTopologyOptions();
        Type cacheType = cache.GetType();
        string providerTypeName = string.IsNullOrWhiteSpace(cacheType.FullName)
            ? cacheType.Name
            : cacheType.FullName!;
        string? providerAssemblyQualifiedName = cacheType.AssemblyQualifiedName;

        return cache switch
        {
            SharedMemoryCache => new(
                LibDbCacheTopologyKind.SharedMemoryOptIn,
                providerTypeName,
                HasVerifiedProviderBackedL2: false),

            MemoryDistributedCache => new(
                LibDbCacheTopologyKind.LocalMemoryDistributedCache,
                providerTypeName,
                HasVerifiedProviderBackedL2: false),

            _ when IsKnownProvider(providerTypeName) ||
                options.TrustedProviderTypeNames.Contains(providerTypeName) ||
                (providerAssemblyQualifiedName is not null &&
                    options.TrustedProviderTypeNames.Contains(providerAssemblyQualifiedName)) => new(
                LibDbCacheTopologyKind.VerifiedProviderBackedL2,
                providerTypeName,
                HasVerifiedProviderBackedL2: true),

            _ => new(
                LibDbCacheTopologyKind.UnverifiedDistributedCache,
                providerTypeName,
                HasVerifiedProviderBackedL2: false)
        };
    }

    private static bool IsKnownProvider(string providerTypeName)
    {
        return providerTypeName.StartsWith(
                "Microsoft.Extensions.Caching.StackExchangeRedis.",
                StringComparison.Ordinal) ||
            providerTypeName.StartsWith(
                "Microsoft.Extensions.Caching.SqlServer.",
                StringComparison.Ordinal) ||
            providerTypeName.StartsWith(
                "Microsoft.Extensions.Caching.Postgres.",
                StringComparison.Ordinal) ||
            providerTypeName.Contains(".NCache.", StringComparison.OrdinalIgnoreCase);
    }
}
