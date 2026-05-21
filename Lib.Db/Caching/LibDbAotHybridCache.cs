// ============================================================================
// File: Lib.Db/Caching/LibDbAotHybridCache.cs
// Purpose: Native AOT-safe local HybridCache implementation for Lib.Db schemas
// Target: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Collections.Concurrent;
using Lib.Db.Contracts.Models;

namespace Lib.Db.Caching;

/// <summary>
/// Minimal Native AOT-safe HybridCache implementation used when System.Text.Json
/// reflection serialization is disabled.
/// </summary>
/// <remarks>
/// The Microsoft.Extensions.Caching.Hybrid default implementation always roots a
/// reflection-backed JSON fallback factory. Lib.Db only needs HybridCache for
/// in-process schema payload caching under Native AOT, so this implementation
/// keeps the service available without distributed serialization.
/// </remarks>
internal sealed class LibDbAotHybridCache : HybridCache
{
    private static readonly TimeSpan s_defaultExpiration = TimeSpan.FromMinutes(5);
    private const int DefaultMaximumKeyLength = 1024;
    private const long DefaultMaximumPayloadBytes = 1024 * 1024;
    private const int MaxEntries = 4096;
    private const int MaxTagsPerEntry = 32;

    private readonly TimeSpan _defaultExpiration;
    private readonly int _maximumKeyLength;
    private readonly long _maximumPayloadBytes;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _tagVersions = new(StringComparer.Ordinal);
    private long _globalVersion;

    public LibDbAotHybridCache()
        : this(Microsoft.Extensions.Options.Options.Create(new HybridCacheOptions()))
    {
    }

    public LibDbAotHybridCache(Microsoft.Extensions.Options.IOptions<HybridCacheOptions> options)
        : this(options.Value)
    {
    }

    internal LibDbAotHybridCache(HybridCacheOptions? options)
    {
        _defaultExpiration = options?.DefaultEntryOptions?.Expiration ?? s_defaultExpiration;
        _maximumKeyLength = options?.MaximumKeyLength ?? DefaultMaximumKeyLength;
        _maximumPayloadBytes = options?.MaximumPayloadBytes ?? DefaultMaximumPayloadBytes;
    }

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> underlyingDataCallback,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsKeyTooLong(key))
        {
            return underlyingDataCallback(state, cancellationToken);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(key, out CacheEntry? entry)
            && entry.ExpiresAt > now
            && entry.ValueType == typeof(T)
            && !IsInvalidated(entry))
        {
            return new ValueTask<T>((T)entry.Value!);
        }

        string[] normalizedTags = NormalizeTags(tags);
        EntryVersion version = CaptureEntryVersion(normalizedTags);

        return CreateAndStoreAsync(
            key,
            state,
            underlyingDataCallback,
            options,
            normalizedTags,
            version,
            cancellationToken);
    }

    public override ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsKeyTooLong(key) || IsPayloadTooLarge(value))
        {
            _entries.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }

        TimeSpan expiration = options?.Expiration ?? _defaultExpiration;
        string[] normalizedTags = NormalizeTags(tags);
        PruneExpiredEntries();
        if (_entries.Count >= MaxEntries)
        {
            return ValueTask.CompletedTask;
        }

        _entries[key] = new CacheEntry(
            typeof(T),
            value,
            DateTimeOffset.UtcNow.Add(expiration),
            CaptureEntryVersion(normalizedTags),
            normalizedTags);

        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (tag == "*")
        {
            Interlocked.Increment(ref _globalVersion);
            return ValueTask.CompletedTask;
        }

        _tagVersions.AddOrUpdate(
            tag,
            static _ => 1,
            static (_, version) => version + 1);

        return ValueTask.CompletedTask;
    }

    private async ValueTask<T> CreateAndStoreAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> underlyingDataCallback,
        HybridCacheEntryOptions? options,
        string[] tags,
        EntryVersion version,
        CancellationToken cancellationToken)
    {
        T value = await underlyingDataCallback(state, cancellationToken).ConfigureAwait(false);
        if (IsPayloadTooLarge(value))
        {
            _entries.TryRemove(key, out _);
            return value;
        }

        PruneExpiredEntries();
        if (_entries.Count >= MaxEntries)
        {
            return value;
        }

        TimeSpan expiration = options?.Expiration ?? _defaultExpiration;
        _entries[key] = new CacheEntry(
            typeof(T),
            value,
            DateTimeOffset.UtcNow.Add(expiration),
            version,
            tags);
        return value;
    }

    private bool IsInvalidated(CacheEntry entry)
    {
        if (entry.Version.GlobalVersion != Volatile.Read(ref _globalVersion))
            return true;

        foreach (string tag in entry.Tags)
        {
            long currentVersion = _tagVersions.TryGetValue(tag, out long version)
                ? version
                : 0;
            long entryVersion = entry.Version.TagVersions.TryGetValue(tag, out long capturedVersion)
                ? capturedVersion
                : 0;

            if (currentVersion != entryVersion)
                return true;
        }

        return false;
    }

    private bool IsKeyTooLong(string key)
        => key.Length > _maximumKeyLength;

    private bool IsPayloadTooLarge<T>(T value)
        => TryEstimatePayloadBytes(value, out long bytes) && bytes > _maximumPayloadBytes;

    private static bool TryEstimatePayloadBytes<T>(T value, out long bytes)
    {
        bytes = 0;
        if (value is null)
            return true;

        switch (value)
        {
            case string text:
                bytes = System.Text.Encoding.UTF8.GetByteCount(text);
                return true;
            case byte[] buffer:
                bytes = buffer.LongLength;
                return true;
            case ArraySegment<byte> segment:
                bytes = segment.Count;
                return true;
            case Memory<byte> memory:
                bytes = memory.Length;
                return true;
            case ReadOnlyMemory<byte> memory:
                bytes = memory.Length;
                return true;
        }

        Type type = typeof(T);
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            bytes = 32;
            return true;
        }

        if (value is SpSchema or TvpSchema)
        {
            bytes = EstimateSchemaPayloadBytes(value);
            return true;
        }

        bytes = long.MaxValue;
        return true;
    }

    private static long EstimateSchemaPayloadBytes<T>(T value)
    {
        const long baseSchemaBytes = 64;
        return value switch
        {
            SpSchema schema => baseSchemaBytes
                + EstimateStringBytes(schema.Name)
                + (schema.Parameters.LongLength * 64)
                + schema.Parameters.Sum(static parameter =>
                    EstimateStringBytes(parameter.Name) + EstimateStringBytes(parameter.UdtTypeName)),
            TvpSchema schema => baseSchemaBytes
                + EstimateStringBytes(schema.Name)
                + (schema.Columns.LongLength * 48)
                + schema.Columns.Sum(static column => EstimateStringBytes(column.Name)),
            _ => long.MaxValue
        };
    }

    private static int EstimateStringBytes(string? value)
        => string.IsNullOrEmpty(value)
            ? 0
            : System.Text.Encoding.UTF8.GetByteCount(value);

    private void PruneExpiredEntries()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (KeyValuePair<string, CacheEntry> pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private EntryVersion CaptureEntryVersion(string[] tags)
    {
        var versions = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string tag in tags)
        {
            versions[tag] = _tagVersions.TryGetValue(tag, out long version)
                ? version
                : 0;
        }

        return new EntryVersion(Volatile.Read(ref _globalVersion), versions);
    }

    private static string[] NormalizeTags(IEnumerable<string>? tags)
        => tags is null
            ? []
            : tags.Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxTagsPerEntry)
                .ToArray();

    private sealed record CacheEntry(
        Type ValueType,
        object? Value,
        DateTimeOffset ExpiresAt,
        EntryVersion Version,
        string[] Tags);

    private sealed record EntryVersion(
        long GlobalVersion,
        IReadOnlyDictionary<string, long> TagVersions);
}
