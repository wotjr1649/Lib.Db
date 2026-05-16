# Lib.Db v2 고급 기능

TVP, AOT, 성능 최적화, Resilience, 캐싱을 다루는 고급 가이드입니다.

---

## 1. TVP (Table-Valued Parameters) & Source Generator

### 1-1. TvpRow 정의 (입력용)

```csharp
using Lib.Db.Contracts.Models;

namespace MyApp.Features.Products;

[TvpRow(TypeName = "dbo.T_Product_V2", UseDatetime2 = true)]
public record ProductRow
{
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### 1-2. DbResult 결과 매핑 (출력용)

```csharp
using Lib.Db.Contracts.Mapping;

namespace MyApp.Features.Products;

[DbResult]
public partial record ProductDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public ProductDto() { }
}
```

### 1-3. DB-First 자동 생성

`libdb.schema.json`에서 TVP 스키마를 정의하면 DTO가 자동 생성됩니다.

```csharp
using Lib.Db.Contracts.Models;

[GenerateTvpFromDb(TvpName = "dbo.T_Product")]
public partial class ProductRow { }
```

### 1-4. TVP 사용 예시

```csharp
List<ProductRow> products =
[
    new() { ProductId = 1, Name = "Laptop", Price = 1200m, CreatedAt = DateTime.UtcNow },
    new() { ProductId = 2, Name = "Mouse", Price = 25.5m, CreatedAt = DateTime.UtcNow }
];

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_InsertProducts")
    .With(new { Products = products })
    .ExecuteAsync();
```

### 1-5. Source Generator 동작 원리

| 어트리뷰트 | Generator | 생성 코드 |
|---|---|---|
| `[TvpRow]` | TvpAccessorGenerator | `TvpRegistry_*.g.cs` (SqlDataRecord 바인딩) |
| `[DbResult]` | ResultAccessorGenerator | `ResultRegistry_*.g.cs` (DbDataReader 매핑) |
| `[GenerateTvpFromDb]` | DbFirstTvpGenerator | DTO 속성 자동 생성 |

**Track 5 하이브리드 알고리즘**:
- **Small (12컬럼 이하)**: `Span.SequenceEqual` 기반 `else-if` 분기
- **Large (12컬럼 초과)**: `FNV-1a` 해시 기반 `switch` 분기 (O(1) 근접)

---

## 2. Native AOT 호환성

### 2-1. 프로젝트 설정

```xml
<PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
    <EnableGeneratedTvpBinder>true</EnableGeneratedTvpBinder>
</PropertyGroup>
```

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
| TVP 전송 | `[TvpRow]` Source Generator | DataTable 수동 구성 |
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

### 5-1. SharedMemoryCache (L2)

`MemoryMappedFile` 기반으로 프로세스 간 캐시를 공유합니다.

- **128 스트라이프 Mutex**: 키별 `XxHash128` 해시로 128개 Mutex를 분산하여 동시성 경합 최소화
- **CRC32 무결성 검증**: 메모리 오염/쓰기 중단 감지
- **자가 치유**: 손상 감지 시 자동 삭제 후 재생성 또는 MemoryCache 폴백

### 5-2. 2단계 캐시 계층

```
요청 → L1 (MemoryCache, 프로세스 내)
       ↓ Miss
       L2 (SharedMemoryCache, MMF 기반 IPC)
       ↓ Miss
       DB (SQL Server)
```

- L1 Hit: 마이크로초 단위 응답
- L2 Hit: 프로세스 간 공유, 네트워크 I/O 없음
- L2 Miss: DB 조회 후 L2 → L1 순으로 자동 저장

### 5-3. 스키마 캐싱 (SchemaService)

`SchemaService`는 SP 파라미터 메타데이터를 캐싱하여 매 호출 시 스키마 조회를 방지합니다.

- `SchemaRefreshIntervalSeconds`: 갱신 주기 (기본 60초)
- `PrewarmSchemas`: 앱 시작 시 미리 로드할 스키마
- `PrewarmIncludePatterns` / `PrewarmExcludePatterns`: 선택적 워밍업

### 5-4. MMF 활성화

`EnableSharedMemoryCache`를 `true`로 설정하거나, `null`(기본값)이면 Windows에서 자동 활성화됩니다. `EnableEpochCoordination`으로 프로세스 간 동기화를 제어합니다.

---

## 6. Always Encrypted 지원

Lib.Db v2는 SQL Server Always Encrypted를 연결 문자열 수준에서 완전 지원합니다.

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
                context.CommandText, context.ElapsedMs, context.InstanceName);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        logger.LogError(context.Exception,
            "[DB Error] {CommandText} on {Instance}",
            context.CommandText, context.InstanceName);
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
DbResult<UserDto?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = 1 })
    .QuerySingleAsync<UserDto>()
    .WithCacheAsync(cache, "user:1", TimeSpan.FromMinutes(5));
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
DbResult<ProductDto?> result = await session.Default
    .Procedure("dbo.usp_GetProduct")
    .With(new { ProductId = 42 })
    .QuerySingleAsync<ProductDto>()
    .WithHybridCacheAsync(hybridCache, "product:42", TimeSpan.FromMinutes(30));
```

### 9-4. 캐시 무효화

```csharp
await cache.InvalidateCacheAsync("user:1");
```

### 9-5. 캐시 동작 흐름

```
WithCacheAsync 호출
  ├─ 캐시 히트 → 역직렬화 → DbResult.Ok 반환 (DB 미호출)
  └─ 캐시 미스 → 원본 쿼리 실행
       ├─ 성공 → 캐시 저장 → DbResult.Ok 반환
       └─ 실패 → DbResult.Fail 반환 (캐시 저장 안 함)
```

---

## 10. BulkInsertAsync (SqlBulkCopy)

`IDbSession.BulkInsertAsync<T>()`는 SqlBulkCopy를 사용하여 대량 INSERT를 수행합니다.

### 10-1. 시그니처

```csharp
[RequiresUnreferencedCode("...")]
Task<DbResult<long>> BulkInsertAsync<T>(
    string instanceName,
    string destinationTable,
    IEnumerable<T> records,
    BulkInsertOptions? options = null,
    CancellationToken ct = default) where T : class;
```

### 10-2. BulkInsertOptions

| 속성 | 타입 | 기본값 | 설명 |
|---|---|---|---|
| `BatchSize` | `int` | 5,000 | 배치당 행 수 |
| `TimeoutSeconds` | `int` | 600 | 명령 타임아웃 (초) |
| `EnableStreaming` | `bool` | `true` | 스트리밍 활성화 |
| `FireTriggers` | `bool` | `false` | INSERT 트리거 실행 여부 |
| `CheckConstraints` | `bool` | `false` | 제약 조건 검사 여부 |
| `KeepIdentity` | `bool` | `false` | IDENTITY 값 유지 여부 |

### 10-3. 사용 예시

```csharp
DbResult<long> result = await session.BulkInsertAsync(
    "Default",
    "[dbo].[SensorReadings]",
    readings,
    new BulkInsertOptions { BatchSize = 10_000, FireTriggers = true });
```

### 10-4. TVP vs BulkInsert 비교

| 항목 | TVP ([TvpRow]) | BulkInsertAsync |
|---|---|---|
| 적합 건수 | ~수천 건 | 수만~수십만 건 이상 |
| AOT 호환 | O (Source Generator) | X (Reflection) |
| SP 통합 | O (파라미터로 전달) | X (직접 테이블 INSERT) |
| 트리거/제약조건 | SP 내에서 제어 | 옵션으로 제어 |
| 트랜잭션 | SP 트랜잭션 활용 | 내부 트랜잭션 |

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

### 11-2. OpenTelemetry 연동

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
