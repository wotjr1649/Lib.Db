using System.Diagnostics;
using Lib.Db.Caching;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Lib.Db.Schema;

/// <summary>
///  Epoch 기반 분산 스키마 캐시 무효화 서비스.
/// <para>
/// v9 FINAL: 프로세스 간 Epoch 동기화로 분산 환경에서 스키마 일관성 보장
/// </para>
/// </summary>
public sealed class SchemaFlushService : ISchemaFlushCoordinator
{
    private readonly EpochStore _epochStore;
    private readonly ISchemaService _schemaService;
    private readonly ILogger<SchemaFlushService> _logger;
    private readonly MemoryCache _lastKnownEpochs;
    
    private static readonly ActivitySource s_activity = new("Lib.Db.SchemaFlush");
    
    public SchemaFlushService(
        EpochStore epochStore,
        ISchemaService schemaService,
        ILogger<SchemaFlushService> logger)
    {
        _epochStore = epochStore ?? throw new ArgumentNullException(nameof(epochStore));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 마지막으로 알려진 Epoch 캐시 (인스턴스당)
        _lastKnownEpochs = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 100  // 최대 100개 인스턴스
        });
    }
    
    /// <inheritdoc />
    public async Task FlushAsync(string instanceHash, CancellationToken ct = default)
    {
        using var activity = s_activity.StartActivity("Flush");
        activity?.SetTag("instance", instanceHash);
        
        var sw = Stopwatch.StartNew();
        
        try
        {
            // 1. Epoch 증가 (프로세스 간 공유)
            long newEpoch = _epochStore.IncrementEpoch(instanceHash);
            
            // 2. 메트릭 추적: Epoch 증가
            DbMetrics.TrackSchemaRefreshFromScope(true, "EpochIncrement");
            
            _logger.LogInformation(
                "[SchemaFlush] Epoch 증가: {Instance} → {Epoch}",
                instanceHash, newEpoch);
            
            // 3. 로컬 스키마 캐시 무효화
            await _schemaService.FlushSchemaAsync(instanceHash, ct).ConfigureAwait(false);
            
            // 4. 로컬 Epoch 업데이트
            _lastKnownEpochs.Set(instanceHash, newEpoch, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromHours(1)
            });
            
            // 5. 메트릭: Flush 소요 시간 추적
            DbMetrics.TrackDurationFromScope(sw.Elapsed);
            
            activity?.SetTag("epoch", newEpoch);
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);
            
            _logger.LogInformation(
                "[SchemaFlush] 완료: {Instance}, Epoch={Epoch}, Duration={Ms}ms",
                instanceHash, newEpoch, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.GetType().Name);
            _logger.LogError(ex,
                "[SchemaFlush] 오류 발생: {Instance}",
                instanceHash);
            throw;
        }
    }
    
    /// <inheritdoc />
    public long GetCurrentEpoch(string instanceHash)
    {
        return _epochStore.GetEpoch(instanceHash);
    }
    
    /// <inheritdoc />
    public async Task<bool> CheckAndSyncEpochAsync(string instanceHash, CancellationToken ct = default)
    {
        using var activity = s_activity.StartActivity("CheckEpoch");
        activity?.SetTag("instance", instanceHash);
        
        // 현재 Epoch 읽기
        long currentEpoch = _epochStore.GetEpoch(instanceHash);
        
        // 마지막으로 알려진 Epoch
        long lastKnown = _lastKnownEpochs.TryGetValue<long>(instanceHash, out long cached)
            ? cached
            : 0;
        
        if (currentEpoch > lastKnown)
        {
            _logger.LogWarning(
                "[SchemaFlush] Epoch 변경 감지: {Instance}, {Old} → {New}. 로컬 캐시 무효화 중...",
                instanceHash, lastKnown, currentEpoch);
            
            // 메트릭: Epoch 동기화 추적
            DbMetrics.TrackSchemaRefreshFromScope(true, "EpochSync");
            
            // 로컬 캐시만 무효화 (Epoch는 증가시키지 않음)
            await _schemaService.FlushSchemaAsync(instanceHash, ct).ConfigureAwait(false);
            
            // 로컬 Epoch 업데이트
            _lastKnownEpochs.Set(instanceHash, currentEpoch, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromHours(1)
            });
            
            activity?.SetTag("synced", true);
            activity?.SetTag("old_epoch", lastKnown);
            activity?.SetTag("new_epoch", currentEpoch);
            
            return true;  // 동기화됨
        }
        
        // 메트릭: 캐시 적중 (변경 없음)
        DbMetrics.TrackCacheHitFromScope("EpochCheck");
        
        activity?.SetTag("synced", false);
        return false;  // 변경 없음
    }
}

/// <summary>
/// Epoch 변경을 주기적으로 감시하는 백그라운드 서비스 (선택적).
/// <para>
/// v9 FINAL: Polling 방식으로 Epoch 변경 감지 및 자동 Flush
/// </para>
/// </summary>
public sealed class EpochWatcherService : BackgroundService
{
    private readonly ISchemaFlushCoordinator _coordinator;
    private readonly ILogger<EpochWatcherService> _logger;
    private readonly string[] _instanceHashes;
    private readonly TimeSpan _checkInterval;
    
    public EpochWatcherService(
        ISchemaFlushCoordinator coordinator,
        LibDbOptions options,
        ILogger<EpochWatcherService> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 감시할 인스턴스 목록 (LibDbOptions에서)
        _instanceHashes = options.WatchedInstances?.ToArray() ?? [];
        _checkInterval = TimeSpan.FromSeconds(options.EpochCheckIntervalSeconds);
        
        if (_instanceHashes.Length == 0)
        {
            _logger.LogWarning("[EpochWatcher] 감시할 인스턴스가 없습니다. 서비스 비활성화됩니다.");
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_instanceHashes.Length == 0)
        {
            _logger.LogInformation("[EpochWatcher] 인스턴스 없음. 종료합니다.");
            return;
        }
        
        _logger.LogInformation(
            "[EpochWatcher] 시작: {Count}개 인스턴스, {Interval}초 간격",
            _instanceHashes.Length,
            _checkInterval.TotalSeconds);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var instanceHash in _instanceHashes)
                {
                    bool synced = await _coordinator.CheckAndSyncEpochAsync(instanceHash, stoppingToken)
                        .ConfigureAwait(false);
                    
                    if (synced)
                    {
                        _logger.LogInformation(
                            "[EpochWatcher] {Instance} 동기화 완료",
                            instanceHash);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[EpochWatcher] Epoch 체크 중 오류");
            }
            
            await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
        }
        
        _logger.LogInformation("[EpochWatcher] 종료됨");
    }
}
