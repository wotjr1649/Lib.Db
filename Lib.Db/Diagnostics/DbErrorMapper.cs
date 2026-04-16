// ============================================================================
// 파일: Lib.Db/Diagnostics/DbErrorMapper.cs
// 설명: SqlException 오류 코드를 DbError 구조체로 변환하는 매퍼입니다.
//       SQL Server 오류 번호별 종류(DbErrorKind), 일시적 오류 여부,
//       한국어 메시지 및 힌트를 FrozenDictionary로 O(1) 조회합니다.
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Frozen;
using Lib.Db.Contracts.Core;
using Microsoft.Data.SqlClient;

namespace Lib.Db.Diagnostics;

#region DbErrorMapper 정적 클래스

/// <summary>
/// SQL Server 오류 코드를 <see cref="DbError"/>로 변환하는 정적 매퍼입니다.
/// <para>
/// <b>[설계 의도]</b><br/>
/// - <b>O(1) 조회</b>: <see cref="FrozenDictionary{TKey, TValue}"/>를 사용하여
///   오류 코드별 매핑을 상수 시간에 조회합니다.<br/>
/// - <b>재시도 판단 통합</b>: 각 오류 코드에 일시적(Transient) 여부를 포함하여
///   <see cref="DbError.IsTransient"/>로 자동 재시도 대상을 결정합니다.<br/>
/// - <b>한국어 메시지</b>: 모든 오류에 한국어 메시지와 해결 힌트를 제공합니다.<br/>
/// - <b>확장 가능</b>: 새로운 오류 코드 추가 시 딕셔너리 항목만 추가하면 됩니다.
/// </para>
/// </summary>
internal static class DbErrorMapper
{
    #region 오류 코드 매핑 테이블 (FrozenDictionary)

    /// <summary>
    /// SQL Server 오류 번호 → (오류 종류, 일시적 여부, 메시지, 힌트) 매핑 테이블입니다.
    /// <para>FrozenDictionary는 읽기 전용이므로 스레드 안전하며, 해시 조회가 최적화되어 있습니다.</para>
    /// </summary>
    private static readonly FrozenDictionary<int, (DbErrorKind Kind, bool IsTransient, string Message, string? Hint)> ErrorMap =
        new Dictionary<int, (DbErrorKind Kind, bool IsTransient, string Message, string? Hint)>
        {
            #region SchemaNotFound — 스키마 객체 미발견

            [208] = (DbErrorKind.SchemaNotFound, false,
                "개체 이름이 유효하지 않습니다.",
                "테이블 또는 뷰 이름의 철자와 스키마(dbo 등)를 확인하세요."),
            [2812] = (DbErrorKind.SchemaNotFound, false,
                "저장 프로시저를 찾을 수 없습니다.",
                "프로시저 이름과 스키마를 확인하고, 배포 여부를 점검하세요."),
            [207] = (DbErrorKind.SchemaNotFound, false,
                "열 이름이 유효하지 않습니다.",
                "SELECT/WHERE 절의 열 이름 철자를 확인하세요."),
            [209] = (DbErrorKind.SchemaNotFound, false,
                "열 이름이 모호합니다.",
                "JOIN 시 테이블 별칭을 명시하여 열을 한정하세요."),
            [2727] = (DbErrorKind.SchemaNotFound, false,
                "인덱스를 찾을 수 없습니다.",
                "인덱스 이름과 대상 테이블을 확인하세요."),

            #endregion

            #region AuthenticationFailed — 인증 실패

            [18456] = (DbErrorKind.AuthenticationFailed, false,
                "DB 로그인에 실패했습니다.",
                "사용자 이름과 비밀번호를 확인하고, 계정이 활성 상태인지 점검하세요."),
            [4060] = (DbErrorKind.AuthenticationFailed, false,
                "요청한 데이터베이스를 열 수 없습니다.",
                "연결 문자열의 Initial Catalog(데이터베이스 이름)을 확인하세요."),
            [916] = (DbErrorKind.AuthenticationFailed, false,
                "서버 보안 주체가 현재 보안 컨텍스트에서 데이터베이스에 액세스할 수 없습니다.",
                "사용자에게 해당 데이터베이스에 대한 접근 권한이 부여되었는지 확인하세요."),

            #endregion

            #region ConnectionLost — 연결 끊김

            [53] = (DbErrorKind.ConnectionLost, true,
                "서버를 찾을 수 없거나 액세스할 수 없습니다.",
                "서버 이름/주소가 올바른지, SQL Server 서비스가 실행 중인지 확인하세요."),
            [10053] = (DbErrorKind.ConnectionLost, true,
                "서버와의 연결이 끊어졌습니다. (소프트웨어에 의한 연결 중단)",
                "네트워크 상태를 확인하고, 방화벽 또는 VPN 설정을 점검하세요."),
            [10054] = (DbErrorKind.ConnectionLost, true,
                "원격 호스트에 의해 기존 연결이 강제로 끊어졌습니다.",
                "SQL Server 서비스 상태와 네트워크 연결을 확인하세요."),
            [10060] = (DbErrorKind.ConnectionLost, true,
                "연결 시간이 초과되었습니다. (서버에 연결할 수 없음)",
                "서버 주소, 포트, 방화벽 규칙을 확인하세요."),
            [233] = (DbErrorKind.ConnectionLost, true,
                "서버에 연결하는 동안 전송 수준 오류가 발생했습니다.",
                "SQL Server가 TCP/IP 프로토콜을 수신 중인지 확인하세요."),
            [64] = (DbErrorKind.ConnectionLost, true,
                "네트워크 연결이 끊어졌습니다.",
                "네트워크 케이블, 무선 연결 상태를 확인하세요."),

            #endregion

            #region Timeout — 시간 초과

            [-2] = (DbErrorKind.Timeout, true,
                "쿼리 실행 제한 시간이 초과되었습니다.",
                "CommandTimeout 값을 늘리거나, 쿼리 실행 계획을 점검하세요."),
            [1222] = (DbErrorKind.Timeout, true,
                "잠금 요청 시간이 초과되었습니다.",
                "동시 트랜잭션의 잠금 경합을 확인하고, 인덱스를 점검하세요."),

            #endregion

            #region Deadlock — 교착 상태

            [1205] = (DbErrorKind.Deadlock, true,
                "교착 상태(Deadlock)가 감지되어 트랜잭션이 희생되었습니다.",
                "트랜잭션 순서를 일관되게 하고, 잠금 범위를 최소화하세요."),

            #endregion

            #region ConstraintViolation — 제약 조건 위반

            [547] = (DbErrorKind.ConstraintViolation, false,
                "외래 키(FK) 제약 조건 위반이 발생했습니다.",
                "참조 대상 테이블에 해당 키 값이 존재하는지 확인하세요."),
            [2627] = (DbErrorKind.ConstraintViolation, false,
                "고유 키(UNIQUE) 또는 기본 키(PK) 제약 조건 위반입니다.",
                "중복 값을 삽입하려고 시도했습니다. 데이터 중복 여부를 확인하세요."),
            [2601] = (DbErrorKind.ConstraintViolation, false,
                "고유 인덱스에 중복 키를 삽입할 수 없습니다.",
                "UNIQUE 인덱스 대상 열에 중복 값이 없는지 확인하세요."),
            [515] = (DbErrorKind.ConstraintViolation, false,
                "NOT NULL 제약 조건 위반 — 열에 NULL을 삽입할 수 없습니다.",
                "필수 열에 값을 제공하거나, 기본값(DEFAULT)을 설정하세요."),

            #endregion

            #region DataConversion — 데이터 형식 변환 오류

            [245] = (DbErrorKind.DataConversion, false,
                "데이터 형식 변환에 실패했습니다.",
                "매개변수의 데이터 타입이 열 정의와 일치하는지 확인하세요."),
            [8115] = (DbErrorKind.DataConversion, false,
                "산술 오버플로 오류가 발생했습니다.",
                "값이 대상 열의 허용 범위를 초과합니다. 데이터 타입을 확인하세요."),
            [8152] = (DbErrorKind.DataConversion, false,
                "문자열 데이터가 잘립니다.",
                "입력 문자열 길이가 열의 최대 길이(nvarchar 등)를 초과합니다."),
            [8134] = (DbErrorKind.DataConversion, false,
                "0으로 나누기 오류가 발생했습니다.",
                "나눗셈 연산에서 분모가 0인지 확인하세요. NULLIF를 활용할 수 있습니다."),
            [2628] = (DbErrorKind.DataConversion, false,
                "문자열 또는 이진 데이터가 잘립니다. (상세 오류)",
                "영향받는 열과 입력 값의 길이를 확인하세요."),

            #endregion

            #region ParameterMismatch — 매개변수 불일치

            [201] = (DbErrorKind.ParameterMismatch, false,
                "저장 프로시저에 필수 매개변수가 누락되었습니다.",
                "프로시저 정의를 확인하고, 모든 필수 매개변수를 전달하세요."),
            [8144] = (DbErrorKind.ParameterMismatch, false,
                "저장 프로시저에 매개변수가 너무 많이 지정되었습니다.",
                "프로시저 정의의 매개변수 수와 전달 값을 비교하세요."),

            #endregion

            #region PermissionDenied — 권한 부족

            [229] = (DbErrorKind.PermissionDenied, false,
                "개체에 대한 실행 권한이 거부되었습니다.",
                "사용자에게 EXECUTE/SELECT 등 필요한 권한을 부여하세요."),
            [230] = (DbErrorKind.PermissionDenied, false,
                "열에 대한 SELECT 권한이 거부되었습니다.",
                "열 수준 권한을 확인하고 필요한 권한을 부여하세요."),
            [297] = (DbErrorKind.PermissionDenied, false,
                "원격 서버에 대한 액세스가 거부되었습니다.",
                "Linked Server 설정 및 원격 로그인 매핑을 확인하세요."),

            #endregion

            #region ResourceExhausted — 리소스 부족

            [701] = (DbErrorKind.ResourceExhausted, true,
                "시스템 메모리가 부족하여 쿼리를 실행할 수 없습니다.",
                "서버 메모리 사용량을 확인하고, 대용량 쿼리를 분할 실행하세요."),
            [1105] = (DbErrorKind.ResourceExhausted, true,
                "파일 그룹의 디스크 공간이 부족합니다.",
                "디스크 여유 공간을 확보하거나, 파일 그룹을 확장하세요."),
            [1138] = (DbErrorKind.ResourceExhausted, true,
                "인덱스 엔트리의 최대 길이를 초과했습니다.",
                "인덱스 키 열의 전체 크기가 900/1700바이트를 초과하지 않도록 조정하세요."),
            [8645] = (DbErrorKind.ResourceExhausted, true,
                "메모리 부여(Memory Grant) 대기 시간이 초과되었습니다.",
                "동시 실행 쿼리 수를 줄이거나 RESOURCE GOVERNOR를 점검하세요."),

            #endregion

            #region TransactionAborted — 트랜잭션 중단

            [3930] = (DbErrorKind.TransactionAborted, false,
                "현재 트랜잭션을 커밋할 수 없으며 롤백만 가능합니다.",
                "이전 오류로 인해 트랜잭션이 DOOMED 상태입니다. XACT_STATE()를 확인하세요."),
            [266] = (DbErrorKind.TransactionAborted, false,
                "트랜잭션 카운트 불일치 — BEGIN/COMMIT/ROLLBACK 짝이 맞지 않습니다.",
                "저장 프로시저 내 트랜잭션 제어 흐름을 점검하세요."),
            [3621] = (DbErrorKind.TransactionAborted, false,
                "문이 종료되었습니다. (SET XACT_ABORT ON 환경)",
                "XACT_ABORT가 ON일 때 오류 발생 시 자동 롤백됩니다. 오류 원인을 먼저 해결하세요."),

            #endregion

            #region QuerySyntax — 쿼리 구문 오류

            [102] = (DbErrorKind.QuerySyntax, false,
                "SQL 구문 오류가 발생했습니다.",
                "쿼리 문법을 확인하세요. 예약어와 괄호, 쉼표 위치를 점검하세요."),
            [137] = (DbErrorKind.QuerySyntax, false,
                "스칼라 변수를 선언해야 합니다.",
                "변수 이름의 철자를 확인하거나 DECLARE 문을 추가하세요."),
            [512] = (DbErrorKind.QuerySyntax, false,
                "하위 쿼리가 2개 이상의 값을 반환했습니다.",
                "서브쿼리가 단일 값을 반환하도록 TOP 1 또는 조건을 추가하세요."),
            [530] = (DbErrorKind.QuerySyntax, false,
                "문이 중단되었습니다. (최대 재귀 수 초과 등)",
                "재귀 CTE의 MAXRECURSION 옵션을 확인하세요."),

            #endregion

            #region CloudTransient — 클라우드 일시적 오류

            [40197] = (DbErrorKind.CloudTransient, true,
                "서비스에서 요청을 처리하는 동안 오류가 발생했습니다. (클라우드)",
                "잠시 후 재시도하세요. 지속되면 Azure 서비스 상태를 확인하세요."),
            [40501] = (DbErrorKind.CloudTransient, true,
                "서비스가 현재 사용 중입니다. (Azure SQL 제한)",
                "잠시 후 재시도하세요. 요청 빈도를 줄이는 것을 검토하세요."),
            [40613] = (DbErrorKind.CloudTransient, true,
                "데이터베이스를 현재 사용할 수 없습니다. (Azure SQL 재구성 중)",
                "Azure SQL이 자동 복구 중입니다. 잠시 후 재시도하세요."),
            [40540] = (DbErrorKind.CloudTransient, true,
                "서비스 목표 변경 중입니다. (Azure SQL 스케일링)",
                "스케일 작업 완료 후 재시도하세요."),
            [10928] = (DbErrorKind.CloudTransient, true,
                "리소스 ID 제한에 도달했습니다. (Azure SQL)",
                "동시 세션/요청 수를 줄이거나 서비스 티어를 업그레이드하세요."),
            [10929] = (DbErrorKind.CloudTransient, true,
                "리소스 최소 보장을 위해 요청이 거부되었습니다. (Azure SQL)",
                "서버 부하가 줄어들 때 재시도하세요."),
            [49918] = (DbErrorKind.CloudTransient, true,
                "현재 리소스 제한으로 인해 요청을 처리할 수 없습니다. (Azure SQL 일시적 오류)",
                "잠시 후 재시도하세요. Azure SQL Database의 DTU/vCore 한계에 도달했을 수 있습니다."),

            #endregion
        }.ToFrozenDictionary();

    #endregion

    #region FromSqlErrorCode — 오류 코드 기반 변환

    /// <summary>
    /// SQL Server 오류 번호를 기반으로 <see cref="DbError"/>를 생성합니다.
    /// <para>
    /// <b>[설계 의도]</b><br/>
    /// - FrozenDictionary O(1) 조회로 오류 코드를 즉시 매핑합니다.<br/>
    /// - 50000 이상은 사용자 정의 오류(RAISERROR/THROW)로 분류합니다.<br/>
    /// - 매핑되지 않는 코드는 <see cref="DbErrorKind.Unknown"/>을 반환합니다.
    /// </para>
    /// </summary>
    /// <param name="sqlErrorCode">SQL Server 오류 번호입니다. (-2 = 타임아웃, 50000+ = 사용자 정의)</param>
    /// <param name="objectName">오류가 발생한 DB 객체 이름입니다. (예: "dbo.usp_GetList")</param>
    /// <param name="severity">SQL Server 오류 심각도(Severity) 수준입니다. (0~25, 기본값: 0)</param>
    /// <param name="innerException">원본 예외 객체입니다. (기본값: null)</param>
    /// <returns>매핑된 <see cref="DbError"/> 구조체입니다.</returns>
    public static DbError FromSqlErrorCode(
        int sqlErrorCode,
        string? objectName = null,
        byte severity = 0,
        Exception? innerException = null)
    {
        // 1) FrozenDictionary에서 O(1) 조회
        if (ErrorMap.TryGetValue(sqlErrorCode, out (DbErrorKind Kind, bool IsTransient, string Message, string? Hint) mapping))
        {
            string message = objectName is not null
                ? $"[{objectName}] {mapping.Message} (오류 코드: {sqlErrorCode})"
                : $"{mapping.Message} (오류 코드: {sqlErrorCode})";

            return new DbError
            {
                Kind = mapping.Kind,
                SqlErrorCode = sqlErrorCode,
                Severity = severity,
                IsTransient = mapping.IsTransient,
                Message = message,
                Hint = mapping.Hint,
                ObjectName = objectName,
                InnerException = innerException
            };
        }

        // 2) 50000 이상 — 사용자 정의 오류 (RAISERROR / THROW)
        if (sqlErrorCode >= 50000)
        {
            string userMessage = objectName is not null
                ? $"[{objectName}] 사용자 정의 오류가 발생했습니다. (오류 코드: {sqlErrorCode})"
                : $"사용자 정의 오류가 발생했습니다. (오류 코드: {sqlErrorCode})";

            return new DbError
            {
                Kind = DbErrorKind.UserDefined,
                SqlErrorCode = sqlErrorCode,
                Severity = severity,
                IsTransient = false,
                Message = userMessage,
                Hint = "RAISERROR 또는 THROW로 발생한 사용자 정의 오류입니다. 프로시저 로직을 확인하세요.",
                ObjectName = objectName,
                InnerException = innerException
            };
        }

        // 3) 매핑 없음 — Unknown
        string unknownMessage = objectName is not null
            ? $"[{objectName}] 분류되지 않은 SQL Server 오류가 발생했습니다. (오류 코드: {sqlErrorCode})"
            : $"분류되지 않은 SQL Server 오류가 발생했습니다. (오류 코드: {sqlErrorCode})";

        return new DbError
        {
            Kind = DbErrorKind.Unknown,
            SqlErrorCode = sqlErrorCode,
            Severity = severity,
            IsTransient = false,
            Message = unknownMessage,
            Hint = null,
            ObjectName = objectName,
            InnerException = innerException
        };
    }

    #endregion

    #region FromSqlException — SqlException 편의 변환

    /// <summary>
    /// <see cref="SqlException"/>을 <see cref="DbError"/>로 변환하는 편의 메서드입니다.
    /// <para>
    /// <b>[설계 의도]</b><br/>
    /// - SqlException의 첫 번째 오류(Errors[0])를 기준으로 매핑합니다.<br/>
    /// - Number, Class(Severity), Procedure 속성을 자동으로 추출합니다.
    /// </para>
    /// </summary>
    /// <param name="sqlException">변환할 <see cref="SqlException"/> 인스턴스입니다.</param>
    /// <param name="objectName">
    /// DB 객체 이름입니다. null이면 <see cref="SqlException"/>의 Procedure 속성을 사용합니다.
    /// </param>
    /// <returns>매핑된 <see cref="DbError"/> 구조체입니다.</returns>
    public static DbError FromSqlException(SqlException sqlException, string? objectName = null)
    {
        string? resolvedObjectName = objectName ?? sqlException.Procedure;
        byte severity = sqlException.Class;

        return FromSqlErrorCode(
            sqlException.Number,
            resolvedObjectName,
            severity,
            sqlException);
    }

    #endregion
}

#endregion
