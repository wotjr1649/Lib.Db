// ============================================================================
// 파일: Lib.Db/Caching/CacheMaintenanceService.cs
// 설명: 캐시 유지보수 서비스 — 만료된 캐시 엔트리 정리 및 메모리 관리
// 대상: .NET 10 / C# 14
// ============================================================================

using Microsoft.Extensions.Caching.Distributed;

namespace Lib.Db.Caching;

/// <summary>
/// 캐시 시스템 자동 유지보수 서비스 (Background Service)
/// <para>
/// 주기적으로 캐시 정리(Compact) 작업을 수행합니다.
/// </para>
/// </summary>
public sealed class CacheMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheMaintenanceService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // 5분 주기

    /// <summary>
    /// 캐시 유지보수 서비스를 초기화합니다.
    /// </summary>
    /// <param name="serviceProvider">서비스 조회를 위한 루트 서비스 공급자</param>
    /// <param name="logger">로그 기록기</param>
    public CacheMaintenanceService(
        IServiceProvider serviceProvider,
        ILogger<CacheMaintenanceService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[CacheMaintenance] 서비스 시작 (Interval: {Interval}분)", _checkInterval.TotalMinutes);

        using PeriodicTimer timer = new PeriodicTimer(_checkInterval);

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

    /// <summary>
    /// 한 사이클의 캐시 유지보수 작업을 수행합니다.
    /// <para>
    /// <b>[설계 의도]</b><br/>
    /// DI 스코프를 생성하여 <see cref="Caching.SharedMemoryCache"/>를 획득하고,
    /// <c>Compact(0.8)</c>를 호출하여 만료된 항목 및 80% 초과 사용 공간을 정리합니다.<br/>
    /// <see cref="SharedMemoryCache"/>가 등록되지 않은 환경에서는 즉시 반환합니다.
    /// </para>
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    private async Task PerformMaintenanceAsync(CancellationToken ct)
    {
        // Scoped Service Provider 생성 (DI 스코프 관리)
        using IServiceScope scope = _serviceProvider.CreateScope();

        // 필수 서비스 조회
        IDistributedCache? cache = scope.ServiceProvider.GetService<IDistributedCache>();

        // SharedMemoryCache가 아닌 경우 스킵
        if (cache is not SharedMemoryCache sharedCache)
        {
            return;
        }

        _logger.LogInformation("[CacheMaintenance] 정리(Compact) 작업을 시작합니다.");

        // 캐시 정리 (Compact)
        // Threshold 0.8 (80% 이상 사용 시 혹은 만료된 항목 정리)
        // SharedMemoryCache.Compact 메서드는 동기 메서드임 (Disk I/O 포함)
        await Task.Run(() => sharedCache.Compact(0.8), ct).ConfigureAwait(false);

        _logger.LogInformation("[CacheMaintenance] 정리 작업 완료.");
    }
}
