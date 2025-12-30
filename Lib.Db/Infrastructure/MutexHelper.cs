// ============================================================================
// File : Lib.Db/Infrastructure/MutexHelper.cs
// Role : 공유 메모리 동기화를 위한 Mutex 생성 유틸리티 (권한 폴백 지원)
// Env  : .NET 10 / C# 14
// Notes:
//   - Global → Local → Unnamed 순으로 Mutex 생성을 시도합니다.
//   - SharedMemoryMappedCache와 ProcessSlotAllocator가 동일한 폴백 로직을 사용하도록 설계되었습니다.
//   - Windows Named Object 권한 문제를 우아하게 처리합니다.
// ============================================================================

#nullable enable

using Microsoft.Extensions.Logging;

namespace Lib.Db.Infrastructure;

#region [Mutex 생성 유틸리티]

/// <summary>
/// 공유 메모리 환경에서 Named Mutex 생성 시 권한 폴백 로직을 제공합니다.
/// </summary>
/// <remarks>
/// <para><b>[설계 목적]</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///   <item>
///     <description>
///       <b>일관성 (Consistency)</b>: <see cref="Caching.SharedMemoryCache"/>와 
///       <see cref="Hosting.ProcessSlotAllocator"/>가 동일한 네임스페이스 폴백 정책을 사용하도록 보장합니다.
///     </description>
///   </item>
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>안정성 (Reliability)</b>: 권한 문제로 인한 예외를 최소화하며, 
///       Graceful Degradation 패턴을 적용합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>크로스 플랫폼 대응</b>: Windows 환경에서 Global/Local 네임스페이스를 시도하고, 
///       실패 시 프로세스 내부 동기화로 폴백합니다.
///     </description>
///   </item>
/// </list>
///
/// <para><b>[폴백 전략]</b></para>
/// <list type="number">
///   <item>
///     <description>
///       <b>1순위 - Global 네임스페이스</b>: <c>Global\{logicalName}</c>
///       <br/>→ 모든 세션/프로세스 간 공유 가능 (관리자 권한 필요할 수 있음)
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>2순위 - Local 네임스페이스</b>: <c>Local\{logicalName}</c>
///       <br/>→ 동일 세션 내 프로세스 간 공유 (일반 사용자 권한)
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>3순위 - Unnamed Mutex</b>: 이름 없음
///       <br/>→ 프로세스 내부만 동기화 (프로세스 간 공유 불가)
///     </description>
///   </item>
/// </list>
///
/// <para><b>[사용 예시]</b></para>
/// <code>
/// using var mutex = MutexHelper.CreateProcessMutex("Lib.Db.TestCache.Stripe0", logger);
/// if (mutex.WaitOne(TimeSpan.FromSeconds(5)))
/// {
///     try
///     {
///         // Critical Section
///     }
///     finally
///     {
///         mutex.ReleaseMutex();
///     }
/// }
/// </code>
/// </remarks>
public static class MutexHelper
{
    #region [Public API - Mutex 생성]

    /// <summary>
    /// Global → Local → Unnamed 순으로 Mutex 생성을 시도합니다.
    /// </summary>
    /// <param name="logicalName">
    /// Mutex 논리 이름 (네임스페이스 접두어 제외)
    /// <br/>예: "Lib.Db.Cache.Stripe0"
    /// </param>
    /// <param name="logger">로거 인스턴스 (폴백 발생 시 경고 로그 기록)</param>
    /// <returns>
    /// 생성된 <see cref="Mutex"/> 인스턴스
    /// <br/>⚠️ 주의: Unnamed Mutex로 폴백된 경우 프로세스 간 동기화가 작동하지 않습니다.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="logicalName"/> 또는 <paramref name="logger"/>가 null인 경우
    /// </exception>
    /// <remarks>
    /// <para><b>[동작 흐름]</b></para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <c>Global\{logicalName}</c> 생성 시도
    ///       <br/>→ 성공 시 즉시 반환
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="UnauthorizedAccessException"/> 발생 시
    ///       <br/>→ <c>Local\{logicalName}</c>로 폴백
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       기타 예외 발생 시
    ///       <br/>→ Unnamed Mutex로 최종 폴백
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>[성능 고려사항]</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       이 메서드는 예외 처리를 포함하므로, 
    ///       가능하면 <b>애플리케이션 시작 시 한 번만 호출</b>하고 결과를 캐싱하십시오.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Mutex 생성 자체는 빠르지만(~1ms 미만), 예외 스택 생성 비용이 있습니다.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public static Mutex CreateProcessMutex(string logicalName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logicalName);
        ArgumentNullException.ThrowIfNull(logger);

        // ====================================================================
        // 1순위: Global 네임스페이스 시도
        // ====================================================================
        try
        {
            var globalMutex = new Mutex(false, $"Global\\{logicalName}");
            
            logger.LogDebug(
                "[MutexHelper] Global Mutex 생성 성공 - 이름: {Name}", 
                logicalName);
            
            return globalMutex;
        }
        catch (UnauthorizedAccessException)
        {
            // Global 네임스페이스 접근 권한 없음 (예상된 케이스)
            logger.LogWarning(
                "[MutexHelper] Global 네임스페이스 권한 없음. Local로 전환합니다. " +
                "이름: {Name}, 영향: 동일 세션 내 프로세스만 동기화됨", 
                logicalName);
        }
        catch (Exception ex)
        {
            // 예상치 못한 예외 (플랫폼 문제, 이름 규칙 위반 등)
            logger.LogError(ex, 
                "[MutexHelper] Global Mutex 생성 중 예외 발생. Local로 폴백 시도. 이름: {Name}", 
                logicalName);
        }

        // ====================================================================
        // 2순위: Local 네임스페이스 폴백
        // ====================================================================
        try
        {
            var localMutex = new Mutex(false, $"Local\\{logicalName}");
            
            logger.LogInformation(
                "[MutexHelper] Local Mutex 생성 성공 - 이름: {Name}, " +
                "영향: 동일 세션 내 프로세스 간 동기화", 
                logicalName);
            
            return localMutex;
        }
        catch (Exception ex)
        {
            // Local도 실패 (매우 드문 케이스: OS 리소스 고갈, 플랫폼 비호환 등)
            logger.LogError(ex, 
                "[MutexHelper] Local Mutex 생성 실패. Unnamed Mutex로 최종 폴백. " +
                "이름: {Name}, 영향: 프로세스 내부만 동기화 (프로세스 간 공유 불가)", 
                logicalName);
        }

        // ====================================================================
        // 3순위: Unnamed Mutex (최종 안전장치)
        // ====================================================================
        logger.LogWarning(
            "[MutexHelper] ⚠️ Unnamed Mutex 사용 - 프로세스 간 동기화가 작동하지 않습니다. " +
            "원래 이름: {Name}", 
            logicalName);
        
        // ⚠️ 중요: 이름이 없으므로 다른 프로세스와 공유되지 않습니다.
        // 하지만 앱이 죽지 않도록 최소한의 동기화는 제공합니다.
        return new Mutex(false);
    }

    #endregion
}

#endregion
