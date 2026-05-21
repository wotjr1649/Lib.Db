# Lib.Db Cookbook

24개 실전 레시피 모음입니다. 각 레시피는 상황 설명, 완전한 코드 예시, 결과 타입, Runtime TVP 주의사항을 포함합니다.

---

## 레시피 1: 단건 조회 (SP)

**상황**: 저장 프로시저로 단일 사용자를 조회합니다.

```csharp
public record UserDto(int Id, string Name, string Email);

DbResult<UserDto?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 42 })
    .QuerySingleAsync<UserDto>();

if (result.IsSuccess && result.Value is UserDto user)
{
    Console.WriteLine($"사용자: {user.Name} ({user.Email})");
}
```

**결과 타입**: `DbResult<UserDto?>`
**주의사항**: 결과가 없으면 `Value`가 `null`인 성공 결과가 반환됩니다. 실패가 아닙니다.

---

## 레시피 2: 다건 스트리밍 조회 (SP)

**상황**: 대량의 주문 목록을 메모리에 한꺼번에 적재하지 않고 스트리밍으로 처리합니다.

```csharp
public record OrderDto(long OrderId, string CustomerName, decimal Amount, DateTime OrderDate);

DbResult<IAsyncEnumerable<OrderDto>> result = await session.Default
    .Procedure("dbo.usp_GetPendingOrders")
    .With(new { StatusCode = "PENDING" })
    .QueryAsync<OrderDto>();

if (result.IsSuccess)
{
    int count = 0;
    await foreach (OrderDto order in result.Value!)
    {
        Console.WriteLine($"주문 #{order.OrderId}: {order.Amount:N0}원");
        count++;
    }
    Console.WriteLine($"총 {count}건 처리 완료");
}
```

**결과 타입**: `DbResult<IAsyncEnumerable<OrderDto>>`
**주의사항**: 스트림은 한 번만 열거할 수 있습니다. 재사용이 필요하면 `ToListAsync()` 또는 `WithCacheListAsync()`를 사용하세요.

---

## 레시피 3: 보간 SQL (값 인수 파라미터화)

**상황**: 간단한 조건 조회를 인라인 SQL로 처리하되, 값 인수를 파라미터로 바인딩합니다.

```csharp
int deptId = 10;
string status = "Active";

DbResult<IAsyncEnumerable<EmployeeDto>> result = await session.Default
    .SqlInterpolated($"SELECT Id, Name, DeptId FROM Employees WHERE DeptId = {deptId} AND Status = {status}")
    .QueryAsync<EmployeeDto>();
// 실제 실행: SELECT Id, Name, DeptId FROM Employees WHERE DeptId = @p0 AND Status = @p1
```

**결과 타입**: `DbResult<IAsyncEnumerable<EmployeeDto>>`
**주의사항**: 보간 SQL은 `SqlInterpolated(...)`를 우선 사용하세요. 일반 `Sql(string)`은 Raw SQL로 처리되므로, 사용자 입력 값은 문자열 결합하지 말고 보간 값 또는 `.With(...)` 파라미터로 전달해야 합니다. 보간 SQL 뒤의 `.With(...)`는 추가 명명 파라미터 병합 용도이며, 자동 생성된 `@pN` 이름과 충돌하면 예외가 발생합니다.

---

## 레시피 4: 스칼라 값 조회

**상황**: 테이블의 행 수, 합계, 최댓값 등 단일 값을 조회합니다.

```csharp
DbResult<int?> countResult = await session.Default
    .SqlInterpolated($"SELECT COUNT(*) FROM Orders WHERE Status = {"PENDING"}")
    .ExecuteScalarAsync<int>();

if (countResult.IsSuccess)
{
    int count = countResult.Value ?? 0;
    Console.WriteLine($"대기 중인 주문: {count}건");
}
```

**결과 타입**: `DbResult<int?>`
**주의사항**: SQL `NULL`을 반환하면 `Value`는 `null`입니다. Nullable 타입(`int?`)으로 받아야 합니다.

---

## 레시피 5: NonQuery 실행

**상황**: INSERT / UPDATE / DELETE를 실행하고 영향받은 행 수를 확인합니다.

```csharp
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_DeactivateExpiredAccounts")
    .With(new { CutoffDate = DateTime.UtcNow.AddDays(-90) })
    .ExecuteAsync();

if (result.IsSuccess)
{
    Console.WriteLine($"비활성화된 계정: {result.Value}건");
}
```

**결과 타입**: `DbResult<int>` (영향받은 행 수)
**주의사항**: SP 내부에서 `SET NOCOUNT ON`이면 `Value`가 `-1`을 반환합니다.

---

## 레시피 6: 다중 결과 셋 (QueryMultiple)

**상황**: 한 번의 SP 호출로 사용자 정보와 주문 목록을 동시에 조회합니다.

```csharp
public record UserInfo(int Id, string Name);
public record OrderInfo(long OrderId, decimal Amount);

DbResult<IMultipleResultReader> result = await session.Default
    .Procedure("dbo.usp_GetUserWithOrders")
    .With(new { UserId = 1 })
    .QueryMultipleAsync();

if (result.IsSuccess)
{
    await using IMultipleResultReader reader = result.Value!;

    UserInfo? user = await reader.ReadSingleAsync<UserInfo>();
    List<OrderInfo> orders = await reader.ReadAsync<OrderInfo>();

    Console.WriteLine($"사용자: {user?.Name}, 주문 {orders.Count}건");
}
```

**결과 타입**: `DbResult<IMultipleResultReader>`
**주의사항**: `ReadAsync` / `ReadSingleAsync`는 반드시 SP가 반환하는 ResultSet 순서대로 호출해야 합니다. C#에서 읽지 않은 추가 ResultSet은 `IMultipleResultReader` dispose 시 정리되므로, SP가 뒤에 진단용 SELECT를 추가해도 호출부가 선언한 ResultSet만 읽을 수 있습니다. 반대로 호출부가 다음 ResultSet을 기대했는데 SP가 더 이상 ResultSet을 반환하지 않으면 `InvalidOperationException`이 발생합니다. ResultSet은 존재하지만 행이 0개인 경우에는 빈 리스트 또는 `default`를 반환합니다. `QueryMultipleAsync`는 MARS가 가능한 연결에서 사용하세요.

---

## 레시피 7: 트랜잭션 (기본 패턴)

**상황**: 주문 삽입과 감사 로그를 하나의 트랜잭션으로 묶습니다.

```csharp
await using IDbTransactionScope tx = await session.BeginTransactionAsync("Default");

DbResult<int> insertResult = await tx
    .Procedure("dbo.usp_InsertOrder")
    .With(new { CustomerId = 100, Amount = 50000m })
    .ExecuteAsync();

if (!insertResult.IsSuccess)
{
    await tx.RollbackAsync();
    return;
}

DbResult<int> logResult = await tx
    .Procedure("dbo.usp_InsertAuditLog")
    .With(new { Action = "OrderCreated", Detail = $"Rows={insertResult.Value}" })
    .ExecuteAsync();

if (logResult.IsSuccess)
{
    DbResult<bool> commitResult = await tx.CommitAsync();
}
// CommitAsync 미호출 시 Dispose에서 자동 롤백
```

**결과 타입**: `IDbTransactionScope`는 `IProcedureStage`를 상속합니다.
**주의사항**: `CommitAsync()`가 호출되지 않은 상태에서 `Dispose`되면 자동 롤백됩니다.

---

## 레시피 8: 트랜잭션 (격리 수준 지정)

**상황**: 재고 차감 시 Serializable 격리 수준으로 동시성 문제를 방지합니다.

```csharp
await using IDbTransactionScope tx = await session.BeginTransactionAsync(
    "Default",
    System.Data.IsolationLevel.Serializable);

DbResult<int?> stock = await tx
    .SqlInterpolated($"SELECT Stock FROM Products WHERE Id = {productId}")
    .ExecuteScalarAsync<int>();

if (stock is { IsSuccess: true, Value: > 0 })
{
    DbResult<int> deduct = await tx
        .Procedure("dbo.usp_DeductStock")
        .With(new { ProductId = productId, Qty = 1 })
        .ExecuteAsync();

    if (deduct.IsSuccess)
    {
        await tx.CommitAsync();
    }
}
```

**결과 타입**: `IDbTransactionScope`
**주의사항**: `Serializable` 격리 수준은 잠금 범위가 넓어 교착 상태(Deadlock) 위험이 증가합니다. 트랜잭션 시간을 최소화하세요.

---

## 레시피 9: 멀티 DB (인스턴스 선택)

**상황**: 주문 DB에서 조회하고 로그 DB에 기록합니다.

```csharp
// appsettings.json: "ConnectionStringNames": ["SalesDb", "LogDb"]

DbResult<IAsyncEnumerable<OrderDto>> orders = await session.Use("SalesDb")
    .Procedure("dbo.usp_GetTodayOrders")
    .QueryAsync<OrderDto>();

DbResult<int> logResult = await session.Use("LogDb")
    .Procedure("dbo.usp_WriteAuditLog")
    .With(new { Message = "주문 조회 실행", CreatedAt = DateTime.UtcNow })
    .ExecuteAsync();
```

**결과 타입**: 각각의 `DbResult<T>`
**주의사항**: `ConnectionStringNames`에 등록되지 않은 이름을 `Use()`에 전달하면 런타임 오류가 발생합니다.

---

## 레시피 10: Ad-hoc 연결 (멀티 테넌트)

**상황**: 테넌트별로 다른 DB 연결 문자열을 동적으로 사용합니다.

```csharp
string tenantConnStr = tenantService.GetConnectionString(tenantId);

DbResult<IAsyncEnumerable<TenantConfig>> result = await session
    .UseConnectionString(tenantConnStr)
    .Sql("SELECT ConfigKey, ConfigValue FROM TenantSettings")
    .QueryAsync<TenantConfig>();
```

**결과 타입**: `DbResult<IAsyncEnumerable<TenantConfig>>`
**주의사항**: `UseConnectionString`은 스키마 캐싱/Resilience 파이프라인이 적용되지만, 연결 문자열이 매번 달라지면 연결 풀 효율이 떨어질 수 있습니다.

---

## 레시피 11: BulkInsertAsync (대량 INSERT)

**상황**: 수만 건의 레코드를 SqlBulkCopy로 고속 삽입합니다.

```csharp
public class SensorReading
{
    public int SensorId { get; init; }
    public double Value { get; init; }
    public DateTime Timestamp { get; init; }
}

List<SensorReading> readings = Enumerable.Range(1, 50_000)
    .Select(i => new SensorReading
    {
        SensorId = i % 100,
        Value = Random.Shared.NextDouble() * 100,
        Timestamp = DateTime.UtcNow
    })
    .ToList();

BulkInsertOptions options = new()
{
    BatchSize = 10_000,
    TimeoutSeconds = 300,
    EnableStreaming = true,
    FireTriggers = false
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

**결과 타입**: `DbResult<long>` (삽입된 행 수)
**주의사항**: Reflection 기반이므로 AOT 환경에서는 사용할 수 없습니다. T의 public property 이름이 대상 테이블 컬럼 이름과 일치해야 합니다.

---

## 레시피 12: TVP (Table-Valued Parameter) 전송

**상황**: Runtime TVP wrapper로 배열 데이터를 스칼라 파라미터와 함께 SP에 전달합니다.

```csharp
using Lib.Db;

public record ProductRow
{
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public DateTime CreatedAt { get; init; }
}

List<ProductRow> products =
[
    new() { ProductId = 1, Name = "Laptop", Price = 1_200_000m, CreatedAt = DateTime.UtcNow },
    new() { ProductId = 2, Name = "Mouse", Price = 25_000m, CreatedAt = DateTime.UtcNow }
];

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new
    {
        RequestedBy = "system",
        Products = LibDb.Tvp("dbo.T_Product_V2", products)
    })
    .ExecuteAsync();
```

반복 호출이 많은 서비스는 시작 시 static shape를 등록하고 같은 row type을 그대로 전달합니다.

```csharp
using System.Data;

builder.Services.AddLibDb(options =>
{
    options.Tvp.Map<ProductRow>("dbo.T_Product_V2")
        .Column("ProductId", SqlDbType.Int, static row => row.ProductId)
        .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100)
        .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2)
        .Column("CreatedAt", SqlDbType.DateTime2, static row => row.CreatedAt, scale: 7);
});
```

**결과 타입**: `DbResult<int>`
**주의사항**: TVP 타입 이름은 DB에 생성된 TVP type과 정확히 일치해야 합니다. New code does not require `[TvpRow]` or a separate generator package; Native AOT 또는 고빈도 경로는 `options.Tvp.Map<T>()`를 권장합니다.

---

## 레시피 13: Dictionary 결과로 동적 조회

**상황**: DTO 정의 없이 동적으로 쿼리 결과를 처리합니다.

```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await session.Default
    .Sql("SELECT TOP 10 * FROM sys.tables")
    .QueryAsync<Dictionary<string, object?>>();

if (result.IsSuccess)
{
    await foreach (Dictionary<string, object?> row in result.Value!)
    {
        string? name = row["name"]?.ToString();
        Console.WriteLine($"테이블: {name}");
    }
}
```

**결과 타입**: `DbResult<IAsyncEnumerable<Dictionary<string, object?>>>`
**주의사항**: 컬럼 이름은 대소문자를 구분합니다. SQL 결과의 정확한 컬럼명을 사용하세요.

---

## 레시피 14: WithTimeout (장시간 쿼리)

**상황**: 무거운 조회 SP에 대해 기본 타임아웃(30초)을 늘립니다.

```csharp
DbResult<IAsyncEnumerable<ReportRow>> result = await session.Default
    .Procedure("dbo.usp_GenerateMonthlyReport")
    .WithTimeout(300) // 5분
    .With(new { Year = 2026, Month = 3 })
    .QueryAsync<ReportRow>();
```

**결과 타입**: `DbResult<IAsyncEnumerable<ReportRow>>`
**주의사항**: `WithTimeout`은 `With` 전에 호출해야 합니다. `IParameterStage`에서만 사용 가능합니다.

---

## 레시피 15: Deconstruct 패턴 매칭

**상황**: DbResult를 분해하여 간결하게 성공/실패를 처리합니다.

```csharp
(bool success, User? user, DbError? error) = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

if (!success)
{
    string message = error!.Value.Kind switch
    {
        DbErrorKind.Timeout => "시간 초과",
        DbErrorKind.ConnectionLost => "연결 끊김",
        DbErrorKind.SchemaNotFound => $"SP 미발견: {error.Value.ObjectName}",
        _ => error.Value.Message
    };
    logger.LogError("DB 오류: {Kind} - {Message}", error.Value.Kind, message);
    return;
}

Console.WriteLine($"조회 성공: {user?.Name}");
```

**결과 타입**: `(bool, T?, DbError?)`
**주의사항**: `error`는 `DbError?` (nullable struct)이므로, `error!.Value.Kind`로 접근합니다.

---

## 레시피 16: IsTransient 기반 수동 재시도

**상황**: Resilience 파이프라인을 사용하지 않고 수동으로 일시적 오류를 재시도합니다.

```csharp
int maxRetries = 3;
DbResult<int> result = default;

for (int attempt = 0; attempt <= maxRetries; attempt++)
{
    result = await session.Default
        .Procedure("dbo.usp_ProcessPayment")
        .With(new { OrderId = 100, Amount = 50000m })
        .ExecuteAsync();

    if (result.IsSuccess || !result.Error!.Value.IsTransient)
        break;

    if (attempt < maxRetries)
    {
        int delayMs = (int)Math.Pow(2, attempt) * 100; // 지수 백오프
        await Task.Delay(delayMs);
    }
}

if (!result.IsSuccess)
{
    Console.WriteLine($"최종 실패: {result.Error!.Value.Kind} - {result.Error.Value.Message}");
}
```

**결과 타입**: `DbResult<int>`
**주의사항**: 프로덕션에서는 `EnableResilience = true` 설정으로 Polly 파이프라인 자동 재시도를 권장합니다.

---

## 레시피 17: 쿼리 결과 캐싱 (IDistributedCache)

**상황**: 자주 조회하지만 변경이 드문 데이터를 캐시합니다.

```csharp
// DI: IDistributedCache cache (MemoryDistributedCache, Redis 등)

DbResult<UserDto?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = 1 })
    .QuerySingleAsync<UserDto>()
    .WithCacheAsync(cache, "user:1", TimeSpan.FromMinutes(5));

if (result.IsSuccess)
{
    Console.WriteLine($"사용자: {result.Value?.Name} (캐시 또는 DB)");
}

// 데이터 변경 후 캐시 무효화
await cache.InvalidateCacheAsync("user:1");
```

**결과 타입**: `DbResult<UserDto?>`
**주의사항**: 캐시 히트 시 DB 호출이 발생하지 않습니다. 데이터 변경 후 반드시 `InvalidateCacheAsync`로 캐시를 삭제하세요.

---

## 레시피 18: 다건 결과 캐싱 (WithCacheListAsync)

**상황**: 카테고리 목록 등 다건 스트리밍 결과를 List로 캐시합니다.

```csharp
DbResult<List<CategoryDto>> result = await session.Default
    .Procedure("dbo.usp_GetAllCategories")
    .QueryAsync<CategoryDto>()
    .WithCacheListAsync(cache, "categories:all", TimeSpan.FromHours(1));

if (result.IsSuccess)
{
    foreach (CategoryDto category in result.Value!)
    {
        Console.WriteLine($"카테고리: {category.Name}");
    }
}
```

**결과 타입**: `DbResult<List<CategoryDto>>`
**주의사항**: 스트림을 List로 구체화하여 캐시하므로, 매우 큰 결과 셋은 메모리 사용량에 주의하세요.

---

## 레시피 19: HybridCache 캐싱

**상황**: L1(메모리) + L2(분산) 2계층 캐시를 활용합니다.

```csharp
// DI: HybridCache hybridCache

DbResult<ProductDto?> result = await session.Default
    .Procedure("dbo.usp_GetProduct")
    .With(new { ProductId = 42 })
    .QuerySingleAsync<ProductDto>()
    .WithHybridCacheAsync(hybridCache, "product:42", TimeSpan.FromMinutes(30));
```

**결과 타입**: `DbResult<ProductDto?>`
**주의사항**: HybridCache 팩토리에서 예외 발생 시 캐시되지 않습니다. DB 쿼리 실패 시 `InvalidOperationException`이 발생합니다.

---

## 레시피 20: JSON 컬럼 매핑

**상황**: DB의 `nvarchar(MAX)` JSON 컬럼을 C# 타입으로 역직렬화합니다.

```csharp
public record ExtraInfo(string Note, int Priority);

DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await session.Default
    .Procedure("dbo.usp_GetTasksWithExtra")
    .QueryAsync<Dictionary<string, object?>>();

if (result.IsSuccess)
{
    await foreach ((Dictionary<string, object?> row, ExtraInfo? extra) in
        result.Value!.WithJsonColumnAsync<ExtraInfo>("EXTRA_DATA"))
    {
        string taskName = row["TaskName"]?.ToString() ?? "";
        string note = extra?.Note ?? "(없음)";
        Console.WriteLine($"작업: {taskName}, 메모: {note}");
    }
}
```

**결과 타입**: `IAsyncEnumerable<(Dictionary<string, object?>, ExtraInfo?)>`
**주의사항**: JSON 컬럼 값이 `null`이거나 빈 문자열이면 `default(T)`를 반환합니다. 유효하지 않은 JSON이면 `JsonException`이 발생합니다.

---

## 레시피 21: 쿼리 인터셉터 (감사/로깅)

**상황**: 모든 DB 호출을 로깅하는 인터셉터를 구현합니다.

```csharp
public sealed class AuditInterceptor(ILogger<AuditInterceptor> logger) : IDbInterceptor
{
    public ValueTask<DbInterceptionResult> OnExecutingAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        logger.LogInformation(
            "[DB 실행] {CommandType}: {CommandText} on {Instance}",
            context.CommandType, context.DiagnosticCommandText, context.InstanceName);
        return ValueTask.FromResult(DbInterceptionResult.Continue);
    }

    public ValueTask OnExecutedAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        logger.LogInformation(
            "[DB 완료] {CommandText} - {ElapsedMs}ms",
            context.DiagnosticCommandText, context.ElapsedMs);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(
        DbInterceptionContext context, CancellationToken ct)
    {
        logger.LogError(context.Exception,
            "[DB 오류] {CommandText} - {ElapsedMs}ms",
            context.DiagnosticCommandText, context.ElapsedMs);
        return ValueTask.CompletedTask;
    }
}

// DI 등록
builder.Services.AddLibDbInterceptor<AuditInterceptor>();
```

**결과 타입**: (인터셉터는 결과를 반환하지 않음)
**주의사항**: 로그에는 `DiagnosticCommandText`를 우선 사용하세요. `CommandText`는 Raw SQL 원문일 수 있습니다. `OnExecutingAsync`에서 `DbInterceptionResult.Suppress`를 반환하면 실제 DB 호출이 건너뛰어집니다. 다중 인터셉터는 DI 등록 순서대로 체인 실행됩니다.

---

## 레시피 22: 파라미터 없는 SP 호출

**상황**: 매개변수가 없는 SP를 호출합니다. `IParameterStage`는 `IExecutionStage<object>`를 상속하므로 `.With()` 없이 바로 실행할 수 있습니다.

```csharp
DbResult<IAsyncEnumerable<SystemInfoDto>> result = await session.Default
    .Procedure("dbo.usp_GetSystemInfo")
    .QueryAsync<SystemInfoDto>();
```

**결과 타입**: `DbResult<IAsyncEnumerable<SystemInfoDto>>`
**주의사항**: SP에 필수 매개변수가 있는데 `.With()`를 생략하면, `DbErrorKind.ParameterMismatch` 오류가 반환됩니다.

---

## 레시피 23: DbResult 에러 로깅 유틸리티

**상황**: 반복되는 에러 처리 로직을 유틸리티 메서드로 추출합니다.

```csharp
public static class DbResultExtensions
{
    public static void LogIfFailed<T>(
        this DbResult<T> result,
        ILogger logger,
        string operationName)
    {
        if (result.IsSuccess)
            return;

        DbError error = result.Error!.Value;

        if (error.IsTransient)
        {
            logger.LogWarning(
                "[{Op}] 일시적 오류 ({Kind}): {Message} | SQL={Code} | Hint={Hint}",
                operationName, error.Kind, error.Message,
                error.SqlErrorCode, error.Hint);
        }
        else
        {
            logger.LogError(
                "[{Op}] 영구 오류 ({Kind}): {Message} | SQL={Code} | Object={Obj} | Hint={Hint}",
                operationName, error.Kind, error.Message,
                error.SqlErrorCode, error.ObjectName, error.Hint);
        }
    }
}

// 사용
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_ProcessOrder")
    .With(new { OrderId = 1 })
    .ExecuteAsync();

result.LogIfFailed(logger, "주문 처리");
```

**결과 타입**: (확장 메서드, void)
**주의사항**: 이 유틸리티는 Lib.Db에 포함되지 않은 사용자 코드입니다. 프로젝트 상황에 맞게 커스터마이즈하세요.

---

## 레시피 24: 전체 파이프라인 조합 (실전 예시)

**상황**: 멀티 DB + 트랜잭션 + TVP + 캐시 무효화 + 에러 처리를 결합한 실전 시나리오입니다.

```csharp
using Lib.Db;

public sealed class OrderService(
    IDbSession session,
    IDistributedCache cache,
    ILogger<OrderService> logger)
{
    public async Task<DbResult<long>> CreateOrderAsync(
        int customerId,
        List<OrderItemRow> items)
    {
        // 1. 재고 확인 (SalesDb)
        DbResult<IAsyncEnumerable<StockInfo>> stockResult = await session.Use("SalesDb")
            .Procedure("dbo.usp_CheckStock")
            .With(new { Items = LibDb.Tvp("dbo.T_OrderItem", items) })
            .QueryAsync<StockInfo>();

        if (!stockResult.IsSuccess)
        {
            stockResult.LogIfFailed(logger, "재고 확인");
            return DbResult<long>.Fail(stockResult.Error!.Value);
        }

        // 2. 트랜잭션으로 주문 생성
        await using IDbTransactionScope tx = await session.BeginTransactionAsync("SalesDb");

        DbResult<long?> orderIdResult = await tx
            .Procedure("dbo.usp_CreateOrder")
            .With(new
            {
                CustomerId = customerId,
                Items = LibDb.Tvp("dbo.T_OrderItem", items)
            })
            .ExecuteScalarAsync<long>();

        if (!orderIdResult.IsSuccess || orderIdResult.Value is not long orderId)
        {
            await tx.RollbackAsync();
            return DbResult<long>.Fail(orderIdResult.Error ?? new DbError
            {
                Kind = DbErrorKind.Unknown,
                Message = "주문 ID를 반환받지 못했습니다."
            });
        }

        // 3. 감사 로그 (LogDb, 트랜잭션 외부)
        DbResult<int> logResult = await session.Use("LogDb")
            .Procedure("dbo.usp_WriteAuditLog")
            .With(new { Action = "OrderCreated", EntityId = orderId })
            .ExecuteAsync();

        logResult.LogIfFailed(logger, "감사 로그");

        // 4. 커밋
        DbResult<bool> commitResult = await tx.CommitAsync();
        if (!commitResult.IsSuccess)
        {
            return DbResult<long>.Fail(commitResult.Error!.Value);
        }

        // 5. 캐시 무효화
        await cache.InvalidateCacheAsync($"customer:{customerId}:orders");

        return DbResult<long>.Ok(orderId);
    }
}
```

**결과 타입**: `DbResult<long>` (생성된 주문 ID)
**주의사항**:
- 트랜잭션은 `SalesDb`에만 적용됩니다. `LogDb` 기록은 트랜잭션 외부이므로, 주문 롤백 시에도 로그는 남을 수 있습니다.
- `OrderItemRow`는 `LibDb.Tvp("dbo.T_OrderItem", items)`로 명시 wrapper를 만들거나, 시작 시 `options.Tvp.Map<OrderItemRow>("dbo.T_OrderItem")`로 등록합니다.
- 캐시 무효화는 커밋 성공 후에 수행합니다.
