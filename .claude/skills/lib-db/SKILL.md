---
name: lib-db
description: |
  Lib.Db v2.1 SQL Server 전용 고성능 데이터 액세스 라이브러리.
  Fluent API, DbResult 패턴, TVP, BulkInsert, 트랜잭션, 인터셉터.
  이 스킬로 AI Agent가 Lib.Db 코드를 즉시 작성할 수 있습니다.
allowed-tools:
  - Bash
  - Read
  - Edit
  - Write
  - Grep
  - Glob
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/appsettings*.json"
---

# Lib.Db v2.1 — AI Agent 코드 작성 스킬

> SQL Server 전용 고성능 데이터 액세스 라이브러리.
> Fluent API + DbResult<T> 패턴 + TVP Source Generator + BulkInsert.

---

## 1. 라이브러리 개요

| 항목 | 값 |
|---|---|
| 패키지 | `Lib.Db` (NuGet), 대상: .NET 10 / C# 14 |
| DB | SQL Server 2025 Express (localhost:1433, sa) |
| 진입점 | `IDbSession` (DI Scoped) — 유일한 외부 진입점 |
| 결과 | `DbResult<T>` (readonly record struct, Zero-Allocation) |
| TVP | `[TvpRow]` + Source Generator (`Lib.Db.TvpGen`) |
| Bulk | `BulkInsertAsync<T>` (Reflection 기반, 비-AOT) |
| 트랜잭션 | `IDbTransactionScope` (자동 롤백, 격리수준 지정) |
| 인터셉터 | `IDbInterceptor` (DI 등록, 체인 실행) |
| 캐싱 | `WithCacheAsync` / `WithHybridCacheAsync` 확장 메서드 |
| 텔레메트리 | `LibDbTelemetry` (OpenTelemetry ActivitySource + Meter) |
| Resilience | Polly (CircuitBreaker + Retry + Timeout) |

---

## 2. NuGet + DI 설정

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost,1433;Database=MyDb;User Id=sa;Password=xxx;TrustServerCertificate=True;",
    "Secondary": "Server=otherhost;Database=OtherDb;..."
  },
  "LibDb": {
    "ConnectionStringNames": ["Default", "Secondary"],
    "EnableSchemaCaching": true,
    "PrewarmSchemas": ["dbo"],
    "DefaultCommandTimeoutSeconds": 30
  }
}
```

### DI 등록 (Program.cs)

```csharp
// 방법 1: IConfiguration 바인딩 (권장)
builder.Services.AddLibDb(builder.Configuration);

// 방법 2: 수동 설정
builder.Services.AddHighPerformanceDb(options =>
{
    options.ConnectionStrings["Default"] = "Server=localhost,1433;...";
    options.ConnectionStringNames = ["Default"];
    options.EnableSchemaCaching = true;
    options.PrewarmSchemas = ["dbo"];
});

// 인터셉터 등록 (선택, 등록 순서대로 체인 실행)
builder.Services.AddLibDbInterceptor<AuditInterceptor>();
```

---

## 3. Fluent API 호출 체인

```
IDbSession                          <- DI 주입 (Scoped)
 |-- .Default                       <- 기본 인스턴스
 |-- .Use("인스턴스명")              <- 이름 지정 인스턴스
 +-- .UseConnectionString("...")    <- Ad-hoc 연결
      |
      v
  IProcedureStage                   <- 1단계: 명령 선택
   |-- .Procedure("dbo.usp_Name")   <- 저장 프로시저
   |-- .Sql("SELECT ...")           <- Raw SQL
   +-- .Sql($"SELECT ... {val}")   <- 보간 SQL (자동 파라미터화)
        |
        v
    IParameterStage                 <- 2단계: 파라미터/옵션
     |-- .With(new { Id = 1 })      <- 파라미터 바인딩
     +-- .WithTimeout(60)           <- 타임아웃(초)
          |
          v
      IExecutionStage<TParams>      <- 3단계: 실행 (모두 Task<DbResult<T>> 반환)
       |-- .QueryAsync<T>()         <- IAsyncEnumerable<T> 스트림
       |-- .QuerySingleAsync<T>()   <- 단건 조회
       |-- .ExecuteScalarAsync<T>() <- 스칼라 (1행1열)
       |-- .ExecuteAsync()          <- NonQuery (영향 행 수)
       +-- .QueryMultipleAsync()    <- 다중 ResultSet
```

---

## 4. 실행 메서드 선택 가이드

| 메서드 | 반환 타입 | 용도 |
|---|---|---|
| `QueryAsync<T>()` | `DbResult<IAsyncEnumerable<T>>` | 다건 스트림 (목록, 대량 읽기) |
| `QuerySingleAsync<T>()` | `DbResult<T?>` | 단건 (사용자 정보, 설정값) |
| `ExecuteScalarAsync<T>()` | `DbResult<T?>` | 1행 1열 (COUNT, SUM, IDENTITY) |
| `ExecuteAsync()` | `DbResult<int>` | INSERT/UPDATE/DELETE (영향 행 수) |
| `QueryMultipleAsync()` | `DbResult<IMultipleResultReader>` | 다중 ResultSet (SP 여러 SELECT) |

---

## 5. DbResult<T> / DbError 구조

### DbResult<T> (readonly record struct)

```csharp
public readonly record struct DbResult<T>
{
    public bool IsSuccess { get; }       // 성공 여부
    public T? Value { get; }             // 성공 값 (실패 시 default)
    public DbError? Error { get; }       // 실패 정보 (성공 시 null)
    public int AffectedRows { get; }     // 영향 행 수 (INSERT/UPDATE/DELETE)

    // 팩토리
    public static DbResult<T> Ok(T value, int affectedRows = 0);
    public static DbResult<T> Fail(DbError error);

    // Deconstruct (패턴 매칭)
    public void Deconstruct(out bool success, out T? value, out DbError? error);
}
```

### DbError (readonly record struct)

```csharp
public readonly record struct DbError
{
    public DbErrorKind Kind { get; init; }    // 오류 종류
    public int SqlErrorCode { get; init; }    // SQL Server 에러 번호
    public byte Severity { get; init; }       // 심각도 (0-25)
    public bool IsTransient { get; init; }    // 일시적 오류 (재시도 가능)
    public required string Message { get; init; }  // 오류 메시지
    public string? Hint { get; init; }        // 해결 힌트
    public string? ObjectName { get; init; }  // 오류 발생 DB 객체
    public Exception? InnerException { get; init; } // 원본 예외
}
```

### IMultipleResultReader (다중 ResultSet)

```csharp
public interface IMultipleResultReader : IAsyncDisposable
{
    Task<List<T>> ReadAsync<T>(CancellationToken ct = default);       // 현재 ResultSet 전체
    Task<T?> ReadSingleAsync<T>(CancellationToken ct = default);      // 현재 ResultSet 단건
}
// 사용 후 반드시 await using 또는 DisposeAsync 호출
```

---

### TResult 지원 타입 매트릭스

| TResult 타입 | Query/QuerySingle | Scalar | 비고 |
|---|---|---|---|
| `record` / `class` DTO | O | - | Dapper 스타일 매핑 |
| `Dictionary<string, object?>` | O | - | 동적 컬럼 |
| `int`, `long`, `string` 등 | - | O | 스칼라 전용 |
| `(T1, T2)` ValueTuple | O | - | 다중 컬럼 -> 튜플 |

---

## 6. DbResult<T> 에러 처리 4가지 패턴

### 패턴 1: IsSuccess 분기

```csharp
DbResult<UserDto?> result = await db.Default
    .Procedure("dbo.usp_GetUser").With(new { UserId = 123 }).QuerySingleAsync<UserDto>();
if (result.IsSuccess) { UserDto? user = result.Value; }
else { DbError error = result.Error!.Value; logger.LogError("{Kind}: {Msg}", error.Kind, error.Message); }
```

### 패턴 2: Deconstruct

```csharp
(bool success, UserDto? user, DbError? error) = await db.Default
    .Procedure("dbo.usp_GetUser").With(new { UserId = 123 }).QuerySingleAsync<UserDto>();
```

### 패턴 3: switch expression (DbErrorKind)

```csharp
string message = result.Error!.Value.Kind switch
{
    DbErrorKind.ConstraintViolation => "중복 데이터",
    DbErrorKind.Timeout => "시간 초과",
    DbErrorKind.Deadlock => "교착 상태",
    _ => result.Error.Value.Message
};
```

### 패턴 4: IsTransient 기반 재시도

```csharp
if (!result.IsSuccess && result.Error!.Value.IsTransient) { /* 재시도 가능 */ }
```

---

## 7. DbErrorKind 16종 대응표

| DbErrorKind | Transient | SQL# | 대응 |
|---|---|---|---|
| `SchemaNotFound` | N | 2812 | SP/테이블명 확인 |
| `AuthenticationFailed` | N | 18456 | 연결문자열 점검 |
| `ConnectionLost` | Y | -2,233 | 재시도 |
| `Timeout` | Y | -2 | 재시도/타임아웃 증가 |
| `Deadlock` | Y | 1205 | 재시도(자동) |
| `ConstraintViolation` | N | 2627,547 | 데이터 검증 |
| `DataConversion` | N | 8114 | 타입 확인 |
| `ParameterMismatch` | N | 8144 | SP 파라미터 확인 |
| `PermissionDenied` | N | 229 | 권한 부여 |
| `ResourceExhausted` | Y | 701,1105 | 리소스 확보 |
| `TransactionAborted` | Y | 3928 | 트랜잭션 재시도 |
| `QuerySyntax` | N | 156,102 | SQL 수정 |
| `UserDefined` | N | 50000+ | RAISERROR/THROW |
| `CloudTransient` | Y | 40613 | 재시도 |
| `Unknown` | N | 기타 | 로깅 후 조사 |

---

## 8. 파라미터 바인딩 6가지

```csharp
// 1) 익명 타입 (가장 일반적)
.With(new { Name = "홍길동", Age = 30 })

// 2) DTO 클래스
.With(new UserParam { Name = "홍길동", Age = 30 })

// 3) Dictionary
.With(new Dictionary<string, object?> { ["Name"] = "홍길동", ["Age"] = 30 })

// 4) 보간 SQL (자동 파라미터화, SQL Injection 방지)
.Sql($"SELECT * FROM Users WHERE Name = {name} AND Age > {age}")

// 5) 파라미터 없음
.Sql("SELECT COUNT(*) FROM Users").ExecuteScalarAsync<int>()

// 6) TVP ([TvpRow] DTO 리스트)
.With(new { Items = tvpRowList })
```

---

## 9. 트랜잭션

```csharp
// 기본 (ReadCommitted)
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default");

// 격리수준 지정
await using IDbTransactionScope tx = await db.BeginTransactionAsync(
    "Default", System.Data.IsolationLevel.Serializable);

// Fluent 체이닝 (tx는 IProcedureStage를 상속)
DbResult<int> r1 = await tx.Procedure("dbo.usp_Insert").With(param).ExecuteAsync();

// 명시적 커밋
DbResult<bool> commitResult = await tx.CommitAsync();

// 명시적 롤백
DbResult<bool> rollbackResult = await tx.RollbackAsync();

// 자동 롤백: CommitAsync 미호출 시 Dispose에서 자동 롤백 (데이터 무결성 보장)
```

---

## 10. TVP [TvpRow]

```csharp
[TvpRow(TypeName = "dbo.OrderItemType")]
public sealed class OrderItemTvpRow
{
    [TvpLength(50)]
    public required string ItemCode { get; init; }
    public int Quantity { get; init; }
    [TvpPrecision(18, 2)]
    public decimal UnitPrice { get; init; }
}
```

| 어트리뷰트 | 대상 | 설명 |
|---|---|---|
| `[TvpRow]` | class/struct | TVP 행 마커 (Source Generator 대상) |
| `[TvpRow(TypeName="...")]` | class/struct | SQL TVP 타입명 명시 |
| `[TvpLength(N)]` | property | 문자열/바이너리 길이 |
| `[TvpPrecision(P, S)]` | property | decimal Precision/Scale |

TVP 검증 모드: `Strict`(기본, 예외) / `LogOnly`(로그만) / `None`(검증 없음)

---

## 11. BulkInsertAsync

```csharp
DbResult<long> result = await db.BulkInsertAsync(
    instanceName: "Default",
    destinationTable: "[dbo].[Users]",
    records: largeDataset,
    options: new BulkInsertOptions
    {
        BatchSize = 10_000, TimeoutSeconds = 600, EnableStreaming = true,
        FireTriggers = false, CheckConstraints = true, KeepIdentity = false
    });
```

> Reflection 기반 -- AOT 환경 사용 불가. 수만~수십만 건 대량 INSERT에 적합.

---

## 12. 인터셉터 IDbInterceptor

```csharp
public sealed class AuditInterceptor(ILogger<AuditInterceptor> logger) : IDbInterceptor
{
    public ValueTask<DbInterceptionResult> OnExecutingAsync(DbInterceptionContext ctx, CancellationToken ct)
    {
        logger.LogInformation("[Before] {Cmd} on {Inst}", ctx.CommandText, ctx.InstanceName);
        return ValueTask.FromResult(DbInterceptionResult.Continue); // Continue | Suppress
    }
    public ValueTask OnExecutedAsync(DbInterceptionContext ctx, CancellationToken ct)
    {
        logger.LogInformation("[After] {Cmd} ({Ms}ms)", ctx.CommandText, ctx.ElapsedMs);
        return ValueTask.CompletedTask;
    }
    public ValueTask OnErrorAsync(DbInterceptionContext ctx, CancellationToken ct)
    {
        logger.LogError(ctx.Exception, "[Error] {Cmd}", ctx.CommandText);
        return ValueTask.CompletedTask;
    }
}
// DI 등록: builder.Services.AddLibDbInterceptor<AuditInterceptor>();
```

DbInterceptionContext 속성: `CommandText`, `CommandType`, `InstanceName`, `StartTime`(UTC), `ElapsedMs`(After/Error), `Result`(After), `Exception`(Error), `State`(Dictionary, 인터셉터 간 공유)

---

## 13. JSON 매핑

```csharp
// Dictionary 결과에서 JSON 컬럼 역직렬화
OrderDetail? detail = row.MapJsonColumn<OrderDetail>("EXTRA_DATA");

// 비동기 스트림 + JSON
await foreach ((Dictionary<string, object?> row, OrderMeta? meta) in
    streamResult.Value.WithJsonColumnAsync<OrderMeta>("META_JSON")) { }

// 문자열 직접 변환
OrderDetail? d = jsonString.FromJson<OrderDetail>();
string json = orderDetail.ToJson();
```

`[JsonColumn]` 어트리뷰트: DTO의 JSON 컬럼 프로퍼티에 마커로 부여.

---

## 14. 쿼리 캐싱

```csharp
// IDistributedCache - 단건
DbResult<UserDto?> r = await db.Default.Procedure("sp").With(p)
    .QuerySingleAsync<UserDto>()
    .WithCacheAsync(cache, "user:1", TimeSpan.FromMinutes(5));

// IDistributedCache - 리스트 (스트림 -> List 구체화)
DbResult<List<ProductDto>> r = await db.Default.Procedure("sp")
    .QueryAsync<ProductDto>()
    .WithCacheListAsync(cache, "products:all", TimeSpan.FromMinutes(10));

// HybridCache (L1+L2 자동)
DbResult<UserDto?> r = await db.Default.Procedure("sp").With(p)
    .QuerySingleAsync<UserDto>()
    .WithHybridCacheAsync(hybridCache, "user:1", TimeSpan.FromMinutes(5));

// 캐시 무효화
await cache.InvalidateCacheAsync("user:1");
```

---

## 15. 연결 풀 메트릭

| 메트릭 이름 | 타입 | 설명 |
|---|---|---|
| `libdb.db_requests_total` | Counter | DB 요청 총 횟수 |
| `libdb.db_request_duration_ms` | Histogram | DB 요청 소요(ms) |
| `libdb.connection.acquire_duration_ms` | Histogram | 연결 획득 소요(ms) |
| `libdb.connection.pool_waits` | Counter | 풀 대기 횟수(100ms+) |
| `libdb.connection.pool_timeouts` | Counter | 풀 타임아웃 횟수 |
| `libdb.cache_requests_total` | Counter | 캐시 작업 횟수 |
| `libdb.cache_op_duration_ms` | Histogram | 캐시 작업 소요(ms) |

ActivitySource/Meter 이름: `"Lib.Db"`. OTel 수집: `.AddSource("Lib.Db")` / `.AddMeter("Lib.Db")`

---

## 16. 멀티 DB 인스턴스

```csharp
// 기본 인스턴스
DbResult<UserDto?> user = await db.Default.Procedure("dbo.usp_GetUser").With(p).QuerySingleAsync<UserDto>();

// 이름 지정 인스턴스
DbResult<IAsyncEnumerable<ReportDto>> r = await db.Use("Analytics").Procedure("sp").QueryAsync<ReportDto>();

// Ad-hoc 연결 (테스트/멀티테넌트)
DbResult<int> r = await db.UseConnectionString("Server=tenant1;...").Procedure("sp").ExecuteAsync();
```

설정: `ConnectionStringNames: ["Default", "Analytics", "Legacy"]` -- 첫 번째가 기본 인스턴스.

---

## 17. 상황별 코드 템플릿 20개

### T01: 단건 조회 (SP)
```csharp
public sealed class UserService(IDbSession db)
{
    public async Task<UserDto?> GetUserAsync(int userId, CancellationToken ct = default)
    {
        DbResult<UserDto?> result = await db.Default
            .Procedure("dbo.usp_GetUser").With(new { UserId = userId }).QuerySingleAsync<UserDto>(ct);
        return result.IsSuccess ? result.Value : null;
    }
}
```

### T02: 다건 스트림
```csharp
public async IAsyncEnumerable<OrderDto> GetOrdersAsync(string status,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    DbResult<IAsyncEnumerable<OrderDto>> result = await db.Default
        .Procedure("dbo.usp_GetOrders").With(new { Status = status }).QueryAsync<OrderDto>(ct);
    if (!result.IsSuccess || result.Value is null) yield break;
    await foreach (OrderDto order in result.Value.WithCancellation(ct)) { yield return order; }
}
```

### T03: INSERT (NonQuery)
```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertOrder").With(new { req.OrderNo, req.CustomerCd }).ExecuteAsync(ct);
```

### T04: 스칼라 (COUNT)
```csharp
DbResult<int?> result = await db.Default
    .Sql("SELECT COUNT(*) FROM dbo.Orders WHERE Status = 'ACTIVE'").ExecuteScalarAsync<int>(ct);
int count = result is { IsSuccess: true, Value: not null } ? result.Value.Value : 0;
```

### T05: 트랜잭션 + 다중 SP
```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", ct);
DbResult<int> r1 = await tx.Procedure("dbo.usp_Withdraw").With(new { req.FromAccount, req.Amount }).ExecuteAsync(ct);
DbResult<int> r2 = await tx.Procedure("dbo.usp_Deposit").With(new { req.ToAccount, req.Amount }).ExecuteAsync(ct);
if (r1.IsSuccess && r2.IsSuccess) { await tx.CommitAsync(ct); }
```

### T06: TVP 전달
```csharp
DbResult<int> result = await db.Default
    .Procedure("dbo.usp_InsertItems").With(new { Items = tvpRowList }).ExecuteAsync(ct);
```

### T07: BulkInsert
```csharp
DbResult<long> result = await db.BulkInsertAsync("Default", "[dbo].[Logs]", records,
    new BulkInsertOptions { BatchSize = 10_000, EnableStreaming = true }, ct);
```

### T08: 다중 ResultSet
```csharp
DbResult<IMultipleResultReader> result = await db.Default
    .Procedure("dbo.usp_GetUserWithOrders").With(new { UserId = userId }).QueryMultipleAsync(ct);
if (result.IsSuccess && result.Value is not null)
{
    await using IMultipleResultReader reader = result.Value;
    UserDto? user = await reader.ReadSingleAsync<UserDto>(ct);
    List<OrderDto> orders = await reader.ReadAsync<OrderDto>(ct);
}
```

### T09: 보간 SQL
```csharp
DbResult<IAsyncEnumerable<ProductDto>> result = await db.Default
    .Sql($"SELECT * FROM Products WHERE Name LIKE {'%' + keyword + '%'} AND Price >= {minPrice}")
    .QueryAsync<ProductDto>(ct);
```

### T10: WithTimeout 오버라이드
```csharp
DbResult<IAsyncEnumerable<BigDto>> result = await db.Default
    .Procedure("dbo.usp_HeavyReport").WithTimeout(120).With(new { Year = 2026 }).QueryAsync<BigDto>();
```

### T11: Dictionary + JSON 매핑
```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await db.Default
    .Procedure("dbo.usp_GetDynamic").QueryAsync<Dictionary<string, object?>>();
await foreach (Dictionary<string, object?> row in result.Value!)
{
    ConfigData? config = row.MapJsonColumn<ConfigData>("CONFIG_JSON");
}
```

### T12: 캐시 + 단건
```csharp
DbResult<UserDto?> result = await db.Default.Procedure("dbo.usp_GetUser").With(new { UserId = userId })
    .QuerySingleAsync<UserDto>().WithCacheAsync(cache, $"user:{userId}", TimeSpan.FromMinutes(10));
```

### T13: 캐시 + 리스트
```csharp
DbResult<List<CategoryDto>> result = await db.Default.Procedure("dbo.usp_GetCategories")
    .QueryAsync<CategoryDto>().WithCacheListAsync(cache, "categories:all", TimeSpan.FromHours(1));
```

### T14: 격리수준 + 트랜잭션
```csharp
await using IDbTransactionScope tx = await db.BeginTransactionAsync("Default", IsolationLevel.Serializable);
```

### T15: 에러 -> API 응답
```csharp
return error.Kind switch
{
    DbErrorKind.SchemaNotFound => NotFound(error.Message),
    DbErrorKind.ConstraintViolation => Conflict(error.Message),
    DbErrorKind.PermissionDenied => Forbid(),
    DbErrorKind.Timeout => StatusCode(504, error.Message),
    _ when error.IsTransient => StatusCode(503, "일시적 오류"),
    _ => StatusCode(500, error.Message)
};
```

### T16: Minimal API
```csharp
app.MapGet("/api/users/{id:int}", async (int id, IDbSession db) =>
{
    DbResult<UserDto?> result = await db.Default.Procedure("dbo.usp_GetUser")
        .With(new { UserId = id }).QuerySingleAsync<UserDto>();
    return result switch
    {
        { IsSuccess: true, Value: not null } r => Results.Ok(r.Value),
        { IsSuccess: true, Value: null } => Results.NotFound(),
        _ => Results.Problem(result.Error!.Value.Message)
    };
});
```

### T17: BackgroundService에서 DB
```csharp
public sealed class BatchService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IDbSession db = scope.ServiceProvider.GetRequiredService<IDbSession>();
        DbResult<IAsyncEnumerable<JobDto>> result = await db.Default
            .Procedure("dbo.usp_GetPendingJobs").QueryAsync<JobDto>(ct);
        if (result.IsSuccess && result.Value is not null)
            await foreach (JobDto job in result.Value.WithCancellation(ct)) { /* 처리 */ }
    }
}
```

### T18: HybridCache
```csharp
DbResult<ProductDto?> result = await db.Default.Procedure("dbo.usp_GetProduct").With(new { ProductId = pid })
    .QuerySingleAsync<ProductDto>().WithHybridCacheAsync(hybridCache, $"product:{pid}", TimeSpan.FromMinutes(30));
```

### T19: 멀티 인스턴스 병렬
```csharp
Task<DbResult<int?>> t1 = db.Default.Sql("SELECT COUNT(*) FROM Orders").ExecuteScalarAsync<int>(ct);
Task<DbResult<int?>> t2 = db.Use("Analytics").Sql("SELECT COUNT(*) FROM PageViews").ExecuteScalarAsync<int>(ct);
await Task.WhenAll(t1, t2);
```

### T20: Raw SQL + Dictionary
```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await db.Default
    .Sql("SELECT TOP 10 * FROM sys.tables ORDER BY modify_date DESC")
    .QueryAsync<Dictionary<string, object?>>();
```

---

## 18. 코딩 규칙

| 규칙 | 예시 |
|---|---|
| **var 금지** | `DbResult<int> result = ...` |
| **sealed 기본** | `public sealed class UserService` |
| **Primary Constructor** | `public sealed class UserService(IDbSession db)` |
| **file-scoped namespace** | `namespace MyApp.Services;` |
| **Collection Expression** | `List<int> ids = [1, 2, 3];` |
| **Pattern Matching** | `result switch { { IsSuccess: true } => ... }` |
| **Lock 타입** | `private readonly Lock _lock = new();` |
| **field 키워드** (C# 14) | `public int X { get; set => field = value; }` |
| **한국어 XML 주석** | `/// <summary>사용자 조회</summary>` |
| **#region 한국어** | `#region 필드 선언` |

파일 헤더:
```csharp
// ============================================================================
// 파일: {Namespace}/{FileName}.cs
// 설명: {한국어 설명}
// 대상: .NET 10 / C# 14
// ============================================================================
```

---

## 19. LibDbOptions 주요 속성

### 연결/인스턴스

| 속성 | 기본값 | 설명 |
|---|---|---|
| `ConnectionStrings` | `[]` | 인스턴스별 연결 문자열 |
| `ConnectionStringNames` | `["Default"]` | 사용할 키 목록 (첫 번째=기본) |

### 스키마 캐싱

| 속성 | 기본값 | 설명 |
|---|---|---|
| `EnableSchemaCaching` | `true` | 스키마 캐싱 ON/OFF |
| `SchemaRefreshIntervalSeconds` | `60` | 갱신 주기(초, 1-86400) |
| `PrewarmSchemas` | `["dbo"]` | 워밍업 스키마 |
| `PrewarmIncludePatterns` | `[]` | 포함 패턴 (* 와일드카드) |
| `PrewarmExcludePatterns` | `[]` | 제외 패턴 |

### 실행/타임아웃

| 속성 | 기본값 | 설명 |
|---|---|---|
| `DefaultCommandTimeoutSeconds` | `30` | 쿼리 타임아웃(초, 1-600) |
| `BulkCommandTimeoutSeconds` | `600` | Bulk 타임아웃(초, 1-3600) |
| `BulkBatchSize` | `5,000` | 배치 사이즈(100-100K) |
| `EnableDryRun` | `false` | 모의 실행 |
| `StrictRequiredParameterCheck` | `true` | SP 필수 파라미터 엄격 검사 |

### TVP/JSON

| 속성 | 기본값 | 설명 |
|---|---|---|
| `TvpValidationMode` | `Strict` | TVP 스키마 검증 |
| `EnableGeneratedTvpBinder` | `true` | SG 기반 TVP Binder |
| `JsonOptions` | `null` | JSON 매핑 옵션 (null=Web 기본) |

### Resilience

| 속성 | 기본값 | 설명 |
|---|---|---|
| `EnableResilience` | `false` | Polly 활성화 |
| `Resilience.MaxRetryCount` | `3` | 최대 재시도 |
| `Resilience.BaseRetryDelayMs` | `100` | 기본 지연(ms) |
| `Resilience.CircuitBreakerFailureRatio` | `0.5` | CB 실패 비율 |
| `Resilience.CircuitBreakerBreakDurationMs` | `30000` | CB 차단 기간(ms) |

### 관측/캐시

| 속성 | 기본값 | 설명 |
|---|---|---|
| `EnableOpenTelemetry` | `false` | OTel 추적/메트릭 |
| `EnableObservability` | `false` | 관측 마스터 스위치 |
| `EnableSharedMemoryCache` | `null` | 공유 메모리 (null=자동, Win=true) |
| `EnableEpochCoordination` | `null` | Epoch 동기화 (null=자동) |

### Chaos Engineering

| 속성 | 기본값 | 설명 |
|---|---|---|
| `Chaos.Enabled` | `false` | 카오스 주입 |
| `Chaos.ExceptionRate` | `0.01` | 예외 발생 확률 |
| `Chaos.LatencyRate` | `0.05` | 지연 발생 확률 |

---

## 20. using 문 참조

```csharp
// --- 핵심 (항상 필요) ---
using Lib.Db.Contracts.Core;        // DbResult<T>, DbError, DbErrorKind, BulkInsertOptions
using Lib.Db.Contracts.Entry;       // IDbSession, IDbTransactionScope, IProcedureStage

// --- TVP ---
using Lib.Db.Contracts.Models;      // [TvpRow], [TvpLength], [TvpPrecision]

// --- 실행 (내부, 일반 불필요) ---
using Lib.Db.Contracts.Execution;   // IMultipleResultReader, DbExecutionOptions

// --- 인터셉터 ---
using Lib.Db.Contracts.Infrastructure; // IDbInterceptor, DbInterceptionContext

// --- 확장 메서드 ---
using Lib.Db.Extensions;           // JsonMappingExtensions, QueryCacheExtensions

// --- DI (자동 네임스페이스) ---
using Microsoft.Extensions.DependencyInjection; // AddLibDb, AddHighPerformanceDb

// --- 설정 ---
using Lib.Db.Configuration;        // LibDbOptions

// --- 캐싱 ---
using Microsoft.Extensions.Caching.Distributed;  // IDistributedCache
using Microsoft.Extensions.Caching.Hybrid;       // HybridCache
```

최소 using (일반 서비스): `Lib.Db.Contracts.Core` + `Lib.Db.Contracts.Entry`
TVP 추가: + `Lib.Db.Contracts.Models`
캐시 추가: + `Lib.Db.Extensions` + `Microsoft.Extensions.Caching.Distributed`
