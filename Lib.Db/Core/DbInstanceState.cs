// ============================================================================
// 파일: Lib.Db/Core/DbInstanceState.cs
// 설명: 인스턴스별 DB 연결/트랜잭션 상태 컨테이너
// 대상: .NET 10 / C# 14
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Execution;

namespace Lib.Db.Core;

/// <summary>
/// 인스턴스별 DB 연결 상태를 격리 관리하는 내부 컨테이너.
/// <para><b>[설계 의도]</b> ConcurrentDictionary의 값으로 사용되어 인스턴스별 독립 연결/트랜잭션을 보장한다.</para>
/// </summary>
internal sealed class DbInstanceState
{
    /// <summary>인스턴스 이름 (ex: "Default", "Reporting")</summary>
    public required string InstanceName { get; init; }

    /// <summary>연결 문자열 해시 (풀 격리용)</summary>
    public required string ConnectionHash { get; init; }

    /// <summary>활성 연결 (Resilient 모드에서는 null — 매 쿼리마다 풀에서 획득)</summary>
    public SqlConnection? Connection { get; set; }

    /// <summary>활성 트랜잭션 (null이면 트랜잭션 미사용)</summary>
    public SqlTransaction? Transaction { get; set; }

    /// <summary>현재 활성 실행기 (트랜잭션 모드일 때 Transactional Executor)</summary>
    public IDbExecutor? ActiveExecutor { get; set; }

    /// <summary>Ad-hoc 연결 여부 (Dispose 시 ConnectionFactory 등록 해제 대상)</summary>
    public bool IsAdHoc { get; init; }
}
