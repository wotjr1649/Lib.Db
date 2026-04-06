// ============================================================================
// 파일명 : Lib.Db/Contracts/Entry/DbEntryContracts.cs
// 설명   : DB 작업 진입점(IDbSession) + 트랜잭션 스코프(IDbTransactionScope) 계약
// 대상   : .NET 10 / C# 14
// 역할   :
//   - 외부(사용자) 관점의 "DB 작업 시작"과 "수명 주기" 계약을 단일 파일로 통합
//   - IDbContext를 제거하고 IDbSession이 유일한 진입점 역할 수행
// ============================================================================

#nullable enable

using Lib.Db.Contracts.Core;

namespace Lib.Db.Contracts.Entry;

#region DB 세션 계약

/// <summary>
/// DB 작업의 유일한 진입점. Fluent API의 시작점이며 인스턴스별 독립 트랜잭션을 지원한다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>단일 진입점</b>: 외부 프로젝트는 이 인터페이스만으로 모든 DB 작업을 수행합니다.<br/>
/// - <b>라이프사이클 통합</b>: 연결(Connection), 트랜잭션(Transaction), 실행(Execution)의 수명 주기를 하나의 세션 객체를 통해 일관되게 관리합니다.<br/>
/// - <b>리소스 안전성</b>: <see cref="IAsyncDisposable"/> 구현으로 비동기 리소스 해제를 보장합니다.
/// </para>
/// </summary>
public interface IDbSession : IAsyncDisposable
{
    #region 인스턴스 선택

    /// <summary>
    /// 등록된 DB 인스턴스로 작업을 시작합니다.
    /// </summary>
    /// <param name="instanceName">등록된 DB 인스턴스 이름</param>
    /// <returns>저장 프로시저/SQL 선택 단계 인터페이스</returns>
    IProcedureStage Use(string instanceName);

    /// <summary>
    /// Ad-hoc 연결 문자열로 작업을 시작합니다.
    /// <para>주로 테스트, 임시 연결, 멀티 테넌트 시나리오에서 사용됩니다.</para>
    /// </summary>
    /// <param name="connectionString">직접 사용할 연결 문자열</param>
    /// <returns>저장 프로시저/SQL 선택 단계 인터페이스</returns>
    IProcedureStage UseConnectionString(string connectionString);

    /// <summary>
    /// 기본 인스턴스(<c>"Default"</c>)로 작업을 시작합니다.
    /// <para>별도의 인스턴스 선택이 필요 없는 일반적인 시나리오에서 사용됩니다.</para>
    /// </summary>
    IProcedureStage Default { get; }

    #endregion

    #region 벌크 연산

    /// <summary>
    /// SqlBulkCopy 기반 대량 INSERT를 수행합니다.
    /// <para>
    /// <b>[설계 의도]</b><br/>
    /// - <b>고성능 벌크</b>: TVP 대비 수만~수십만 건 이상에서 더 빠른 성능을 제공합니다.<br/>
    /// - <b>Reflection 사용</b>: T의 public property를 열 매핑에 사용하므로 AOT 환경에서는 지원되지 않습니다.<br/>
    /// - <b>DbResult 패턴</b>: 성공/실패를 명시적으로 반환합니다.
    /// </para>
    /// </summary>
    /// <typeparam name="T">레코드 타입 (public property가 대상 테이블 컬럼과 매핑)</typeparam>
    /// <param name="instanceName">등록된 DB 인스턴스 이름</param>
    /// <param name="destinationTable">대상 테이블 이름 (예: "[gap].[BulkTarget]")</param>
    /// <param name="records">삽입할 레코드 컬렉션</param>
    /// <param name="options">벌크 삽입 옵션 (null 시 기본값 사용)</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>삽입된 행 수를 포함하는 <see cref="DbResult{T}"/></returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "BulkInsertAsync는 Reflection을 사용하여 T의 속성을 열거합니다. AOT 환경에서는 사용할 수 없습니다.")]
    Task<DbResult<long>> BulkInsertAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkInsertOptions? options = null,
        CancellationToken ct = default) where T : class;

    #endregion

    #region 트랜잭션 시작

    /// <summary>
    /// 인스턴스별 독립 트랜잭션을 시작합니다. (기본 격리 수준: ReadCommitted)
    /// <para>
    /// 반환되는 <see cref="IDbTransactionScope"/>는
    /// <c>await using</c> 패턴으로 사용하는 것을 권장합니다.
    /// 커밋이 호출되지 않은 상태로 Dispose 될 경우, 자동으로 롤백됩니다.
    /// </para>
    /// </summary>
    /// <param name="instanceName">대상 DB 인스턴스 이름</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>트랜잭션 스코프 인터페이스</returns>
    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        CancellationToken ct = default);

    /// <summary>
    /// 지정 인스턴스에서 특정 격리 수준으로 트랜잭션을 시작합니다.
    /// <para>
    /// 반환되는 <see cref="IDbTransactionScope"/>는
    /// <c>await using</c> 패턴으로 사용하는 것을 권장합니다.
    /// 커밋이 호출되지 않은 상태로 Dispose 될 경우, 자동으로 롤백됩니다.
    /// </para>
    /// </summary>
    /// <param name="instanceName">대상 DB 인스턴스 이름</param>
    /// <param name="isolationLevel">트랜잭션 격리 수준</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>트랜잭션 스코프 인터페이스</returns>
    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        System.Data.IsolationLevel isolationLevel,
        CancellationToken ct = default);

    #endregion
}

#endregion

#region 트랜잭션 범위 계약

/// <summary>
/// 데이터베이스 트랜잭션의 수명과 커밋/롤백을 관리하는 범위(Scope) 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>안전한 기본값</b>: 명시적 커밋(Commit) 없이는 절대 반영되지 않는 'Secure by Default' 원칙을 따릅니다.<br/>
/// - <b>자동 롤백</b>: 예외 발생이나 실수로 인한 커밋 누락 시, Dispose 단계에서 자동으로 롤백하여 데이터 무결성을 지킵니다.<br/>
/// - <b>Fluent API 통합</b>: <see cref="IProcedureStage"/>를 상속하여 트랜잭션 내에서 Fluent 체이닝을 직접 시작할 수 있습니다.
/// </para>
/// </summary>
public interface IDbTransactionScope : IProcedureStage, IAsyncDisposable
{
    #region 트랜잭션 제어

    /// <summary>
    /// 현재 트랜잭션을 명시적으로 커밋합니다.
    /// <para>
    /// 커밋이 성공적으로 완료되면,
    /// 이후 Dispose 시 자동 롤백은 수행되지 않습니다.
    /// </para>
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>커밋 성공 여부를 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<bool>> CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// 현재 트랜잭션을 명시적으로 롤백합니다.
    /// <para>
    /// 일반적으로 예외 처리 경로에서 호출되며,
    /// Dispose 시에도 커밋되지 않은 경우 자동 롤백됩니다.
    /// </para>
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    /// <returns>롤백 성공 여부를 포함하는 <see cref="DbResult{T}"/></returns>
    Task<DbResult<bool>> RollbackAsync(CancellationToken ct = default);

    #endregion
}

#endregion
