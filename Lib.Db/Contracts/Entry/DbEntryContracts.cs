// ============================================================================
// 파일명 : Lib.Db/Contracts/Entry/DbEntryContracts.cs
// 설명   : DB 작업 진입점/세션/트랜잭션 스코프 계약 + 보간 SQL 핸들러
// 대상   : .NET 10 / C# 14
// 역할   :
//   - 외부(사용자) 관점의 "DB 작업 시작"과 "수명 주기" 계약을 단일 파일로 통합
//   - 인스턴스 선택, 세션/트랜잭션 수명, 보간 SQL(Zero-Allocation) 진입 제공
// ============================================================================

#nullable enable

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lib.Db.Contracts.Entry;

#region DB 컨텍스트 계약

/// <summary>
/// 데이터베이스 작업의 진입점(Entry Point)을 정의하는 컨텍스트 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>추상화 계층</b>: 구체적인 DB 구현(Sql, Oracle 등)에 의존하지 않고 논리적인 진입점만 제공합니다.<br/>
/// - <b>DI 친화적</b>: 애플리케이션 서비스에서 이 인터페이스를 주입받아 모든 데이터 액세스를 시작합니다.<br/>
/// - <b>책임 분리</b>: 실제 실행(<see cref="IDbSession"/>)과 진입(<see cref="IDbContext"/>)을 분리하여 관심사를 명확히 합니다.
/// </para>
/// <para>
/// 이 컨텍스트는 실제 DB 실행 로직을 직접 수행하지 않으며,
/// Fluent 실행 파이프라인(<see cref="IProcedureStage"/>)의 시작점 역할만 담당합니다.
/// </para>
/// </summary>
public interface IDbContext
{
    #region 인스턴스 선택

    /// <summary>
    /// 설정 파일(appsettings.json 등)에 등록된
    /// DB 인스턴스 이름을 사용하여 작업을 시작합니다.
    /// </summary>
    /// <param name="instanceName">등록된 DB 인스턴스 이름</param>
    /// <returns>저장 프로시저/SQL 선택 단계 인터페이스</returns>
    IProcedureStage UseInstance(string instanceName);

    /// <summary>
    /// Ad-hoc 연결 문자열(Connection String)을 직접 지정하여
    /// DB 작업을 시작합니다.
    /// <para>
    /// 주로 테스트, 임시 연결, 멀티 테넌트 시나리오에서 사용됩니다.
    /// </para>
    /// </summary>
    /// <param name="connectionString">직접 사용할 연결 문자열</param>
    /// <returns>저장 프로시저/SQL 선택 단계 인터페이스</returns>
    IProcedureStage UseConnectionString(string connectionString);

    /// <summary>
    /// 기본 인스턴스(<c>"Default"</c>)를 사용하여
    /// DB 작업을 시작합니다.
    /// <para>
    /// 별도의 인스턴스 선택이 필요 없는
    /// 일반적인 애플리케이션 시나리오에서 사용됩니다.
    /// </para>
    /// </summary>
    IProcedureStage Default { get; }

    #endregion

    #region 트랜잭션 시작

    /// <summary>
    /// 명시적 데이터베이스 트랜잭션 스코프를 시작합니다.
    /// <para>
    /// 반환되는 <see cref="IDbTransactionScope"/>는
    /// <c>await using</c> 패턴으로 사용하는 것을 권장합니다.
    /// </para>
    /// <para>
    /// 커밋이 호출되지 않은 상태로 Dispose 될 경우,
    /// 자동으로 롤백됩니다.
    /// </para>
    /// </summary>
    /// <param name="instanceName">대상 DB 인스턴스 이름</param>
    /// <param name="isoLevel">
    /// 트랜잭션 격리 수준
    /// (<see cref="IsolationLevel.ReadCommitted"/>가 기본값)
    /// </param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>트랜잭션 스코프 인터페이스</returns>
    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        IsolationLevel isoLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);

    #endregion
}

#endregion

#region DB 세션 계약

/// <summary>
/// [통합] 데이터베이스 세션 인터페이스
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>라이프사이클 통합</b>: 연결(Connection), 트랜잭션(Transaction), 실행(Execution)의 수명 주기를 하나의 세션 객체를 통해 일관되게 관리합니다.<br/>
/// - <b>리소스 안전성</b>: <see cref="IAsyncDisposable"/> 구현으로 비동기 리소스 해제를 보장합니다.
/// </para>
/// </summary>
public interface IDbSession : IAsyncDisposable, IDisposable
{
    // --- [핵심 진입점] ---
    /// <summary>
    /// 지정된 인스턴스를 대상으로 작업을 시작합니다.
    /// </summary>
    /// <param name="instanceName">연결 대상 인스턴스 이름</param>
    /// <returns>프로시저 단계를 시작할 수 있는 빌더</returns>
    IProcedureStage Use(string instanceName);

    // --- [트랜잭션 관리] ---
    /// <summary>
    /// 명시적 트랜잭션을 시작합니다.
    /// </summary>
    /// <param name="isoLevel">격리 수준 (기본: ReadCommitted)</param>
    /// <param name="ct">취소 토큰</param>
    Task BeginTransactionAsync(IsolationLevel isoLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);

    /// <summary>
    /// 활성 트랜잭션을 커밋합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// 활성 트랜잭션을 롤백합니다.
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task RollbackAsync(CancellationToken ct = default);

    // --- [단축 실행 API] ---
    /// <summary>
    /// Zero-Allocation SQL 문자열 보간 지원
    /// </summary>
    IExecutionStage<Dictionary<string, object?>> Sql(
        [InterpolatedStringHandlerArgument("")] ref SessionSqlStringHandler handler);
}

#endregion

#region 트랜잭션 범위 계약

/// <summary>
/// 데이터베이스 트랜잭션의 수명과 커밋/롤백을 관리하는 범위(Scope) 인터페이스입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>안전한 기본값</b>: 명시적 커밋(Commit) 없이는 절대 반영되지 않는 'Secure by Default' 원칙을 따릅니다.<br/>
/// - <b>자동 롤백</b>: 예외 발생이나 실수로 인한 커밋 누락 시, Dispose 단계에서 자동으로 롤백하여 데이터 무결성을 지킵니다.
/// </para>
/// </summary>
public interface IDbTransactionScope : IAsyncDisposable
{
    #region 명령 준비

    /// <summary>
    /// 저장 프로시저(SP) 실행을 트랜잭션 컨텍스트 내에서 준비합니다.
    /// <para>
    /// 반환되는 <see cref="IParameterStage"/>를 통해
    /// 파라미터 설정 및 실제 실행 단계로 이어집니다.
    /// </para>
    /// </summary>
    /// <param name="spName">실행할 저장 프로시저 이름</param>
    /// <returns>파라미터 설정 단계 인터페이스</returns>
    IParameterStage Procedure(string spName);

    /// <summary>
    /// 일반 SQL 쿼리(Text) 실행을 트랜잭션 컨텍스트 내에서 준비합니다.
    /// <para>
    /// 반환되는 <see cref="IParameterStage"/>를 통해
    /// 파라미터 설정 및 실제 실행 단계로 이어집니다.
    /// </para>
    /// </summary>
    /// <param name="sqlText">실행할 SQL 텍스트</param>
    /// <returns>파라미터 설정 단계 인터페이스</returns>
    IParameterStage Sql(string sqlText);

    #endregion

    #region 트랜잭션 제어

    /// <summary>
    /// 현재 트랜잭션을 명시적으로 커밋합니다.
    /// <para>
    /// 커밋이 성공적으로 완료되면,
    /// 이후 Dispose 시 자동 롤백은 수행되지 않습니다.
    /// </para>
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// 현재 트랜잭션을 명시적으로 롤백합니다.
    /// <para>
    /// 일반적으로 예외 처리 경로에서 호출되며,
    /// Dispose 시에도 커밋되지 않은 경우 자동 롤백됩니다.
    /// </para>
    /// </summary>
    /// <param name="ct">취소 토큰</param>
    Task RollbackAsync(CancellationToken ct = default);

    #endregion
}

#endregion

#region 보간 핸들러

/// <summary>
/// <see cref="DbSession.Sql(ref SessionSqlStringHandler)"/> 메서드 전용 핸들러.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>Zero-Allocation</b>: <c>ref struct</c>로 설계되어 힙 할당 없이 스택 상에서만 존재하며, 내부 버퍼를 재사용합니다.<br/>
/// - <b>안전한 파라미터화</b>: 보간된 값들을 자동으로 추출하여 파라미터화하므로 SQL Injection을 원천 차단합니다.
/// </para>
/// </summary>
[InterpolatedStringHandler]
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct SessionSqlStringHandler
{
    private readonly StringBuilder _builder;
    public readonly Dictionary<string, object?> Parameters;
    private int _paramIndex;

    /// <summary>
    /// [컴파일러 호출] DbSession의 내부 리소스를 빌려서 사용합니다.
    /// </summary>
    public SessionSqlStringHandler(
        int literalLength,
        int formattedCount,
        DbSession session) // <--- session 인스턴스가 주입됨
    {
        // 세션의 공유 빌더 사용
        _builder = session.GetSharedBuilder();
        _builder.EnsureCapacity(_builder.Length + literalLength + (formattedCount * 5));

        // 파라미터 딕셔너리 생성 (풀링 가능하지만 여기선 새로 생성)
        Parameters = new Dictionary<string, object?>(formattedCount);
        _paramIndex = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string s)
    {
        _builder.Append(s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value)
    {
        string paramName = $"@p{_paramIndex++}";
        _builder.Append(paramName);
        Parameters[paramName] = value;
    }

    // 포맷 지정자 지원
    public void AppendFormatted<T>(T value, string? format) => AppendFormatted(value);
}


#endregion