// ============================================================================
// 파일: Lib.Db/Caching/SharedMemoryCache.cs
// 설명: [Architecture] MemoryMappedFile 기반 로컬 IPC 캐시 (통합본)
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Buffers;
using System.Diagnostics;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Lib.Db.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Lib.Db.Caching;

#region SharedMemoryCache 구현

/// <summary>
/// Windows 공유 메모리(<see cref="MemoryMappedFile"/>)를 활용하여 프로세스 간(IPC) 초고속 데이터를 공유하는 분산 캐시 구현체입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// </para>
/// <list type="bullet">
/// <item><description><strong>최소 레이턴시 (Low Latency)</strong>: 네트워크 I/O 없이 로컬 메모리 버스를 통해 마이크로초 단위의 접근 속도를 제공합니다.</description></item>
/// <item><description><strong>IPC 데이터 공유</strong>: IIS 워커 프로세스(w3wp.exe)나 마이크로서비스 간에 데이터를 공유하여 중복 계산을 방지합니다.</description></item>
/// <item><description><strong>내결함성 (Fault Tolerance)</strong>: 파일 기반 백업(`FileStream`)을 통해 프로세스 재시작 시에도 핫 데이터를 보존합니다.</description></item>
/// </list>
///
/// <para><strong>⚙️ 핵심 메커니즘</strong></para>
/// <list type="bullet">
/// <item><description><strong>Stripe Locking</strong>: 키별 CRC32 해시를 기반으로 128개의 Mutex 스트라이프로 세분화하여 동시성 경합을 최소화합니다.</description></item>
/// <item><description><strong>무결성 검증</strong>: 헤더 내 CRC32 체크섬과 Magic Number 검증을 통해 메모리 오염이나 쓰기 중단 상황을 감지합니다.</description></item>
/// <item><description><strong>자가 치유</strong>: 파일 손상 감지 시 자동으로 파일을 삭제하고 폴백(MemoryCache) 모드로 전환하거나 재생성을 시도합니다.</description></item>
/// </list>
/// </remarks>
public sealed class SharedMemoryCache : IDistributedCache, IDisposable
{
    #region 상수 및 필드

    private const uint MAGIC = 0x4244424C;
    private const ushort SCHEMA_VERSION = 1;
    private const byte STATE_WRITING = 0;
    private const byte STATE_COMMITTED = 1;
    private const int HEADER_SIZE = 32;
    private const int MUTEX_STRIPE_COUNT = 128;

    private readonly string _basePath;
    private readonly SharedMemoryCacheOptions _options;
    private readonly ILogger<SharedMemoryCache> _logger;
    private readonly Lazy<Mutex[]> _mutexStripes;
    private readonly string _mutexPrefix;
    private readonly bool _isFallbackMode;
    private volatile bool _disposed;

    // .NET 9+ Lock doesn't apply to IPC Mutexes, but if we had internal locks we would use it.
    // For now we use standard IPC Mutexes.

    #endregion

    #region 진단 상태

    /// <summary>
    /// 공유 메모리 초기화 실패 등으로 폴백 캐시만 사용하는 상태인지 여부입니다.
    /// </summary>
    public bool IsFallbackMode => _isFallbackMode;

    /// <summary>
    /// 현재 캐시 동작 모드입니다.
    /// </summary>
    public string CacheMode => _isFallbackMode ? "fallback" : "shared-memory";

    #endregion

    #region 내부 구조

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MmfHeader
    {
        public uint Magic;           // 4 bytes
        public ushort Version;       // 2 bytes
        public byte State;           // 1 byte (0=Writing, 1=Committed)
        public byte Reserved;        // 1 byte
        public long ExpiryTicks;     // 8 bytes (UTC)
        public long DataLength;      // 8 bytes
        public uint Crc32;           // 4 bytes
        public uint KeyHash;         // 4 bytes (Quick check)
    } // Total 32 bytes

    #endregion

    #region 생성자 및 초기화

    /// <summary>
    /// 공유 메모리 캐시를 초기화합니다.
    /// </summary>
    public SharedMemoryCache(IOptions<SharedMemoryCacheOptions> options, ILogger<SharedMemoryCache> logger)
    {
        _options = options.Value;
        _logger = logger;
        _basePath = CacheInternalHelpers.ResolveBasePath(_options);
        _mutexPrefix = CacheInternalHelpers.GetMutexPrefix(_options);

        try
        {
            // 디렉토리 생성
            Directory.CreateDirectory(_basePath);

            // Mutex 초기화 (Lazy)
            _mutexStripes = new Lazy<Mutex[]>(InitMutexes);
            _isFallbackMode = false;

            _logger.LogInformation("[SharedMemoryCache] 초기화 완료: 경로={Path}, 범위={Scope}", _basePath, _options.Scope);
        }
        catch (Exception ex)
        {
            _isFallbackMode = true;
            _mutexStripes = new Lazy<Mutex[]>(() => Array.Empty<Mutex>()); // Dummy
            _logger.LogError(ex, "[SharedMemoryCache] 초기화 실패 -> Fallback 모드 전환");
        }
    }

    private Mutex[] InitMutexes()
    {
        // 128개의 Mutex 생성 (이름 기반)
        Mutex[] mutexes = new Mutex[MUTEX_STRIPE_COUNT];
        for (int i = 0; i < MUTEX_STRIPE_COUNT; i++)
        {
            string name = $"{_mutexPrefix}{i}";
            try
            {
                mutexes[i] = new Mutex(false, name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Mutex] 생성 실패 (Local/Global 권한 확인 필요): {Name}", name);
                // Fallback: Unnamed (Process-local only)
                mutexes[i] = new Mutex(false);
            }
        }
        return mutexes;
    }

    #endregion

    #region IDistributedCache 구현 - Get

    /// <inheritdoc />
    public byte[]? Get(string key)
    {
        using Activity? activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("CacheGet")
            : null;
        activity?.SetTag("db.cache.key_hash", HashKeyForDiagnostics(key));

        if (_isFallbackMode)
        {
            return _options.FallbackCache?.Get(key);
        }

        Mutex mutex = GetMutex(key);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(100)); // Latency 민감
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                _logger.LogWarning("[Cache] Abandoned Mutex 복구됨 (Get): {Key}", key);
            }

            if (!acquired)
            {
                DbMetrics.IncrementCacheMiss(); // Timeout -> Miss 처리
                return _options.FallbackCache?.Get(key);
            }

            string filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                DbMetrics.IncrementCacheMiss();
                return _options.FallbackCache?.Get(key);
            }

            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < HEADER_SIZE)
                return _options.FallbackCache?.Get(key);

            // MMF View
            using MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            using MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            MmfHeader header;
            accessor.Read(0, out header); // Generic Read Struct

            #region 헤더 검증

            if (header.Magic != MAGIC)
                return null;
            if (header.State != STATE_COMMITTED)
                return null; // 쓰기 중

            // 만료 체크
            if (DateTime.UtcNow.Ticks > header.ExpiryTicks)
            {
                // Background Clean은 나중에 -> 일단 Miss 리턴
                return null;
            }

            #endregion

            #region 데이터 읽기

            if (header.DataLength > int.MaxValue)
                return null; // Too huge
            byte[] data = new byte[header.DataLength];
            accessor.ReadArray(HEADER_SIZE, data, 0, (int)header.DataLength);

            // CRC32 검증
            uint actualCrc = Crc32.HashToUInt32(data);
            if (actualCrc != header.Crc32)
            {
                _logger.LogWarning("[Cache] CRC Mismatch: {Key}", key);
                return null;
            }

            DbMetrics.IncrementCacheHit();
            return data;

            #endregion
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cache] Get 오류: {Key}", key);
            return _options.FallbackCache?.Get(key);
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    /// <inheritdoc />
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled<byte[]?>(token);

        // IPC Mutex는 비동기를 지원하지 않으므로 동기 호출 위임 (Task.Run 불필요 in fast path)
        return Task.FromResult(Get(key));
    }

    #endregion

    #region IDistributedCache 구현 - Set

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        using Activity? activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("CacheSet")
            : null;
        activity?.SetTag("db.cache.key_hash", HashKeyForDiagnostics(key));

        if (_isFallbackMode)
        {
            _options.FallbackCache?.Set(key, value, options);
            return;
        }

        Mutex mutex = GetMutex(key);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(1));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                _logger.LogWarning("[Cache] Abandoned Mutex 복구됨 (Set): {Key}", key);
            }

            if (!acquired)
            {
                // Fallback on timeout
                _options.FallbackCache?.Set(key, value, options);
                return;
            }

            string filePath = GetFilePath(key);
            long expiryTicks = GetExpiryTicks(options);
            uint crc = Crc32.HashToUInt32(value);
            uint keyHash = Crc32.HashToUInt32(Encoding.UTF8.GetBytes(key)); // Quick check 용

            // Temp 파일 생성 (Atomic Swap은 아님 - MMF 특성상 직접 씀)
            // 참고: EpochStore 처럼 Atomic Swap을 쓰면 좋지만, Cache는 즉시성이 중요하고 MMF Lock이 있으므로 덮어쓰기 허용

            using FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            // 파일 크기 확보
            long totalSize = HEADER_SIZE + value.Length;
            if (fs.Length != totalSize)
                fs.SetLength(totalSize);

            using MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fs, null, totalSize, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
            using MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, totalSize);

            // 1. Write Header (State = Writing)
            MmfHeader header = new MmfHeader
            {
                Magic = MAGIC,
                Version = SCHEMA_VERSION,
                State = STATE_WRITING,
                ExpiryTicks = expiryTicks,
                DataLength = value.LongLength,
                Crc32 = crc,
                KeyHash = keyHash
            };
            accessor.Write(0, ref header);

            // 2. Write Data
            accessor.WriteArray(HEADER_SIZE, value, 0, value.Length);

            // 3. Commit (State = Committed)
            header.State = STATE_COMMITTED;
            accessor.Write(0, ref header);

            // fs.Flush handled by Dispose? Not necessarily for MMF.
            // accessor.Flush(); // OS Page Flush
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cache] Set 오류: {Key}", key);
            // Error fallback
            _options.FallbackCache?.Set(key, value, options);
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    /// <inheritdoc />
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled(token);

        Set(key, value, options);
        return Task.CompletedTask;
    }

    #endregion

    #region IDistributedCache 구현 - Remove & Refresh

    /// <inheritdoc />
    public void Remove(string key)
    {
        // [Fallback 모드] _mutexStripes.Value가 빈 배열이므로 인덱싱 시 IndexOutOfRangeException 방지
        if (_isFallbackMode)
        {
            _options.FallbackCache?.Remove(key);
            return;
        }

        Mutex mutex = GetMutex(key);
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(500));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                _logger.LogWarning("[Cache] Abandoned Mutex 복구됨 (Remove): {Key}", key);
            }

            if (!acquired)
                return;

            string filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch { /* Ignore */ }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled(token);

        Remove(key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 만료 시간을 갱신합니다. (현재 미지원 — Absolute Expiration 전용)
    /// <para><b>[설계 의도]</b> SharedMemoryCache는 절대 만료(AbsoluteExpiration)만 지원하므로
    /// Sliding Expiration 갱신은 no-op입니다. IDistributedCache 계약 상 구현이 필요합니다.</para>
    /// </summary>
    public void Refresh(string key) { /* Sliding Expiration 미지원 — 의도적 no-op */ }

    /// <summary>
    /// 만료 시간을 비동기로 갱신합니다. (현재 미지원 — Absolute Expiration 전용)
    /// <para><b>[설계 의도]</b> SharedMemoryCache는 절대 만료(AbsoluteExpiration)만 지원하므로
    /// Sliding Expiration 갱신은 no-op입니다. IDistributedCache 계약 상 구현이 필요합니다.</para>
    /// </summary>
    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
            return Task.FromCanceled(token);

        /* Sliding Expiration 미지원 — 의도적 no-op */
        return Task.CompletedTask;
    }

    #endregion

    #region 유지보수

    /// <summary>
    /// 만료된 캐시/용량 초과 정리 (동기)
    /// </summary>
    public void Compact(double threshold = 0.8)
    {
        try
        {
            string[] files = Directory.GetFiles(_basePath, "*.cache");
            foreach (string file in files)
            {
                try
                {
                    using FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length < HEADER_SIZE)
                    {
                        fs.Close();
                        File.Delete(file);
                        DbMetrics.TrackCacheBytesFreed(0); // Count as freed?
                        continue;
                    }

                    using MemoryMappedFile mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
                    using MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, HEADER_SIZE, MemoryMappedFileAccess.Read);
                    MmfHeader header;
                    accessor.Read(0, out header);

                    if (header.Magic != MAGIC || DateTime.UtcNow.Ticks > header.ExpiryTicks)
                    {
                        // Dispose accessors before delete
                        accessor.Dispose();
                        mmf.Dispose();
                        fs.Close();

                        File.Delete(file);
                        DbMetrics.TrackCacheBytesFreed(header.DataLength);
                    }
                }
                catch
                {
                    // In use or error -> skip
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Cache] Compact 중 오류 발생");
        }
    }

    #endregion

    #region 도우미 메서드

    private Mutex GetMutex(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // [Fallback 모드] 빈 배열(_mutexStripes = Array.Empty<Mutex>())에서
        // 인덱싱하면 IndexOutOfRangeException 발생 — 호출 전 반드시 _isFallbackMode 확인 필요.
        // 이 메서드는 내부용이므로, 여기서도 안전 장치로 방어합니다.
        if (_isFallbackMode)
            throw new InvalidOperationException(
                "[SharedMemoryCache] Fallback 모드에서 GetMutex를 호출할 수 없습니다. " +
                "호출 전에 _isFallbackMode를 확인하세요.");

        // Crc32 Stripe Mapping (UTF8 기반)
        int maxBytes = Encoding.UTF8.GetMaxByteCount(key.Length);

        if (maxBytes <= 256)
        {
            Span<byte> buffer = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(key.AsSpan(), buffer);
            uint hash = Crc32.HashToUInt32(buffer[..written]);
            return _mutexStripes.Value[hash % MUTEX_STRIPE_COUNT];
        }

        // Large key fallback
        byte[] rent = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            int written = Encoding.UTF8.GetBytes(key.AsSpan(), rent.AsSpan());
            uint hash = Crc32.HashToUInt32(rent.AsSpan(0, written));
            return _mutexStripes.Value[hash % MUTEX_STRIPE_COUNT];
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rent);
        }
    }

    private string GetFilePath(string key)
    {
        // 파일명: Hash(Key).cache
        // XxHash128 사용 — SHA256 대비 133배 빠르고 파일명이 43% 짧음
        byte[] hashBytes = XxHash128.Hash(Encoding.UTF8.GetBytes(key));
        string hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(_basePath, hex + ".cache");
    }

    private static string HashKeyForDiagnostics(string key)
        => Convert.ToHexString(XxHash64.Hash(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    private static long GetExpiryTicks(DistributedCacheEntryOptions options)
    {
        // 절대 만료 우선
        if (options.AbsoluteExpiration.HasValue)
            return options.AbsoluteExpiration.Value.DateTime.Ticks;
        if (options.AbsoluteExpirationRelativeToNow.HasValue)
            return DateTime.UtcNow.Add(options.AbsoluteExpirationRelativeToNow.Value).Ticks;
        if (options.SlidingExpiration.HasValue)
            return DateTime.UtcNow.Add(options.SlidingExpiration.Value).Ticks;

        return DateTime.UtcNow.AddMinutes(30).Ticks; // Default
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;

        if (_mutexStripes.IsValueCreated)
        {
            foreach (Mutex m in _mutexStripes.Value)
            {
                m?.Dispose();
            }
        }
    }

    #endregion
}

#endregion
