# Lib.Db v2 Fluent API 레퍼런스

Fluent API 호출 체인, 단계별 메서드, 실행 메서드 매트릭스, 파라미터 바인딩, DbResult 패턴 매칭, DbErrorKind 매핑표, 확장 메서드를 한 곳에서 참조하는 완전한 레퍼런스입니다.

---

## 1. 호출 체인 다이어그램

```
IDbSession (진입점, Scoped DI)
|
+-- .Default -----------------> IProcedureStage (기본 인스턴스)
+-- .Use("name") -------------> IProcedureStage (명명된 인스턴스)
+-- .UseConnectionString("cs") -> IProcedureStage (Ad-hoc 연결)
+-- .BeginTransactionAsync("name")
|   +--> IDbTransactionScope (IProcedureStage 상속)
|        +-- .CommitAsync()  -> Task<DbResult<bool>>
|        +-- .RollbackAsync() -> Task<DbResult<bool>>
|        +-- (Dispose 시 자동 롤백)
+-- .BeginTransactionAsync("name", IsolationLevel)
|   +--> IDbTransactionScope (격리 수준 지정)
+-- .BulkInsertAsync<T>(...) -> Task<DbResult<long>>
    (SqlBulkCopy, Reflection 기반, AOT 비호환)

IProcedureStage (1단계: 명령 선택)
|
+-- .Procedure("sp_name") ----> IParameterStage
+-- .Sql("raw sql") ----------> IParameterStage
+-- .SqlInterpolated($"interpolated {v}") -> IParameterStage (자동 파라미터화)

IParameterStage (2단계: 파라미터/옵션) [IExecutionStage<object> 상속]
|
+-- .With<T>(parameters) -----> IExecutionStage<T>
+-- .WithTimeout(seconds) ----> IParameterStage (체이닝)
+-- (파라미터 없이 바로 실행 가능 -- IExecutionStage<object> 상속)

IExecutionStage<TParams> (3단계: 실행)
|
+-- .QueryAsync<TResult>() ----------> Task<DbResult<IAsyncEnumerable<TResult>>>
+-- .QuerySingleAsync<TResult>() ----> Task<DbResult<TResult?>>
+-- .ExecuteScalarAsync<TScalar>() --> Task<DbResult<TScalar?>>
+-- .QueryMultipleAsync() -----------> Task<DbResult<IMultipleResultReader>>
+-- .ExecuteAsync() -----------------> Task<DbResult<int>>
```

---

## 2. 단계별 메서드 전체 목록

### 2-1. IDbSession (진입점)

```csharp
public interface IDbSession : IAsyncDisposable
{
    // 인스턴스 선택
    IProcedureStage Use(string instanceName);
    IProcedureStage UseConnectionString(string connectionString);
    IProcedureStage Default { get; }

    // 벌크 연산 (Reflection, AOT 비호환)
    [RequiresUnreferencedCode("...")]
    Task<DbResult<long>> BulkInsertAsync<T>(
        string instanceName,
        string destinationTable,
        IEnumerable<T> records,
        BulkInsertOptions? options = null,
        CancellationToken ct = default) where T : class;

    // 트랜잭션
    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        CancellationToken ct = default);

    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        System.Data.IsolationLevel isolationLevel,
        CancellationToken ct = default);
}
```

### 2-2. IDbTransactionScope (트랜잭션)

```csharp
public interface IDbTransactionScope : IProcedureStage, IAsyncDisposable
{
    Task<DbResult<bool>> CommitAsync(CancellationToken ct = default);
    Task<DbResult<bool>> RollbackAsync(CancellationToken ct = default);
}
```

### 2-3. IProcedureStage (1단계)

```csharp
public interface IProcedureStage
{
    IParameterStage Procedure(string spName);
    IParameterStage Sql(string sqlText);
    IParameterStage Sql(FormattableString sql);
    IParameterStage SqlInterpolated(FormattableString sql)
        => Sql(sql);
}
```

`Sql(string)`은 Raw SQL 텍스트를 그대로 전달합니다. 사용자 입력 값은 문자열 결합하지 말고 `SqlInterpolated(FormattableString)`, `Sql(FormattableString)` 또는 `.With(...)` 파라미터로 전달하세요.
`SqlInterpolated(FormattableString)`는 외부 구현체 호환성을 위해 default interface method로 제공되며, 기본 동작은 `Sql(FormattableString)` 위임입니다.

### 2-4. IParameterStage (2단계)

```csharp
public interface IParameterStage : IExecutionStage<object>
{
    IExecutionStage<TParams> With<TParams>(TParams parameters);
    IParameterStage WithTimeout(int timeoutSeconds);
}
```

### 2-5. IExecutionStage\<TParams> (3단계)

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

### 2-6. IMultipleResultReader

```csharp
public interface IMultipleResultReader : IAsyncDisposable
{
    Task<List<T>> ReadAsync<T>(CancellationToken ct = default);
    Task<T?> ReadSingleAsync<T>(CancellationToken ct = default);
}
```

---

## 3. 실행 메서드별 TResult 지원 타입 매트릭스

| 메서드 | `Dictionary<string,object?>` | Record/Class (DTO) | Primitive (`int`, `string` 등) | `DataTable` |
|---|:---:|:---:|:---:|:---:|
| `QueryAsync<TResult>()` | O | O | O | X |
| `QuerySingleAsync<TResult>()` | O | O | O | X |
| `ExecuteScalarAsync<TScalar>()` | X | X | O | X |
| `QueryMultipleAsync()` | (ReadAsync로 사용) | (ReadAsync로 사용) | (ReadSingleAsync로 사용) | X |
| `ExecuteAsync()` | (반환: int) | (반환: int) | (반환: int) | X |

**DataTable은 직접 지원하지 않습니다.** Source Generator 기반 매핑 파이프라인은 `DbDataReader` -> `IAsyncEnumerable<T>` 변환에 최적화되어 있으며, `DataTable.Load()` 패턴은 AOT 호환성과 Zero-Allocation 원칙에 맞지 않아 의도적으로 제외되었습니다.

---

## 4. DataTable 변환 워크어라운드

`Dictionary<string, object?>` 결과를 `DataTable`로 변환하는 패턴입니다.

```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await session.Default
    .Procedure("dbo.usp_GetReport")
    .With(new { Year = 2026 })
    .QueryAsync<Dictionary<string, object?>>();

if (!result.IsSuccess)
    return;

DataTable dt = new();
await foreach (Dictionary<string, object?> row in result.Value!)
{
    if (dt.Columns.Count == 0)
    {
        foreach (string key in row.Keys)
        {
            dt.Columns.Add(key);
        }
    }

    DataRow dr = dt.NewRow();
    foreach (KeyValuePair<string, object?> kv in row)
    {
        dr[kv.Key] = kv.Value ?? DBNull.Value;
    }
    dt.Rows.Add(dr);
}
```

> **주의**: 이 패턴은 모든 행을 메모리에 적재하므로, 대량 데이터에서는 스트리밍(`await foreach`) 방식을 권장합니다.

---

## 5. 파라미터 바인딩 6가지 방식

### 5-1. 익명 타입

가장 일반적인 방식입니다. 속성 이름이 SP 매개변수 이름(`@`제외)과 일치해야 합니다.

```csharp
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpdateUser")
    .With(new { Id = 1, Name = "Alice", Age = 30 })
    .ExecuteAsync();
```

### 5-2. DTO / Record 클래스

재사용 가능한 파라미터 객체입니다.

```csharp
public record UpdateUserParams(int Id, string Name, int Age);

UpdateUserParams param = new(Id: 1, Name: "Alice", Age: 30);

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpdateUser")
    .With(param)
    .ExecuteAsync();
```

### 5-3. Dictionary\<string, object?>

런타임에 동적으로 파라미터를 구성할 때 사용합니다.

```csharp
Dictionary<string, object?> param = new()
{
    ["Id"] = 1,
    ["Name"] = "Alice",
    ["Age"] = 30
};

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpdateUser")
    .With(param)
    .ExecuteAsync();
```

### 5-4. 보간 SQL (값 인수 자동 파라미터화)

`SqlInterpolated(FormattableString)` 또는 `Sql(FormattableString)`를 사용하면 보간 값 인수가 `@p0`, `@p1`, ... 파라미터로 변환됩니다.
이 경로는 값 기반 SQL injection 위험을 줄이지만, SQL 구조 전체를 검증하는 파서는 아닙니다.
테이블명, 컬럼명, 정렬 방향 같은 식별자/구문 조각은 사용자 입력을 직접 조립하지 말고 allow-list로 선택하세요.
보간 SQL 뒤의 `.With(...)`는 수동 명명 파라미터를 추가로 병합할 때만 사용하세요. 자동 생성된 `@pN` 이름과 충돌하면 예외가 발생합니다.

```csharp
int userId = 42;
string name = "Alice";

DbResult<User?> result = await session.Default
    .SqlInterpolated($"SELECT * FROM Users WHERE Id = {userId} AND Name = {name}")
    .QuerySingleAsync<User>();
// 실제 실행 SQL: SELECT * FROM Users WHERE Id = @p0 AND Name = @p1
```

### 5-5. TVP (Table-Valued Parameter)

Source Generator가 `[TvpRow]` 어트리뷰트를 통해 `SqlDataRecord` 바인딩 코드를 자동 생성합니다.

```csharp
List<ProductRow> products =
[
    new() { ProductId = 1, Name = "Laptop", Price = 1200m },
    new() { ProductId = 2, Name = "Mouse", Price = 25.5m }
];

DbResult<int> result = await session.Default
    .Procedure("dbo.usp_InsertProducts")
    .With(new { Products = products })
    .ExecuteAsync();
```

### 5-6. 파라미터 없이 실행

`IParameterStage`가 `IExecutionStage<object>`를 상속하므로, `.With()` 호출 없이 바로 실행할 수 있습니다.

```csharp
DbResult<IAsyncEnumerable<Category>> result = await session.Default
    .Procedure("dbo.usp_GetAllCategories")
    .QueryAsync<Category>();
```

---

## 6. DbResult\<T> 패턴 매칭 4가지

### 6-1. if/else (기본)

```csharp
DbResult<User?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

if (result.IsSuccess)
{
    User? user = result.Value;
    Console.WriteLine($"사용자: {user?.Name}");
}
else
{
    DbError error = result.Error!.Value;
    Console.WriteLine($"오류: {error.Message}");
}
```

### 6-2. Deconstruct

```csharp
(bool success, User? user, DbError? error) = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

if (success)
{
    Console.WriteLine($"사용자: {user?.Name}");
}
else
{
    Console.WriteLine($"오류: {error!.Value.Message}");
}
```

### 6-3. switch expression (DbErrorKind 분기)

```csharp
DbResult<User?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

if (!result.IsSuccess)
{
    string message = result.Error!.Value.Kind switch
    {
        DbErrorKind.Timeout => "쿼리 시간 초과 - CommandTimeout을 늘려 주세요.",
        DbErrorKind.ConnectionLost => "DB 연결 끊김 - 네트워크를 확인하세요.",
        DbErrorKind.SchemaNotFound => $"SP를 찾을 수 없음: {result.Error.Value.ObjectName}",
        DbErrorKind.Deadlock => "교착 상태 발생 - 재시도합니다.",
        DbErrorKind.ConstraintViolation => "제약 조건 위반 - 데이터를 확인하세요.",
        _ => result.Error.Value.Message
    };
    Console.WriteLine(message);
}
```

### 6-4. IsTransient 기반 재시도 판단

```csharp
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_ProcessOrder")
    .With(new { OrderId = 100 })
    .ExecuteAsync();

if (!result.IsSuccess && result.Error!.Value.IsTransient)
{
    // 일시적 오류 -> 자동 재시도 대상
    // Polly Resilience 파이프라인이 활성화된 경우 자동 처리됨
    Console.WriteLine($"일시적 오류 발생, 재시도 가능: {result.Error.Value.Message}");
}
else if (!result.IsSuccess)
{
    // 영구적 오류 -> 즉시 실패 처리
    Console.WriteLine($"영구 오류: {result.Error!.Value.Kind} - {result.Error.Value.Message}");
    Console.WriteLine($"힌트: {result.Error.Value.Hint}");
}
```

---

## 7. DbErrorKind 16종 + SQL 에러코드 매핑표 (44개)

### 7-1. 오류 분류 요약

| DbErrorKind | 설명 | Transient | SQL 코드 수 |
|---|---|:---:|:---:|
| `None` | 미지정 | - | 0 |
| `SchemaNotFound` | 스키마 객체 미발견 | X | 5 |
| `AuthenticationFailed` | DB 인증 실패 | X | 3 |
| `ConnectionLost` | 연결 끊김 | O | 5 |
| `Timeout` | 시간 초과 | O | 2 |
| `Deadlock` | 교착 상태 | O | 1 |
| `ConstraintViolation` | 제약 조건 위반 | X | 4 |
| `DataConversion` | 데이터 변환 오류 | X | 5 |
| `ParameterMismatch` | 매개변수 불일치 | X | 2 |
| `PermissionDenied` | 권한 부족 | X | 3 |
| `ResourceExhausted` | 리소스 부족 | O | 4 |
| `TransactionAborted` | 트랜잭션 중단 | X | 3 |
| `QuerySyntax` | 쿼리 구문 오류 | X | 4 |
| `UserDefined` | 사용자 정의 (50000+) | X | (동적) |
| `CloudTransient` | 클라우드 일시적 오류 | O | 6 |
| `Unknown` | 분류 불가 | - | (나머지) |

### 7-2. SQL 에러코드 전체 매핑표

| SQL Code | DbErrorKind | Transient | 메시지 |
|---:|---|:---:|---|
| **SchemaNotFound** ||||
| 207 | SchemaNotFound | X | 열 이름이 유효하지 않습니다 |
| 208 | SchemaNotFound | X | 개체 이름이 유효하지 않습니다 |
| 209 | SchemaNotFound | X | 열 이름이 모호합니다 |
| 2727 | SchemaNotFound | X | 인덱스를 찾을 수 없습니다 |
| 2812 | SchemaNotFound | X | 저장 프로시저를 찾을 수 없습니다 |
| **AuthenticationFailed** ||||
| 916 | AuthenticationFailed | X | 데이터베이스 액세스 불가 |
| 4060 | AuthenticationFailed | X | 데이터베이스를 열 수 없습니다 |
| 18456 | AuthenticationFailed | X | DB 로그인 실패 |
| **ConnectionLost** ||||
| 64 | ConnectionLost | O | 네트워크 연결 끊김 |
| 233 | ConnectionLost | O | 전송 수준 오류 |
| 10053 | ConnectionLost | O | 소프트웨어에 의한 연결 중단 |
| 10054 | ConnectionLost | O | 원격 호스트에 의한 강제 종료 |
| 10060 | ConnectionLost | O | 연결 시간 초과 |
| **Timeout** ||||
| -2 | Timeout | O | 쿼리 실행 제한 시간 초과 |
| 1222 | Timeout | O | 잠금 요청 시간 초과 |
| **Deadlock** ||||
| 1205 | Deadlock | O | 교착 상태 감지 |
| **ConstraintViolation** ||||
| 515 | ConstraintViolation | X | NOT NULL 위반 |
| 547 | ConstraintViolation | X | FK 제약 조건 위반 |
| 2601 | ConstraintViolation | X | 고유 인덱스 중복 키 |
| 2627 | ConstraintViolation | X | PK/UNIQUE 제약 조건 위반 |
| **DataConversion** ||||
| 245 | DataConversion | X | 데이터 형식 변환 실패 |
| 2628 | DataConversion | X | 문자열/이진 데이터 잘림 (상세) |
| 8115 | DataConversion | X | 산술 오버플로 |
| 8134 | DataConversion | X | 0으로 나누기 |
| 8152 | DataConversion | X | 문자열 데이터 잘림 |
| **ParameterMismatch** ||||
| 201 | ParameterMismatch | X | 필수 매개변수 누락 |
| 8144 | ParameterMismatch | X | 매개변수 초과 지정 |
| **PermissionDenied** ||||
| 229 | PermissionDenied | X | 실행 권한 거부 |
| 230 | PermissionDenied | X | SELECT 권한 거부 |
| 297 | PermissionDenied | X | 원격 서버 액세스 거부 |
| **ResourceExhausted** ||||
| 701 | ResourceExhausted | O | 시스템 메모리 부족 |
| 1105 | ResourceExhausted | O | 디스크 공간 부족 |
| 1138 | ResourceExhausted | O | 인덱스 엔트리 최대 길이 초과 |
| 8645 | ResourceExhausted | O | 메모리 부여 대기 시간 초과 |
| **TransactionAborted** ||||
| 266 | TransactionAborted | X | 트랜잭션 카운트 불일치 |
| 3621 | TransactionAborted | X | 문 종료 (XACT_ABORT ON) |
| 3930 | TransactionAborted | X | 커밋 불가, 롤백만 가능 |
| **QuerySyntax** ||||
| 102 | QuerySyntax | X | SQL 구문 오류 |
| 137 | QuerySyntax | X | 스칼라 변수 미선언 |
| 512 | QuerySyntax | X | 하위 쿼리 다중 값 반환 |
| 530 | QuerySyntax | X | 최대 재귀 수 초과 |
| **CloudTransient** ||||
| 10928 | CloudTransient | O | 리소스 ID 제한 도달 |
| 10929 | CloudTransient | O | 리소스 최소 보장 거부 |
| 40197 | CloudTransient | O | 서비스 처리 오류 (클라우드) |
| 40501 | CloudTransient | O | 서비스 사용 중 (Azure 제한) |
| 40540 | CloudTransient | O | 서비스 목표 변경 중 (스케일링) |
| 40613 | CloudTransient | O | 데이터베이스 사용 불가 (재구성) |
| **UserDefined** ||||
| 50000+ | UserDefined | X | RAISERROR / THROW (동적) |

---

## 8. 확장 메서드 체이닝

### 8-1. 쿼리 결과 캐싱 (IDistributedCache)

`QuerySingleAsync` / `QueryAsync` 결과에 `.WithCacheAsync()` / `.WithCacheListAsync()`를 체이닝하여 캐시를 적용합니다.

#### 단건 캐싱

```csharp
DbResult<UserDto?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = 1 })
    .QuerySingleAsync<UserDto>()
    .WithCacheAsync(cache, "user:1", TimeSpan.FromMinutes(5));
```

**시그니처:**

```csharp
public static Task<DbResult<T?>> WithCacheAsync<T>(
    this Task<DbResult<T?>> resultTask,
    IDistributedCache cache,
    string cacheKey,
    TimeSpan duration,
    JsonSerializerOptions? jsonOptions = null,
    CancellationToken ct = default)
```

#### 다건 캐싱 (스트림 -> List)

```csharp
DbResult<List<CategoryDto>> result = await session.Default
    .Procedure("dbo.usp_GetCategories")
    .QueryAsync<CategoryDto>()
    .WithCacheListAsync(cache, "categories:all", TimeSpan.FromHours(1));
```

**시그니처:**

```csharp
public static Task<DbResult<List<T>>> WithCacheListAsync<T>(
    this Task<DbResult<IAsyncEnumerable<T>>> resultTask,
    IDistributedCache cache,
    string cacheKey,
    TimeSpan duration,
    JsonSerializerOptions? jsonOptions = null,
    CancellationToken ct = default)
```

### 8-2. HybridCache 캐싱

L1(메모리) + L2(분산) 계층 캐시를 자동 적용합니다.

```csharp
DbResult<UserDto?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { UserId = 1 })
    .QuerySingleAsync<UserDto>()
    .WithHybridCacheAsync(hybridCache, "user:1", TimeSpan.FromMinutes(10));
```

**시그니처:**

```csharp
public static Task<DbResult<T?>> WithHybridCacheAsync<T>(
    this Task<DbResult<T?>> resultTask,
    HybridCache hybridCache,
    string cacheKey,
    TimeSpan duration,
    CancellationToken ct = default)
```

### 8-3. 캐시 무효화

```csharp
await cache.InvalidateCacheAsync("user:1");
```

**시그니처:**

```csharp
public static Task InvalidateCacheAsync(
    this IDistributedCache cache,
    string cacheKey,
    CancellationToken ct = default)
```

### 8-4. JSON 컬럼 매핑

DB에서 JSON 문자열로 저장된 컬럼을 C# 타입으로 역직렬화합니다.

#### Dictionary 행에서 JSON 컬럼 추출

```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await session.Default
    .Procedure("dbo.usp_GetOrders")
    .QueryAsync<Dictionary<string, object?>>();

if (result.IsSuccess)
{
    await foreach (Dictionary<string, object?> row in result.Value!)
    {
        OrderDetail? detail = row.MapJsonColumn<OrderDetail>("EXTRA_DATA");
        Console.WriteLine($"주문: {row["OrderId"]}, 상세: {detail?.Note}");
    }
}
```

#### 스트림 전체에 JSON 매핑 적용

```csharp
DbResult<IAsyncEnumerable<Dictionary<string, object?>>> result = await session.Default
    .Procedure("dbo.usp_GetOrders")
    .QueryAsync<Dictionary<string, object?>>();

if (result.IsSuccess)
{
    await foreach ((Dictionary<string, object?> row, OrderDetail? json) in
        result.Value!.WithJsonColumnAsync<OrderDetail>("EXTRA_DATA"))
    {
        Console.WriteLine($"주문: {row["OrderId"]}, JSON: {json?.Note}");
    }
}
```

#### 문자열 직접 변환

```csharp
string jsonStr = """{"name":"Alice","age":30}""";
UserInfo? info = jsonStr.FromJson<UserInfo>();

string serialized = info.ToJson();
```

**JSON 확장 메서드 시그니처:**

```csharp
// Dictionary 행 -> JSON 객체
public static T? MapJsonColumn<T>(
    this Dictionary<string, object?> row,
    string columnName,
    JsonSerializerOptions? options = null)

// 스트림 -> (행, JSON) 튜플 스트림
public static IAsyncEnumerable<(Dictionary<string, object?> Row, T? Json)>
    WithJsonColumnAsync<T>(
        this IAsyncEnumerable<Dictionary<string, object?>> rows,
        string columnName,
        JsonSerializerOptions? options = null)

// 문자열 -> 객체
public static T? FromJson<T>(this string? json, JsonSerializerOptions? options = null)

// 객체 -> 문자열
public static string ToJson<T>(this T value, JsonSerializerOptions? options = null)
```

---

## 9. DI 등록 확장 메서드

| 메서드 | 설명 |
|---|---|
| `AddLibDb(IConfiguration)` | appsettings.json 기반 일괄 등록 (권장) |
| `AddHighPerformanceDb(Action<LibDbOptions>)` | 수동 구성 일괄 등록 |
| `RegisterLibDbCoreServices()` | 핵심 서비스만 등록 (테스트용) |
| `AddLibDbResilience()` | Polly 파이프라인 등록 |
| `AddLibDbHostedServices()` | 워밍업 Hosted Service 등록 |
| `AddSchemaFlushCoordination(string?)` | Epoch 기반 분산 스키마 캐시 조정 |
| `AddLibDbInterceptor<T>()` | 쿼리 인터셉터 등록 |

```csharp
// 기본 등록
builder.Services.AddLibDb(builder.Configuration);

// 인터셉터 추가
builder.Services.AddLibDbInterceptor<AuditInterceptor>();
builder.Services.AddLibDbInterceptor<PerformanceInterceptor>();
```
