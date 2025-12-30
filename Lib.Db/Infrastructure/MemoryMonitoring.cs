// ============================================================================
// 파일: Lib.Db/Infrastructure/MemoryMonitoring.cs
// 설명: 시스템 메모리 압력 모니터링 구현
// 특징: GC 메트릭 기반 + 500ms 캐싱 전략
// 대상: .NET 10 / C# 14
// ============================================================================
#nullable enable

using Lib.Db.Contracts.Execution;

namespace Lib.Db.Infrastructure;

#region [메모리 압력 모니터]

/// <summary>
/// GC 메트릭 기반 시스템 메모리 압력을 모니터링하는 구현체입니다.
/// <para>
/// <b>주요 기능</b>:
/// <list type="bullet">
///   <item>시스템 전체 메모리 사용률 추적</item>
///   <item>500ms 간격 캐싱으로 성능 최적화</item>
///   <item>85% 임계값 기반 위험 상태 감지</item>
/// </list>
/// </para>
/// </summary>
public sealed class SystemMemoryMonitor : IMemoryPressureMonitor
{
    private double _cachedLoadFactor;
    private long _lastCheckTick;
    private const long CacheDurationTicks = 500 * 10000; // 500ms

    /// <summary>
    /// 메모리 사용률이 85%를 초과하면 위험 상태로 간주합니다.
    /// <para>
    /// 이 임계값을 초과하면 대용량 작업을 제한하거나 경고를 발생시킬 수 있습니다.
    /// </para>
    /// </summary>
    public bool IsCritical => LoadFactor > 0.85;

    /// <summary>
    /// 시스템 메모리 사용률 (0.0 ~ 1.0)을 반환합니다.
    /// <para>
    /// <b>캐싱 전략</b>: 500ms 간격으로만 실제 System Call을 수행하여<br/>
    /// 빈번한 조회 시에도 성능을 보장합니다.
    /// </para>
    /// </summary>
    public double LoadFactor
    {
        get
        {
            long now = DateTime.UtcNow.Ticks;
            
            // 500ms 간격으로만 실제 System Call 수행
            if (now - _lastCheckTick > CacheDurationTicks)
            {
                var info = GC.GetGCMemoryInfo();
                long total = info.TotalAvailableMemoryBytes;
                long used = info.MemoryLoadBytes;
                _cachedLoadFactor = total > 0 ? (double)used / total : 0.0;
                _lastCheckTick = now;
            }
            
            return _cachedLoadFactor;
        }
    }
}

#endregion
