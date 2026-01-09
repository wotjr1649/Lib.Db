using Microsoft.Extensions.Options;

namespace Lib.Db.Caching;

/// <summary>
/// Named Mutex를 사용한 프로세스 간 리더 선출 구현체.
/// </summary>
public sealed class CacheLeaderElection : ICacheLeaderElection
{
    private readonly SharedMemoryCacheOptions _options;
    private readonly ILogger<CacheLeaderElection> _logger;
    private readonly string _mutexName;
    private readonly string _leaseFilePath;
    
    private Mutex? _leaderMutex;
    private bool _isLeader;
    private bool _disposed;

    public bool IsLeader => _isLeader;

    public CacheLeaderElection(
        IOptions<SharedMemoryCacheOptions> options,
        ILogger<CacheLeaderElection> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        // Mutex 이름 생성: "Global\Lib.Db.Leader_..." or "Local\Lib.Db.Leader_..."
        // CacheInternalHelpers가 제공하는 표준 로직 사용
        string prefix = CacheInternalHelpers.GetMutexPrefix(_options, "Leader");
        // 단일 Leader Mutex이므로 뒤에 "Lock" 같은 고정 접미사 사용 (Stripe 없음)
        _mutexName = $"{prefix}Lock";

        string basePath = CacheInternalHelpers.ResolveBasePath(_options);
        _leaseFilePath = Path.Combine(basePath, "leader.lease");
        
        // 디렉토리 확인
        Directory.CreateDirectory(basePath);
    }

    public bool TryAcquireLeadership()
    {
        if (_disposed) return false;
        if (_isLeader)
        {
            // 이미 리더인 경우, Heartbeat 갱신만 수행
            UpdateHeartbeat();
            return true;
        }

        try
        {
            // Mutex 생성 (없으면 생성, 있으면 오픈)
            _leaderMutex ??= new Mutex(false, _mutexName);

            // 0ms 대기 (Non-blocking)
            if (_leaderMutex.WaitOne(0))
            {
                _isLeader = true;
                _logger.LogInformation("[LeaderElection] 리더십 획득 성공 (PID: {Pid})", Environment.ProcessId);
                UpdateHeartbeat();
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            // 이전 리더 Crash -> 즉시 리더십 인수
            _isLeader = true;
            _logger.LogWarning("[LeaderElection] 이전 리더 비정상 종료 감지. 리더십 인수 (PID: {Pid})", Environment.ProcessId);
            UpdateHeartbeat();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LeaderElection] 리더십 획득 시도 중 오류 발생");
        }

        return false;
    }

    private void UpdateHeartbeat()
    {
        try
        {
            string content = $"PID={Environment.ProcessId},Time={DateTime.UtcNow:O}";
            File.WriteAllText(_leaseFilePath, content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LeaderElection] Heartbeat 갱신 실패");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isLeader)
        {
            _logger.LogInformation("[LeaderElection] 리더십 반납 (PID: {Pid})", Environment.ProcessId);
            try
            {
                _leaderMutex?.ReleaseMutex();
            }
            catch (Exception ex)
            {
                // 이미 해제되었거나 오류 발생 시 무시
                _logger.LogDebug(ex, "[LeaderElection] Mutex Release 오류 (무시됨)");
            }
            _isLeader = false;
        }

        _leaderMutex?.Dispose();
        _leaderMutex = null;
    }
}

/// <summary>
/// 공유 메모리가 비활성화되었을 때 사용되는 No-Op 리더 선출 구현체.
/// </summary>
public sealed class NoOpCacheLeaderElection : ICacheLeaderElection
{
    public bool IsLeader => false;

    public bool TryAcquireLeadership() => false;

    public void Dispose() { }
}
