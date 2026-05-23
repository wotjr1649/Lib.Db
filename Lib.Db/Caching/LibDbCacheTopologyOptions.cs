// ============================================================================
// File: Lib.Db/Caching/LibDbCacheTopologyOptions.cs
// Purpose: Provider-neutral cache topology classification options
// ============================================================================

#nullable enable

namespace Lib.Db.Caching;

internal sealed class LibDbCacheTopologyOptions
{
    public ISet<string> TrustedProviderTypeNames { get; } =
        new HashSet<string>(StringComparer.Ordinal);
}

internal sealed record LibDbTrustedDistributedCacheProvider(string ProviderTypeName);
