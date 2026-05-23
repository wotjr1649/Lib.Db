// ============================================================================
// File: Lib.Db/Diagnostics/LibDbCacheTopologyDiagnostics.cs
// Purpose: Redacted cache topology diagnostics for Lib.Db health surfaces
// ============================================================================

#nullable enable

using Lib.Db.Caching;

namespace Lib.Db.Diagnostics;

internal sealed record LibDbCacheTopologySnapshot(
    string Kind,
    bool HasVerifiedProviderBackedL2,
    string? ProviderTypeName,
    bool SharedMemoryEnabled,
    bool EpochCoordinationEnabled,
    IReadOnlyList<string> Warnings);

internal static class LibDbCacheTopologyDiagnostics
{
    public static LibDbCacheTopologySnapshot CreateSnapshot(
        LibDbCacheTopologyState topology,
        bool sharedMemoryEnabled,
        bool epochCoordinationEnabled)
    {
        ArgumentNullException.ThrowIfNull(topology);

        List<string> warnings = [];

        if (topology.Kind == LibDbCacheTopologyKind.UnverifiedDistributedCache)
        {
            warnings.Add("An IDistributedCache is registered, but Lib.Db has not verified it as provider-backed L2.");
        }

        if (topology.Kind == LibDbCacheTopologyKind.LocalMemoryDistributedCache)
        {
            warnings.Add("MemoryDistributedCache is local memory and is not production distributed L2.");
        }

        if (topology.Kind == LibDbCacheTopologyKind.LocalOnly)
        {
            warnings.Add("No provider-backed L2 cache is registered; Lib.Db is running local-only.");
        }

        return new LibDbCacheTopologySnapshot(
            topology.Kind.ToString(),
            topology.HasVerifiedProviderBackedL2,
            topology.ProviderTypeName,
            sharedMemoryEnabled,
            epochCoordinationEnabled,
            warnings);
    }
}
