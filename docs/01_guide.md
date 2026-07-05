# Lib.Db Guide

아키텍처, 설정, Fluent API, Runtime TVP를 통합한 현재 API 핵심 가이드입니다.

---

## 1. 아키텍처 개요

### 1-1. 단일 진입점: IDbSession

현재 API에서는 `IDbSession`이 모든 DB 작업의 유일한 진입점입니다.
인스턴스 선택, 트랜잭션, Fluent API 체이닝이 하나의 세션 객체로 통합됩니다.

```
IDbSession
  ├─ .Default          → IProcedureStage (기본 DB)
  ├─ .Use("DB1")       → IProcedureStage (명명된 DB)
  ├─ .UseConnectionString("...") → IProcedureStage (Ad-hoc)
  ├─ .Schema           → ISchemaMaintenanceStage (기본 DB 스키마 관리)
  ├─ .UseSchema("DB1") → ISchemaMaintenanceStage (명명된 DB 스키마 관리)
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
| **Caching** | Provider-neutral local/L2 캐시 | HybridCache, optional IDistributedCache provider, SharedMemoryCache opt-in |

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
    "EnableResilience": true,
    "EnableObservability": false
  }
}
```

**핵심 규칙**:
- `ConnectionStringNames`는 `IReadOnlyList<string>`이며, 첫 번째 항목이 `Default` 인스턴스
- 최상위 `ConnectionStrings` 섹션에서 `ConnectionStringNames`에 나열된 키만 자동 수집
- Runtime TVP static shape는 `appsettings.json`이 아니라 `AddLibDb(options => options.Tvp.Map<T>(...).Column(...))` 코드 등록으로 고정

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
테이블명, 컬럼명, 정렬 방향 같은 SQL 구조는 파라미터화되지 않으므로 사용자 입력을 직접 조립하지 말고 allow-list로 선택하세요. `RawSqlPolicy.DenyWriteText`는 mutating raw SQL과 bare and qualified `sp_executesql` mutating wrappers를 차단하는 guardrail이지만, 운영 보안 경계는 `DenyAllText`, 저장 프로시저 권한, 최소 권한 DB 계정으로 구성하세요.

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

## Migration And History

Version-specific migration notes and release history live in [History](./history.md). This guide describes the current API surface.
