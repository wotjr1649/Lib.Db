using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Lib.Db.Configuration;

namespace Lib.Db.Infrastructure.Resilience;

/// <summary>
/// 상황에 따라 적응형으로 회복탄력성 정책을 생성하는 팩터리입니다.
/// <para>
/// <b>[Self-Healing Logic]</b>
/// - 장애 발생률이 높아지면 Circuit Breaker의 민감도를 높입니다.
/// - 일시적 오류(Transient Error) 패턴을 학습하여 Retry 간격(Exponential Backoff)을 조절합니다.
/// </para>
/// </summary>
public class AdaptivePolicyFactory
{
    private readonly ILogger<AdaptivePolicyFactory> _logger;
    private readonly LibDbOptions _options;

    // 동적 상태 추적을 위한 간단한 카운터 (실제 구현에선 Metrics/SlidingWindow 사용 권장)
    private static int s_failureCount;

    public AdaptivePolicyFactory(ILogger<AdaptivePolicyFactory> logger, LibDbOptions options)
    {
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// 현재 시스템 상태에 최적화된 Resiliency Pipeline을 생성합니다.
    /// </summary>
    public ResiliencePipeline CreateResiliencePipeline(string key)
    {
        var builder = new ResiliencePipelineBuilder();

        // 1. 적응형 Retry 전략
        var retryStrategy = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(), // 모든 예외 핸들링 (데모용)
            MaxRetryAttempts = _options.EnableResilience ? 3 : 1,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
            OnRetry = args =>
            {
                Interlocked.Increment(ref s_failureCount);
                _logger.LogWarning("재시도(Adaptive) 발생. 현재 실패 카운트: {Count}. 대기: {Delay}", 
                    s_failureCount, args.RetryDelay);
                return ValueTask.CompletedTask;
            }
        };

        // 실패가 급증하면 지수 백오프를 더 공격적으로 적용
        if (s_failureCount > 10)
        {
            retryStrategy.Delay = TimeSpan.FromMilliseconds(500);
            retryStrategy.MaxRetryAttempts = 5;
            _logger.LogInformation("시스템 불안정 감지: 방어적 재시도 정책(High-Resilience)이 적용되었습니다.");
        }

        builder.AddRetry(retryStrategy);

        // 2. 적응형 Circuit Breaker
        if (_options.EnableResilience)
        {
            double failureRatio = s_failureCount > 50 ? 0.2 : 0.5; // 불안정하면 20%만 실패해도 차단

            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = failureRatio,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    _logger.LogError("Circuit Breaker 열림! (BreakDuration: {Duration})", args.BreakDuration);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    Interlocked.Exchange(ref s_failureCount, 0); // 상태 초기화
                    _logger.LogInformation("Circuit Breaker 닫힘. 시스템이 회복되었습니다.");
                    return ValueTask.CompletedTask;
                }
            });
        }

        // 3. Timeout (고정)
        builder.AddTimeout(TimeSpan.FromSeconds(30));

        return builder.Build();
    }
}
