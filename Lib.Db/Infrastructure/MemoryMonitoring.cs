// ============================================================================
// 파일: Lib.Db/Infrastructure/MemoryMonitoring.cs
// 설명: 시스템 메모리 압력 모니터링 구현
// 특징: GC 메트릭 기반 + 500ms 캐싱 전략 + ARM64 안전 Volatile 접근
// 대상: .NET 10 / C# 14
// ============================================================================
#nullable enable

using System.Diagnostics;
using System.Threading;
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
///   <item>ARM64 다중 스레드 안전: Volatile.Read/Write 사용</item>
///   <item>DateTime.UtcNow.Ticks 대신 Stopwatch.GetTimestamp() 사용으로 오버헤드 감소</item>
/// </list>
/// </para>
/// <para>
/// <b>[설계 의도]</b><br/>
/// <c>_cachedLoadFactor</c>(double)와 <c>_lastCheckTimestamp</c>(long)는 여러 스레드에서
/// 동시에 접근될 수 있습니다. ARM64 등 약한 메모리 모델 아키텍처에서 stale read를 방지하기 위해
/// <see cref="Volatile.Read{T}"/>/<see cref="Volatile.Write{T}"/>를 사용합니다.<br/>
/// 타임스탬프는 <see cref="DateTime.UtcNow"/>보다 오버헤드가 낮은
/// <see cref="Stopwatch.GetTimestamp()"/>로 교체하였습니다.
/// </para>
/// </summary>
public sealed class SystemMemoryMonitor : IMemoryPressureMonitor
{
    #region 필드 선언 (C# 14)

    // [Thread-Safety] ARM64 약한 메모리 모델 대응: Volatile.Read/Write로만 접근
    private double _cachedLoadFactor;
    private long _lastCheckTimestamp;

    /// <summary>500ms에 해당하는 Stopwatch 틱 수 (정적 초기화, 재계산 불필요)</summary>
    private static readonly long s_cacheDuration =
        (long)(Stopwatch.Frequency * 0.5); // 500ms

    #endregion

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
    /// <b>캐싱 전략</b>: 500ms 간격으로만 실제 GC System Call을 수행하여<br/>
    /// 빈번한 조회 시에도 성능을 보장합니다.<br/>
    /// 필드 접근은 <see cref="Volatile"/>로 보호되어 ARM64에서도 stale read가 발생하지 않습니다.
    /// </para>
    /// </summary>
    public double LoadFactor
    {
        get
        {
            long now = Stopwatch.GetTimestamp();

            // 500ms 간격으로만 실제 System Call 수행
            // Volatile.Read: ARM64 등 약한 메모리 모델에서 stale read 방지
            if (now - Volatile.Read(ref _lastCheckTimestamp) > s_cacheDuration)
            {
                GCMemoryInfo info = GC.GetGCMemoryInfo();
                long total = info.TotalAvailableMemoryBytes;
                long used = info.MemoryLoadBytes;
                Volatile.Write(ref _cachedLoadFactor, total > 0 ? (double)used / total : 0.0);
                Volatile.Write(ref _lastCheckTimestamp, now);
            }

            return Volatile.Read(ref _cachedLoadFactor);
        }
    }
}

#endregion
