// ============================================================================
// File : Lib.Db/Hosting/PassiveProcessSlotAllocator.cs
// Role : 공유 메모리 비활성화 시 사용되는 Passive 슬롯 할당자 (Null Object Pattern)
// Env  : .NET 10 / C# 14
// Notes:
//   - SlotId = -1, IsLeader = false, HasSlot = false를 항상 반환
//   - DI가 깨지지 않도록 안전한 기본 구현 제공
//   - 실제 Mutex 생성 없이 메모리 효율적
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Infrastructure;

namespace Lib.Db.Hosting;

#region [Passive 슬롯 할당자 - Null Object Pattern]

/// <summary>
/// 공유 메모리가 비활성화된 환경에서 사용되는 Passive 모드 슬롯 할당자입니다.
/// </summary>
/// <remarks>
/// <para><b>[설계 목적: Null Object Pattern]</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>NullReferenceException 방지</b>: <see cref="IProcessSlotAllocator"/>를 
///       DI로 주입받는 코드가 null 체크 없이 안전하게 동작하도록 보장합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Graceful Degradation</b>: 공유 메모리 비활성화 시에도 앱이 정상 동작하도록 합니다.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>명시적 상태</b>: <c>SlotId = -1</c>로 "슬롯 없음" 상태를 명확하게 표현합니다.
///     </description>
///   </item>
/// </list>
///
/// <para><b>[사용 시나리오]</b></para>
/// <list type="number">
///   <item>
///     <description>
///       <c>LibDbOptions.EnableSharedMemoryCache = false</c>로 설정된 경우
///     </description>
///   </item>
///   <item>
///     <description>
///       Linux/macOS 등 Named Mutex를 지원하지 않는 환경
///     </description>
///   </item>
///   <item>
///     <description>
///       단일 프로세스 환경 (Docker 컨테이너, Lambda 등)
///     </description>
///   </item>
/// </list>
///
/// <para><b>[동작 예시]</b></para>
/// <code>
/// // DI 컨테이너 등록 (ServiceRegistrationHelpers.cs에서)
/// if (!enableSharedMemory)
/// {
///     services.TryAddSingleton&lt;IProcessSlotAllocator&gt;(
///         new PassiveProcessSlotAllocator());
/// }
/// 
/// // 사용하는 쪽 코드는 동일
/// public class MyService(IProcessSlotAllocator allocator)
/// {
///     public void DoWork()
///     {
///         if (allocator.IsLeader)
///         {
///             // Passive 모드에서는 절대 실행되지 않음
///         }
///         
///         if (!allocator.HasSlot)
///         {
///             // Passive 모드에서는 항상 여기로 진입
///             Console.WriteLine("슬롯 없음, 일반 작업만 수행");
///         }
///     }
/// }
/// </code>
///
/// <para><b>[성능 특성]</b></para>
/// <list type="bullet">
///   <item><description>메모리: 거의 없음 (필드 3개만)</description></item>
///   <item><description>CPU: O(1) (모든 속성이 상수)</description></item>
///   <item><description>Mutex 생성 없음 (OS 리소스 절약)</description></item>
/// </list>
/// </remarks>
internal sealed class PassiveProcessSlotAllocator : IProcessSlotAllocator
{
    #region [Properties - IProcessSlotAllocator 구현]

    /// <summary>
    /// 항상 -1을 반환합니다 (Passive Mode).
    /// </summary>
    /// <value>-1</value>
    public int SlotId => -1;

    /// <summary>
    /// 항상 false를 반환합니다 (리더 없음).
    /// </summary>
    /// <value>false</value>
    public bool IsLeader => false;

    /// <summary>
    /// 항상 false를 반환합니다 (슬롯 없음).
    /// </summary>
    /// <value>false</value>
    public bool HasSlot => false;

    #endregion

    #region [생성자]

    /// <summary>
    /// Passive 슬롯 할당자를 생성합니다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이 생성자는 매개변수가 없으며, 어떤 리소스도 할당하지 않습니다.
    /// </para>
    /// <para><b>[내부 사용 전용]</b></para>
    /// <para>
    /// 이 클래스는 <c>internal</c>로 선언되어 외부에서 직접 생성할 수 없습니다.
    /// <br/><c>ServiceRegistrationHelpers</c>에서만 인스턴스를 생성합니다.
    /// </para>
    /// </remarks>
    internal PassiveProcessSlotAllocator()
    {
        // 상태가 모두 상수이므로 초기화 불필요
    }

    #endregion
}

#endregion
