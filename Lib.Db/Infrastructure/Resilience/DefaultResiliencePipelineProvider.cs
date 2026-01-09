// ============================================================================
// 파일: Lib.Db/Infrastructure/Resilience/DefaultResiliencePipelineProvider.cs
// 설명: Resilience Pipeline 기본 제공자
// 타겟: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Infrastructure;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Lib.Db.Infrastructure.Resilience;

/// <summary>
/// Resilience Pipeline을 구성하는 기본 제공자입니다.
/// <para>
/// <b>[구성 요소]</b><br/>
/// - Circuit Breaker: 장애 전파 방지<br/>
/// - Retry: 일시적 오류 자동 재시도<br/>
/// - Timeout: 전체 작업 시간 제한
/// </para>
/// </summary>
internal sealed class DefaultResiliencePipelineProvider(
    IOptions<LibDbOptions> options,
    ITransientSqlErrorDetector errorDetector,
    ILogger<DefaultResiliencePipelineProvider> logger
) : IResiliencePipelineProvider
{
    #region [필드] 내부 필드

    private readonly LibDbOptions _options = options.Value;
    private readonly ITransientSqlErrorDetector _errorDetector = errorDetector;
    private readonly ILogger _logger = logger;
    private readonly ResiliencePipeline _pipeline = BuildPipeline(options.Value, errorDetector, logger);

    public bool IsEnabled => true;

    public ResiliencePipeline Pipeline => _pipeline;

    #endregion

    #region [메서드] Resilience Pipeline 구축



    /// <summary>
    /// 설정 기반으로 Resilience Pipeline을 구축합니다.
    /// </summary>
    /// <param name="options">LibDbOptions 설정</param>
    /// <param name="detector">일시적 오류 감지기</param>
    /// <param name="logger">로거</param>
    /// <returns>구성된 Resilience Pipeline</returns>
    private static ResiliencePipeline BuildPipeline(
        LibDbOptions options,
        ITransientSqlErrorDetector detector,
        ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();

        // 1. Circuit Breaker (회로 차단기)
        var cbOptions = options.Resilience;
        if (cbOptions.CircuitBreakerThreshold > 0)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = cbOptions.CircuitBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromMilliseconds(cbOptions.CircuitBreakerSamplingDurationMs),
                MinimumThroughput = cbOptions.CircuitBreakerThreshold,
                BreakDuration = TimeSpan.FromMilliseconds(cbOptions.CircuitBreakerBreakDurationMs),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(detector.IsTransient),
                OnOpened = args =>
                {
                    logger.LogWarning("[Resilience] 회로 열림 (차단)! 차단 시간={Duration}ms", args.BreakDuration.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("[Resilience] 회로 복구 완료");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    logger.LogInformation("[Resilience] 회로 반개 (테스트 중) - 연결 테스트 중");
                    return ValueTask.CompletedTask;
                }
            });
        }

        // 2. Retry (재시도)
        if (cbOptions.MaxRetryCount > 0)
        {
            // 사용자 정의 RetryBackoffType을 Polly의 DelayBackoffType으로 매핑
            var backoffType = cbOptions.RetryBackoffType switch
            {
                LibDbOptions.RetryBackoffType.Exponential => DelayBackoffType.Exponential,
                LibDbOptions.RetryBackoffType.Linear => DelayBackoffType.Linear,
                LibDbOptions.RetryBackoffType.Constant => DelayBackoffType.Constant,
                _ => DelayBackoffType.Exponential
            };

            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = cbOptions.MaxRetryCount,
                Delay = TimeSpan.FromMilliseconds(cbOptions.BaseRetryDelayMs),
                BackoffType = backoffType,
                UseJitter = cbOptions.UseRetryJitter,
                MaxDelay = TimeSpan.FromMilliseconds(cbOptions.MaxRetryDelayMs),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(detector.IsTransient),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "[Resilience] 재시도 {Attempt}/{Max} - {Delay}ms 대기 후 재시도, 원인: {Error}",
                        args.AttemptNumber,
                        cbOptions.MaxRetryCount,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            });
        }

        // 3. Timeout (전체 제한 시간)
        // 전체 실행 시간이 제한을 초과하지 않도록 보장합니다.
        int timeoutSeconds = options.DefaultCommandTimeoutSeconds;
        if (timeoutSeconds > 0)
        {
             // 표준 timeout이 먼저 발동하도록 버퍼를 추가하거나, 여기서 엄격한 timeout을 사용합니다.
             // 일반적으로 Polly timeout은 재시도를 포함한 전체 작업에 대한 것입니다.
             builder.AddTimeout(TimeSpan.FromSeconds(timeoutSeconds + 5)); 
        }

        return builder.Build();
    }

    #endregion
}
