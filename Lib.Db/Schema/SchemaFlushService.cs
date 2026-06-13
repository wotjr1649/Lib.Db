// ============================================================================
// 파일: Lib.Db/Schema/SchemaFlushService.cs
// 설명: 스키마 캐시 플러시 서비스 — 변경 감지 시 스키마 메타데이터 캐시 갱신
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Diagnostics;
using Lib.Db.Caching;
using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Contracts.Schema;
using Lib.Db.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Lib.Db.Schema;

/// <summary>
/// SharedMemoryCache opt-in 환경에서 Epoch 기반 스키마 캐시 무효화를 수행하는 서비스입니다.
/// <para>동일 호스트 프로세스 간 Epoch 동기화로 로컬 shared-memory 캐시 일관성을 보조합니다.</para>
/// </summary>
public sealed class SchemaFlushService : ISchemaFlushCoordinator, IDisposable
{
    private readonly EpochStore _epochStore;
    private readonly ISchemaService _schemaService;
    private readonly LibDbOptions _options;
    private readonly ILogger<SchemaFlushService> _logger;
    private readonly MemoryCache _lastKnownEpochs;

    /// <summary>
    /// 스키마 플러시 서비스를 초기화합니다.
    /// </summary>
    /// <param name="epochStore">프로세스 간 Epoch 저장소</param>
    /// <param name="schemaService">스키마 캐시 플러시 대상 서비스</param>
    /// <param name="options">관측 가능성 및 런타임 동작 옵션</param>
    /// <param name="logger">로그 기록기</param>
    public SchemaFlushService(
        EpochStore epochStore,
        ISchemaService schemaService,
        LibDbOptions options,
        ILogger<SchemaFlushService> logger)
    {
        _epochStore = epochStore ?? throw new ArgumentNullException(nameof(epochStore));
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
        using Activity? activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("Flush")
            : null;
        string diagnosticInstance = DbDiagnosticRedactor.RedactInstanceId(instanceHash) ?? instanceHash;
        activity?.SetTag("instance", diagnosticInstance);

        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            // 1. Epoch 증가 (프로세스 간 공유)
            long newEpoch = _epochStore.IncrementEpoch(instanceHash);

            // 2. 메트릭 추적: Epoch 증가
            DbMetrics.TrackSchemaRefreshFromScope(true, "EpochIncrement");

            _logger.LogInformation(
                "[SchemaFlush] Epoch 증가: {Instance} → {Epoch}",
                diagnosticInstance, newEpoch);

            // 3. 로컬 스키마 캐시 무효화
            await _schemaService.FlushSchemaAsync(instanceHash, ct).ConfigureAwait(false);

            // 4. 로컬 Epoch 업데이트
            string epochCacheKey = SchemaCacheIdentity.ForCache(instanceHash);
            _lastKnownEpochs.Set(epochCacheKey, newEpoch, new MemoryCacheEntryOptions
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
                diagnosticInstance, newEpoch, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.GetType().Name);
            _logger.LogError(
                "[SchemaFlush] 오류 발생: {Instance} (ErrorType: {ErrorType})",
                diagnosticInstance, ex.GetType().Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task FlushTvpAsync(string instanceHash, string tvpName, CancellationToken ct = default)
    {
        using Activity? activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("FlushTvp")
            : null;
        string diagnosticInstance = DbDiagnosticRedactor.RedactInstanceId(instanceHash) ?? instanceHash;
        activity?.SetTag("instance", diagnosticInstance);
        activity?.SetTag("tvp", tvpName);

        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            long newEpoch = _epochStore.IncrementEpoch(instanceHash);

            DbMetrics.TrackSchemaRefreshFromScope(true, "EpochIncrement");

            _logger.LogInformation(
                "[SchemaFlush] TVP Epoch 증가: {Instance}, TVP={TvpName}, Epoch={Epoch}",
                diagnosticInstance, tvpName, newEpoch);

            await _schemaService.FlushTvpAsync(tvpName, instanceHash, ct).ConfigureAwait(false);

            string epochCacheKey = SchemaCacheIdentity.ForCache(instanceHash);
            _lastKnownEpochs.Set(epochCacheKey, newEpoch, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromHours(1)
            });

            DbMetrics.TrackDurationFromScope(sw.Elapsed);

            activity?.SetTag("epoch", newEpoch);
            activity?.SetTag("duration_ms", sw.ElapsedMilliseconds);

            _logger.LogInformation(
                "[SchemaFlush] TVP 완료: {Instance}, TVP={TvpName}, Epoch={Epoch}, Duration={Ms}ms",
                diagnosticInstance, tvpName, newEpoch, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.GetType().Name);
            _logger.LogError(
                "[SchemaFlush] TVP 오류 발생: {Instance}, TVP={TvpName} (ErrorType: {ErrorType})",
                diagnosticInstance, tvpName, ex.GetType().Name);
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
        using Activity? activity = _options.EnableObservability
            ? LibDbTelemetry.ActivitySource.StartActivity("CheckEpoch")
            : null;
        string diagnosticInstance = DbDiagnosticRedactor.RedactInstanceId(instanceHash) ?? instanceHash;
        activity?.SetTag("instance", diagnosticInstance);

        // 현재 Epoch 읽기
        long currentEpoch = _epochStore.GetEpoch(instanceHash);

        // 마지막으로 알려진 Epoch
        string epochCacheKey = SchemaCacheIdentity.ForCache(instanceHash);
        long lastKnown = _lastKnownEpochs.TryGetValue<long>(epochCacheKey, out long cached)
            ? cached
            : 0;

        if (currentEpoch > lastKnown)
        {
            _logger.LogWarning(
                "[SchemaFlush] Epoch 변경 감지: {Instance}, {Old} → {New}. 로컬 캐시 무효화 중...",
                diagnosticInstance, lastKnown, currentEpoch);

            // 메트릭: Epoch 동기화 추적
            DbMetrics.TrackSchemaRefreshFromScope(true, "EpochSync");

            // 로컬 캐시만 무효화 (Epoch는 증가시키지 않음)
            await _schemaService.FlushSchemaAsync(instanceHash, ct).ConfigureAwait(false);

            // 로컬 Epoch 업데이트
            _lastKnownEpochs.Set(epochCacheKey, currentEpoch, new MemoryCacheEntryOptions
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

    /// <summary>
    /// 프로세스 로컬 Epoch 상태 캐시를 해제합니다.
    /// </summary>
    public void Dispose()
        => _lastKnownEpochs.Dispose();
}

/// <summary>
/// Epoch 변경을 주기적으로 감시하는 백그라운드 서비스 (선택적).
/// <para>
/// Polling 방식으로 Epoch 변경을 감지하고 자동 Flush를 수행합니다.
/// </para>
/// </summary>
public sealed class EpochWatcherService : BackgroundService
{
    private readonly ISchemaFlushCoordinator _coordinator;
    private readonly ILogger<EpochWatcherService> _logger;
    private readonly string[] _instanceHashes;
    private readonly TimeSpan _checkInterval;

    /// <summary>
    /// Epoch 감시 서비스를 초기화합니다.
    /// </summary>
    /// <param name="coordinator">스키마 플러시 조정자</param>
    /// <param name="options">Lib.Db 옵션</param>
    /// <param name="logger">로그 기록기</param>
    public EpochWatcherService(
        ISchemaFlushCoordinator coordinator,
        LibDbOptions options,
        ILogger<EpochWatcherService> logger)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);

        _checkInterval = TimeSpan.FromSeconds(options.EpochCheckIntervalSeconds);
        bool enableSharedMemory = options.EnableSharedMemoryCache is true;
        if (options.EnableEpochCoordination is true && !enableSharedMemory)
        {
            throw new InvalidOperationException(
                "Lib.Db: EnableEpochCoordination=true requires explicit shared-memory cache opt-in. " +
                "Call services.AddLibDbSharedMemoryCache(), or disable EnableEpochCoordination.");
        }

        bool enableEpoch = options.EnableEpochCoordination.GetValueOrDefault(enableSharedMemory);

        if (!enableEpoch)
        {
            _instanceHashes = [];
            _logger.LogInformation("[EpochWatcher] Epoch 조정이 비활성화되어 서비스를 실행하지 않습니다.");
            return;
        }

        // 감시할 인스턴스 목록 (LibDbOptions에서)
        _instanceHashes = options.WatchedInstances?.ToArray() ?? [];

        if (_instanceHashes.Length == 0)
        {
            _logger.LogWarning("[EpochWatcher] 감시할 인스턴스가 없습니다. 서비스 비활성화됩니다.");
        }
    }

    /// <inheritdoc />
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
                foreach (string instanceHash in _instanceHashes)
                {
                    bool synced = await _coordinator.CheckAndSyncEpochAsync(instanceHash, stoppingToken)
                        .ConfigureAwait(false);

                    if (synced)
                    {
                        _logger.LogInformation(
                            "[EpochWatcher] {Instance} 동기화 완료",
                            DbDiagnosticRedactor.RedactInstanceId(instanceHash));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    "[EpochWatcher] Epoch 체크 중 오류 (ErrorType: {ErrorType})",
                    ex.GetType().Name);
            }

            await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("[EpochWatcher] 종료됨");
    }
}
