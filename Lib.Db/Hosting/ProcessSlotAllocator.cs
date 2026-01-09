// ============================================================================
// File : Lib.Db/Hosting/ProcessSlotAllocator.cs
// Role : Mutex 기반 프로세스 슬롯 자동 할당 구현 (0~31)
// Env  : .NET 10 / C# 14
// Notes:
//   - Non-blocking WaitOne(0)으로 빠른 슬롯 탐색
//   - AbandonedMutexException 처리로 크래시 복구
//   - Passive Mode로 Graceful Degradation
//   - Singleton으로 등록하여 앱 수명 동안 슬롯 유지
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Infrastructure;
using Lib.Db.Infrastructure;

namespace Lib.Db.Hosting;

#region [프로세스 슬롯 할당자 - 실제 구현]

/// <summary>
/// Mutex 기반으로 0~31 슬롯을 경쟁적으로 획득하여 프로세스별 고유 ID를 제공합니다.
/// </summary>
/// <remarks>
/// <para><b>[구현 전략]</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>순차 탐색</b>: 0번부터 31번까지 순서대로 시도하여 빈 슬롯을 찾습니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Non-blocking</b>: <c>WaitOne(0)</c>을 사용하여 이미 사용 중인 슬롯에서 대기하지 않습니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>크래시 복구</b>: <see cref="AbandonedMutexException"/>을 캐치하여 
///       이전 소유자가 비정상 종료한 슬롯을 복구합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Passive Mode</b>: 모든 슬롯이 사용 중이면 <c>SlotId = -1</c>로 동작합니다.
///     </description>
///   </item>
/// </list>
///
/// <para><b>[Mutex 네이밍 규칙]</b></para>
/// <para>
/// <c>Lib.Db.{isolationKey}.Slot.{0~31}</c>
/// <br/>예: <c>Lib.Db.a1b2c3d4.Slot.0</c>
/// </para>
/// <para>
/// 동일한 <c>isolationKey</c>를 사용하는 프로세스만 동일한 슬롯 풀을 공유합니다.
/// </para>
///
/// <para><b>[동시성 안전성]</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       생성자에서 슬롯을 획득하고, <see cref="Dispose"/>에서 반납합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="SlotId"/>, <see cref="IsLeader"/>, <see cref="HasSlot"/>은 
///       생성 후 불변(Immutable)이므로 스레드 안전합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       Mutex는 <b>Thread-Affine</b>이므로, 같은 스레드에서 <c>WaitOne</c>과 <c>ReleaseMutex</c>를 호출해야 합니다.
///       (DI Singleton이므로 문제없음)
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class ProcessSlotAllocator : IProcessSlotAllocator, IDisposable
{
    #region [상수 및 필드]

    /// <summary>지원하는 최대 슬롯 개수 (0~31)</summary>
    private const int MAX_SLOTS = 32;

    /// <summary>획득한 슬롯의 Mutex (null이면 Passive Mode)</summary>
    private readonly Mutex? _slotMutex;

    /// <summary>할당된 슬롯 ID (0~31 또는 -1)</summary>
    private readonly int _slotId = -1;

    /// <summary>로거</summary>
    private readonly ILogger<ProcessSlotAllocator> _logger;

    #endregion

    #region [Properties - IProcessSlotAllocator 구현]

    /// <inheritdoc />
    public int SlotId => _slotId;

    /// <inheritdoc />
    public bool IsLeader => _slotId == 0;

    /// <inheritdoc />
    public bool HasSlot => _slotId >= 0;

    #endregion

    #region [생성자]

    /// <summary>
    /// 프로세스 슬롯 할당자를 생성하고 사용 가능한 슬롯을 자동으로 획득합니다.
    /// </summary>
    /// <param name="isolationKey">
    /// 격리 키 (동일한 키를 가진 프로세스끼리만 슬롯 풀을 공유)
    /// </param>
    /// <param name="logger">로거</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="isolationKey"/> 또는 <paramref name="logger"/>가 null인 경우
    /// </exception>
    /// <remarks>
    /// <para><b>[실행 흐름]</b></para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <b>Step 1</b>: Slot 0부터 순차적으로 Mutex 생성 및 <c>WaitOne(0)</c> 시도
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 2</b>: 획득 성공 시 → <c>_slotId = i</c>, <c>_slotMutex = mutex</c>, 반복 종료
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 3</b>: <see cref="AbandonedMutexException"/> 발생 시 → 복구하여 획득
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Step 4</b>: 모든 슬롯 실패 시 → Passive Mode (<c>_slotId = -1</c>)
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>[성능 특성]</b></para>
    /// <list type="bullet">
    ///   <item><description>평균 시간: O(1) ~ O(32) (빈 슬롯까지 탐색)</description></item>
    ///   <item><description>블로킹 없음 (<c>WaitOne(0)</c> 사용)</description></item>
    ///   <item><description>시작 시 한 번만 실행 (생성자)</description></item>
    /// </list>
    /// </remarks>
    public ProcessSlotAllocator(
        string isolationKey,
        ILogger<ProcessSlotAllocator> logger)
    {
        ArgumentNullException.ThrowIfNull(isolationKey);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // ====================================================================
        // 슬롯 획득 루프 (0번부터 31번까지 순차 탐색)
        // ====================================================================
        for (int i = 0; i < MAX_SLOTS; i++)
        {
            var mutexName = $"Lib.Db.{isolationKey}.Slot.{i}";
            var mutex = MutexHelper.CreateProcessMutex(mutexName, logger);

            try
            {
                // ============================================================
                // [핵심] Non-blocking 시도 (WaitOne(0))
                // ============================================================
                // 즉시 획득 가능하면 true 반환, 이미 사용 중이면 false 반환
                if (mutex.WaitOne(0))
                {
                    _slotId = i;
                    _slotMutex = mutex;

                    logger.LogInformation(
                        "[ProcessSlot] Slot {SlotId} 획득 성공 (Leader={IsLeader}, IsolationKey={Key})",
                        i, IsLeader, isolationKey);

                    return; // 성공, 반복 종료
                }
            }
            catch (AbandonedMutexException)
            {
                // ============================================================
                // [Critical] 이전 소유자 비정상 종료 → 복구
                // ============================================================
                // OS가 Mutex를 "Abandoned" 상태로 마킹했지만,
                // 이 예외가 발생했다는 것은 우리가 새 소유자가 되었다는 의미입니다.
                // 따라서 정상적으로 획득한 것으로 처리합니다.
                _slotId = i;
                _slotMutex = mutex;

                logger.LogWarning(
                    "[ProcessSlot] Slot {SlotId} 복구 완료 (Abandoned Mutex 획득, IsolationKey={Key})",
                    i, isolationKey);

                return; // 복구 성공, 반복 종료
            }
            catch (Exception ex)
            {
                // ============================================================
                // 기타 예외: 일시적 오류 또는 이미 사용 중
                // ============================================================
                logger.LogDebug(
                    "[ProcessSlot] Slot {SlotId} 점유 중 또는 오류 (IsolationKey={Key}): {Error}",
                    i, isolationKey, ex.Message);

                // 실패한 Mutex는 즉시 해제 (메모리 누수 방지)
                mutex.Dispose();
            }
        }

        // ====================================================================
        // Passive Mode: 모든 슬롯(0~31)이 사용 중
        // ====================================================================
        logger.LogWarning(
            "[ProcessSlot] ⚠️ 모든 슬롯({MaxSlots})이 사용 중입니다. " +
            "Passive Mode로 전환합니다. (SlotId=-1, IsLeader=false, IsolationKey={Key})",
            MAX_SLOTS, isolationKey);
    }

    #endregion

    #region [IDisposable 구현]

    /// <summary>
    /// 획득한 슬롯을 반납합니다 (Mutex 해제).
    /// </summary>
    /// <remarks>
    /// <para><b>[중요]</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       이 메서드는 앱 종료 시 DI 컨테이너에 의해 자동으로 호출됩니다.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Passive Mode인 경우 (_slotMutex == null) 아무 작업도 하지 않습니다.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>ReleaseMutex</c> 실패 시에도 앱이 죽지 않도록 예외를 삼킵니다.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public void Dispose()
    {
        if (_slotMutex == null)
        {
            // Passive Mode: 반납할 슬롯이 없음
            return;
        }

        try
        {
            _slotMutex.ReleaseMutex();
            _logger.LogInformation(
                "[ProcessSlot] Slot {SlotId} 반납 완료",
                _slotId);
        }
        catch (Exception ex)
        {
            // ReleaseMutex 실패는 치명적이지 않음 (앱 종료 중이므로)
            // 로그만 남기고 계속 진행
            _logger.LogError(ex,
                "[ProcessSlot] Slot {SlotId} 반납 중 예외 발생 (무시됨)",
                _slotId);
        }
        finally
        {
            // Mutex 핸들 해제
            _slotMutex.Dispose();
        }
    }

    #endregion
}

#endregion
