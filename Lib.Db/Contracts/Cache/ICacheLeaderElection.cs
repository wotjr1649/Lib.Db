using System;

namespace Lib.Db.Contracts.Cache;

/// <summary>
/// 프로세스 간 리더 선출을 위한 인터페이스.
/// </summary>
public interface ICacheLeaderElection : IDisposable
{
    /// <summary>
    /// 리더십 획득을 시도합니다. (Non-blocking)
    /// </summary>
    /// <returns>리더십 획득 성공 여부</returns>
    bool TryAcquireLeadership();

    /// <summary>
    /// 현재 프로세스가 리더인지 확인합니다.
    /// </summary>
    bool IsLeader { get; }
}
