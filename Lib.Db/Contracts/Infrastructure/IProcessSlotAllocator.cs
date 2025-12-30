// ============================================================================
// File : Lib.Db/Contracts/Infrastructure/IProcessSlotAllocator.cs
// Role : 프로세스별 고유 슬롯을 자동으로 할당하는 서비스 인터페이스
// Env  : .NET 10 / C# 14
// Notes:
//   - Mutex 기반으로 0~31 슬롯을 경쟁적으로 획득합니다.
//   - Leader Election, Snowflake ID 생성 등에 활용 가능합니다.
//   - Passive Mode(-1)를 통해 슬롯 고갈 시에도 안전하게 동작합니다.
// ============================================================================

#nullable enable

namespace Lib.Db.Contracts.Infrastructure;

#region 프로세스 슬롯 할당 인터페이스

/// <summary>
/// 동일한 격리 키(IsolationKey)를 공유하는 프로세스들 사이에서 
/// 고유한 슬롯 ID(0~31)를 자동으로 할당하고 관리하는 서비스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>리더 선출 (Leader Election)</b>: Slot 0을 획득한 프로세스에게만 특별한 권한(마이그레이션 등)을 부여하여 중복 실행을 방지합니다.<br/>
/// - <b>분산 식별자</b>: 각 프로세스에 고유 ID(SlotId)를 부여하여 Snowflake ID 생성 등의 NodeID로 활용합니다.<br/>
/// - <b>Graceful Degradation</b>: 모든 슬롯이 고갈되어도 앱이 중단되지 않고 Passive Mode(-1)로 안전하게 동작하도록 보장합니다.
/// </para>
/// </summary>
///
/// <para><b>[동작 원리]</b></para>
/// <list type="number">
///   <item>
///     <description>
///       프로세스 시작 시 0번 슬롯부터 순차적으로 <c>WaitOne(0)</c> (Non-blocking)으로 Mutex 획득을 시도합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       성공 시 해당 슬롯 번호를 <see cref="SlotId"/>로 저장하고, 프로세스가 종료될 때까지 Mutex를 유지합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       모든 슬롯(0~31)이 이미 사용 중이면 <c>SlotId = -1</c> (Passive Mode)로 동작합니다.
///     </description>
///   </item>
/// </list>
///
/// <para><b>[사용 예시]</b></para>
/// <code>
/// // DI 컨테이너에서 주입
/// public class MyService(IProcessSlotAllocator slotAllocator)
/// {
///     public void Initialize()
///     {
///         if (slotAllocator.IsLeader)
///         {
///             // 리더만 수행하는 초기화 작업
///             Console.WriteLine("I am the leader!");
///         }
///         
///         if (slotAllocator.HasSlot)
///         {
///             // SlotId를 Snowflake ID의 NodeID로 사용
///             var nodeId = slotAllocator.SlotId;
///             var uniqueId = GenerateSnowflakeId(nodeId);
///         }
///     }
/// }
/// </code>
///
/// <para><b>[라이프사이클]</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       이 서비스는 <b>Singleton</b>으로 등록되어야 합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       앱 시작 시 슬롯을 획득하고, 앱 종료 시 <see cref="IDisposable.Dispose"/>를 통해 슬롯을 반납합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       프로세스가 비정상 종료(Crash)되면 OS가 Mutex를 자동 회수하며, 
///       다른 프로세스가 <see cref="AbandonedMutexException"/>을 통해 복구할 수 있습니다.
///     </description>
///   </item>
/// </list>
/// </remarks>
public interface IProcessSlotAllocator
{
    #region 속성 (슬롯 상태)

    /// <summary>
    /// 현재 프로세스에 할당된 슬롯 ID를 반환합니다.
    /// </summary>
    /// <value>
    /// <list type="bullet">
    ///   <item><description><b>0~31</b>: 슬롯 획득 성공</description></item>
    ///   <item><description><b>-1</b>: Passive Mode (모든 슬롯 사용 중)</description></item>
    /// </list>
    /// </value>
    /// <remarks>
    /// <para><b>[중요]</b></para>
    /// <para>
    /// 이 값은 생성자에서 한 번 결정되고 이후 변경되지 않습니다 (Immutable).
    /// </para>
    /// <para><b>[활용 예시]</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>Snowflake ID NodeID</b>: <c>var nodeId = slotAllocator.SlotId;</c>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>로그 태깅</b>: <c>logger.LogInformation("SlotId={SlotId}: Processing...", slotAllocator.SlotId);</c>
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    int SlotId { get; }

    /// <summary>
    /// 현재 프로세스가 리더인지 여부를 반환합니다.
    /// </summary>
    /// <value>
    /// <list type="bullet">
    ///   <item><description><c>true</c>: SlotId == 0 (리더)</description></item>
    ///   <item><description><c>false</c>: SlotId != 0 (팔로워 또는 Passive)</description></item>
    /// </list>
    /// </value>
    /// <remarks>
    /// <para><b>[리더 역할 예시]</b></para>
    /// <list type="bullet">
    ///   <item><description>데이터베이스 마이그레이션 실행</description></item>
    ///   <item><description>공유 캐시 초기화</description></item>
    ///   <item><description>스케줄러 작업 조정</description></item>
    ///   <item><description>분산 락 획득</description></item>
    /// </list>
    /// <para><b>[주의사항]</b></para>
    /// <para>
    /// 리더가 크래시되면 다른 프로세스가 재시작 시 Slot 0을 복구하여 
    /// 새로운 리더가 될 수 있습니다.
    /// </para>
    /// </remarks>
    bool IsLeader { get; }

    /// <summary>
    /// 현재 프로세스가 유효한 슬롯을 가지고 있는지 여부를 반환합니다.
    /// </summary>
    /// <value>
    /// <list type="bullet">
    ///   <item><description><c>true</c>: SlotId >= 0 (슬롯 보유)</description></item>
    ///   <item><description><c>false</c>: SlotId == -1 (Passive Mode)</description></item>
    /// </list>
    /// </value>
    /// <remarks>
    /// <para><b>[Passive Mode 처리]</b></para>
    /// <code>
    /// if (!slotAllocator.HasSlot)
    /// {
    ///     logger.LogWarning("Passive Mode: 슬롯 없이 동작 중");
    ///     // 리더 역할 없이 일반 작업만 수행
    ///     return;
    /// }
    /// 
    /// // 슬롯이 있을 때만 수행하는 로직
    /// PerformSlotBasedTask(slotAllocator.SlotId);
    /// </code>
    /// </remarks>
    bool HasSlot { get; }

    #endregion
}

#endregion
