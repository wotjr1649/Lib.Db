// ============================================================================
// 파일: Lib.Db/Infrastructure/ChaosEngineering.cs
// 설명: 카오스 엔지니어링 구현 (개발/테스트 환경용)
// 특징: Thread-Safe Random.Shared 활용
// 대상: .NET 10 / C# 14
// ============================================================================
#nullable enable

using Lib.Db.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace Lib.Db.Infrastructure;

#region [카오스 주입기]

/// <summary>
/// 개발 환경용 카오스 몽키 구현체입니다.
/// <para>
/// Random.Shared를 사용하여 Thread-Safe를 보장합니다.
/// </para>
/// </summary>
/// <param name="options">Lib.Db 전역 옵션 모니터</param>
public sealed class ConfigurableChaosInjector(IOptionsMonitor<LibDbOptions> options) : IChaosInjector
{
    private readonly IOptionsMonitor<LibDbOptions> _options = options;

    /// <summary>
    /// 카오스 정책을 비동기적으로 주입합니다.
    /// <para>
    /// 설정된 확률에 따라 예외 또는 지연을 발생시킵니다.
    /// </para>
    /// </summary>
    public async Task InjectAsync(CancellationToken ct)
    {
        // LibDbOptions.Chaos를 통해 접근
        var cfg = _options.CurrentValue.Chaos;
        if (!cfg.Enabled) return;

        // [개선] Thread-Safe Random.Shared 사용 (.NET 6+)
        double roll = Random.Shared.NextDouble();

        // 1. 예외 주입
        if (roll < cfg.ExceptionRate)
        {
            throw new InvalidOperationException(
                $"[카오스 테스트] 인위적 오류가 주입되었습니다 (확률: {cfg.ExceptionRate:P})");
        }

        // 2. 지연 주입
        if (roll >= cfg.ExceptionRate && roll < (cfg.ExceptionRate + cfg.LatencyRate))
        {
            int delay = Random.Shared.Next(cfg.MinLatencyMs, cfg.MaxLatencyMs);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }
}

#endregion
