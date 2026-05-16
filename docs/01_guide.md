# Lib.Db v2 가이드

아키텍처, 설정, Fluent API, 마이그레이션을 통합한 v2 핵심 가이드입니다.

---

## 1. 아키텍처 개요

### 1-1. 단일 진입점: IDbSession

v2에서는 `IDbSession`이 모든 DB 작업의 유일한 진입점입니다.
인스턴스 선택, 트랜잭션, Fluent API 체이닝이 하나의 세션 객체로 통합됩니다.

```
IDbSession
  ├─ .Default          → IProcedureStage (기본 DB)
  ├─ .Use("DB1")       → IProcedureStage (명명된 DB)
  ├─ .UseConnectionString("...") → IProcedureStage (Ad-hoc)
  └─ .BeginTransactionAsync("DB1") → IDbTransactionScope
```

### 1-2. 3-Stage Fluent Pipeline

모든 쿼리는 3단계 인터페이스 체인으로 실행됩니다.

```
IProcedureStage → IParameterStage → IExecutionStage<T> → DbResult<T>
  (명령 선택)       (파라미터/옵션)      (실행 및 결과)
```

### 1-3. 레이어 구조

| 레이어 | 역할 | 핵심 컴포넌트 |
|---|---|---|
| **Contracts** | 순수 인터페이스/DTO | IDbSession, IProcedureStage, DbResult |
| **Core** | 저수준 프리미티브 | DbSession, InterpolatedStringHandler |
| **Infrastructure** | 바인딩/진단 | DbBinder, DiagnosticLogger |
| **Execution** | SQL 실행 엔진 | SqlDbExecutor, DbConnectionFactory |
| **Caching** | L1+L2 하이브리드 캐시 | SharedMemoryCache, GlobalCacheEpoch |

---

## 2. 설정 (Configuration)

### 2-1. appsettings.json 기본 구조

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyDb;User Id=app_user;Password=***;Encrypt=True;TrustServerCertificate=False;",
    "LogDb": "Server=localhost;Database=LogDb;User Id=log_user;Password=***;Encrypt=True;TrustServerCertificate=False;"
  },
  "LibDb": {
    "ConnectionStringNames": ["Default", "LogDb"],
    "ConnectionSecurityProfile": "Production",
    "RawSqlPolicy": "DenyWriteText",
    "Mars": "ForceEnable",
    "EnableSchemaCaching": true,
    "SchemaRefreshIntervalSeconds": 60,
    "PrewarmSchemas": ["dbo"],
    "PrewarmExcludePatterns": ["*_Test*", "*_Legacy*"],
    "StrictRequiredParameterCheck": true,
    "EnableGeneratedTvpBinder": true,
    "EnableResilience": true,
    "EnableObservability": false
  }
}
```

**핵심 규칙**:
- `ConnectionStringNames`는 `IReadOnlyList<string>`이며, 첫 번째 항목이 `Default` 인스턴스
- 최상위 `ConnectionStrings` 섹션에서 `ConnectionStringNames`에 나열된 키만 자동 수집

### 2-2. DI 등록

#### AddLibDb (권장)

```csharp
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// appsettings.json "LibDb" 섹션 자동 바인딩
builder.Services.AddLibDb(builder.Configuration);

IHost host = builder.Build();
await host.RunAsync();
```

#### AddHighPerformanceDb (수동 구성)

```csharp
builder.Services.AddHighPerformanceDb(options =>
{
    options.ConnectionStrings["Main"] = "Server=...";
    options.ConnectionStringNames = ["Main"];
    options.DefaultCommandTimeoutSeconds = 60;
    options.EnableSharedMemoryCache = false;
});
```

---

## 3. Fluent API 사용법

### 3-1. 저장 프로시저 (Procedure)

```csharp
public sealed class UserRepository(IDbSession session)
{
    public record User(int Id, string Name, string Email);

    public async Task<DbResult<User?>> GetUserAsync(int id)
    {
        DbResult<User?> result = await session.Default
            .Procedure("dbo.usp_GetUser")
            .With(new { Id = id })
            .QuerySingleAsync<User>();

        return result;
    }
}
```

### 3-2. Raw SQL (문자열)

```csharp
DbResult<IAsyncEnumerable<User>> result = await session.Default
    .Sql("SELECT * FROM Users WHERE DeptId = @DeptId")
    .With(new { DeptId = 10 })
    .QueryAsync<User>();
```

### 3-3. 보간 SQL (값 인수 파라미터화)

```csharp
int userId = 42;
string name = "Alice";

DbResult<User?> result = await session.Default
    .SqlInterpolated($"SELECT * FROM Users WHERE Id = {userId} AND Name = {name}")
    .QuerySingleAsync<User>();

// 실제 실행: SELECT * FROM Users WHERE Id = @p0 AND Name = @p1
```

보간된 값 인수는 파라미터로 바인딩되어 값 기반 SQL injection 위험을 줄입니다.
테이블명, 컬럼명, 정렬 방향 같은 SQL 구조는 파라미터화되지 않으므로 사용자 입력을 직접 조립하지 말고 allow-list로 선택하세요.

### 3-4. 타임아웃 설정

```csharp
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_HeavyReport")
    .WithTimeout(120)
    .With(new { Year = 2026 })
    .ExecuteAsync();
```

### 3-5. 5개 실행 메서드

| 메서드 | 반환 타입 | 용도 |
|---|---|---|
| `QueryAsync<T>()` | `DbResult<IAsyncEnumerable<T>>` | 다건 스트리밍 조회 |
| `QuerySingleAsync<T>()` | `DbResult<T?>` | 단건 조회 |
| `ExecuteScalarAsync<T>()` | `DbResult<T?>` | 스칼라 값 (1행 1열) |
| `QueryMultipleAsync()` | `DbResult<IMultipleResultReader>` | 다중 결과 셋 |
| `ExecuteAsync()` | `DbResult<int>` | NonQuery (영향 행 수) |

### 3-6. DbResult 패턴 매칭

모든 실행 메서드는 `DbResult<T>`를 반환합니다. 예외 대신 결과 타입으로 성공/실패를 구분합니다.

```csharp
DbResult<User?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

// 방법 1: Deconstruct
(bool success, User? user, DbError? error) = result;
if (!success)
{
    string message = error!.Value.Kind switch
    {
        DbErrorKind.Timeout => "쿼리 시간 초과",
        DbErrorKind.ConnectionLost => "DB 연결 끊김",
        DbErrorKind.SchemaNotFound => $"SP를 찾을 수 없음: {error.Value.ObjectName}",
        _ => error.Value.Message
    };
    Console.WriteLine(message);
    return;
}

// 방법 2: 속성 직접 접근
if (result.IsSuccess)
{
    User? user = result.Value;
}
```

---

## 4. 트랜잭션

### 4-1. 기본 패턴

`IDbTransactionScope`는 `IProcedureStage`를 상속하므로, 트랜잭션 내에서 바로 Fluent 체이닝이 가능합니다.
명시적 `CommitAsync()` 없이 Dispose되면 자동 롤백됩니다.

```csharp
await using IDbTransactionScope tx = await session.BeginTransactionAsync("Default");

DbResult<int> insertResult = await tx
    .Procedure("dbo.usp_InsertOrder")
    .With(new { CustomerId = 100, Amount = 5000m })
    .ExecuteAsync();

if (!insertResult.IsSuccess)
{
    await tx.RollbackAsync();
    return;
}

DbResult<int> logResult = await tx
    .Procedure("dbo.usp_InsertAuditLog")
    .With(new { Action = "OrderCreated", RowCount = insertResult.Value })
    .ExecuteAsync();

if (logResult.IsSuccess)
{
    DbResult<bool> commitResult = await tx.CommitAsync();
}
// CommitAsync 미호출 시 Dispose에서 자동 롤백
```

---

## 5. 멀티 DB 지원

### 5-1. 인스턴스 선택

```json
{
  "ConnectionStrings": {
    "SalesDb": "Server=sales-server;Database=Sales;...",
    "LogDb": "Server=log-server;Database=Logs;..."
  },
  "LibDb": {
    "ConnectionStringNames": ["SalesDb", "LogDb"]
  }
}
```

```csharp
public sealed class MultiDbService(IDbSession session)
{
    public async Task ProcessAsync()
    {
        // SalesDb에서 조회
        DbResult<IAsyncEnumerable<Order>> orders = await session.Use("SalesDb")
            .Procedure("dbo.usp_GetPendingOrders")
            .QueryAsync<Order>();

        // LogDb에 기록
        DbResult<int> logResult = await session.Use("LogDb")
            .Procedure("dbo.usp_WriteLog")
            .With(new { Message = "주문 처리 시작" })
            .ExecuteAsync();
    }
}
```

### 5-2. Ad-hoc 연결 (멀티 테넌트)

```csharp
string tenantConnStr = GetTenantConnectionString(tenantId);

DbResult<IAsyncEnumerable<TenantData>> result = await session
    .UseConnectionString(tenantConnStr)
    .Sql("SELECT * FROM TenantConfig")
    .QueryAsync<TenantData>();
```

---

## 6. v1 → v2 마이그레이션

### 6-1. 핵심 변경 사항

| v1 | v2 | 설명 |
|---|---|---|
| `IDbContext` | `IDbSession` | 단일 진입점 통합 |
| `UseInstance(name)` | `Use(name)` | 메서드명 간소화 |
| 예외 throw | `DbResult<T>` | 모든 실행 결과를 Result 타입으로 래핑 |
| `ConnectionStringName` (단수) | `ConnectionStringNames` (복수 리스트) | 멀티 DB 네이티브 지원 |
| 13개 실행 경로 | 5개 실행 메서드 | API 단순화 |

### 6-2. 코드 변환 예시

| v1 | v2 |
|---|---|
| `IDbContext db` (생성자) | `IDbSession session` (생성자) |
| `db.Default.Procedure(...).QuerySingleAsync<T>()` | `session.Default.Procedure(...).QuerySingleAsync<T>()` → `DbResult<T?>` |
| `try/catch (SqlException)` | `if (!result.IsSuccess) { /* error.Kind 분기 */ }` |
| `"ConnectionStringName": "Default"` | `"ConnectionStringNames": ["Default"]` |

DI 등록(`AddLibDb`)은 동일하지만, 주입 대상이 `IDbSession`으로 변경되므로 모든 생성자를 업데이트해야 합니다.

---

## 7. v2.1 신규 기능 요약

### 7.1 BulkInsertAsync (대량 INSERT)

SqlBulkCopy 기반 고성능 대량 INSERT입니다. TVP 대비 수만~수십만 건 이상에서 더 빠른 성능을 제공합니다.

```csharp
public class SensorReading
{
    public int SensorId { get; init; }
    public double Value { get; init; }
    public DateTime Timestamp { get; init; }
}

List<SensorReading> readings = GetReadings(); // 50,000건

BulkInsertOptions options = new()
{
    BatchSize = 10_000,
    TimeoutSeconds = 300,
    EnableStreaming = true,
    FireTriggers = false,
    CheckConstraints = false,
    KeepIdentity = false
};

DbResult<long> result = await session.BulkInsertAsync(
    "Default",
    "[dbo].[SensorReadings]",
    readings,
    options);

if (result.IsSuccess)
{
    Console.WriteLine($"삽입 완료: {result.Value:N0}건");
}
```

**BulkInsertOptions 속성:**

| 속성 | 타입 | 기본값 | 설명 |
|---|---|---|---|
| `BatchSize` | `int` | 5,000 | 배치당 행 수 |
| `TimeoutSeconds` | `int` | 600 | 명령 타임아웃 (초) |
| `EnableStreaming` | `bool` | `true` | 스트리밍 활성화 |
| `FireTriggers` | `bool` | `false` | INSERT 트리거 실행 여부 |
| `CheckConstraints` | `bool` | `false` | 제약 조건 검사 여부 |
| `KeepIdentity` | `bool` | `false` | IDENTITY 값 유지 여부 |

> **주의**: BulkInsertAsync는 Reflection 기반이므로 AOT 환경에서는 사용할 수 없습니다.

### 7.2 트랜잭션 격리 수준

`BeginTransactionAsync`에 `IsolationLevel`을 지정하여 격리 수준을 제어할 수 있습니다.

```csharp
// 기본 (ReadCommitted)
await using IDbTransactionScope tx1 = await session.BeginTransactionAsync("Default");

// Serializable
await using IDbTransactionScope tx2 = await session.BeginTransactionAsync(
    "Default",
    System.Data.IsolationLevel.Serializable);

// Snapshot (SQL Server에서 Snapshot Isolation이 활성화된 경우)
await using IDbTransactionScope tx3 = await session.BeginTransactionAsync(
    "Default",
    System.Data.IsolationLevel.Snapshot);
```

지원되는 격리 수준:

| IsolationLevel | 설명 | 사용 시나리오 |
|---|---|---|
| `ReadUncommitted` | Dirty Read 허용 | 근사치 통계 조회 |
| `ReadCommitted` | 기본값, 커밋된 데이터만 읽기 | 일반적인 OLTP |
| `RepeatableRead` | 트랜잭션 내 반복 읽기 보장 | 중요 비즈니스 로직 |
| `Serializable` | 최고 격리, 범위 잠금 | 재고 차감, 금융 거래 |
| `Snapshot` | 스냅샷 기반, 잠금 없는 읽기 | 읽기 경합이 심한 환경 |

---

## 8. v2.1 → v2.2 마이그레이션

### 8-1. Breaking Changes

| 변경 | 영향 | 마이그레이션 |
|---|---|---|
| `EnableOpenTelemetry` → `[Obsolete]` | 컴파일 경고 발생 | `EnableObservability`로 변경 |
| `AdaptivePolicyFactory` 삭제 | 직접 참조 시 빌드 오류 | `DefaultResiliencePipelineProvider` 사용 (DI 자동 등록) |
| `date` 타입 매핑 변경 | `[GenerateTvpFromDb]` 사용 시 생성 타입 변경 | `DateTime` → `DateOnly` 확인, `TimeSpan` → `TimeOnly` 확인 |

### 8-2. 신규 기능

#### MARS 정책 (`MarsPolicy`)

```json
{
  "LibDb": {
    "Mars": "ForceEnable"
  }
}
```

| 값 | 동작 |
|---|---|
| `Disabled` | MARS 미사용. `QueryMultipleAsync` 호출 시 예외 |
| `Auto` (기본값) | `QueryMultipleAsync` 사용 시 MARS 미설정이면 예외 (v2.1과 동일 동작) |
| `ForceEnable` | `AddLibDb()` 등록 시 ConnectionString에 `MultipleActiveResultSets=True` 자동 주입 |

#### 관측 가능성 통합 (`EnableObservability`)

v2.2부터 `EnableOpenTelemetry`와 `EnableObservability`가 단일 속성으로 통합되었습니다.

```csharp
// v2.1
options.EnableOpenTelemetry = true;

// v2.2 (권장)
options.EnableObservability = true;
```

> `EnableOpenTelemetry`는 하위 호환을 위해 유지되지만 v3.0에서 제거됩니다.

### 8-3. 개선 사항 (코드 변경 불필요)

- **Nullable 매핑 수정**: `[DbResult]` 어트리뷰트의 `int?` 등 Nullable 프로퍼티가 DB NULL 시 올바르게 `null` 설정
- **Source Generator 성능**: CompilationProvider 최적화로 IDE 빌드 속도 향상
- **BulkInsert 성능**: Expression Tree 기반 getter 컴파일로 Reflection 제거
- **메모리 안전**: Meter/ActivitySource 이중 등록 제거, NegativeCache 원자성 개선
- **HealthCheck**: `HealthCheckThrottleSeconds` 옵션이 실제 적용 (이전 1초 하드코딩)
