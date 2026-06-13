// ============================================================================
// 파일: Lib.Db.Contracts/Core/DbResult.cs
// 설명: DB 작업 결과를 나타내는 공용 타입 (DbResult<T>, DbError, DbErrorKind)
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.Contracts.Core;

#region DbErrorKind 열거형

/// <summary>
/// DB 오류의 종류를 분류하는 열거형입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>패턴 매칭 활용</b>: switch expression으로 오류 종류별 분기 처리를 간결하게 합니다.<br/>
/// - <b>재시도 판단</b>: <see cref="DbError.IsTransient"/>과 연계하여 자동 재시도 대상을 결정합니다.<br/>
/// - <b>확장성</b>: 새로운 오류 유형 추가 시 열거 값만 추가하면 됩니다.
/// </para>
/// </summary>
public enum DbErrorKind
{
    /// <summary>오류 종류가 지정되지 않았습니다.</summary>
    None = 0,

    /// <summary>저장 프로시저, 테이블 등 스키마 객체를 찾을 수 없습니다.</summary>
    SchemaNotFound,

    /// <summary>DB 인증에 실패했습니다. (로그인 오류)</summary>
    AuthenticationFailed,

    /// <summary>DB 연결이 끊어졌습니다.</summary>
    ConnectionLost,

    /// <summary>쿼리 실행 제한 시간이 초과되었습니다.</summary>
    Timeout,

    /// <summary>교착 상태(Deadlock)가 감지되었습니다.</summary>
    Deadlock,

    /// <summary>제약 조건 위반이 발생했습니다. (PK, FK, UNIQUE 등)</summary>
    ConstraintViolation,

    /// <summary>데이터 형식 변환 오류가 발생했습니다.</summary>
    DataConversion,

    /// <summary>저장 프로시저 매개변수가 일치하지 않습니다.</summary>
    ParameterMismatch,

    /// <summary>권한이 부족합니다.</summary>
    PermissionDenied,

    /// <summary>리소스(메모리, 디스크 등)가 부족합니다.</summary>
    ResourceExhausted,

    /// <summary>트랜잭션이 중단되었습니다.</summary>
    TransactionAborted,

    /// <summary>쿼리 구문 오류가 발생했습니다.</summary>
    QuerySyntax,

    /// <summary>사용자 정의 오류입니다. (RAISERROR / THROW)</summary>
    UserDefined,

    /// <summary>클라우드 환경의 일시적 오류입니다.</summary>
    CloudTransient,

    /// <summary>분류되지 않은 알 수 없는 오류입니다.</summary>
    Unknown
}

#endregion

#region DbError 구조체

/// <summary>
/// DB 작업 오류 정보를 담는 불변 구조체입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>Zero-Allocation</b>: readonly record struct로 힙 할당을 방지합니다.<br/>
/// - <b>풍부한 오류 컨텍스트</b>: SQL 오류 코드, 심각도, 일시적 오류 여부, 힌트 등을 포함합니다.<br/>
/// - <b>패턴 매칭 친화</b>: record struct의 속성 패턴으로 간결한 조건 분기가 가능합니다.
/// </para>
/// </summary>
public readonly record struct DbError
{
    #region 필수 속성

    /// <summary>오류의 종류를 나타냅니다.</summary>
    public DbErrorKind Kind { get; init; }

    /// <summary>SQL Server에서 반환한 오류 번호입니다. (예: 2812 = SP를 찾을 수 없음)</summary>
    public int SqlErrorCode { get; init; }

    /// <summary>SQL Server 오류 심각도(Severity) 수준입니다. (0~25)</summary>
    public byte Severity { get; init; }

    /// <summary>일시적(Transient) 오류 여부입니다. true이면 자동 재시도 대상입니다.</summary>
    public bool IsTransient { get; init; }

    /// <summary>사용자에게 표시할 오류 메시지입니다.</summary>
    public required string Message { get; init; }

    #endregion

    #region 선택 속성

    /// <summary>오류 해결을 위한 힌트 메시지입니다. (예: "연결 문자열을 확인하세요.")</summary>
    public string? Hint { get; init; }

    /// <summary>오류가 발생한 DB 객체 이름입니다. (예: "dbo.usp_GetList")</summary>
    public string? ObjectName { get; init; }

    /// <summary>진단용 예외입니다. Lib.Db가 반환하는 public 실패 결과는 원본 provider 예외를 보관하지 않습니다.</summary>
    public Exception? InnerException { get; init; }

    #endregion
}

#endregion

#region DbResult<T> 구조체

/// <summary>
/// DB 작업의 성공/실패 결과를 나타내는 제네릭 불변 구조체입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>예외 대체</b>: 예외 대신 결과 타입을 사용하여 정상 흐름과 오류 흐름을 구분합니다.<br/>
/// - <b>Zero-Allocation</b>: readonly record struct로 힙 할당을 방지합니다.<br/>
/// - <b>패턴 매칭</b>: Deconstruct 및 속성 패턴으로 간결한 성공/실패 분기가 가능합니다.<br/>
/// - <b>AOT 호환</b>: 제네릭이지만 리플렉션을 사용하지 않아 AOT 컴파일과 완전 호환됩니다.
/// </para>
/// </summary>
/// <typeparam name="T">성공 시 반환되는 값의 타입입니다.</typeparam>
public readonly record struct DbResult<T>
{
    #region 속성 선언

    /// <summary>작업 성공 여부입니다.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>성공 시 반환된 값입니다. 실패 시 default입니다.</summary>
    public T? Value { get; private init; }

    /// <summary>실패 시 오류 정보입니다. 성공 시 null입니다.</summary>
    public DbError? Error { get; private init; }

    /// <summary>영향받은 행 수입니다. (INSERT/UPDATE/DELETE 결과)</summary>
    public int AffectedRows { get; private init; }

    #endregion

    #region 팩토리 메서드

    /// <summary>
    /// 성공 결과를 생성합니다.
    /// </summary>
    /// <param name="value">반환할 값입니다.</param>
    /// <param name="affectedRows">영향받은 행 수입니다. (기본값: 0)</param>
    /// <returns>성공 상태의 <see cref="DbResult{T}"/> 인스턴스입니다.</returns>
    public static DbResult<T> Ok(T value, int affectedRows = 0) => new()
    {
        IsSuccess = true,
        Value = value,
        Error = null,
        AffectedRows = affectedRows
    };

    /// <summary>
    /// 실패 결과를 생성합니다.
    /// </summary>
    /// <param name="error">오류 정보입니다.</param>
    /// <returns>실패 상태의 <see cref="DbResult{T}"/> 인스턴스입니다.</returns>
    public static DbResult<T> Fail(DbError error) => new()
    {
        IsSuccess = false,
        Value = default,
        Error = error,
        AffectedRows = 0
    };

    #endregion

    #region Deconstruct (패턴 매칭 지원)

    /// <summary>
    /// 결과를 분해하여 패턴 매칭에 활용합니다.
    /// </summary>
    /// <param name="success">성공 여부입니다.</param>
    /// <param name="value">반환된 값입니다.</param>
    /// <param name="error">오류 정보입니다.</param>
    public void Deconstruct(out bool success, out T? value, out DbError? error)
    {
        success = IsSuccess;
        value = Value;
        error = Error;
    }

    #endregion
}

#endregion
