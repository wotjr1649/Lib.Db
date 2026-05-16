# Lib.Db v2 설계 사양서

> 작성일: 2026-04-05
> 대상: .NET 10 / C# 14 / SQL Server 2025
> 상태: 브레인스토밍 완료 → 구현 계획 전환 대기

---

## 1. 목적

Lib.Db v1은 고성능 SQL Server 데이터 액세스 라이브러리로 구축 완료되었으나, 구조 분석 결과 다음 문제가 확인되었다:

- 13개 실행 경로 파편화 (Fluent + Bulk + Pipeline + Resumable)
- throw 기반 에러 처리 (호출자가 실패 원인을 구조적으로 알 수 없음)
- 과설계 (CacheLeaderElection, 1024 Mutex, Temp+MERGE Bulk)
- var 665회, 리플렉션 26곳, sealed 비율 55%
- 단일 DB 연결 제한

v2는 **Fluent API 단일 진입점**, **DbResult<T> 구조화된 에러**, **완전 비동기 + C# 14 전면 적용**으로 이를 해결한다.

---

## 2. 설계 원칙

1. **Fluent API Only**: 외부 프로젝트는 오직 Fluent API로만 DB를 사용한다. 내부 실행기/전략/연결 팩토리를 노출하지 않는다.
2. **DbResult<T> 단일 통일**: 모든 실행 메서드가 `DbResult<T>`를 반환한다. throw 없이 호출자가 성공/실패/원인을 구조적으로 확인한다.
3. **완전 비동기**: 동기 메서드 0개. 모든 public 메서드는 async + CancellationToken 필수.
4. **C# 14 전면 적용**: Primary Constructor, field 키워드, Collection Expression, Lock 타입, Pattern Matching, sealed 기본, var 금지.
5. **AOT 완전 호환**: 리플렉션 0곳, Source Generator 전면, IL 경고 0건.
6. **한글 XML 주석 필수**: 모든 public/internal 메서드에 한국어 XML doc comment 작성.

---

## 3. 기능 범위

### 3.1 제거 (12개)

| 기능 | 근거 |
|---|---|
| BulkInsertAsync | 하드코딩 SQL, SP 미사용 |
| BulkUpdateAsync | Temp+MERGE 데드락 위험, SP 미사용 |
| BulkDeleteAsync | Temp+JOIN DELETE, SP 미사용 |
| BulkInsertPipelineAsync | Bulk의 Channel 래퍼, SP 미사용 |
| BulkUpdatePipelineAsync | 위와 동일 |
| BulkDeletePipelineAsync | 위와 동일 |
| QueryResumableAsync | Redis 외부 의존, ETL 전용, 95% 불필요 |
| CacheLeaderElection | 라이브러리가 프로세스 토폴로지를 결정하면 안 됨 |
| Chaos Engineering | 프로덕션 라이브러리에 불필요 |
| byte[]→Stream 변환 (ExecuteScalar) | BLOB 전용 API가 아닌 Scalar에 부적절 |
| SHA256 레거시 마이그레이션 | v2 정리 |
| IResumableStateStore | Resumable과 함께 제거 |

### 3.2 유지 + 개선 (코어)

| 기능 | 개선 내용 |
|---|---|
| Fluent API 5개 실행 메서드 | DbResult<T> 통일, 반환 타입 변경 |
| SchemaService + NegativeCache | 유지 (핵심 가치) |
| SharedMemoryCache | Mutex 버그 수정, 1024→128 스트라이프, opt-in |
| Resilient/Transactional 전략 | internal 은닉 |
| TvpGen Source Generator | FNV-1a 중복 제거, TypeMapping 통합, .NET 10 최적화 |
| OpenTelemetry + Polly | 유지 |

---

## 4. Fluent API 설계

### 4.1 Public 인터페이스 (외부 노출 전체)

```csharp
public interface IDbSession : IAsyncDisposable
{
    /// <summary>등록된 DB 인스턴스로 작업 시작</summary>
    IProcedureStage Use(string instanceName);

    /// <summary>Ad-hoc 연결 문자열로 작업 시작</summary>
    IProcedureStage UseConnectionString(string connectionString);

    /// <summary>기본 인스턴스("Default")로 작업 시작</summary>
    IProcedureStage Default { get; }

    /// <summary>인스턴스별 독립 트랜잭션 시작</summary>
    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName, CancellationToken ct = default);
}

public interface IProcedureStage
{
    /// <summary>저장 프로시저 지정</summary>
    IParameterStage Procedure(string spName);

    /// <summary>Raw SQL 텍스트 지정</summary>
    IParameterStage Sql(string sqlText);

    /// <summary>보간 SQL (자동 파라미터화, Zero-Alloc)</summary>
    IParameterStage Sql(FormattableString sql);
}

public interface IParameterStage : IExecutionStage<object>
{
    /// <summary>파라미터 객체 설정</summary>
    IExecutionStage<TParams> With<TParams>(TParams parameters);

    /// <summary>타임아웃 오버라이드 (초)</summary>
    IParameterStage WithTimeout(int timeoutSeconds);
}

public interface IExecutionStage<in TParams>
{
    /// <summary>스트림 조회</summary>
    Task<DbResult<IAsyncEnumerable<TResult>>> QueryAsync<TResult>(
        CancellationToken ct = default);

    /// <summary>단건 조회</summary>
    Task<DbResult<TResult?>> QuerySingleAsync<TResult>(
        CancellationToken ct = default);

    /// <summary>스칼라 조회 (1행 1열)</summary>
    Task<DbResult<TScalar?>> ExecuteScalarAsync<TScalar>(
        CancellationToken ct = default);

    /// <summary>다중 결과셋 조회</summary>
    Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(
        CancellationToken ct = default);

    /// <summary>명령 실행 (NonQuery)</summary>
    Task<DbResult<int>> ExecuteAsync(CancellationToken ct = default);
}
```

### 4.2 Internal 은닉

| 현재 public | v2 | 이유 |
|---|---|---|
| IDbExecutor | internal | Fluent 뒤에서만 동작 |
| IDbExecutionStrategy | internal | Resilient/Transactional 내부 전환 |
| IDbConnectionFactory | internal | 연결 관리 내부 |
| IDbContext | 제거 | IDbSession으로 통합 (중복) |

### 4.3 v1 → v2 변경 요약

```
v1: IDbContext + IDbSession (중복) + 13개 실행 경로 + 4개 Sql 오버로드
v2: IDbSession (유일) + 5개 실행 메서드 + 3개 Sql 오버로드 + DbResult 통일
```

---

## 5. 구조화된 에러 전달

### 5.1 DbResult<T>

```csharp
public readonly record struct DbResult<T>
{
    /// <summary>실행 성공 여부</summary>
    public bool IsSuccess { get; init; }

    /// <summary>성공 시 결과 값</summary>
    public T? Value { get; init; }

    /// <summary>실패 시 에러 정보</summary>
    public DbError? Error { get; init; }

    /// <summary>영향 받은 행 수 (ExecuteAsync 전용)</summary>
    public int AffectedRows { get; init; }

    /// <summary>Pattern Matching 지원</summary>
    public void Deconstruct(out bool success, out T? value, out DbError? error)
        => (success, value, error) = (IsSuccess, Value, Error);

    public static DbResult<T> Ok(T value, int affectedRows = 0)
        => new() { IsSuccess = true, Value = value, AffectedRows = affectedRows };

    public static DbResult<T> Fail(DbError error)
        => new() { IsSuccess = false, Error = error };
}
```

### 5.2 DbError (2계층 분류)

```csharp
public readonly record struct DbError
{
    /// <summary>에러 대분류</summary>
    public DbErrorKind Kind { get; init; }

    /// <summary>원본 SQL Server 에러코드</summary>
    public int SqlErrorCode { get; init; }

    /// <summary>SQL Server Severity (1-25)</summary>
    public byte Severity { get; init; }

    /// <summary>자동 재시도 가능한 일시적 에러인지</summary>
    public bool IsTransient { get; init; }

    /// <summary>한국어 에러 메시지</summary>
    public string Message { get; init; }

    /// <summary>해결 힌트</summary>
    public string? Hint { get; init; }

    /// <summary>문제 발생 객체명</summary>
    public string? ObjectName { get; init; }

    /// <summary>원본 예외 (디버깅용)</summary>
    public Exception? InnerException { get; init; }
}
```

### 5.3 DbErrorKind (15개 분류)

```csharp
public enum DbErrorKind
{
    None,
    SchemaNotFound,        // 208, 2812, 207, 209, 2727
    AuthenticationFailed,  // 18456, 4060, 916
    ConnectionLost,        // 10053, 10054, 10060, 233, 64
    Timeout,               // -2, 1222
    Deadlock,              // 1205
    ConstraintViolation,   // 547, 2627, 2601, 515
    DataConversion,        // 245, 8115, 8152, 8134
    ParameterMismatch,     // 201, 8144
    PermissionDenied,      // 229, 230, 297
    ResourceExhausted,     // 701, 1105, 1138, 8645
    TransactionAborted,    // 3930, 266, 3621
    QuerySyntax,           // 102, 137, 512, 530
    UserDefined,           // 50000+ (SP RAISERROR/THROW)
    CloudTransient,        // 40197, 40501, 40613
    Unknown
}
```

### 5.4 SqlException 매핑

| SqlErrorCode | Kind | Severity | IsTransient |
|---|---|---|---|
| 208, 2812, 207, 209 | SchemaNotFound | 16 | false |
| 18456, 4060, 916 | AuthenticationFailed | 14 | false |
| 10053, 10054, 10060, 233, 64 | ConnectionLost | 20 | true |
| -2, 1222 | Timeout | 16 | true |
| 1205 | Deadlock | 13 | true |
| 547, 2627, 2601, 515 | ConstraintViolation | 14-16 | false |
| 245, 8115, 8152, 8134 | DataConversion | 16 | false |
| 201, 8144 | ParameterMismatch | 16 | false |
| 229, 230, 297 | PermissionDenied | 14 | false |
| 701, 1105, 1138, 8645 | ResourceExhausted | 17-19 | true |
| 3930, 266 | TransactionAborted | 16 | context |
| 102, 137, 512 | QuerySyntax | 15-16 | false |
| 50000+ | UserDefined | varies | false (기본) |
| 40197, 40501, 40613 | CloudTransient | 16-17 | true |
| 기타 | Unknown | varies | false |

### 5.5 호출자 사용 패턴

```csharp
DbResult<User?> result = await session.Default
    .Procedure("usp_GetUser")
    .With(new { UserId = 123 })
    .QuerySingleAsync<User>();

if (result is { IsSuccess: true, Value: { } user })
{
    // 성공
}
else if (result.Error is { Kind: DbErrorKind.ConstraintViolation, SqlErrorCode: 2627 })
{
    // 중복 키 — 세부 분기
}
else if (result.Error is { Kind: DbErrorKind.UserDefined, SqlErrorCode: 50001 })
{
    // SP 커스텀 에러
}
```

---

## 6. 멀티 DB 연결

### 6.1 인스턴스별 독립 관리

```csharp
internal sealed class DbInstanceState
{
    public string InstanceName { get; init; }
    public string ConnectionHash { get; init; }
    public SqlConnection? Connection { get; set; }
    public SqlTransaction? Transaction { get; set; }
    public IDbExecutionStrategy Strategy { get; set; }
}
```

DbSession 내부에 `ConcurrentDictionary<string, DbInstanceState>`로 인스턴스별 상태를 격리 관리한다.

### 6.2 사용 패턴

```csharp
// 병렬 다중 DB 쿼리
Task<DbResult<IAsyncEnumerable<Order>>> t1 = session.Use("OrderDB")
    .Procedure("usp_GetOrders").With(param).QueryAsync<Order>();
Task<DbResult<IAsyncEnumerable<Stock>>> t2 = session.Use("StockDB")
    .Procedure("usp_GetStock").With(param).QueryAsync<Stock>();
await Task.WhenAll(t1, t2);

// 독립 트랜잭션
await using IDbTransactionScope tx1 = await session.BeginTransactionAsync("OrderDB");
await using IDbTransactionScope tx2 = await session.BeginTransactionAsync("StockDB");
// tx1, tx2 완전 독립 — 하나 롤백해도 다른 하나에 영향 없음
```

### 6.3 IDbTransactionScope (트랜잭션 Fluent)

```csharp
/// <summary>
/// 트랜잭션 스코프 — Fluent API와 동일한 실행 메서드를 제공하되,
/// 모든 명령이 동일 트랜잭션 내에서 실행된다.
/// </summary>
public interface IDbTransactionScope : IProcedureStage, IAsyncDisposable
{
    /// <summary>트랜잭션 커밋</summary>
    Task<DbResult<bool>> CommitAsync(CancellationToken ct = default);

    /// <summary>트랜잭션 롤백</summary>
    Task<DbResult<bool>> RollbackAsync(CancellationToken ct = default);
}
// Dispose 시 미커밋 상태면 자동 롤백
```

### 6.4 트랜잭션 정책

- 인스턴스별 독립 트랜잭션만 지원
- 분산 트랜잭션(TransactionScope/MSDTC) 미지원
- 동일 인스턴스에 중복 BeginTransaction 시 에러

### 6.5 Sql(FormattableString) 파라미터 추출

v2에서 `Sql(FormattableString)`은 `IParameterStage`를 반환한다. 내부적으로 `SqlInterpolatedStringHandler`가 보간 인수를 `@p0, @p1, ...` 파라미터로 자동 추출하여 `IParameterStage` 상태에 저장한다. 호출자가 `.With()` 없이 바로 실행 메서드를 호출할 수 있다 (`IParameterStage`가 `IExecutionStage<object>`를 상속하므로).

---

## 7. 트랜잭션 동작 명세 (모호성 해소)

### 7.1 두 가지 실행 모드

Lib.Db v2는 두 가지 모드에서 쿼리를 실행한다:

| 모드 | 진입 방법 | 트랜잭션 | CommitAsync/RollbackAsync |
|---|---|---|---|
| **Auto-commit** | `session.Default.Procedure(...).ExecuteAsync()` | 각 명령이 즉시 자동 커밋 | 불필요 — 호출 불가 (IDbTransactionScope 없음) |
| **Explicit Transaction** | `await session.BeginTransactionAsync("DB1")` → `tx.Procedure(...).ExecuteAsync()` | 명시적 커밋/롤백 필요 | 필수 — CommitAsync로 확정, 미커밋 시 Dispose에서 자동 롤백 |

### 7.2 Auto-commit 모드 (기본)

```csharp
// BeginTransactionAsync를 호출하지 않으면 auto-commit
DbResult<int> result = await session.Default
    .Procedure("usp_InsertOrder")
    .With(orderDto)
    .ExecuteAsync();
// → 성공 시 즉시 커밋됨. 롤백 불가.
// → 실패 시 해당 명령만 롤백됨 (다른 명령에 영향 없음).
```

- SQL Server의 기본 트랜잭션 모드는 autocommit (IMPLICIT_TRANSACTIONS = OFF)
- Microsoft.Data.SqlClient는 이 기본값을 변경하지 않음
- 각 SqlCommand 실행이 개별 트랜잭션으로 감싸짐

### 7.3 Explicit Transaction 모드

```csharp
await using IDbTransactionScope tx = await session.BeginTransactionAsync("OrderDB");

// SP 실행 — 아직 커밋 안 됨 (@@TRANCOUNT = 1)
DbResult<int> r1 = await tx.Procedure("usp_InsertOrder").With(order).ExecuteAsync();
DbResult<int> r2 = await tx.Procedure("usp_DeductStock").With(stock).ExecuteAsync();

if (r1.IsSuccess && r2.IsSuccess)
{
    await tx.CommitAsync();   // 여기서 최종 커밋 — 두 SP 모두 확정
}
else
{
    await tx.RollbackAsync(); // 두 SP 모두 취소
}
// 또는 CommitAsync 호출 없이 Dispose → 자동 롤백
```

### 7.4 SP 내부 트랜잭션과의 중첩 동작

SP 내부에 `BEGIN TRAN / COMMIT` 가 있는 경우:

| 상황 | @@TRANCOUNT | 실제 동작 |
|---|---|---|
| C# BeginTransaction 호출 | 1 | 외부 트랜잭션 시작 |
| SP 내부 BEGIN TRAN | 2 | 중첩 (@@TRANCOUNT 증가만) |
| SP 내부 COMMIT | 1 | @@TRANCOUNT 감소만, **실제 커밋 안 됨** |
| C# CommitAsync 호출 | 0 | **여기서 실제 커밋** |
| C# RollbackAsync 호출 | 0 | SP가 COMMIT 했어도 **전체 롤백** |

**SP 내부에서 ROLLBACK한 경우:**
- @@TRANCOUNT = 0이 됨 (전체 롤백)
- C#의 CommitAsync 호출 시 에러 발생
- Lib.Db는 이를 `DbErrorKind.TransactionAborted`로 매핑

### 7.5 Lib.Db v2의 트랜잭션 설계 결정

1. **Auto-commit이 기본**: `session.Default.Procedure(...)` 사용 시 트랜잭션 관리 없음. 가장 단순.
2. **Explicit은 opt-in**: `BeginTransactionAsync()` 호출 시에만 트랜잭션 모드 진입.
3. **IDbTransactionScope가 Fluent API를 겸함**: 트랜잭션 스코프 내에서 바로 `.Procedure().ExecuteAsync()` 가능.
4. **미커밋 시 자동 롤백**: `await using`으로 안전한 리소스 해제.
5. **SP 내부 ROLLBACK 감지**: @@TRANCOUNT = 0 상태를 감지하여 `DbResult.Error`로 전달.

### 7.6 SQL Server Express 트랜잭션 지원 확인

| 기능 | Express 지원 |
|---|---|
| BEGIN TRANSACTION / COMMIT / ROLLBACK | 완전 지원 |
| SAVE TRANSACTION (Savepoint) | 완전 지원 |
| 중첩 트랜잭션 (@@TRANCOUNT) | 완전 지원 |
| SET XACT_ABORT ON | 완전 지원 |
| IMPLICIT_TRANSACTIONS | 완전 지원 |

Express의 제한은 CPU(4코어), 메모리(1,410MB), DB크기(10GB)뿐이며, 트랜잭션 엔진은 Enterprise와 동일하다.

출처: https://learn.microsoft.com/en-us/sql/sql-server/editions-and-components-of-sql-server-2022

---

## 8. 완전 비동기 + .NET 10 / C# 14 최신 문법

### 7.1 비동기 원칙

- 동기 메서드 0개
- ConfigureAwait(false) 전면 적용
- ValueTask 우선 (캐시 히트 경로)
- CancellationToken 모든 public 메서드 필수
- async void 전면 금지
- IAsyncDisposable (DbSession, IDbTransactionScope, IMultipleResultReader)

### 7.2 C# 14 전면 적용

| 항목 | 규칙 |
|---|---|
| Primary Constructor | 모든 DI 의존성 클래스 |
| field 키워드 | 유효성 검사 속성 |
| Collection Expression | 모든 배열/리스트 초기화 |
| file-scoped namespace | 100% |
| Lock 타입 | 모든 동기화 (object lock 금지) |
| Pattern Matching | switch expression, is pattern |
| sealed 기본 | 상속 의도 없는 모든 클래스 |
| var 금지 | 전체 코드베이스 |
| FrozenDictionary | 불변 조회 테이블 |

### 7.3 AOT 완전 호환

- 리플렉션 0곳 (v1: 26곳 → Source Gen 전면 대체)
- Activator.CreateInstance 0곳 (v1: 2곳 → 정적 팩토리)
- MakeGenericType 0곳 (v1: 3곳 → 제네릭 제약/Source Gen)
- IL 경고 억제 0건

### 7.4 성능 기술

- Span<T> / stackalloc — 할당 절감형 경로
- ArrayPool<T> — GC 압박 제거
- InterpolatedStringHandler (ref struct) — 보간 SQL 파라미터화 지원
- [SkipLocalsInit] — 로컬 초기화 비용 제거
- TieredPGO — 런타임 최적화
- Source Generator 전면 — 리플렉션 0

### 7.5 한글 주석

- 모든 public/internal 메서드: 한국어 XML doc comment 필수
- 파일 헤더: 파일명, 한국어 설명, 대상 프레임워크
- 설계 의도: `<para><b>[설계 의도]</b></para>`로 상세 기술
- #region: 한국어 섹션명

---

## 9. 캐싱 시스템 단순화

| 작업 | 상세 |
|---|---|
| CacheLeaderElection 삭제 | 앱 레이어 책임으로 이동 |
| Mutex 레이스 수정 | _disposed 플래그 + GetMutex 검증 |
| AbandonedMutexException 처리 | SharedMemoryCache Get/Set에 추가 |
| Mutex 스트라이프 1024→128 | 메모리 87.5% 절감 |
| MMF opt-in, IMemoryCache 기본 | 단순 시작, 필요 시 확장 |
| CacheMaintenanceService | Leader 의존 제거, 독립 실행 |
| SHA256 레거시 경로 제거 | v2 정리 |

---

## 10. TvpGen 최적화

| 작업 | 상세 |
|---|---|
| FNV-1a 해시 공유 추출 | TvpAccessor + ResultAccessor → SharedHashUtils.cs |
| DbFirstTvpGenerator TypeMapping 통합 | 인라인 MapSqlTypeToCSharp 제거 |
| MiniJsonParser → System.Text.Json | regex 파서 교체 |
| string.GetHashCode → FNV-1a 통일 | 비결정적 해시 제거 |
| Dictionary → FrozenDictionary | .NET 10 최적화 |
| SanitizeIdentifier 중복 통합 | 공유 유틸리티 |
| CancellationToken 추가 | DbFirstTvpGenerator.Execute() |

---

## 11. 코드 포맷 일관성

| 작업 | 수량 |
|---|---|
| var → 명시적 타입 | 665곳 / 41파일 |
| 비-sealed → sealed | 48곳 |
| object lock → Lock 타입 | 전체 |
| Mappers.cs 리플렉션 → Source Gen | 25곳 |
| Activator.CreateInstance 제거 | 2곳 |
| MakeGenericType 축소 | 3곳 |
| 빌드 경고 해소 | 68건 → 0건 |

---

## 12. 검증 기준

| # | 항목 | 기준 |
|---|---|---|
| V1 | Fluent Only 강제 | internal 직접 접근 시 컴파일 에러 |
| V2 | DbResult 동작 | 에러 시나리오 전체 통과 |
| V3 | SqlException 매핑 | 에러코드별 DbError 정확 매핑 |
| V4 | 빌드 경고 | 0건 |
| V5 | var 사용 | 0건 |
| V6 | 리플렉션 | 0곳 |
| V7 | CRAP Score | 핵심 5파일 < 30 |
| V8 | 멀티 DB | 동시 2개 연결 성공 |
| V9 | Mutex 안정 | ObjectDisposedException 없음 |
| V10 | NuGet Pack | .nupkg + .snupkg 생성 |
| V11 | AOT 경고 | IL 경고 0건 |
| V12 | 테스트 | 전체 통과 |
