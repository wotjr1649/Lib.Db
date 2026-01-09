// ============================================================================
// 파일: Lib.Db/Hosting/SchemaWarmupService.cs
// 설명: 애플리케이션 시작 시 DB 스키마 메타데이터를 미리 로드하는 백그라운드 서비스
// 대상: .NET 10 / C# 14
// 특징:
//   - LibDbOptions.ConnectionStrings × PrewarmSchemas 조합으로 워밍업 대상 생성
//   - 옵션 기반 MaxConcurrency(PrewarmMaxConcurrency)로 DB 부하 제어
//   - Warmup 전용 DbRequestInfo 구성 후 DbMetrics.TrackSchemaRefresh(in info) 사용
//   - Warmup 실패는 로깅만 하고 애플리케이션은 계속 실행
// ============================================================================

#nullable enable

using System.Diagnostics;
using Lib.Db.Contracts.Schema;

namespace Lib.Db.Hosting;

#region [0. 스키마 워밍업 서비스]

/// <summary>
/// 애플리케이션 시작 시 설정된 DB/스키마를 백그라운드에서 미리 로드(Warmup)하는 서비스입니다.
/// <para>
/// - LibDbOptions.ConnectionStrings 의 인스턴스 × PrewarmSchemas 의 스키마 조합으로 워밍업 대상을 생성합니다.<br/>
/// - PrewarmMaxConcurrency 로 동시 실행 수를 제한하여 DB 부하를 제어합니다.<br/>
/// - 각 워밍업 작업에 대해 Warmup 전용 <see cref="DbRequestInfo"/> 를 구성하고
///   <see cref="DbMetrics.TrackSchemaRefresh(bool, string, in DbRequestInfo)"/> 로 계측합니다.
/// </para>
/// </summary>
public sealed class SchemaWarmupService(
    ISchemaService schemaService,
    LibDbOptions options,
    ILogger<SchemaWarmupService> logger
) : BackgroundService
{
    private readonly ISchemaService _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
    private readonly LibDbOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<SchemaWarmupService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    #region [ExecuteAsync 진입점]

    /// <summary>
    /// 호스트 시작 시 한 번 호출되어 스키마 워밍업을 수행합니다.
    /// </summary>
    /// <param name="stoppingToken">호스트 종료 시그널 토큰</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. 워밍업 대상이 없으면 바로 Skip
        if (_options.ConnectionStrings is not { Count: > 0 })
        {
            _logger.LogFastInfo($"[SchemaWarmup] ConnectionStrings 가 비어 있어 워밍업을 건너뜁니다.");
            return;
        }

        if (_options.PrewarmSchemas is not { Count: > 0 })
        {
            _logger.LogFastInfo($"[SchemaWarmup] PrewarmSchemas 가 비어 있어 워밍업을 건너뜁니다.");
            return;
        }

        // 2. 워밍업 대상 목록 구성
        var workItems = BuildWarmupTargets();
        if (workItems.Count == 0)
        {
            _logger.LogFastInfo($"[SchemaWarmup] 유효한 워밍업 대상이 없어 작업을 건너뜁니다.");
            return;
        }

        var concurrency = GetEffectiveConcurrency(_options.PrewarmMaxConcurrency, workItems.Count);

        _logger.LogFastInfo(
            $"[SchemaWarmup] 시작 - 인스턴스 {_options.ConnectionStrings!.Count}개, " +
            $"스키마 {_options.PrewarmSchemas!.Count}개, " +
            $"총 작업 {workItems.Count}개, 동시성 {concurrency}개.");

        try
        {
            await RunWarmupAsync(workItems, concurrency, stoppingToken).ConfigureAwait(false);

            _logger.LogFastInfo(
                $"[SchemaWarmup] 모든 워밍업 작업이 완료되었습니다. (작업 수: {workItems.Count})");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogFastInfo($"[SchemaWarmup] 호스트 종료 요청으로 워밍업이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            // Warmup 실패로 애플리케이션까지 죽는 것을 방지하기 위해 최상위에서 한 번 더 방어
            _logger.LogFastWarn(
                ex,
                $"[SchemaWarmup] 예기치 못한 오류로 일부 워밍업이 실패했습니다. 애플리케이션은 계속 실행됩니다.");
        }
    }

    #endregion

    #region [1. 워밍업 대상 구성]

    /// <summary>
    /// ConnectionStrings를 순회하며 각 인스턴스에 대해 모든 PrewarmSchemas를 단일 타겟으로 구성합니다.
    /// </summary>
    private List<WarmupTarget> BuildWarmupTargets()
    {
        var result = new List<WarmupTarget>(
            capacity: _options.ConnectionStrings!.Count);

        foreach (var kvp in _options.ConnectionStrings!)
        {
            var instanceId = kvp.Key;

            // 빈 문자열 등을 필터링하여 유효한 스키마만 추출
            var targetSchemas = _options.PrewarmSchemas!
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            
            if (targetSchemas.Count > 0)
            {
                result.Add(new WarmupTarget(instanceId, targetSchemas));
            }
        }

        return result;
    }

    /// <summary>
    /// PrewarmMaxConcurrency와 작업 수를 기반으로 실제 사용할 동시성 값을 계산합니다.
    /// </summary>
    /// <param name="requested">옵션에 설정된 PrewarmMaxConcurrency 값 (0이면 자동)</param>
    /// <param name="workItemCount">전체 워밍업 작업 수</param>
    private static int GetEffectiveConcurrency(int requested, int workItemCount)
    {
        if (workItemCount <= 0)
            return 0;

        // 요청값이 0이면 CPU 코어 수 기반으로 자동 결정
        var baseValue = requested > 0 ? requested : Environment.ProcessorCount;

        if (baseValue <= 0)
            baseValue = 1;
        else if (baseValue > 1024)
            baseValue = 1024;

        // 작업 수보다 동시성이 많을 필요는 없음
        return Math.Min(baseValue, workItemCount);
    }

    #endregion

    #region [2. 워밍업 실행 (MaxConcurrency 적용)]

    /// <summary>
    /// 지정된 워밍업 대상들에 대해 동시성 제한을 두고 스키마 사전 로드를 수행합니다.
    /// </summary>
    private async Task RunWarmupAsync(
        IReadOnlyList<WarmupTarget> workItems,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        if (maxConcurrency <= 0)
            maxConcurrency = 1;

        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>(workItems.Count);

        foreach (var item in workItems)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            tasks.Add(ExecuteWarmupForTargetAsync(item, gate, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 단일 인스턴스/스키마 목록 조합에 대해 스키마 워밍업을 수행하고, Warmup 전용 메트릭을 기록합니다.
    /// </summary>
    private async Task ExecuteWarmupForTargetAsync(
        WarmupTarget target,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestInfo = CreateWarmupRequestInfo(in target);
            var stopwatch = Stopwatch.StartNew();
            var success = false;
            string schemaLogStr = string.Join(",", target.SchemaNames);

            try
            {
                var preloadResult = await _schemaService
                    .PreloadSchemaAsync(target.SchemaNames, target.InstanceId, cancellationToken)
                    .ConfigureAwait(false);

                // 검증 결과 확인 및 경고 로깅
                if (preloadResult.MissingSchemas.Count > 0)
                {
                    var missingStr = string.Join(",", preloadResult.MissingSchemas);
                    // LogFastWarn(Exception? ex, string message) pattern
                    _logger.LogFastWarn(
                        null,
                        $"[SchemaWarmup] 경고 - 요청한 스키마 중 일부가 DB에서 발견되지 않아 워밍업에서 제외되었습니다. 누락된 스키마: [{missingStr}], 인스턴스: '{target.InstanceId}'");
                }

                success = true;

                _logger.LogFastInfo(
                    $"[SchemaWarmup] 인스턴스='{target.InstanceId}', 스키마='{schemaLogStr}' 사전 로드 완료. (로드된 항목: {preloadResult.LoadedItemsCount})");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogFastInfo(
                    $"[SchemaWarmup] 취소됨 - 인스턴스='{target.InstanceId}', 스키마='{schemaLogStr}'.");
                throw;
            }
            catch (Exception ex)
            {
                // 개별 워밍업 실패는 로깅만 하고 전체 Warmup은 계속 진행
                _logger.LogFastWarn(
                    ex,
                    $"[SchemaWarmup] 인스턴스='{target.InstanceId}', 스키마='{schemaLogStr}' 사전 로드 중 오류 발생. (애플리케이션은 계속 실행됩니다.)");
            }
            finally
            {
                stopwatch.Stop();

                // 1) Warmup 전용 Duration 메트릭
                DbMetrics.TrackDuration(stopwatch.Elapsed, in requestInfo);

                // 2) Warmup 전용 SchemaRefresh 메트릭
                //    - kind: "Warmup" 으로 고정
                //    - Target(스키마명)은 DbRequestInfo.Target 에 설정됨
                DbMetrics.TrackSchemaRefresh(success, "Warmup", in requestInfo);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    #endregion

    #region [3. Warmup 전용 DbRequestInfo 구성]

    /// <summary>
    /// 스키마 워밍업 작업에 특화된 <see cref="DbRequestInfo"/> 를 구성합니다.
    /// <para>
    /// - Operation: "SCHEMA_WARMUP"<br/>
    /// - CommandKind: "Warmup"<br/>
    /// - IsTransactional: false<br/>
    /// - CorrelationId: "warmup:{InstanceId}:{SchemaCount}"
    /// </para>
    /// </summary>
    /// <param name="target">워밍업 대상 인스턴스/스키마 정보</param>
    private static DbRequestInfo CreateWarmupRequestInfo(in WarmupTarget target)
        => new(
            InstanceId: target.InstanceId,
            DbSystem: "mssql",
            DbName: null,              // 필요 시 DbExecutionContext 확장 후 주입 가능
            DbUser: null,
            ServerAddress: null,
            ServerPort: null,
            Operation: "SCHEMA_WARMUP", // Warmup 전용 Operation
            Target: "bulk-load", // 여러 스키마이므로 고정값 사용
            CommandKind: "Warmup",
            IsTransactional: false,
            CorrelationId: $"warmup:{target.InstanceId}:{target.SchemaNames.Count}"
        );

    #endregion

    #region [4. 내부 레코드 - 워밍업 대상 모델]

    /// <summary>
    /// 스키마 워밍업 대상(인스턴스 ID + 스키마 이름 목록)을 나타내는 경량 레코드입니다.
    /// </summary>
    /// <param name="InstanceId">LibDbOptions.ConnectionStrings 의 키 (인스턴스 해시/이름)</param>
    /// <param name="SchemaNames">워밍업 대상 스키마 이름 목록</param>
    private readonly record struct WarmupTarget(
        string InstanceId,
        List<string> SchemaNames
    );

    #endregion
}

#endregion
