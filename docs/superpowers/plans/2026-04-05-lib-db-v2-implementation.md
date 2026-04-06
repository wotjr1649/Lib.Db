# Lib.Db v2 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lib.Db v1의 13개 실행 경로를 Fluent API 단일 진입점 + DbResult<T> 구조화 에러로 전면 리팩토링

**Architecture:** v2는 IDbSession을 유일한 public 진입점으로 사용한다. 모든 실행 메서드는 DbResult<T>를 반환하여 throw 없이 에러를 전달한다. 내부 실행기/전략/연결 팩토리는 internal로 은닉한다. 인스턴스별 독립 연결/트랜잭션을 ConcurrentDictionary로 관리한다.

**Tech Stack:** .NET 10 / C# 14 / Microsoft.Data.SqlClient / Polly v8 / xUnit / Source Generator

**Spec:** `docs/superpowers/specs/2026-04-05-lib-db-v2-design.md`

---

## Phase A: 기반 타입 (DbResult, DbError, DbErrorKind)

### Task 1: DbResult<T>, DbError, DbErrorKind 생성

**Files:**
- Create: `Lib.Db/Contracts/Core/DbResult.cs`
- Test: `Tests/Lib.Db.TestSuite/Unit/DbResultTests.cs`

- [ ] **Step 1: 테스트 파일 작성 — DbResult 성공/실패/Deconstruct/Pattern Matching**

```csharp
// Tests/Lib.Db.TestSuite/Unit/DbResultTests.cs
namespace Lib.Db.Verification.Tests.Unit;

public sealed class DbResultTests
{
    [Fact]
    public void Ok_ShouldSetIsSuccessTrue()
    {
        DbResult<int> result = DbResult<int>.Ok(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_ShouldSetIsSuccessFalse()
    {
        DbError error = new()
        {
            Kind = DbErrorKind.SchemaNotFound,
            SqlErrorCode = 2812,
            Message = "SP를 찾을 수 없습니다."
        };
        DbResult<int> result = DbResult<int>.Fail(error);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(DbErrorKind.SchemaNotFound, result.Error.Value.Kind);
    }

    [Fact]
    public void Deconstruct_ShouldSupportPatternMatching()
    {
        DbResult<string> result = DbResult<string>.Ok("hello");
        (bool success, string? value, DbError? error) = result;
        Assert.True(success);
        Assert.Equal("hello", value);
        Assert.Null(error);
    }

    [Fact]
    public void PatternMatching_ShouldWorkWithIsExpression()
    {
        DbResult<int> result = DbResult<int>.Ok(99, affectedRows: 5);
        Assert.True(result is { IsSuccess: true, Value: 99, AffectedRows: 5 });
    }
}
```

- [ ] **Step 2: 테스트 실행 — 컴파일 에러 확인**

Run: `dotnet test Lib.Db/Lib.Db.slnx --filter "FullyQualifiedName~DbResultTests" --no-restore`
Expected: FAIL — `DbResult<T>` 타입 없음

- [ ] **Step 3: DbResult.cs 구현**

```csharp
// Lib.Db/Contracts/Core/DbResult.cs
// ============================================================================
// 파일: Lib.Db/Contracts/Core/DbResult.cs
// 설명: 구조화된 DB 실행 결과 + 에러 분류 체계
// 대상: .NET 10 / C# 14
// ============================================================================

namespace Lib.Db.Contracts.Core;

#region DB 실행 결과

/// <summary>
/// DB 실행 결과를 구조적으로 전달하는 불변 레코드.
/// <para><b>[설계 의도]</b> throw 대신 호출자에게 성공/실패/원인을 명시적으로 전달한다.</para>
/// </summary>
public readonly record struct DbResult<T>
{
    /// <summary>실행 성공 여부</summary>
    public bool IsSuccess { get; init; }

    /// <summary>성공 시 결과 값 (실패 시 default)</summary>
    public T? Value { get; init; }

    /// <summary>실패 시 에러 정보 (성공 시 null)</summary>
    public DbError? Error { get; init; }

    /// <summary>영향 받은 행 수 (ExecuteAsync 전용, 나머지 0)</summary>
    public int AffectedRows { get; init; }

    /// <summary>Pattern Matching Deconstruct 지원</summary>
    public void Deconstruct(out bool success, out T? value, out DbError? error)
        => (success, value, error) = (IsSuccess, Value, Error);

    /// <summary>성공 결과 생성</summary>
    public static DbResult<T> Ok(T value, int affectedRows = 0)
        => new() { IsSuccess = true, Value = value, AffectedRows = affectedRows };

    /// <summary>실패 결과 생성</summary>
    public static DbResult<T> Fail(DbError error)
        => new() { IsSuccess = false, Error = error };
}

#endregion

#region DB 에러 상세

/// <summary>
/// DB 에러 상세 정보 — 2계층 분류 (Kind + SqlErrorCode)
/// <para><b>[설계 의도]</b> 호출자가 Kind로 대분류 분기, SqlErrorCode로 세부 분기 가능.</para>
/// </summary>
public readonly record struct DbError
{
    /// <summary>에러 대분류</summary>
    public DbErrorKind Kind { get; init; }

    /// <summary>원본 SQL Server 에러코드 (예: 2812, 547, 50001)</summary>
    public int SqlErrorCode { get; init; }

    /// <summary>SQL Server Severity (1-25)</summary>
    public byte Severity { get; init; }

    /// <summary>자동 재시도 가능한 일시적 에러인지</summary>
    public bool IsTransient { get; init; }

    /// <summary>한국어 에러 메시지</summary>
    public string Message { get; init; }

    /// <summary>해결 힌트 (유사 SP 이름, 연결 문자열 확인 안내 등)</summary>
    public string? Hint { get; init; }

    /// <summary>문제 발생 객체명 (SP명, 테이블명, 컬럼명)</summary>
    public string? ObjectName { get; init; }

    /// <summary>원본 예외 (디버깅용)</summary>
    public Exception? InnerException { get; init; }
}

#endregion

#region DB 에러 분류 (15개)

/// <summary>
/// DB 에러 대분류 — switch expression으로 분기 처리
/// <para><b>[설계 의도]</b> SQL Server 에러코드를 의미 있는 비즈니스 카테고리로 매핑.</para>
/// </summary>
public enum DbErrorKind
{
    /// <summary>에러 없음</summary>
    None,
    /// <summary>테이블/SP/컬럼/인덱스 미존재 (208, 2812, 207, 209, 2727)</summary>
    SchemaNotFound,
    /// <summary>로그인/인증 실패 (18456, 4060, 916)</summary>
    AuthenticationFailed,
    /// <summary>네트워크/연결 단절 (10053, 10054, 10060, 233, 64)</summary>
    ConnectionLost,
    /// <summary>쿼리 타임아웃 (-2, 1222)</summary>
    Timeout,
    /// <summary>데드락 감지 (1205)</summary>
    Deadlock,
    /// <summary>FK/UK/PK/Check 제약조건 위반 (547, 2627, 2601, 515)</summary>
    ConstraintViolation,
    /// <summary>타입 변환/오버플로우/절삭 (245, 8115, 8152, 8134)</summary>
    DataConversion,
    /// <summary>SP 매개변수 불일치 (201, 8144)</summary>
    ParameterMismatch,
    /// <summary>권한 거부 (229, 230, 297)</summary>
    PermissionDenied,
    /// <summary>메모리/tempdb/로그 부족 (701, 1105, 1138, 8645)</summary>
    ResourceExhausted,
    /// <summary>트랜잭션 중단/롤백 (3930, 266, 3621)</summary>
    TransactionAborted,
    /// <summary>구문 오류/서브쿼리/재귀 (102, 137, 512, 530)</summary>
    QuerySyntax,
    /// <summary>SP RAISERROR/THROW 사용자 정의 에러 (50000+)</summary>
    UserDefined,
    /// <summary>Azure SQL 일시적 오류 (40197, 40501, 40613)</summary>
    CloudTransient,
    /// <summary>분류 불가 에러</summary>
    Unknown
}

#endregion
```

- [ ] **Step 4: 테스트 실행 — 전체 통과 확인**

Run: `dotnet test Lib.Db/Lib.Db.slnx --filter "FullyQualifiedName~DbResultTests" --no-restore`
Expected: 4 PASS

- [ ] **Step 5: 커밋**

```bash
git add Lib.Db/Lib.Db/Contracts/Core/DbResult.cs Tests/Lib.Db.TestSuite/Unit/DbResultTests.cs
git commit -m "feat: add DbResult<T>, DbError, DbErrorKind foundation types"
```

---

### Task 2: DbErrorMapper — SqlException → DbError 변환기

**Files:**
- Create: `Lib.Db/Diagnostics/DbErrorMapper.cs`
- Test: `Tests/Lib.Db.TestSuite/Unit/DbErrorMapperTests.cs`

- [ ] **Step 1: 테스트 작성 — 에러코드별 매핑 검증**

```csharp
// Tests/Lib.Db.TestSuite/Unit/DbErrorMapperTests.cs
namespace Lib.Db.Verification.Tests.Unit;

public sealed class DbErrorMapperTests
{
    [Theory]
    [InlineData(2812, DbErrorKind.SchemaNotFound, false)]
    [InlineData(208, DbErrorKind.SchemaNotFound, false)]
    [InlineData(1205, DbErrorKind.Deadlock, true)]
    [InlineData(18456, DbErrorKind.AuthenticationFailed, false)]
    [InlineData(10054, DbErrorKind.ConnectionLost, true)]
    [InlineData(2627, DbErrorKind.ConstraintViolation, false)]
    [InlineData(8115, DbErrorKind.DataConversion, false)]
    [InlineData(229, DbErrorKind.PermissionDenied, false)]
    [InlineData(701, DbErrorKind.ResourceExhausted, true)]
    [InlineData(102, DbErrorKind.QuerySyntax, false)]
    [InlineData(40501, DbErrorKind.CloudTransient, true)]
    public void FromSqlErrorCode_ShouldMapCorrectly(
        int errorCode, DbErrorKind expectedKind, bool expectedTransient)
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(errorCode, "test_object");
        Assert.Equal(expectedKind, error.Kind);
        Assert.Equal(expectedTransient, error.IsTransient);
        Assert.Equal(errorCode, error.SqlErrorCode);
        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact]
    public void FromSqlErrorCode_UserDefined_ShouldMapAbove50000()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(50001, "usp_Test");
        Assert.Equal(DbErrorKind.UserDefined, error.Kind);
        Assert.False(error.IsTransient);
    }

    [Fact]
    public void FromSqlErrorCode_Unknown_ShouldReturnUnknown()
    {
        DbError error = DbErrorMapper.FromSqlErrorCode(99999);
        Assert.Equal(DbErrorKind.Unknown, error.Kind);
    }
}
```

- [ ] **Step 2: 테스트 실행 — 컴파일 에러 확인**

Run: `dotnet test Lib.Db/Lib.Db.slnx --filter "FullyQualifiedName~DbErrorMapperTests"`
Expected: FAIL — `DbErrorMapper` 없음

- [ ] **Step 3: DbErrorMapper 구현**

```csharp
// Lib.Db/Diagnostics/DbErrorMapper.cs
// ============================================================================
// 파일: Lib.Db/Diagnostics/DbErrorMapper.cs
// 설명: SqlException/에러코드를 DbError로 변환하는 내부 매퍼
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Collections.Frozen;
using Lib.Db.Contracts.Core;

namespace Lib.Db.Diagnostics;

/// <summary>
/// SQL Server 에러코드를 DbError로 변환하는 정적 매퍼.
/// <para><b>[설계 의도]</b> FrozenDictionary로 O(1) 조회. 에러코드별 한국어 메시지와 힌트 제공.</para>
/// </summary>
internal static class DbErrorMapper
{
    #region 에러코드 매핑 테이블

    private static readonly FrozenDictionary<int, (DbErrorKind Kind, bool IsTransient, string Message, string? Hint)> s_mappings =
        new Dictionary<int, (DbErrorKind, bool, string, string?)>
        {
            // SchemaNotFound
            [208]  = (DbErrorKind.SchemaNotFound, false, "테이블이 존재하지 않습니다.", "테이블명과 스키마(dbo 등)를 확인하세요."),
            [2812] = (DbErrorKind.SchemaNotFound, false, "저장 프로시저를 찾을 수 없습니다.", "SP 이름과 스키마를 확인하세요."),
            [207]  = (DbErrorKind.SchemaNotFound, false, "잘못된 컬럼명입니다.", "컬럼명을 확인하세요."),
            [209]  = (DbErrorKind.SchemaNotFound, false, "모호한 컬럼명입니다.", "테이블 별칭을 지정하세요."),
            [2727] = (DbErrorKind.SchemaNotFound, false, "인덱스를 찾을 수 없습니다.", null),
            // AuthenticationFailed
            [18456] = (DbErrorKind.AuthenticationFailed, false, "로그인이 실패했습니다.", "User Id와 Password를 확인하세요."),
            [4060]  = (DbErrorKind.AuthenticationFailed, false, "데이터베이스를 열 수 없습니다.", "연결 문자열의 Database 항목을 확인하세요."),
            [916]   = (DbErrorKind.AuthenticationFailed, false, "현재 보안 컨텍스트로 DB에 접근할 수 없습니다.", null),
            // ConnectionLost
            [10053] = (DbErrorKind.ConnectionLost, true, "네트워크 연결이 클라이언트에 의해 중단되었습니다.", "네트워크 상태를 확인하세요."),
            [10054] = (DbErrorKind.ConnectionLost, true, "원격 호스트에 의해 연결이 강제 종료되었습니다.", null),
            [10060] = (DbErrorKind.ConnectionLost, true, "네트워크 연결 타임아웃입니다.", "서버 주소와 포트를 확인하세요."),
            [233]   = (DbErrorKind.ConnectionLost, true, "연결 초기화에 실패했습니다.", null),
            [64]    = (DbErrorKind.ConnectionLost, true, "통신 링크 오류입니다.", null),
            // Timeout
            [-2]   = (DbErrorKind.Timeout, true, "쿼리 타임아웃이 발생했습니다.", ".WithTimeout()으로 시간을 늘리세요."),
            [1222] = (DbErrorKind.Timeout, true, "잠금 요청 대기 시간이 초과했습니다.", null),
            // Deadlock
            [1205] = (DbErrorKind.Deadlock, true, "데드락이 감지되었습니다.", "Polly 재시도 정책이 자동 적용됩니다."),
            // ConstraintViolation
            [547]  = (DbErrorKind.ConstraintViolation, false, "제약조건 위반입니다.", "FK/Check 제약조건을 확인하세요."),
            [2627] = (DbErrorKind.ConstraintViolation, false, "중복 키 — 이미 존재하는 데이터입니다.", "PK/UK 값을 확인하세요."),
            [2601] = (DbErrorKind.ConstraintViolation, false, "유니크 인덱스에 중복 행을 삽입할 수 없습니다.", null),
            [515]  = (DbErrorKind.ConstraintViolation, false, "NOT NULL 컬럼에 NULL 값이 전달되었습니다.", null),
            // DataConversion
            [245]  = (DbErrorKind.DataConversion, false, "데이터 타입 변환에 실패했습니다.", null),
            [8115] = (DbErrorKind.DataConversion, false, "산술 오버플로우가 발생했습니다.", null),
            [8152] = (DbErrorKind.DataConversion, false, "문자열 또는 이진 데이터가 잘립니다.", "컬럼 길이를 확인하세요."),
            [8134] = (DbErrorKind.DataConversion, false, "0으로 나누기 오류입니다.", null),
            [2628] = (DbErrorKind.DataConversion, false, "문자열 데이터가 잘립니다.", "컬럼 길이를 확인하세요."),
            // ParameterMismatch
            [201]  = (DbErrorKind.ParameterMismatch, false, "필수 매개변수가 누락되었습니다.", null),
            [8144] = (DbErrorKind.ParameterMismatch, false, "프로시저에 너무 많은 인수가 전달되었습니다.", null),
            // PermissionDenied
            [229] = (DbErrorKind.PermissionDenied, false, "개체에 대한 권한이 거부되었습니다.", null),
            [230] = (DbErrorKind.PermissionDenied, false, "컬럼에 대한 권한이 거부되었습니다.", null),
            [297] = (DbErrorKind.PermissionDenied, false, "이 작업을 수행할 권한이 없습니다.", null),
            // ResourceExhausted
            [701]  = (DbErrorKind.ResourceExhausted, true, "시스템 메모리가 부족합니다.", null),
            [1105] = (DbErrorKind.ResourceExhausted, true, "트랜잭션 로그가 가득 찼습니다.", null),
            [1138] = (DbErrorKind.ResourceExhausted, true, "tempdb 크기 제한에 도달했습니다.", null),
            [8645] = (DbErrorKind.ResourceExhausted, true, "메모리 리소스 대기 중 타임아웃이 발생했습니다.", null),
            // TransactionAborted
            [3930] = (DbErrorKind.TransactionAborted, false, "현재 트랜잭션을 커밋할 수 없습니다.", "트랜잭션을 롤백하세요."),
            [266]  = (DbErrorKind.TransactionAborted, false, "BEGIN/COMMIT 문 수가 일치하지 않습니다.", "SP 내부 트랜잭션 구조를 확인하세요."),
            [3621] = (DbErrorKind.TransactionAborted, false, "문이 종료되었습니다.", null),
            // QuerySyntax
            [102] = (DbErrorKind.QuerySyntax, false, "구문 오류입니다.", null),
            [137] = (DbErrorKind.QuerySyntax, false, "스칼라 변수를 선언해야 합니다.", null),
            [512] = (DbErrorKind.QuerySyntax, false, "서브쿼리가 2개 이상의 값을 반환했습니다.", null),
            [530] = (DbErrorKind.QuerySyntax, false, "최대 재귀 한도에 도달했습니다.", null),
            // CloudTransient
            [40197] = (DbErrorKind.CloudTransient, true, "Azure SQL 서비스 오류입니다.", "잠시 후 다시 시도하세요."),
            [40501] = (DbErrorKind.CloudTransient, true, "Azure SQL 서비스가 사용 중입니다.", "10초 후 다시 시도하세요."),
            [40613] = (DbErrorKind.CloudTransient, true, "Azure SQL 데이터베이스를 사용할 수 없습니다.", null),
            [40540] = (DbErrorKind.CloudTransient, true, "로그인 시도 횟수 초과로 연결이 종료되었습니다.", "연결 풀링을 사용하세요."),
            [10928] = (DbErrorKind.CloudTransient, true, "Azure SQL 리소스 한도에 도달했습니다.", null),
            [10929] = (DbErrorKind.CloudTransient, true, "탄력적 풀 리소스 한도에 도달했습니다.", null),
        }.ToFrozenDictionary();

    #endregion

    #region 공개 API

    /// <summary>
    /// SQL Server 에러코드를 DbError로 변환한다.
    /// </summary>
    /// <param name="sqlErrorCode">SQL Server 에러코드</param>
    /// <param name="objectName">문제 객체명 (SP명, 테이블명 등)</param>
    /// <param name="severity">SQL Server Severity</param>
    /// <param name="innerException">원본 예외</param>
    public static DbError FromSqlErrorCode(
        int sqlErrorCode,
        string? objectName = null,
        byte severity = 0,
        Exception? innerException = null)
    {
        // 사용자 정의 에러 (50000+)
        if (sqlErrorCode >= 50000)
        {
            return new DbError
            {
                Kind = DbErrorKind.UserDefined,
                SqlErrorCode = sqlErrorCode,
                Severity = severity,
                IsTransient = false,
                Message = innerException?.Message ?? $"사용자 정의 에러 ({sqlErrorCode})",
                ObjectName = objectName,
                InnerException = innerException
            };
        }

        // 매핑 테이블 조회
        if (s_mappings.TryGetValue(sqlErrorCode, out (DbErrorKind Kind, bool IsTransient, string Message, string? Hint) mapping))
        {
            string message = objectName is not null
                ? $"{mapping.Message} (객체: {objectName})"
                : mapping.Message;

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

        // 분류 불가
        return new DbError
        {
            Kind = DbErrorKind.Unknown,
            SqlErrorCode = sqlErrorCode,
            Severity = severity,
            IsTransient = false,
            Message = innerException?.Message ?? $"알 수 없는 DB 에러 ({sqlErrorCode})",
            ObjectName = objectName,
            InnerException = innerException
        };
    }

    /// <summary>
    /// SqlException을 DbError로 변환한다.
    /// </summary>
    public static DbError FromSqlException(
        Microsoft.Data.SqlClient.SqlException ex,
        string? objectName = null)
    {
        return FromSqlErrorCode(
            ex.Number,
            objectName,
            (byte)ex.Class,
            ex);
    }

    #endregion
}
```

- [ ] **Step 4: 테스트 실행 — 전체 통과**

Run: `dotnet test Lib.Db/Lib.Db.slnx --filter "FullyQualifiedName~DbErrorMapperTests"`
Expected: 13 PASS

- [ ] **Step 5: 커밋**

```bash
git add Lib.Db/Lib.Db/Diagnostics/DbErrorMapper.cs Tests/Lib.Db.TestSuite/Unit/DbErrorMapperTests.cs
git commit -m "feat: add DbErrorMapper with FrozenDictionary SQL error mapping"
```

---

## Phase B: 기능 제거 (Bulk/Pipeline/Resumable/Chaos/LeaderElection)

### Task 3: IProcedureStage에서 Bulk/Pipeline/Resumable 제거

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbStageContracts.cs`

- [ ] **Step 1: DbStageContracts.cs에서 제거할 메서드 식별**

제거 대상 (IProcedureStage에서):
- `BulkInsertAsync<T>`, `BulkUpdateAsync<T>`, `BulkDeleteAsync<T>`
- `BulkInsertPipelineAsync<T>`, `BulkUpdatePipelineAsync<T>`, `BulkDeletePipelineAsync<T>`
- `QueryResumableAsync<TCursor, TResult>`
- 관련 `#region 대량 처리`, `#region 파이프라인 처리`, `#region 복구형 조회`

- [ ] **Step 2: IProcedureStage에서 7개 메서드 + 3개 region 삭제**

`Lib.Db/Contracts/Entry/DbStageContracts.cs`에서 line 76~194 (Bulk/Pipeline/Resumable region 전체) 삭제.
`System.Threading.Channels` using도 제거.

- [ ] **Step 3: 빌드 확인 — 컴파일 에러 목록 확보**

Run: `dotnet build Lib.Db/Lib.Db.slnx 2>&1 | grep "error CS"`
Expected: DbRequestBuilder.cs, SqlDbExecutor.cs 등에서 구현 참조 에러

- [ ] **Step 4: 커밋 (컴파일 에러 상태 — Phase B 완료 시 해소)**

```bash
git add Lib.Db/Lib.Db/Contracts/Entry/DbStageContracts.cs
git commit -m "refactor: remove Bulk/Pipeline/Resumable from IProcedureStage contract"
```

---

### Task 4: SqlDbExecutor에서 Bulk/Pipeline/Resumable 구현 제거

**Files:**
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`

- [ ] **Step 1: 제거 대상 메서드 식별 (SqlDbExecutor.cs)**

제거:
- `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync` (public)
- `BulkInsertInternalAsync`, `BulkUpdateOptimizedAsync`, `BulkDeleteOptimizedAsync` (private)
- `BulkInsertPipelineAsync`, `BulkUpdatePipelineAsync`, `BulkDeletePipelineAsync` (public)
- `BulkPipelineInternalAsync`, `ProcessChannelBatchAsync`, `DrainChannelAsync` (private)
- `QueryResumableAsync`, `QueryResumableInternalAsync` (public/private)
- `FlushBulkAsync`, `FlushBulkToConnectionAsync` (private)
- `BuildMergeSql`, `AdaptiveBatchSizer` struct (private)
- `s_bulkCounter` static field
- 관련 로깅 메서드: `LogDryRunBulk`, `LogDryRunResumable`

- [ ] **Step 2: 위 메서드/타입/필드 전부 삭제**

- [ ] **Step 3: 빌드 — 에러 감소 확인**

Run: `dotnet build Lib.Db/Lib.Db.slnx 2>&1 | grep "error CS" | wc -l`

- [ ] **Step 4: 커밋**

```bash
git add Lib.Db/Lib.Db/Execution/Executors/SqlDbExecutor.cs
git commit -m "refactor: remove Bulk/Pipeline/Resumable from SqlDbExecutor"
```

---

### Task 5: DbRequestBuilder에서 Bulk/Pipeline/Resumable 제거

**Files:**
- Modify: `Lib.Db/Fluent/DbRequestBuilder.cs`

- [ ] **Step 1: DbRequestBuilder에서 IProcedureStage 구현 중 제거 대상 메서드 삭제**

Bulk 3종 + Pipeline 3종 + Resumable 위임 메서드 전부 삭제.

- [ ] **Step 2: 커밋**

```bash
git add Lib.Db/Lib.Db/Fluent/DbRequestBuilder.cs
git commit -m "refactor: remove Bulk/Pipeline/Resumable from DbRequestBuilder"
```

---

### Task 6: CacheLeaderElection + Chaos + IResumableStateStore 제거

**Files:**
- Delete: `Lib.Db/Caching/CacheLeaderElection.cs`
- Delete: `Lib.Db/Contracts/Cache/ICacheLeaderElection.cs`
- Delete: `Lib.Db/Infrastructure/ChaosEngineering.cs`
- Modify: `Lib.Db/Extensions/ServiceRegistrationHelpers.cs` (DI 등록 제거)
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs` (IChaosInjector 참조 제거)
- Modify: `Lib.Db/Caching/CacheMaintenanceService.cs` (Leader 의존 제거)

- [ ] **Step 1: 파일 삭제**

```bash
rm Lib.Db/Lib.Db/Caching/CacheLeaderElection.cs
rm Lib.Db/Lib.Db/Contracts/Cache/ICacheLeaderElection.cs
rm Lib.Db/Lib.Db/Infrastructure/ChaosEngineering.cs
```

- [ ] **Step 2: ServiceRegistrationHelpers.cs에서 ICacheLeaderElection, IChaosInjector, IResumableStateStore DI 등록 제거**

- [ ] **Step 3: SqlDbExecutor 생성자에서 IChaosInjector 매개변수 + 호출 제거**

`_chaosInjector.InjectAsync()` 호출부 모두 삭제.

- [ ] **Step 4: CacheMaintenanceService에서 ICacheLeaderElection 의존 제거 — 독립 실행으로 변경**

Leader 검사 로직 제거, 타이머 기반 독립 실행으로 단순화.

- [ ] **Step 5: IResumableStateStore 인터페이스 + 구현 찾아 제거**

- [ ] **Step 6: 빌드 성공 확인**

Run: `dotnet build Lib.Db/Lib.Db.slnx`
Expected: 0 errors

- [ ] **Step 7: 기존 테스트 실행 — 깨진 테스트 확인 및 수정**

Run: `dotnet test Lib.Db/Lib.Db.slnx`
Chaos/Resumable/Bulk 관련 테스트는 삭제하거나 주석 처리.

- [ ] **Step 8: 커밋**

```bash
git add -A
git commit -m "refactor: remove CacheLeaderElection, ChaosEngineering, IResumableStateStore"
```

---

## Phase C: Fluent API 재구성 + DbResult 통합

### Task 7: IExecutionStage 반환 타입을 DbResult<T>로 변경

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbStageContracts.cs`

- [ ] **Step 1: IExecutionStage<TParams>의 5개 메서드 시그니처 변경**

```csharp
public interface IExecutionStage<in TParams>
{
    Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(CancellationToken ct = default);
    Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct = default);
    Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(CancellationToken ct = default);
    Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct = default);
    Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Sql(string, params ReadOnlySpan) 오버로드 제거 (3개 → params 제거)**

IProcedureStage에서 `Sql(string sqlFormat, params ReadOnlySpan<object?> args)` 삭제.

- [ ] **Step 3: Sql(FormattableString) 반환 타입을 IParameterStage로 변경**

```csharp
// 변경 전: IExecutionStage<Dictionary<string, object?>> Sql(FormattableString sql);
// 변경 후:
IParameterStage Sql(FormattableString sql);
```

- [ ] **Step 4: 커밋**

```bash
git add Lib.Db/Lib.Db/Contracts/Entry/DbStageContracts.cs
git commit -m "refactor: change IExecutionStage return types to DbResult<T>"
```

---

### Task 8: IDbContext 제거 + IDbSession 통합

**Files:**
- Modify: `Lib.Db/Contracts/Entry/DbEntryContracts.cs`
- Modify: `Lib.Db/Core/DbSession.cs`

- [ ] **Step 1: IDbContext 인터페이스 삭제, IDbSession에 Use/UseConnectionString/Default 통합**

IDbSession이 유일한 진입점:
```csharp
public interface IDbSession : IAsyncDisposable
{
    IProcedureStage Use(string instanceName);
    IProcedureStage UseConnectionString(string connectionString);
    IProcedureStage Default { get; }
    Task<IDbTransactionScope> BeginTransactionAsync(string instanceName, CancellationToken ct = default);
}
```

- [ ] **Step 2: IDbTransactionScope 추가**

```csharp
public interface IDbTransactionScope : IProcedureStage, IAsyncDisposable
{
    Task<DbResult<bool>> CommitAsync(CancellationToken ct = default);
    Task<DbResult<bool>> RollbackAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: DbSession.cs에서 IDbContext 구현 제거, IDbSession만 구현**

- [ ] **Step 4: IDbExecutor, IDbExecutionStrategy, IDbConnectionFactory를 internal로 변경**

- [ ] **Step 5: 빌드 + 에러 수정**

Run: `dotnet build Lib.Db/Lib.Db.slnx`

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "refactor: merge IDbContext into IDbSession, add IDbTransactionScope"
```

---

### Task 9: DbRequestBuilder → DbResult 반환 구현

**Files:**
- Modify: `Lib.Db/Fluent/DbRequestBuilder.cs`
- Modify: `Lib.Db/Execution/Executors/SqlDbExecutor.cs`

- [ ] **Step 1: DbRequestBuilder의 ExecutionStage 내부에서 try-catch → DbResult 변환**

모든 실행 메서드에서:
```csharp
public async Task<DbResult<TResult?>> QuerySingleAsync<TResult>(CancellationToken ct)
{
    try
    {
        TResult? value = await _executor.QuerySingleAsync<TParams, TResult>(...).ConfigureAwait(false);
        return DbResult<TResult?>.Ok(value);
    }
    catch (SqlException ex)
    {
        DbError error = DbErrorMapper.FromSqlException(ex, _commandText);
        return DbResult<TResult?>.Fail(error);
    }
    catch (Exception ex)
    {
        DbError error = new()
        {
            Kind = DbErrorKind.Unknown,
            Message = ex.Message,
            InnerException = ex
        };
        return DbResult<TResult?>.Fail(error);
    }
}
```

- [ ] **Step 2: 5개 실행 메서드 전부 위 패턴 적용**

QueryAsync, QuerySingleAsync, ExecuteScalarAsync, QueryMultipleAsync, ExecuteAsync

- [ ] **Step 3: 빌드 성공 확인**

- [ ] **Step 4: 커밋**

```bash
git add -A
git commit -m "feat: implement DbResult<T> return in all execution methods"
```

---

## Phase D: 멀티 DB 연결 + 트랜잭션

### Task 10: DbSession 멀티 인스턴스 재구현

**Files:**
- Modify: `Lib.Db/Core/DbSession.cs`
- Create: `Lib.Db/Core/DbInstanceState.cs`
- Create: `Lib.Db/Core/DbTransactionScope.cs`

- [ ] **Step 1: DbInstanceState 내부 타입 생성**

```csharp
// Lib.Db/Core/DbInstanceState.cs
internal sealed class DbInstanceState
{
    public required string InstanceName { get; init; }
    public required string ConnectionHash { get; init; }
    public SqlConnection? Connection { get; set; }
    public SqlTransaction? Transaction { get; set; }
    public required IDbExecutionStrategy Strategy { get; set; }
}
```

- [ ] **Step 2: DbSession에 ConcurrentDictionary<string, DbInstanceState> 적용**

`_connection` 단일 필드 → `_instances` Dict로 교체.

- [ ] **Step 3: DbTransactionScope 구현**

`IDbTransactionScope : IProcedureStage, IAsyncDisposable` 구현.
내부적으로 `DbInstanceState`의 Transaction 참조. Dispose 시 미커밋이면 자동 롤백.

- [ ] **Step 4: 테스트 작성 — 멀티 인스턴스 + 독립 트랜잭션**

- [ ] **Step 5: 빌드 + 테스트**

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "feat: implement multi-DB instance management with independent transactions"
```

---

## Phase E: 캐싱 단순화

### Task 11: SharedMemoryCache Mutex 버그 수정 + 스트라이프 축소

**Files:**
- Modify: `Lib.Db/Caching/SharedMemoryCache.cs`
- Modify: `Lib.Db/Caching/CacheCoordination.cs`

- [ ] **Step 1: _disposed 플래그 + GetMutex 검증 추가**

```csharp
private volatile bool _disposed;

private Mutex GetMutex(string key)
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    return _mutexStripes.Value[GetStripeIndex(key)];
}
```

- [ ] **Step 2: AbandonedMutexException try-catch 추가 (Get/Set)**

- [ ] **Step 3: MUTEX_STRIPE_COUNT 1024→128 변경**

- [ ] **Step 4: CacheCoordination.cs SHA256 레거시 경로 제거**

- [ ] **Step 5: 테스트 + 커밋**

```bash
git add -A
git commit -m "fix: SharedMemoryCache mutex race condition + reduce stripes to 128"
```

---

## Phase F: TvpGen 최적화

### Task 12: FNV-1a 공유 추출 + TypeMapping 통합

**Files:**
- Create: `Lib.Db.TvpGen/SharedHashUtils.cs`
- Modify: `Lib.Db.TvpGen/TvpAccessorGenerator.cs`
- Modify: `Lib.Db.TvpGen/ResultAccessorGenerator.cs`
- Modify: `Lib.Db.TvpGen/DbFirstTvpGenerator.cs`

- [ ] **Step 1: SharedHashUtils.cs 생성 — FNV-1a + SanitizeIdentifier 공유**

- [ ] **Step 2: TvpAccessor + ResultAccessor에서 중복 코드 → SharedHashUtils 호출로 교체**

- [ ] **Step 3: DbFirstTvpGenerator의 인라인 MapSqlTypeToCSharp → TypeMappingRegistry 호출로 교체**

- [ ] **Step 4: string.GetHashCode → FNV-1a 통일 (TvpAccessor:684)**

- [ ] **Step 5: 빌드 + 테스트**

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "refactor: extract SharedHashUtils, unify TypeMappingRegistry in TvpGen"
```

---

## Phase G: 코드 포맷 일관성

### Task 13: var 제거 + sealed + Lock 타입

**Files:**
- Modify: 전체 41 파일 (var 665곳)
- Modify: 48곳 클래스 (sealed 추가)

- [ ] **Step 1: dotnet format 실행 (.editorconfig 규칙 적용)**

```bash
dotnet format Lib.Db/Lib.Db.slnx --diagnostics IDE0008
```

- [ ] **Step 2: 나머지 수동 var → 명시적 타입 교체 (dotnet format이 못 잡는 것)**

- [ ] **Step 3: 비-sealed 클래스에 sealed 추가**

Grep으로 식별 후 일괄 적용.

- [ ] **Step 4: object lock → Lock 타입 교체 (HealthCheck.cs 등)**

- [ ] **Step 5: 빌드 경고 0건 확인**

```bash
dotnet build Lib.Db/Lib.Db.slnx 2>&1 | grep "warning" | wc -l
```
Expected: 0

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "style: remove all var usage, add sealed, use Lock type"
```

---

## Phase H: 테스트 갱신 + 한글 주석 + 최종 검증

### Task 14: 테스트 갱신 — DbResult 패턴 + 에러 시나리오

**Files:**
- Modify: `Tests/Lib.Db.TestSuite/` 전체

- [ ] **Step 1: 기존 테스트를 DbResult 패턴으로 마이그레이션**

기존 `Assert.NotNull(result)` → `Assert.True(result.IsSuccess)`

- [ ] **Step 2: 에러 시나리오 테스트 추가 (없는 SP, 없는 테이블)**

- [ ] **Step 3: 멀티 DB 연결 테스트 추가**

- [ ] **Step 4: Fluent Only 계약 테스트 — internal 접근 시 컴파일 에러 확인**

- [ ] **Step 5: 전체 테스트 실행**

```bash
dotnet test Lib.Db/Lib.Db.slnx
```
Expected: 전체 PASS

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "test: update all tests for DbResult<T> pattern + add error scenarios"
```

---

### Task 15: 한글 XML 주석 전면 적용 + 최종 검증

**Files:**
- Modify: 모든 public/internal 메서드

- [ ] **Step 1: 모든 public 인터페이스 메서드에 한글 XML doc 확인/추가**

- [ ] **Step 2: 파일 헤더 확인/추가 (모든 .cs 파일)**

- [ ] **Step 3: 최종 검증 체크리스트 실행**

```bash
# V1: Fluent Only
dotnet build Lib.Db/Lib.Db.slnx  # internal 접근 시 에러 없음

# V4: 빌드 경고 0건
dotnet build Lib.Db/Lib.Db.slnx 2>&1 | grep "경고" | wc -l  # 0

# V5: var 0건
grep -rn "\bvar\b" Lib.Db/Lib.Db/**/*.cs | wc -l  # 0

# V10: NuGet Pack
dotnet pack Lib.Db/Lib.Db.slnx --no-build  # .nupkg + .snupkg

# V12: 테스트 전체 통과
dotnet test Lib.Db/Lib.Db.slnx  # ALL PASS
```

- [ ] **Step 4: 최종 커밋**

```bash
git add -A
git commit -m "docs: add Korean XML comments to all public APIs + final verification"
```

---

## 실행 순서 요약

```
Phase A: 기반 타입 (Task 1-2)
  ↓
Phase B: 기능 제거 (Task 3-6)
  ↓
Phase C: Fluent API + DbResult (Task 7-9)
  ↓
Phase D: 멀티 DB (Task 10)
  ↓
Phase E: 캐싱 (Task 11)
  ↓
Phase F: TvpGen (Task 12)
  ↓
Phase G: 코드 포맷 (Task 13)
  ↓
Phase H: 테스트 + 주석 + 검증 (Task 14-15)
```

총 **15 Tasks**, 각 Task 2-5분 단위 Steps.
