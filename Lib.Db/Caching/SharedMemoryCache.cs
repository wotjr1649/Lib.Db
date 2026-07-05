// ============================================================================
// 파일: Lib.Db/Caching/SharedMemoryCache.cs
// 설명: [Architecture] MemoryMappedFile 기반 로컬 IPC 캐시 (통합본)
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lib.Db.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Lib.Db.Caching;

#region SharedMemoryCache 구현

/// <summary>
/// <see cref="MemoryMappedFile"/> 기반 파일 매핑을 활용하여 동일 호스트 프로세스 간(IPC) 데이터를 공유하는 캐시 구현체입니다.
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
/// <item><description><strong>기밀성/무결성 보호</strong>: AES-GCM payload 보호, 헤더 HMAC 태그, CRC32 체크섬, Magic Number 검증을 통해 로컬 파일 노출과 변조, 메모리 오염, 쓰기 중단 상황을 감지합니다.</description></item>
/// <item><description><strong>자가 치유</strong>: 파일 손상 감지 시 자동으로 파일을 삭제하고 폴백(MemoryCache) 모드로 전환하거나 재생성을 시도합니다.</description></item>
/// </list>
/// </remarks>
public sealed class SharedMemoryCache : IDistributedCache, IDisposable
{
    #region 상수 및 필드

    private const uint MAGIC = 0x4244424C;
    private const ushort SCHEMA_VERSION = 3;
    private const byte STATE_WRITING = 0;
    private const byte STATE_COMMITTED = 1;
    private const int HEADER_METADATA_SIZE = 32;
    private const int MAC_SIZE = 32;
    private const int AES_GCM_NONCE_SIZE = 12;
    private const int AES_GCM_TAG_SIZE = 16;
    private const int PROTECTED_PAYLOAD_OVERHEAD = AES_GCM_NONCE_SIZE + AES_GCM_TAG_SIZE;
    private const int LOCAL_KEY_MATERIAL_SIZE = 32;
    private const int HEADER_SIZE = HEADER_METADATA_SIZE + MAC_SIZE;
    private const int MUTEX_STRIPE_COUNT = 128;
    private const string LOCAL_KEY_MATERIAL_FILE_NAME = "shared-memory-cache.key";

    private static readonly byte[] IntegrityKeyDomain = Encoding.UTF8.GetBytes("Lib.Db.SharedMemoryCache.IntegrityKey.v1");
    private static readonly byte[] IntegrityMacDomain = Encoding.UTF8.GetBytes("Lib.Db.SharedMemoryCache.File.v3");
    private static readonly byte[] PayloadProtectionKeyDomain = Encoding.UTF8.GetBytes("Lib.Db.SharedMemoryCache.PayloadProtectionKey.v1");

    private readonly string _basePath;
    private readonly SharedMemoryCacheOptions _options;
    private readonly ILogger<SharedMemoryCache> _logger;
    private readonly Lazy<Mutex[]> _mutexStripes;
    private readonly Lazy<Mutex> _quotaMutex;
    private readonly string _mutexPrefix;
    private readonly string _mutexScope;
    private readonly byte[] _integrityKey;
    private readonly byte[] _payloadProtectionKey;
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
        _basePath = CacheInternalHelpers.ResolveStoragePath(_options);
        _mutexPrefix = CacheInternalHelpers.GetMutexPrefix(_options);
        _mutexScope = _options.Scope.ToString();
        _integrityKey = [];
        _payloadProtectionKey = [];
        _quotaMutex = new Lazy<Mutex>(InitQuotaMutex);

        try
        {
            if (!AesGcm.IsSupported)
                throw new PlatformNotSupportedException("AES-GCM is not supported on this platform.");

            // 디렉토리 생성
            Directory.CreateDirectory(_basePath);
            byte[] localKeyMaterial = LoadOrCreateLocalKeyMaterial();
            try
            {
                _integrityKey = CreateIntegrityKey(_options.IsolationKey, localKeyMaterial);
                _payloadProtectionKey = CreatePayloadProtectionKey(_options.IsolationKey, localKeyMaterial);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(localKeyMaterial);
            }

            // Mutex 초기화 (Lazy)
            _mutexStripes = new Lazy<Mutex[]>(InitMutexes);
            _ = _quotaMutex.Value;
            _isFallbackMode = false;

            _logger.LogInformation(
                "[SharedMemoryCache] 초기화 완료: 범위={Scope}, 경로해시={PathHash}",
                _options.Scope, HashKeyForDiagnostics(_basePath));
        }
        catch (Exception ex)
        {
            CryptographicOperations.ZeroMemory(_integrityKey);
            CryptographicOperations.ZeroMemory(_payloadProtectionKey);
            _isFallbackMode = true;
            _mutexStripes = new Lazy<Mutex[]>(() => Array.Empty<Mutex>()); // Dummy
            _logger.LogError(
                "[SharedMemoryCache] 초기화 실패 -> Fallback 모드 전환 (ErrorType: {ErrorType})",
                ex.GetType().Name);
        }
    }

    private Mutex InitQuotaMutex()
    {
        string name = $"{_mutexPrefix}quota";
        try
        {
            return new Mutex(false, name);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "[Mutex] quota mutex 생성 실패 -> Fallback 모드 전환 (Scope: {Scope}, ErrorType: {ErrorType})",
                _mutexScope,
                ex.GetType().Name);
            throw;
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
                _logger.LogWarning(
                    "[Mutex] 생성 실패 (Scope: {Scope}, Stripe: {Stripe}, ErrorType: {ErrorType})",
                    _mutexScope,
                    i,
                    ex.GetType().Name);
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
                _logger.LogWarning(
                    "[Cache] Abandoned Mutex 복구됨 (Get): {KeyHash}",
                    HashKeyForDiagnostics(key));
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
            if (header.Version != SCHEMA_VERSION)
                return null;
            if (header.State != STATE_COMMITTED)
                return null; // 쓰기 중
            if (header.KeyHash != ComputeKeyHash(key))
                return null;

            // 만료 체크
            if (DateTime.UtcNow.Ticks > header.ExpiryTicks)
            {
                // Background Clean은 나중에 -> 일단 Miss 리턴
                return null;
            }

            #endregion

            #region 데이터 읽기

            if (header.DataLength < 0 ||
                header.DataLength > int.MaxValue ||
                HEADER_SIZE + header.DataLength > fs.Length)
            {
                return null;
            }

            byte[] storedMac = new byte[MAC_SIZE];
            accessor.ReadArray(HEADER_METADATA_SIZE, storedMac, 0, MAC_SIZE);
            byte[] protectedData = new byte[(int)header.DataLength];
            accessor.ReadArray(HEADER_SIZE, protectedData, 0, (int)header.DataLength);

            try
            {
                // CRC32 검증
                uint actualCrc = Crc32.HashToUInt32(protectedData);
                if (actualCrc != header.Crc32)
                {
                    _logger.LogWarning(
                        "[Cache] CRC Mismatch: {KeyHash}",
                        HashKeyForDiagnostics(key));
                    return null;
                }

                if (!VerifyIntegrity(header, key, protectedData, storedMac))
                {
                    _logger.LogWarning(
                        "[Cache] Integrity check failed: {KeyHash}",
                        HashKeyForDiagnostics(key));
                    return null;
                }

                byte[]? data = UnprotectPayload(protectedData);
                if (data is null)
                {
                    _logger.LogWarning(
                        "[Cache] Payload protection check failed: {KeyHash}",
                        HashKeyForDiagnostics(key));
                    return null;
                }

                DbMetrics.IncrementCacheHit();
                return data;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedData);
            }

            #endregion
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "[Cache] Get 오류: {KeyHash} (ErrorType: {ErrorType})",
                HashKeyForDiagnostics(key), ex.GetType().Name);
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
                _logger.LogWarning(
                    "[Cache] Abandoned Mutex 복구됨 (Set): {KeyHash}",
                    HashKeyForDiagnostics(key));
            }

            if (!acquired)
            {
                _logger.LogWarning(
                    "[Cache] Set mutex timed out: {KeyHash}",
                    HashKeyForDiagnostics(key));
                return;
            }

            Mutex quotaMutex = _quotaMutex.Value;
            bool quotaAcquired = false;
            try
            {
                try
                {
                    quotaAcquired = quotaMutex.WaitOne(TimeSpan.FromSeconds(1));
                }
                catch (AbandonedMutexException)
                {
                    quotaAcquired = true;
                    _logger.LogWarning(
                        "[Cache] Abandoned quota mutex 복구됨 (Set): {KeyHash}",
                        HashKeyForDiagnostics(key));
                }

                if (!quotaAcquired)
                {
                    _logger.LogWarning(
                        "[Cache] quota reservation timed out: {KeyHash}",
                        HashKeyForDiagnostics(key));
                    return;
                }

                string filePath = GetFilePath(key);
                long protectedPayloadSize = value.LongLength + PROTECTED_PAYLOAD_OVERHEAD;
                long totalSize = HEADER_SIZE + protectedPayloadSize;
                if (!TryReserveStorageQuota(filePath, totalSize, key))
                {
                    DeleteSharedEntryAfterQuotaRejection(filePath, key);
                    return;
                }

                byte[] protectedValue = ProtectPayload(value);
                try
                {
                    long expiryTicks = GetExpiryTicks(options);
                    uint crc = Crc32.HashToUInt32(protectedValue);
                    uint keyHash = ComputeKeyHash(key); // Quick check 용

                    // MMF lock을 잡은 상태에서 최종 cache 파일에 보호 payload를 기록합니다.
                    using FileStream fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                    // 파일 크기 확보
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
                        DataLength = protectedValue.LongLength,
                        Crc32 = crc,
                        KeyHash = keyHash
                    };
                    accessor.Write(0, ref header);

                    // 2. Write protected payload
                    accessor.WriteArray(HEADER_SIZE, protectedValue, 0, protectedValue.Length);

                    // 3. Commit (State = Committed)
                    header.State = STATE_COMMITTED;
                    byte[] mac = ComputeIntegrityMac(header, key, protectedValue);
                    accessor.WriteArray(HEADER_METADATA_SIZE, mac, 0, mac.Length);
                    CryptographicOperations.ZeroMemory(mac);
                    accessor.Write(0, ref header);

                    // fs.Flush handled by Dispose? Not necessarily for MMF.
                    // accessor.Flush(); // OS Page Flush
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedValue);
                }
            }
            finally
            {
                if (quotaAcquired)
                    quotaMutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "[Cache] Set 오류: {KeyHash} (ErrorType: {ErrorType})",
                HashKeyForDiagnostics(key), ex.GetType().Name);
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
                _logger.LogWarning(
                    "[Cache] Abandoned Mutex 복구됨 (Remove): {KeyHash}",
                    HashKeyForDiagnostics(key));
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
                    using MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, HEADER_METADATA_SIZE, MemoryMappedFileAccess.Read);
                    MmfHeader header;
                    accessor.Read(0, out header);

                    bool shouldDelete = ShouldDeleteDuringCompaction(header, fs.Length);
                    if (!shouldDelete && !HasExpectedCrc(fs, header.DataLength, header.Crc32))
                        shouldDelete = true;

                    if (shouldDelete)
                    {
                        long freedBytes = Math.Max(0, Math.Min(header.DataLength, fs.Length));

                        // Dispose accessors before delete
                        accessor.Dispose();
                        mmf.Dispose();
                        fs.Close();

                        File.Delete(file);
                        DbMetrics.TrackCacheBytesFreed(freedBytes);
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
            _logger.LogError(
                "[Cache] Compact 중 오류 발생 (ErrorType: {ErrorType})",
                ex.GetType().Name);
        }
    }

    private static bool ShouldDeleteDuringCompaction(MmfHeader header, long fileLength)
    {
        if (header.Magic != MAGIC || header.Version != SCHEMA_VERSION)
            return true;
        if (header.State != STATE_COMMITTED)
            return true;
        if (DateTime.UtcNow.Ticks > header.ExpiryTicks)
            return true;
        if (header.DataLength < 0 || header.DataLength > int.MaxValue)
            return true;
        if (HEADER_SIZE + header.DataLength > fileLength)
            return true;

        return false;
    }

    private static bool HasExpectedCrc(FileStream fs, long dataLength, uint expectedCrc)
    {
        var crc = new Crc32();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            fs.Position = HEADER_SIZE;
            long remaining = dataLength;
            while (remaining > 0)
            {
                int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                    return false;

                crc.Append(buffer.AsSpan(0, read));
                remaining -= read;
            }

            return crc.GetCurrentHashAsUInt32() == expectedCrc;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

    private bool TryReserveStorageQuota(string filePath, long entrySize, string key)
    {
        long maxBytes = _options.MaxCacheSizeBytes;
        if (entrySize > maxBytes)
        {
            LogQuotaExceeded(key, entrySize, maxBytes);
            return false;
        }

        if (!TryGetStorageSizeExcluding(filePath, out long currentSize))
            return false;

        if (currentSize <= maxBytes - entrySize)
            return true;

        Compact();

        if (!TryGetStorageSizeExcluding(filePath, out currentSize))
            return false;

        if (currentSize <= maxBytes - entrySize)
            return true;

        long projectedBytes = currentSize > long.MaxValue - entrySize ? long.MaxValue : currentSize + entrySize;
        LogQuotaExceeded(key, projectedBytes, maxBytes);
        return false;
    }

    private bool TryGetStorageSizeExcluding(string excludedFilePath, out long totalSize)
    {
        totalSize = 0;
        string normalizedExcludedPath = Path.GetFullPath(excludedFilePath);
        try
        {
            foreach (string file in Directory.EnumerateFiles(_basePath, "*.cache"))
            {
                if (PathsEqual(Path.GetFullPath(file), normalizedExcludedPath))
                    continue;

                FileInfo info = new(file);
                if (info.Length > _options.MaxCacheSizeBytes - totalSize)
                {
                    totalSize = _options.MaxCacheSizeBytes + 1;
                    return true;
                }

                totalSize += info.Length;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Cache] quota check failed (ErrorType: {ErrorType})",
                ex.GetType().Name);
            return false;
        }
    }

    private void DeleteSharedEntryAfterQuotaRejection(string filePath, string key)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Cache] quota rejected shared entry cleanup failed: {KeyHash} (ErrorType: {ErrorType})",
                HashKeyForDiagnostics(key),
                ex.GetType().Name);
        }

        try
        {
            _options.FallbackCache?.Remove(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Cache] quota rejected fallback entry cleanup failed: {KeyHash} (ErrorType: {ErrorType})",
                HashKeyForDiagnostics(key),
                ex.GetType().Name);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }

    private void LogQuotaExceeded(string key, long projectedBytes, long maxBytes)
    {
        _logger.LogWarning(
            "[Cache] quota exceeded: {KeyHash} (ProjectedBytes: {ProjectedBytes}, MaxBytes: {MaxBytes})",
            HashKeyForDiagnostics(key),
            projectedBytes,
            maxBytes);
    }

    private byte[] LoadOrCreateLocalKeyMaterial()
    {
        string path = Path.Combine(_basePath, LOCAL_KEY_MATERIAL_FILE_NAME);
        byte[]? existing = TryReadLocalKeyMaterial(path);
        if (existing is not null)
            return existing;

        byte[] generated = RandomNumberGenerator.GetBytes(LOCAL_KEY_MATERIAL_SIZE);
        try
        {
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(generated);
            stream.Flush(flushToDisk: true);
            return generated;
        }
        catch (IOException)
        {
            CryptographicOperations.ZeroMemory(generated);
            existing = TryReadLocalKeyMaterial(path);
            if (existing is not null)
                return existing;

            throw;
        }
    }

    private static byte[]? TryReadLocalKeyMaterial(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            byte[] material = File.ReadAllBytes(path);
            if (material.Length == LOCAL_KEY_MATERIAL_SIZE)
                return material;

            CryptographicOperations.ZeroMemory(material);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[] CreateIntegrityKey(string? isolationKey, ReadOnlySpan<byte> localKeyMaterial)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(IntegrityKeyDomain);
        hash.AppendData([0]);
        AppendLengthPrefixedUtf8(hash, string.IsNullOrWhiteSpace(isolationKey) ? "default" : isolationKey.Trim());
        hash.AppendData(localKeyMaterial);
        return hash.GetHashAndReset();
    }

    private static byte[] CreatePayloadProtectionKey(string? isolationKey, ReadOnlySpan<byte> localKeyMaterial)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(PayloadProtectionKeyDomain);
        hash.AppendData([0]);
        AppendLengthPrefixedUtf8(hash, string.IsNullOrWhiteSpace(isolationKey) ? "default" : isolationKey.Trim());
        hash.AppendData(localKeyMaterial);
        return hash.GetHashAndReset();
    }

    private byte[] ProtectPayload(ReadOnlySpan<byte> plaintext)
    {
        byte[] protectedPayload = new byte[PROTECTED_PAYLOAD_OVERHEAD + plaintext.Length];
        Span<byte> nonce = protectedPayload.AsSpan(0, AES_GCM_NONCE_SIZE);
        Span<byte> ciphertext = protectedPayload.AsSpan(AES_GCM_NONCE_SIZE, plaintext.Length);
        Span<byte> tag = protectedPayload.AsSpan(AES_GCM_NONCE_SIZE + plaintext.Length, AES_GCM_TAG_SIZE);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_payloadProtectionKey, AES_GCM_TAG_SIZE);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return protectedPayload;
    }

    private byte[]? UnprotectPayload(ReadOnlySpan<byte> protectedPayload)
    {
        if (protectedPayload.Length < PROTECTED_PAYLOAD_OVERHEAD)
            return null;

        ReadOnlySpan<byte> nonce = protectedPayload[..AES_GCM_NONCE_SIZE];
        ReadOnlySpan<byte> ciphertext = protectedPayload[AES_GCM_NONCE_SIZE..^AES_GCM_TAG_SIZE];
        ReadOnlySpan<byte> tag = protectedPayload[^AES_GCM_TAG_SIZE..];
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_payloadProtectionKey, AES_GCM_TAG_SIZE);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
    }

    private bool VerifyIntegrity(
        MmfHeader header,
        string key,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> storedMac)
    {
        if (storedMac.Length != MAC_SIZE)
            return false;

        byte[] expectedMac = ComputeIntegrityMac(header, key, data);
        try
        {
            return CryptographicOperations.FixedTimeEquals(storedMac, expectedMac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedMac);
        }
    }

    private byte[] ComputeIntegrityMac(MmfHeader header, string key, ReadOnlySpan<byte> data)
    {
        Span<byte> headerBytes = stackalloc byte[HEADER_METADATA_SIZE];
        MemoryMarshal.Write(headerBytes, in header);

        using IncrementalHash hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _integrityKey);
        hmac.AppendData(IntegrityMacDomain);
        hmac.AppendData([0]);
        hmac.AppendData(headerBytes);
        AppendLengthPrefixedUtf8(hmac, key);
        hmac.AppendData(data);
        return hmac.GetHashAndReset();
    }

    private static void AppendLengthPrefixedUtf8(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static uint ComputeKeyHash(string key)
        => Crc32.HashToUInt32(Encoding.UTF8.GetBytes(key));

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

        CryptographicOperations.ZeroMemory(_integrityKey);
        CryptographicOperations.ZeroMemory(_payloadProtectionKey);

        if (_quotaMutex.IsValueCreated)
            _quotaMutex.Value.Dispose();

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
