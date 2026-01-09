using Microsoft.Extensions.Caching.Distributed;

namespace Lib.Db.Caching;

/// <summary>
/// 캐시 시스템 자동 유지보수 서비스 (Background Service)
/// <para>
/// 리더 프로세스로 선출된 인스턴스에서만 주기적으로 캐시 정리(Compact) 작업을 수행합니다.
/// </para>
/// </summary>
public sealed class CacheMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheMaintenanceService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // 5분 주기

    public CacheMaintenanceService(
        IServiceProvider serviceProvider,
        ILogger<CacheMaintenanceService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[CacheMaintenance] 서비스 시작 (Interval: {Interval}분)", _checkInterval.TotalMinutes);

        using var timer = new PeriodicTimer(_checkInterval);
        
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await PerformMaintenanceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CacheMaintenance] 유지보수 작업 중 오류 발생");
            }
        }

        _logger.LogInformation("[CacheMaintenance] 서비스 중지");
    }

    private async Task PerformMaintenanceAsync(CancellationToken ct)
    {
        // Scoped Service Provider 생성 (DI 스코프 관리)
        using var scope = _serviceProvider.CreateScope();
        
        // 필수 서비스 조회
        var leaderElection = scope.ServiceProvider.GetService<ICacheLeaderElection>();
        var cache = scope.ServiceProvider.GetService<IDistributedCache>();

        // SharedMemoryCache가 아니거나 리더 선출이 비활성화된 경우 스킵
        if (leaderElection == null || cache is not SharedMemoryCache sharedCache)
        {
            return;
        }

        // 1. 리더십 획득 시도
        if (leaderElection.TryAcquireLeadership())
        {
            if (leaderElection.IsLeader)
            {
                _logger.LogInformation("[CacheMaintenance] 리더 권한으로 정리(Compact) 작업을 시작합니다.");
                
                // 2. 캐시 정리 (Compact)
                // Threshold 0.8 (80% 이상 사용 시 혹은 만료된 항목 정리)
                // SharedMemoryCache.Compact 메서드는 동기 메서드임 (Disk I/O 포함)
                await Task.Run(() => sharedCache.Compact(0.8), ct).ConfigureAwait(false);
                
                _logger.LogInformation("[CacheMaintenance] 정리 작업 완료.");
            }
        }
    }
}
