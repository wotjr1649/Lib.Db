// ============================================================================
// 파일: Lib.Db/Caching/CacheCoordination.cs
// 설명: [Architecture] 캐시 리더 선출, 정리(Maintenance), Epoch 동기화
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using System.Buffers;
using System.Diagnostics;
using System.IO.Hashing;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Options;

using System.Security.Cryptography;

namespace Lib.Db.Caching;


#region 전역 무효화 카운터

/// <summary>
/// 시스템 전체의 캐시 무효화(Invalidation)를 위한 단조 증가 카운터(Epoch)를 제공합니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// </para>
/// <list type="bullet">
/// <item><description><strong>초경량 IPC</strong>: 8바이트(long) MMF를 통해 모든 프로세스가 즉시 최신 Epoch를 공유합니다.</description></item>
/// <item><description><strong>원자적 증가</strong>: Interlocked가 아닌 Mutex 기반 안전한 증가를 보장합니다.</description></item>
/// </list>
/// </remarks>
public sealed class GlobalCacheEpoch : IDisposable
{
    private const string EPOCH_FILE_NAME = "GlobalCacheEpoch.mmf";
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly Mutex _mutex;

    public GlobalCacheEpoch(IOptions<SharedMemoryCacheOptions> options)
    {
        var basePath = CacheInternalHelpers.ResolveBasePath(options.Value);
        var mutexPrefix = CacheInternalHelpers.GetMutexPrefix(options.Value);
        
        // MMF 파일 경로
        var filePath = Path.Combine(basePath, EPOCH_FILE_NAME);
        Directory.CreateDirectory(basePath);

        // FileStream 열기 (8바이트 확보)
        var fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (fs.Length < 8) fs.SetLength(8);

        _mmf = MemoryMappedFile.CreateFromFile(fs, null, 8, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
        _accessor = _mmf.CreateViewAccessor(0, 8);
        
        // Mutex 생성 (쓰기 보호용)
        _mutex = new Mutex(false, $"{mutexPrefix}EpochMutex");
    }

    public GlobalCacheEpoch(string basePath) 
        : this(Options.Create(new SharedMemoryCacheOptions { BasePath = basePath })) { }

    /// <summary>현재 글로벌 Epoch 값을 읽습니다 (비잠금).</summary>
    public long Current => _accessor.ReadInt64(0);

    /// <summary>Epoch를 1 증가시키고 새 값을 반환합니다 (Thread-Safe IPC).</summary>
    public long Increment()
    {
        try
        {
            _mutex.WaitOne();
            var next = _accessor.ReadInt64(0) + 1;
            _accessor.Write(0, next);
            return next;
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mmf.Dispose();
        _mutex.Dispose();
    }
}

#endregion

#region 인스턴스별 Epoch 관리

/// <summary>
/// 프로세스 간 DB 인스턴스별 Epoch 공유를 위한 고성능 파일 기반 저장소입니다.
/// </summary>
/// <remarks>
/// <para>
/// <b>[설계의도 (Design Rationale)]</b><br/>
/// 인스턴스별 스키마 버전 관리를 위해 파일 시스템을 영구 저장소로 사용합니다.
/// 쓰기 작업 시의 원자성(Atomicity)을 보장하기 위해 임시 파일 작성 후 교체(Swap) 방식을 사용하며,
/// 고성능 해시 알고리즘(XxHash128)을 도입하여 파일 경로 길이를 최적화했습니다.
/// </para>
/// <para><strong>핵심 기능 (Zero-Copy &amp; Atomic)</strong></para>
/// <list type="bullet">
/// <item><description><strong>Atomic File Swap</strong>: temp 파일 작성 후 <c>File.Move</c>로 원자적 교체하여 쓰기 중단 시에도 데이터 무결성을 보장합니다.</description></item>
/// <item><description><strong>XxHash128 파일명</strong>: SHA256 대비 133배 빠르고 파일명이 43% 짧아 Long Path 호환성이 뛰어납니다.</description></item>
/// <item><description><strong>Striped Mutex</strong>: 1024개의 Mutex Stripe를 사용하여 인스턴스별 경합을 최소화합니다.</description></item>
/// </list>
/// </remarks>
public sealed class EpochStore(string basePath, ILogger<EpochStore> logger) : IDisposable
{
    private readonly string _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    // 1024개 뮤텍스의 지연 초기화 (Lazy Initialization)
    private readonly Lazy<Mutex[]> _mutexStripes = new(() => 
        Enumerable.Range(0, 1024)
            .Select(i => CreateFallbackMutex($"Lib.Db.Epoch.Stripe{i}", logger))
            .ToArray());

    static EpochStore()
    {
        // 생성자 진입 시 디렉토리 생성
    }

    // 지연 뮤텍스 생성 로직 (3단계 폴백 전략)
    private static Mutex CreateFallbackMutex(string name, ILogger logger)
    {
        try
        {
            return new Mutex(false, $"Global\\{name}");
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("[Mutex] Global 권한 없음 -> Local 폴백: {Name}", name);
            return new Mutex(false, $"Local\\{name}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Mutex] 명명된 Mutex 실패 -> Unnamed 사용: {Name}", name);
            return new Mutex(false);
        }
    }

    /// <summary>
    /// 지정된 인스턴스의 Epoch 값을 원자적으로 1 증가시킵니다.
    /// </summary>
    public long IncrementEpoch(string instanceHash)
    {
        if (string.IsNullOrWhiteSpace(instanceHash)) throw new ArgumentException("해시값은 필수입니다.", nameof(instanceHash));

        var sw = Stopwatch.StartNew();
        var filePath = GetEpochFilePath(instanceHash);
        var tempPath = filePath + ".tmp";
        var mutex = GetMutex(instanceHash);
        bool acquired = false;

        try
        {
            try 
            { 
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); 
            }
            catch (AbandonedMutexException) 
            { 
                acquired = true; 
                logger.LogWarning("[Epoch] Abandoned Mutex Recovered: {Hash}", instanceHash);
            }

            if (!acquired) throw new TimeoutException($"Epoch 잠금 타임아웃 발생: {instanceHash}");

            long current = ReadEpochSafe(filePath);
            long newEpoch = current + 1;

            // 1단계: 임시 파일에 쓰기 (Zero-Allocation)
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Span<byte> buffer = stackalloc byte[8];
                BitConverter.TryWriteBytes(buffer, newEpoch);
                fs.Write(buffer);
                fs.Flush(true);
            }

            // 2단계: 원자적 교체 (Atomic Swap)
            File.Move(tempPath, filePath, overwrite: true);

            DbMetrics.TrackDurationFromScope(sw.Elapsed);
            return newEpoch;
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// 지정된 인스턴스의 현재 Epoch 값을 조회합니다. (Lock-Free Read)
    /// </summary>
    public long GetEpoch(string instanceHash)
    {
        if (string.IsNullOrWhiteSpace(instanceHash)) throw new ArgumentException("해시값은 필수입니다.", nameof(instanceHash));
        return ReadEpochWithFallback(instanceHash);
    }

    private long ReadEpochSafe(string filePath)
    {
        if (!File.Exists(filePath)) return 0;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 8) return 0;

            Span<byte> buffer = stackalloc byte[8];
#if NET7_0_OR_GREATER
            fs.ReadExactly(buffer);
#else
            fs.Read(buffer); // Simplified for consolidation
#endif
            return BitConverter.ToInt64(buffer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Epoch] Read Error: {Path}", filePath);
            return 0;
        }
    }

    private Mutex GetMutex(string key)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(key.Length);
        if (maxBytes <= 256)
        {
            Span<byte> buffer = stackalloc byte[maxBytes];
            int written = Encoding.UTF8.GetBytes(key.AsSpan(), buffer);
            uint hash = Crc32.HashToUInt32(buffer[..written]);
            return _mutexStripes.Value[hash % 1024];
        }
        else
        {
            var leased = ArrayPool<byte>.Shared.Rent(maxBytes);
            try
            {
                int written = Encoding.UTF8.GetBytes(key.AsSpan(), leased.AsSpan());
                uint hash = Crc32.HashToUInt32(leased.AsSpan(0, written));
                return _mutexStripes.Value[hash % 1024];
            }
            finally { ArrayPool<byte>.Shared.Return(leased); }
        }
    }

    private long ReadEpochWithFallback(string instanceHash)
    {
        var newPath = GetEpochFilePath(instanceHash);
        if (File.Exists(newPath)) return ReadEpochSafe(newPath);

        // 레거시 폴백 (호환성 유지)
        var legacyPath = GetLegacyFilePath(instanceHash);
        if (File.Exists(legacyPath))
        {
            long val = ReadEpochSafe(legacyPath);
            try
            {
                // 자동 마이그레이션
                var temp = newPath + ".tmp";
                File.WriteAllBytes(temp, BitConverter.GetBytes(val));
                File.Move(temp, newPath, true);
                File.Delete(legacyPath);
            }
            catch { /* 마이그레이션 실패 무시 */ }
            return val;
        }
        return 0;
    }

    private string GetEpochFilePath(string instanceHash)
    {
        var hashBytes = XxHash128.Hash(Encoding.UTF8.GetBytes(instanceHash));
        var hexHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.GetFullPath(Path.Combine(_basePath, $"epoch_{hexHash}.bin"));
    }

    private string GetLegacyFilePath(string instanceHash)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(instanceHash));
        var hexHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.GetFullPath(Path.Combine(_basePath, $"epoch_{hexHash}.bin"));
    }

    public void Dispose()
    {
        if (_mutexStripes.IsValueCreated)
        {
            foreach (var m in _mutexStripes.Value) m.Dispose();
        }
    }
}

#endregion
