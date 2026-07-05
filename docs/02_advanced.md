# Lib.Db Advanced Features

Runtime TVP, AOT, 성능 최적화, Resilience, 캐싱을 다루는 고급 가이드입니다.

---

## 1. Runtime TVP (Table-Valued Parameters)

TVP 입력은 별도 생성기 패키지가 아니라 `Lib.Db` 런타임이 직접 처리합니다.
SQL Server에는 사용자 정의 table type과 `READONLY` TVP 파라미터가 있어야 하며, C# 호출부에서는 TVP row sequence를 명시 wrapper 또는 등록형 static shape로 전달합니다.

### 1-1. 기본 API: 명시 TVP wrapper

가장 단순한 경로는 `LibDb.Tvp("schema.TypeName", rows)`입니다.
한 저장 프로시저에 스칼라 파라미터와 TVP 파라미터가 함께 있어도 동일한 `.With(new { ... })` 객체 안에 넣습니다.

```csharp
using Lib.Db;

public sealed record ProductRow(
    int ProductId,
    string Name,
    decimal Price,
    DateTime CreatedAt);

List<ProductRow> products =
[
    new(1, "Laptop", 1_200_000m, DateTime.UtcNow),
    new(2, "Mouse", 25_000m, DateTime.UtcNow)
];

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new
    {
        RequestedBy = userId,
        Products = LibDb.Tvp("dbo.T_Product_V2", products)
    })
    .ExecuteAsync(ct);
```

이 경로는 기존 POCO/record를 그대로 받을 수 있어 전환 비용이 낮습니다. 다만 row metadata를 런타임에 해석해야 하므로 Native AOT 또는 매우 잦은 반복 호출에서는 아래 static-shape fast-path를 우선 사용합니다.

### 1-2. 고성능 반복 호출: 등록형 static-shape fast-path

동일 row type과 동일 TVP type을 반복 호출하는 경로는 애플리케이션 시작 시 한 번 등록합니다.
등록된 shape는 컬럼 이름, SQL 타입, precision/scale/size, null 허용 여부, static getter를 고정하므로 reflection 의존도를 제거하고 Native AOT에 가장 적합합니다.

```csharp
using System.Data;
using Lib.Db.Configuration;

builder.Services.AddLibDb(options =>
{
    options.Tvp.Map<ProductRow>("dbo.T_Product_V2")
        .Column("ProductId", SqlDbType.Int, static row => row.ProductId)
        .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100)
        .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
        .Column("CreatedAt", SqlDbType.DateTime2, static row => row.CreatedAt, scale: 7);
});
```

등록 후에는 `EnableAutoTvpBinding` 기본값이 `true`이므로 같은 row type의 `IEnumerable<T>`를 그대로 전달해도 TVP로 바인딩됩니다.

```csharp
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new { RequestedBy = userId, Products = products })
    .ExecuteAsync(ct);
```

### 1-3. 명시 wrapper + 재사용 shape

자동 바인딩보다 호출부에서 TVP임을 드러내고 싶으면 static shape를 직접 만들어 wrapper에 넘깁니다.

```csharp
using System.Data;
using Lib.Db;
using Lib.Db.Execution.Tvp;

static readonly TvpShape<ProductRow> ProductTvpShape = TvpShape.For<ProductRow>()
    .Column("ProductId", SqlDbType.Int, static row => row.ProductId)
    .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100)
    .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
    .Column("CreatedAt", SqlDbType.DateTime2, static row => row.CreatedAt, scale: 7)
    .Build();

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new
    {
        RequestedBy = userId,
        Products = LibDb.Tvp("dbo.T_Product_V2", products, ProductTvpShape)
    })
    .ExecuteAsync(ct);
```

### 1-4. Schema-adaptive descriptor

DB TVP 스키마가 운영 중에 nullable/default-safe 범위에서 확장될 수 있는 경로는 descriptor를 조회해 `Adaptive` 정책을 명시합니다.
필수 컬럼 누락이나 타입 불일치처럼 데이터 손상을 만들 수 있는 변경은 보정하지 않고 실패해야 합니다.

```csharp
using Lib.Db;
using Lib.Db.Execution.Tvp;

TvpSchemaDescriptor descriptor = await session.UseSchema("Default")
    .GetTvpAsync("dbo.T_Product_V2", ct);

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new
    {
        RequestedBy = userId,
        Products = LibDb.Tvp(descriptor, products, TvpBindingPolicy.Adaptive)
    })
    .ExecuteAsync(ct);
```

### 1-5. Legacy compatibility fallback

`[TvpRow]` 관련 타입은 과거 코드 호환과 제한적인 reflection fallback을 위해 남아 있습니다.
New code should use Runtime TVP APIs directly. Native AOT 또는 고빈도 호출 경로에서는 `options.Tvp.Map<T>()` 또는 `TvpShape.For<T>()`를 사용합니다.

| 경로 | 권장 사용처 | AOT 적합성 |
|---|---|---|
| `LibDb.Tvp("dbo.Type", rows)` | 전환 초기, 저빈도 호출, 호출부 명시성 | reflection fallback 경고 가능 |
| `options.Tvp.Map<T>().Column(...)` | 반복 호출, 서비스 전역 fast-path | 권장 |
| `LibDb.Tvp("dbo.Type", rows, shape)` | 호출부 명시성 + static shape | 권장 |
| `LibDb.Tvp(descriptor, rows, Adaptive)` | nullable/default-safe schema drift 대응 | reflection fallback 경고 가능 |
| `[TvpRow]` fallback | Legacy compatibility | 신규 코드 비권장 |

---

## 2. Native AOT 호환성

### 2-1. 프로젝트 설정

```xml
<PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
    <IsTrimmable>true</IsTrimmable>
</PropertyGroup>
```

TVP 입력 경로는 별도 MSBuild source-generator 플래그가 아니라 `options.Tvp.Map<T>()` 또는 `TvpShape.For<T>()`로 static shape를 고정합니다.

### 2-2. Shadow DTO 패턴

AOT 환경에서 `IConfiguration` 바인딩 시 복잡한 타입이 경고를 발생시키는 것을 방지합니다.

1. **LibDbConfig** (Shadow DTO): `appsettings.json`과 1:1 매핑되는 POCO
2. **LibDbOptions** (Runtime): 복잡한 런타임 객체 포함
3. **ApplyTo()**: Shadow DTO 값을 Runtime Options로 수동 매핑

### 2-3. JsonSerializerContext

```csharp
[JsonSerializable(typeof(MyDto))]
public partial class AppJsonContext : JsonSerializerContext;
```

모든 직렬화 대상 타입은 `JsonSerializerContext`에 등록하여 리플렉션을 제거합니다.

---

## 3. 성능 최적화

### 3-1. 보간 SQL 파라미터 처리

공개 Fluent API의 `SqlInterpolated(FormattableString)` 경로는 보간 값 인수를 `@pN` 파라미터로 수집합니다.
SQL 구조 자체를 검증하는 파서는 아니므로 식별자와 절 이름은 allow-list로 선택하세요.

```csharp
int id = 42;
// 보간 값은 @p0 파라미터로 바인딩됨
DbResult<User?> result = await session.Default
    .SqlInterpolated($"SELECT * FROM Users WHERE Id = {id}")
    .QuerySingleAsync<User>();
```

### 3-2. Span<T> & ArrayPool

- 모든 바이트 버퍼: `ArrayPool<byte>.Shared`에서 대여/반납
- 식별자 정규화: `SearchValues<char>` SIMD 가속 + `string.Create`
- TVP 직렬화: `Span<byte>` 기반 직접 기록

### 3-3. sealed 클래스와 TieredPGO

```csharp
// sealed → 가상 메서드 디스패치 제거 → 인라이닝 대상
public sealed class UserRepository(IDbSession session)
{
    // ...
}
```

- `sealed` 클래스: JIT 역가상화(Devirtualization) 활성화
- TieredPGO: 핫 패스를 런타임에 재컴파일하여 최적화
- `SkipLocalsInit`: 로컬 변수 초기화 생략으로 스택 프레임 비용 절감

### 3-4. 성능 모범 사례

| 항목 | 권장 | 비권장 |
|---|---|---|
| SQL 파라미터 | `SqlInterpolated($"...{value}")` 보간 | 문자열 연결 |
| DTO 타입 | `[DbResult] partial record` | 리플렉션 매핑 |
| TVP 전송 | `LibDb.Tvp(..., shape)` 또는 `options.Tvp.Map<T>()` | DataTable 수동 구성 |
| 클래스 선언 | `sealed class` | 비sealed |
| 대량 결과 | `QueryAsync` (스트리밍) | ToList 전체 적재 |

---

## 4. Resilience (회복 탄력성)

### 4-1. Polly v8 파이프라인

`EnableResilience = true` 설정 시 자동으로 활성화됩니다.

```
요청 → [Timeout] → [Retry] → [Circuit Breaker] → DB
```

### 4-2. 재시도 정책

| 설정 | 기본값 | 설명 |
|---|---|---|
| `MaxRetryCount` | 3 | 최대 재시도 횟수 |
| `BaseRetryDelayMs` | 100 | 초기 지연 (ms) |
| `MaxRetryDelayMs` | 2000 | 최대 지연 (ms) |
| `UseRetryJitter` | true | Thunder Herd 방지 |
| `RetryBackoffType` | Exponential | 지수 백오프 |

### 4-3. Circuit Breaker

| 설정 | 기본값 | 설명 |
|---|---|---|
| `CircuitBreakerThreshold` | 5 | 최소 처리량 |
| `CircuitBreakerSamplingDurationMs` | 30000 | 샘플링 기간 |
| `CircuitBreakerBreakDurationMs` | 30000 | 차단 유지 시간 |
| `CircuitBreakerFailureRatio` | 0.5 | 실패율 임계값 |

### 4-4. Deadlock 자동 처리

SQL Server Deadlock(에러 1205)은 Transient 오류로 분류되어 자동 재시도됩니다.
DbResult의 `DbError.Kind`가 `DbErrorKind.Deadlock`으로 매핑됩니다.

### 4-5. appsettings.json 설정

```json
{
  "LibDb": {
    "EnableResilience": true,
    "Resilience": {
      "MaxRetryCount": 3,
      "BaseRetryDelayMs": 100,
      "MaxRetryDelayMs": 2000,
      "UseRetryJitter": true,
      "RetryBackoffType": "Exponential",
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerFailureRatio": 0.5,
      "CircuitBreakerBreakDurationMs": 30000
    }
  }
}
```

---

## 5. 캐싱

### 5-1. Provider-neutral 기본값

Lib.Db는 기본 등록 경로에서 `IDistributedCache` provider를 만들지 않습니다. 애플리케이션이 Redis, SQL Server, Postgres, NCache 등 provider-backed L2를 직접 등록하면 `HybridCache`가 L1 뒤의 L2로 활용할 수 있고, provider가 없으면 프로세스 내 L1/local schema cache로 동작합니다.

`MemoryDistributedCache`는 `IDistributedCache` 인터페이스를 구현하지만 프로세스 로컬 메모리입니다. 운영용 L2로 간주하지 않습니다.

### 5-2. 캐시 계층

```
요청 → L1 (HybridCache / process-local)
       ↓ Miss
       L2 (host-registered IDistributedCache provider, optional)
       ↓ Miss
       DB (SQL Server)
```

- L1 Hit: 프로세스 내 캐시 응답
- L2 Hit: 애플리케이션이 등록한 provider-backed cache 응답
- L2 없음: DB 조회 후 L1/local schema cache만 사용

### 5-3. 스키마 캐싱 (SchemaService)

`SchemaService`는 SP 파라미터 메타데이터를 캐싱하여 매 호출 시 스키마 조회를 방지합니다.

- `SchemaRefreshIntervalSeconds`: 갱신 주기 (기본 60초)
- `PrewarmSchemas`: 앱 시작 시 미리 로드할 스키마
- `PrewarmIncludePatterns` / `PrewarmExcludePatterns`: 선택적 워밍업

### 5-4. SharedMemoryCache opt-in

`SharedMemoryCache`는 동일 호스트 프로세스 간 공유를 위한 고급 opt-in 기능입니다. OS에 따라 자동 활성화되지 않습니다.

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStrings["Main"] = "...";
    options.ConnectionStringNames = ["Main"];
});

builder.Services.AddLibDbSharedMemoryCache();
```

`AddLibDbSharedMemoryCache()`는 `EnableSharedMemoryCache = true`를 최종 옵션에 반영합니다. Redis/SQL/Postgres/NCache 같은 외부 `IDistributedCache` provider와 함께 등록하면 Lib.Db가 fail-fast합니다. SharedMemoryCache는 keyed integrity metadata와 quota enforcement를 사용하며, quota 확인이 불가능하거나 초과된 쓰기는 약한 fallback write로 대체하지 않습니다.

`Microsoft.Extensions.DependencyInjection`은 같은 service type이 여러 번 등록되면 단일 `IDistributedCache` 해석에서 마지막 등록을 사용합니다. 그래서 `AddLibDbSharedMemoryCache()` 호출 뒤에 다른 provider가 추가되는 잘못된 구성은 등록 시점이 아니라 Generic Host 시작 시 `LibDbSharedMemoryCacheStartupValidator`가 전체 `IDistributedCache` 등록을 검사해 실패시킵니다. Generic Host를 시작하지 않는 비호스팅 테스트/도구에서는 이 hosted validator가 자동 실행되지 않으므로, shared-memory opt-in과 외부 provider를 섞지 않는 구성을 별도로 검증해야 합니다.

---

## 6. Always Encrypted 지원

Lib.Db는 SQL Server Always Encrypted를 연결 문자열 수준에서 지원합니다.

### 6-1. 설정 방법

연결 문자열에 다음을 추가합니다:

```
Column Encryption Setting=Enabled
```

`appsettings.json` 예시:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyDb;User Id=app_user;Password=***;Encrypt=True;TrustServerCertificate=False;Column Encryption Setting=Enabled"
  }
}
```

### 6-2. 연결 문자열 검증

`LibDbOptions.IsAlwaysEncryptedEnabled()` 정적 메서드로 런타임에서 Always Encrypted 설정 여부를 확인할 수 있습니다:

```csharp
string connStr = configuration.GetConnectionString("Default")!;
bool isEnabled = LibDbOptions.IsAlwaysEncryptedEnabled(connStr);

if (!isEnabled)
{
    logger.LogWarning("Always Encrypted가 비활성화 상태입니다. 암호화된 열 접근 시 오류가 발생할 수 있습니다.");
}
```

### 6-3. 사용 예시

Always Encrypted가 활성화된 연결에서는 기존 Fluent API를 그대로 사용합니다:

```csharp
// 암호화된 열에 대한 파라미터 바인딩은 ADO.NET이 자동 처리
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_InsertUser")
    .With(new { SSN = "123-45-6789", Name = "홍길동" })
    .ExecuteAsync();
```

### 6-4. 주의사항

| 항목 | 설명 |
|---|---|
| **파라미터 바인딩** | Always Encrypted 열에 대한 파라미터 바인딩은 ADO.NET 드라이버가 자동 처리합니다 |
| **TVP 제약** | TVP 내의 암호화된 열은 SQL Server에서 지원되지 않습니다 |
| **성능 영향** | 암/복호화로 인한 CPU 오버헤드가 발생하며, 첫 연결 시 CMK(Column Master Key) 조회 지연이 있습니다 |
| **인덱스 제약** | 결정적(Deterministic) 암호화만 등호(=) 비교 및 JOIN을 지원합니다 |
| **CMK 저장소** | Windows Certificate Store, Azure Key Vault 등 CMK 제공자 구성이 필요합니다 |

---

## 7. 쿼리 인터셉터 (IDbInterceptor)

DB 명령 실행 전후를 가로채는 사용자 수준 인터셉터입니다. 로깅, 감사, 메트릭, 쿼리 변환 등을 실행 파이프라인에 비침투적으로 삽입할 수 있습니다.

### 7-1. 인터페이스 정의

```csharp
public interface IDbInterceptor
{
    ValueTask<DbInterceptionResult> OnExecutingAsync(
        DbInterceptionContext context, CancellationToken ct);

    ValueTask OnExecutedAsync(
        DbInterceptionContext context, CancellationToken ct);

    ValueTask OnErrorAsync(
        DbInterceptionContext context, CancellationToken ct);
}

public enum DbInterceptionResult
{
    Continue,   // 실행 계속
    Suppress    // 실행 억제 (DB 호출 건너뜀)
}
```

### 7-2. DbInterceptionContext

| 속성 | 타입 | 설명 |
|---|---|---|
| `CommandText` | `string` | SP 이름 또는 SQL 텍스트 |
| `DiagnosticCommandText` | `string?` | 진단/로그용 명령 텍스트. Raw SQL 원문 노출을 피하려면 이 값을 우선 사용 |
| `CommandType` | `CommandType` | 명령 유형 (StoredProcedure / Text) |
| `InstanceName` | `string` | 대상 인스턴스 이름 |
| `StartTime` | `DateTime` | 실행 시작 시각 (UTC) |
| `ElapsedMs` | `long?` | 실행 소요 시간 (밀리초, OnExecuted/OnError에서 설정) |
| `Result` | `object?` | 실행 결과 (OnExecuted에서 설정) |
| `Exception` | `Exception?` | 발생한 예외 (OnError에서 설정) |
| `State` | `Dictionary<string, object?>` | 인터셉터 간 데이터 전달용 |

### 7-3. 구현 및 등록

```csharp
public sealed class SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger) : IDbInterceptor
{
    private const long SlowThresholdMs = 1000;

    public ValueTask<DbInterceptionResult> OnExecutingAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        return ValueTask.FromResult(DbInterceptionResult.Continue);
    }

    public ValueTask OnExecutedAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        if (context.ElapsedMs > SlowThresholdMs)
        {
            logger.LogWarning(
                "[Slow Query] {CommandText} took {ElapsedMs}ms on {Instance}",
                context.DiagnosticCommandText, context.ElapsedMs, context.InstanceName);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        logger.LogError(context.Exception,
            "[DB Error] {CommandText} on {Instance}",
            context.DiagnosticCommandText, context.InstanceName);
        return ValueTask.CompletedTask;
    }
}

// DI 등록 (등록 순서대로 체인 실행)
builder.Services.AddLibDbInterceptor<SlowQueryInterceptor>();
builder.Services.AddLibDbInterceptor<AuditInterceptor>();
```

---

## 8. JSON 컬럼 매핑

DB에 JSON 문자열로 저장된 컬럼(`nvarchar(MAX)`, `json` 타입)을 C# 타입으로 자동 역직렬화합니다.

### 8-1. Dictionary 결과에서 JSON 추출

```csharp
public record ExtraInfo(string Note, int Priority);

DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await session.Default
    .Procedure("dbo.usp_GetTasks")
    .QueryAsync<Dictionary<string, object?>>();

if (result.IsSuccess)
{
    await foreach (Dictionary<string, object?> row in result.Value!)
    {
        ExtraInfo? extra = row.MapJsonColumn<ExtraInfo>("EXTRA_DATA");
        Console.WriteLine($"메모: {extra?.Note}");
    }
}
```

### 8-2. 스트림 전체에 JSON 매핑

```csharp
await foreach ((Dictionary<string, object?> row, ExtraInfo? json) in
    result.Value!.WithJsonColumnAsync<ExtraInfo>("EXTRA_DATA"))
{
    Console.WriteLine($"작업: {row["TaskName"]}, 우선순위: {json?.Priority}");
}
```

### 8-3. 문자열 직접 변환

```csharp
// 역직렬화
string jsonStr = """{"name":"Alice","age":30}""";
UserInfo? info = jsonStr.FromJson<UserInfo>();

// 직렬화
string serialized = info.ToJson();
```

### 8-4. 확장 메서드 목록

| 메서드 | 대상 | 설명 |
|---|---|---|
| `MapJsonColumn<T>()` | `Dictionary<string, object?>` | 단일 행의 JSON 컬럼 역직렬화 |
| `WithJsonColumnAsync<T>()` | `IAsyncEnumerable<Dictionary<string, object?>>` | 스트림 전체에 JSON 매핑 |
| `FromJson<T>()` | `string?` | JSON 문자열 역직렬화 |
| `ToJson<T>()` | `T` | 객체를 JSON 문자열로 직렬화 |

> 모든 메서드는 선택적으로 `JsonSerializerOptions`를 받습니다. 기본값은 Web 옵션(camelCase, 대소문자 무관)입니다.

---

## 9. 쿼리 결과 캐싱

기존 Fluent API에 `.WithCacheAsync()`, `.WithCacheListAsync()`, `.WithHybridCacheAsync()`를 체이닝하여 캐시를 적용합니다.

### 9-1. IDistributedCache 단건 캐싱

```csharp
string userProfileCacheKey = cacheKeys.UserProfile(userId); // opaque app-owned label, not the raw identifier

DbResult<UserDto?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = userId })
    .QuerySingleAsync<UserDto>()
    .WithCacheAsync(cache, userProfileCacheKey, TimeSpan.FromMinutes(5));
```

### 9-2. IDistributedCache 다건 캐싱

스트림을 List로 구체화하여 캐시합니다.

```csharp
DbResult<List<CategoryDto>> result = await session.Default
    .Procedure("dbo.usp_GetCategories")
    .QueryAsync<CategoryDto>()
    .WithCacheListAsync(cache, "categories:all", TimeSpan.FromHours(1));
```

### 9-3. HybridCache (L1 + L2)

```csharp
string productCacheKey = cacheKeys.Product(productId); // opaque app-owned label

DbResult<ProductDto?> result = await session.Default
    .Procedure("dbo.usp_GetProduct")
    .With(new { ProductId = productId })
    .QuerySingleAsync<ProductDto>()
    .WithHybridCacheAsync(
        hybridCache,
        productCacheKey,
        TimeSpan.FromMinutes(30),
        tags: ["entity:product-catalog", "schema:product"]);
```

`tags`는 `RemoveByTagAsync` 기반 논리 무효화를 위한 애플리케이션 소유 라벨입니다. null은 태그 없음을 의미하며, null 요소, 빈 문자열/공백, 앞뒤 공백, wildcard 예약값 `*`, 중복 제거 후 32개 초과 태그는 거부됩니다. 태그에는 사용자 ID, 전자메일, 토큰, 연결 문자열, SQL 원문 같은 민감값을 넣지 마세요.

이 overload는 이미 생성된 `Task<DbResult<T?>>`를 받습니다. 따라서 캐시 히트여도 DB Task 생성, Task 생성 시점의 부작용, 이후 background fault 자체를 막는 lazy factory API가 아닙니다.

Native AOT 또는 trimming 배포에서 provider-backed L2를 사용할 때는 HybridCache payload serializer도 AOT-compatible이어야 합니다. Runtime reflection serializer를 전제로 하지 말고 source-generated metadata 또는 명시 serializer 구성을 사용하세요.

캐시 lookup 또는 factory 실패가 public 예외로 변환될 때 Lib.Db는 `DB query failed.` 같은 일반 메시지를 가진 `InvalidOperationException`을 throw합니다. 원본 SQL, provider exception, row value, cache payload, tenant/user identifier는 public error message나 `InnerException`으로 노출하지 않습니다.

### 9-4. 캐시 무효화

```csharp
await cache.InvalidateCacheAsync(userProfileCacheKey);
await hybridCache.RemoveByTagAsync("entity:product-catalog");
```

`RemoveByTagAsync`는 태그에 연결된 entry의 논리 invalidation을 요청합니다. provider-backed L2가 없는 local-only 구성에서는 현재 프로세스의 HybridCache entry에만 의미가 있습니다. provider-backed L2가 있으면 current-server/L2 가시성은 바뀌지만, 다른 서버가 이미 들고 있는 in-memory L1 entry가 현재 서버 호출만으로 물리적으로 지워진다고 가정하면 안 됩니다.

### 9-5. 캐시 동작 흐름

```
WithCacheAsync 호출
  ├─ 캐시 히트 → 역직렬화 → DbResult.Ok 반환 (DB 미호출)
  └─ 캐시 미스 → 원본 쿼리 실행
       ├─ 성공 → 캐시 저장 → DbResult.Ok 반환
       └─ 실패 → DbResult.Fail 반환 (캐시 저장 안 함)
```

---

## 10. Bulk Mutations (SqlBulkCopy + staged DML)

Lib.Db는 두 종류의 bulk 경로를 제공합니다.

- Legacy reflection `BulkInsertAsync<T>(..., BulkInsertOptions?)`는 기존 호환성을 위해 유지됩니다. 이 overload는 public property reflection을 사용하므로 Native AOT 환경에는 적합하지 않습니다.
- AOT-safe `BulkShape<T>` overload는 column metadata와 getter를 명시하고 `SqlBulkCopy` 및 staged set-based DML을 사용합니다. Insert, update, delete, upsert, merge-like mutation을 지원합니다.

Update/delete/upsert/merge는 stage table에 먼저 적재한 뒤 SQL Server DML로 대상 테이블을 변경합니다. SQL Server `MERGE` statement는 기본 engine으로 사용하지 않습니다. Stage unique index로 duplicate source key를 target DML 전에 거부하며, target key column은 애플리케이션이 소유한 `PRIMARY KEY` 또는 `UNIQUE` 제약/인덱스로 보호되어야 합니다.

### 10-1. 시그니처

```csharp
[RequiresUnreferencedCode("...")]
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkInsertOptions? options = null,
    CancellationToken ct = default) where T : class;

Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default) where T : notnull;

Task<DbResult<long>> BulkUpdateAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default) where T : notnull;

Task<DbResult<long>> BulkDeleteAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default) where T : notnull;

Task<DbResult<BulkUpsertResult>> BulkUpsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkWriteOptions? options = null,
    CancellationToken ct = default) where T : notnull;

Task<DbResult<BulkMergeResult>> BulkMergeAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkShape<T> shape,
    BulkMergeOptions? options = null,
    CancellationToken ct = default) where T : notnull;
```

### 10-2. BulkShape<T>

```csharp
BulkShape<SensorReading> shape = BulkShape.For<SensorReading>()
    .Key("SensorId", SqlDbType.Int, static row => row.SensorId)
    .Column("Value", SqlDbType.Decimal, static row => row.Value, precision: 9, scale: 2)
    .Column("Timestamp", SqlDbType.DateTime2, static row => row.Timestamp, scale: 7)
    .Build();
```

`BulkShape<T>`는 reflection 대신 static getter를 사용합니다. Shape 생성 시 다음 metadata를 검증합니다.

- `decimal` column은 precision/scale을 명시해야 하며 `decimal(18,0)`으로 조용히 fallback하지 않습니다.
- string/binary key column은 유효한 fixed size가 필요하며 `max` size를 사용할 수 없습니다. Non-key string/binary column은 명시 size 또는 `max` 요청을 사용할 수 있습니다.
- temporal column은 SQL Server가 지원하는 scale 범위만 허용합니다.
- CLR `TValue`와 선언한 `SqlDbType`은 호환되어야 하며 enum은 underlying type이 SQL type과 맞아야 합니다.
- Stage key는 32개 column 이하이고, 선언된 key width가 SQL Server index key portability limit인 900 bytes를 넘으면 안 됩니다.

`DateOnly`와 `TimeOnly`는 Runtime TVP 경로와 같은 provider-facing convention으로 정규화됩니다. Bulk reader는 row enumerator를 한 번만 dispose하고 EOF에서 current row state를 지웁니다.

### 10-3. BulkInsertOptions / BulkWriteOptions

| 속성 | 타입 | 기본값 | 설명 |
|---|---|---|---|
| `BatchSize` | `int` | 5,000 | 배치당 행 수 |
| `TimeoutSeconds` | `int` | 600 | 명령 타임아웃 (초) |
| `EnableStreaming` | `bool` | `true` | 스트리밍 활성화 |
| `FireTriggers` | `bool` | `false` | INSERT 트리거 실행 여부 |
| `CheckConstraints` | `bool` | legacy `false`, AOT-safe `true` | 제약 조건 검사 여부 |
| `KeepIdentity` | `bool` | `false` | IDENTITY 값 유지 여부 |
| `UseTransaction` | `bool` | AOT-safe `true` | AOT-safe bulk 작업의 로컬 트랜잭션 사용 여부 |

`FireTriggers`, `CheckConstraints`, `KeepIdentity`는 direct `BulkInsertAsync`의 `SqlBulkCopy` destination flag입니다. Staged update/delete/upsert/merge는 target 변경이 일반 SQL Server DML이므로 `FireTriggers = true`, `KeepIdentity = true`, `CheckConstraints = false` 같은 misleading non-default 값을 연결 open 전에 거부합니다.

Direct AOT-safe insert에서 `UseTransaction = false`는 명시적인 non-atomic 성능 opt-out입니다. provider 실패나 취소가 일부 row 전송 이후 발생하면 partial row가 남을 수 있으므로, 원자성이 필요한 쓰기에서는 기본값을 유지하세요.

### 10-4. 사용 예시

```csharp
DbResult<long> result = await session.BulkInsertAsync(
    "Default",
    "[dbo].[SensorReadings]",
    readings,
    shape,
    new BulkWriteOptions { BatchSize = 10_000, FireTriggers = true });
```

```csharp
DbResult<BulkUpsertResult> result = await session.BulkUpsertAsync(
    "Default",
    "[dbo].[SensorReadings]",
    readings,
    shape);

if (result.IsSuccess)
{
    Console.WriteLine($"inserted={result.Value.Inserted}, updated={result.Value.Updated}");
}
```

`BulkMergeAsync`의 기본 action은 matched row update와 missing row insert이며, 결과는 inserted/updated/deleted count를 분리해서 반환합니다. `DeleteMatched`는 단독 action일 때만 허용되고, `DeleteNotMatchedBySource`는 현재 bulk merge에서 지원하지 않는 action으로 거부됩니다.

Non-cancellation failure는 rollback 이후 redacted `DbResult<T>` 실패로 반환됩니다. Public `DbError`에는 raw SQL, provider exception, row value, payload, connection string value, public `InnerException`을 노출하지 않습니다.

### 10-5. TVP vs Bulk 비교

| 항목 | TVP (Runtime TVP) | Legacy BulkInsert | AOT-safe BulkShape |
|---|---|---|---|
| 적합 건수 | ~수천 건 | 수만~수십만 건 이상 | 수만~수십만 건 이상 |
| AOT 호환 | O (static-shape fast-path) | X (Reflection) | O (explicit shape) |
| SP 통합 | O (파라미터로 전달) | X (직접 테이블 INSERT) | X (직접/staged DML) |
| 트리거/제약조건 | SP 내에서 제어 | 옵션으로 제어 | insert는 bulk-copy flag, staged DML은 SQL Server 기본 동작 |
| 트랜잭션 | SP 트랜잭션 활용 | 내부 트랜잭션 | 기본 로컬 트랜잭션 |

---

## 11. 연결 풀 메트릭

Lib.Db는 OpenTelemetry 기반의 연결 풀 모니터링 메트릭을 제공합니다.

### 11-1. 메트릭 정의 (LibDbTelemetry)

| 메트릭 이름 | 타입 | 단위 | 설명 |
|---|---|---|---|
| `libdb.db_requests_total` | Counter | - | DB 요청 총 횟수 |
| `libdb.db_request_duration_ms` | Histogram | ms | DB 요청 소요 시간 |
| `libdb.connection.acquire_duration_ms` | Histogram | ms | 연결 획득 소요 시간 |
| `libdb.connection.pool_waits` | Counter | - | 연결 풀 대기 횟수 (100ms 이상) |
| `libdb.connection.pool_timeouts` | Counter | - | 연결 풀 타임아웃 횟수 |
| `libdb.cache_requests_total` | Counter | - | 캐시 연산 횟수 |
| `libdb.cache_op_duration_ms` | Histogram | ms | 캐시 연산 소요 시간 |
| `libdb.cache_cleanup_total` | Counter | - | 캐시 정리 사이클 수 |
| `libdb.cache_bytes_freed` | Gauge | bytes | 캐시 정리 시 해제된 바이트 |
| `libdb.cache.bytes_freed` | Counter | bytes | 캐시 정리 이벤트에서 누적 기록된 해제 바이트 |

### 11-2. OpenTelemetry 연동

`EnableObservability = true`이면 Lib.Db ActivitySource/Meter 기반 추적과 메트릭 기록이 활성화됩니다. 일반 `ILogger` 로그는 이 옵션과 별개로 애플리케이션 로깅 설정을 따릅니다.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Lib.Db"); // Lib.Db 메트릭 수집
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource("Lib.Db"); // Lib.Db 트레이스 수집
    });
```

### 11-3. 연결 풀 모니터링 지표 해석

| 지표 | 정상 | 경고 | 조치 |
|---|---|---|---|
| `acquire_duration_ms` | < 10ms | > 100ms | 풀 크기 증가, 연결 누수 확인 |
| `pool_waits` | 0 | 증가 추세 | Max Pool Size 확인 |
| `pool_timeouts` | 0 | > 0 | 연결 누수, 풀 크기, 타임아웃 설정 점검 |
