# Fluent API 레퍼런스 (Fluent API Reference)

<!-- AI_CONTEXT: START -->
<!-- ROLE: REFERENCE_GUIDE -->
<!-- TARGET: Contracts/Entry/DbStageContracts.cs, Contracts/Execution/DbExecutionContracts.cs, Fluent/DbRequestBuilder.cs -->
<!-- AI_CONTEXT: END -->

`Lib.Db`의 핵심인 **3단계 Fluent API**를 완전히 설명합니다. 모든 쿼리 작업은 ①명령 정의 → ②파라미터 설정 → ③실행 순서로 진행됩니다.

---

## 📋 목차

1. [기본 패턴](#1-기본-패턴-basic-pattern)
2. [1단계: 명령 정의 (IProcedureStage)](#2-1단계-명령-정의-iprocedurestage)
3. [2단계: 파라미터 설정 (IParameterStage)](#3-2단계-파라미터-설정-iparameterstage)
4. [3단계: 실행 메서드 (IExecutionStage)](#4-3단계-실행-메서드-iexecutionstage)
5. [고급 시나리오](#5-고급-시나리오-advanced-scenarios)

---

## 1. 기본 패턴 (Basic Pattern)

모든 요청은 `IDbContext` (또는 `IProcedureStage`)의 인스턴스로부터 시작하며, **3단계 체이닝**으로 구성됩니다.

```csharp
await db.Default             // 0. Executor 선택 (Connection String Name)
    .Sql("SELECT ...")       // 1. 명령 정의 (Sql, Procedure, Bulk 등)
    .With(new { ... })       // 2. 파라미터 바인딩 (선택)
    .QueryAsync<T>();        // 3. 실행 및 매핑
```

**단계별 인터페이스 전환**:
- `IProcedureStage` → `IParameterStage` → `IExecutionStage<TParams>` → 실행

---

## 2. 1단계: 명령 정의 (IProcedureStage)

### 2-1. 저장 프로시저 (Stored Procedure)

#### `Procedure(string spName)`

저장 프로시저를 실행합니다.

```csharp
// 기본 사용
var users = await db.Default
    .Procedure("dbo.usp_GetUsers")
    .With(new { DepartmentId = 10 })
    .QueryAsync<User>()
    .ToListAsync();

// 파라미터 없이 실행
int affected = await db.Default
    .Procedure("dbo.usp_RefreshCache")
    .ExecuteAsync();
```

---

### 2-2. Ad-hoc SQL

#### `Sql(string sqlText)`

일반 SQL 문자열을 실행합니다.

```csharp
// SELECT 조회
var result = await db.Default
    .Sql("SELECT * FROM Users WHERE Id = @Id")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

// DDL 실행
await db.Default
    .Sql(@"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Logs')
        CREATE TABLE Logs (Id INT, Message NVARCHAR(MAX))
    ")
    .ExecuteAsync();
```

#### `Sql(ref SqlInterpolatedStringHandler handler)`

**Zero-Allocation** 보간 문자열 핸들러를 사용하여 SQL과 파라미터를 처리합니다.
C# 컴파일러가 자동으로 `string` 보간을 이 오버로드로 변환하여, 임시 문자열 할당을 제거하고 파라미터를 안전하게 추출합니다.

```csharp
int userId = 1;
string userName = "John";

// 컴파일러가 SqlInterpolatedStringHandler를 사용하여 처리
// 1. 임시 string 할당 없음 (Zero-Allocation)
// 2. 파라미터 자동 추출 (@p0, @p1) 및 SQL Injection 방지
var user = await db.Default
    .Sql($"SELECT * FROM Users WHERE Id = {userId} AND Name = {userName}")
    .QuerySingleAsync<User>();

// 실제 실행되는 SQL: "SELECT * FROM Users WHERE Id = @p0 AND Name = @p1"
// 파라미터: { @p0 = 1, @p1 = "John" }
```

> [!NOTE]
> 이 방식은 가장 권장되는 패턴이며, 내부적으로 `ArrayPool`을 사용하여 가비지 컬렉션(GC) 발생을 억제합니다.

#### `Sql(string format, params ReadOnlySpan<object?> args)`

**성능 최적화** 버전입니다. `ArrayPool`을 사용하여 GC 압박을 최소화합니다.

```csharp
// Span을 사용한 최적화 (대량 파라미터 시 유리)
await db.Default
    .Sql("SELECT * FROM Users WHERE Id = {0} AND Status = {1}",
         1, "Active")
    .ExecuteScalarAsync<int>();
```

---

### 2-3. Bulk 작업 (대량 처리)

#### `BulkInsertAsync<T>(string tableName, IEnumerable<T> data, CancellationToken ct)`

`SqlBulkCopy`를 사용하여 대량 데이터를 고속 삽입합니다.

```csharp
// 10만 건 데이터 삽입 (일반 INSERT 대비 100배 이상 빠름)
public record Product(int Id, string Name, decimal Price);

var products = Enumerable.Range(1, 100_000)
    .Select(i => new Product(i, $"Product{i}", i * 10.5m));

await db.Default.BulkInsertAsync("dbo.Products", products);
```

**성능 팁**:
- 인덱스가 많은 테이블은 사전에 인덱스를 비활성화하고 삽입 후 재구축.
- `BulkBatchSize` 옵션(기본 5000)을 조정하여 메모리 사용량 제어.

#### `BulkUpdateAsync<T>(string tableName, IE numerable<T> data, string[] keyColumns, string[] updateColumns, CancellationToken ct)`

임시 테이블 + MERGE 패턴으로 대량 업데이트를 수행합니다.

```csharp
// 가격 일괄 업데이트
var updates = new[]
{
    new Product(1, "", 99.99m),
    new Product(2, "", 149.99m)
};

await db.Default.BulkUpdateAsync(
    "dbo.Products",
    updates,
    keyColumns: new[] { "Id" },           // 매칭 기준
    updateColumns: new[] { "Price" }      // 업데이트할 컬럼
);
```

**동작 원리**:
1. 임시 테이블 `#Temp_Products` 생성
2. `BulkCopy`로 데이터 삽입
3. `MERGE` 문으로 본 테이블 업데이트
4. 임시 테이블 자동 삭제

#### `BulkDeleteAsync<T>(string tableName, IEnumerable<T> data, string[] keyColumns, CancellationToken ct)`

키 기반으로 대량 삭제를 수행합니다.

```csharp
// 특정 ID 목록 삭제
var idsToDelete = new[] { 1, 2, 3, 100, 200 }
    .Select(id => new { Id = id });

await db.Default.BulkDeleteAsync(
    "dbo.OldData",
    idsToDelete,
    keyColumns: new[] { "Id" }
);
```

---

### 2-4. Pipeline 작업 (Channel 기반 스트리밍)

실시간으로 유입되는 데이터를 버퍼링하여 배치 단위로 처리합니다.

#### `BulkInsertPipelineAsync<T>(string tableName, ChannelReader<T> reader, int batchSize, CancellationToken ct)`

Channel을 통해 스트리밍 데이터를 Bulk Insert합니다.

```csharp
// 실시간 로그 수집 시나리오
var channel = Channel.CreateUnbounded<LogEntry>();
var writer = channel.Writer;

// 백그라운드에서 Pipeline 실행
var pipelineTask = db.Default.BulkInsertPipelineAsync(
    "dbo.Logs",
    channel.Reader,
    batchSize: 1000  // 1000건씩 배치 처리
);

// 실시간으로 데이터 전송
for (int i = 0; i < 50_000; i++)
{
    await writer.WriteAsync(new LogEntry(i, $"Log {i}", DateTime.Now));
}

writer.Complete();
await pipelineTask;  // 모든 데이터 삽입 완료 대기
```

**장점**:
- 메모리 사용량 일정 (Channel 버퍼 크기로 제어)
- 생산자-소비자 패턴으로 병렬 처리 가능

#### `BulkUpdatePipelineAsync<T>()`, `BulkDeletePipelineAsync<T>()`

Update/Delete도 동일한 Pipeline 패턴 지원.

```csharp
// 실시간 가격 업데이트
await db.Default.BulkUpdatePipelineAsync(
    "dbo.Prices",
    priceChannel.Reader,
    keyColumns: new[] { "ProductId" },
    updateColumns: new[] { "CurrentPrice", "UpdatedAt" },
    batchSize: 500
);
```

---

### 2-5. Resumable Query (복구형 쿼리)

네트워크 단절이나 일시적 오류 발생 시 **마지막 커서 위치부터 자동으로 재개**하는 쿼리입니다.

#### `QueryResumableAsync<TCursor, TResult>(Func<TCursor, string> queryBuilder, Func<TResult, TCursor> cursorSelector, TCursor initialCursor, CancellationToken ct)`

```csharp
// 1억 건 데이터를 배치 단위로 안전하게 조회
public record Order(long Id, string Customer, decimal Amount);

var allOrders = new List<Order>();

await foreach (var order in db.Default.QueryResumableAsync(
    // 커서 기반 쿼리 생성
    queryBuilder: (long lastId) => 
        $"SELECT TOP 10000 * FROM Orders WHERE Id > {lastId} ORDER BY Id",
    
    // 다음 커서 값 추출
    cursorSelector: (order) => order.Id,
    
    // 초기 커서
    initialCursor: 0L
))
{
    allOrders.Add(order);
    
    // 네트워크 끊김 시뮬레이션
    if (allOrders.Count == 50000)
        throw new IOException("Network lost");
    
    // 자동으로 lastId부터 재시도됨
}
```

**동작 방식**:
1. `queryBuilder(0)` 실행 → 처음 10,000건 조회
2. 마지막 레코드의 Id (예: 9999)를 `cursorSelector`로 추출
3. `queryBuilder(9999)` 실행 → 다음 10,000건 조회
4. 오류 발생 시 마지막 커서(9999)부터 재시도
5. 결과가 0건이면 스트림 종료

---

## 3. 2단계: 파라미터 설정 (IParameterStage)

### 3-1. 파라미터 바인딩

#### `With<TParams>(TParams parameters)`

쿼리 실행에 필요한 파라미터를 설정합니다.

**익명 객체**:
```csharp
.With(new { Id = 1, Name = "John", CreatedAt = DateTime.Now })
```

**DTO 클래스**:
```csharp
public record UserFilter(int DepartmentId, string Role);

.With(new UserFilter(10, "Admin"))
```

**TVP (Table-Valued Parameter)**:
```csharp
[TvpRow(TypeName = "dbo.Tvp_UserIds")]
public record UserIdRow(int UserId);

var ids = new[] { new UserIdRow(1), new UserIdRow(2), new UserIdRow(3) };

.With(new { UserIds = ids })  // @UserIds 파라미터로 TVP 전달
```

**DbParameter 직접 사용** (Output Parameter):
```csharp
var outParam = new SqlParameter("@TotalCount", SqlDbType.Int) 
{ 
    Direction = ParameterDirection.Output 
};

await db.Default
    .Procedure("dbo.usp_ProcessOrders")
    .With(new { Year = 2024, outParam })
    .ExecuteAsync();

int totalCount = (int)outParam.Value;
```

---

### 3-2. 실행 옵션

#### `WithTimeout(int timeoutSeconds)`

명령 실행 타임아웃을 설정합니다. (기본값: `LibDbOptions.DefaultCommandTimeoutSeconds`)

```csharp
// 장시간 실행되는 배치 작업
await db.Default
    .Procedure("dbo.usp_MonthlyReport")
    .WithTimeout(600)  // 10분
    .With(new { Year = 2024, Month = 12 })
    .ExecuteAsync();

// 체이닝 가능
await db.Default
    .Sql("SELECT * FROM LargeTable")
    .WithTimeout(120)
    .With(new { Limit = 1000000 })
    .QueryAsync<Row>();
```

---

## 4. 3단계: 실행 메서드 (IExecutionStage)

### 4-1. 조회 (Query)

#### `QueryAsync<TResult>(CancellationToken ct)`

결과를 **비동기 스트림** (`IAsyncEnumerable<T>`)으로 반환합니다.

**특징**:
- 메모리에 모든 결과를 적재하지 않고 순차적으로 소비
- `yield return` 방식으로 1건씩 반환
- `await foreach`로 사용

```csharp
// 대량 데이터 스트리밍 조회
await foreach (var user in db.Default
    .Sql("SELECT * FROM Users")
    .QueryAsync<User>())
{
    Console.WriteLine(user.Name);
    // 메모리 사용량 일정
}

// List로 변환 (메모리에 전체 로드)
var users = await db.Default
    .Sql("SELECT * FROM Users")
    .QueryAsync<User>()
    .ToListAsync();
```

#### `QuerySingleAsync<TResult>(CancellationToken ct)`

단일 행을 조회합니다. 결과가 없으면 `null` 반환.

```csharp
// ID로 사용자 조회
var user = await db.Default
    .Sql("SELECT * FROM Users WHERE Id = @Id")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

if (user is null)
{
    Console.WriteLine("User not found");
}

// Record 타입과 함께 사용
public record User(int Id, string Name, string Email);
```

---

### 4-2. 스칼라 (Scalar)

#### `ExecuteScalarAsync<TScalar>(CancellationToken ct)`

첫 번째 행의 첫 번째 열 값을 반환합니다.

```csharp
// COUNT 조회
int userCount = await db.Default
    .Sql("SELECT COUNT(*) FROM Users")
    .ExecuteScalarAsync<int>();

// SUM 조회
decimal totalSales = await db.Default
    .Sql("SELECT SUM(Amount) FROM Orders WHERE Year = @Year")
    .With(new { Year = 2024 })
    .ExecuteScalarAsync<decimal>();

// SCOPE_IDENTITY 조회 (INSERT 후 자동 생성 ID)
int newId = await db.Default
    .Sql(@"
        INSERT INTO Users (Name) VALUES (@Name);
        SELECT CAST(SCOPE_IDENTITY() AS INT);
    ")
    .With(new { Name = "New User" })
    .ExecuteScalarAsync<int>();
```

---

### 4-3. 다중 결과 (Multiple Result Sets)

#### `QueryMultipleAsync(CancellationToken ct)`

여러 SELECT 결과를 순차적으로 읽습니다. `IMultipleResultReader`를 반환.

```csharp
// 저장 프로시저가 3개의 결과셋 반환
/*
CREATE PROCEDURE usp_GetDashboard
AS
BEGIN
    SELECT * FROM Users;              -- 결과셋 1
    SELECT * FROM Orders;             -- 결과셋 2
    SELECT COUNT(*) AS Total FROM Products;  -- 결과셋 3
END
*/

await using var reader = await db.Default
    .Procedure("dbo.usp_GetDashboard")
    .QueryMultipleAsync();

// 결과셋 1: 사용자 목록
var users = await reader.ReadAsync<User>();

// 결과셋 2: 주문 목록
var orders = await reader.ReadAsync<Order>();

// 결과셋 3: 단일 집계 값
var summary = await reader.ReadSingleAsync<ProductSummary>();
Console.WriteLine($"Total Products: {summary.Total}");
```

**IMultipleResultReader 메서드**:

| 메서드 | 설명 |
|---|---|
| `ReadAsync<T>()` | 현재 결과셋 전체를 `List<T>`로 반환 |
| `ReadSingleAsync<T>()` | 현재 결과셋의 첫 행만 반환 (null 가능) |

> [!TIP]
> MultipleResultReader는 순차적으로만 읽을 수 있습니다. 이전 결과셋으로 돌아갈 수 없습니다.

---

### 4-4. 명령 실행 (NonQuery)

#### `ExecuteAsync(CancellationToken ct)`

데이터 변경 명령(INSERT/UPDATE/DELETE)을 실행하고 **영향받은 행 수**를 반환합니다.

```csharp
// INSERT
int inserted = await db.Default
    .Sql("INSERT INTO Users (Name, Email) VALUES (@Name, @Email)")
    .With(new { Name = "John", Email = "john@example.com" })
    .ExecuteAsync();

Console.WriteLine($"{inserted} row(s) inserted");

// UPDATE
int updated = await db.Default
    .Sql("UPDATE Users SET Email = @Email WHERE Id = @Id")
    .With(new { Id = 1, Email = "newemail@example.com" })
    .ExecuteAsync();

// DELETE
int deleted = await db.Default
    .Sql("DELETE FROM Users WHERE Id = @Id")
    .With(new { Id = 999 })
    .ExecuteAsync();

// DDL (테이블 생성 등) - 항상 -1 반환
await db.Default
    .Sql("CREATE TABLE TempData (Id INT, Value NVARCHAR(MAX))")
    .ExecuteAsync();
```

---

## 5. 고급 시나리오 (Advanced Scenarios)

### 5-1. 트랜잭션 처리

```csharp
// 방법 1: IDbExecutor의 기본 트랜잭션
// (주의: 현재 버전에서는 명시적 트랜잭션 API가 제공되지 않으므로,
// TransactionScope 또는 별도의 Connection/Transaction 관리 필요)

using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

await db.Default
    .Sql("INSERT INTO Orders ...").With(order).ExecuteAsync();

await db.Default
    .Sql("UPDATE Inventory ...").With(inventory).ExecuteAsync();

scope.Complete();
```

---

### 5-2. Custom 연결 문자열 하드코딩

**시나리오**: `appsettings.json` 대신 코드에서 연결 문자열을 직접 지정해야 하는 경우 (동적 멀티테넌시, 런타임 DB 선택 등)

#### 방법 1: IDbContext.UseConnectionString() 사용

```csharp
// 시나리오: 멀티테넌트 환경에서 테넌트별 DB 동적 선택
string GetConnectionString(string tenantId)
{
    return $"Server=tenant-{tenantId}.database.windows.net;Database=TenantDb;...";
}

// 사용 예시
string tenantId = GetCurrentTenantId();
string connectionString = GetConnectionString(tenantId);

// Custom 연결 문자열로 쿼리 실행
var users = await db.UseConnectionString(connectionString)
    .Sql("SELECT * FROM Users WHERE TenantId = @TenantId")
    .With(new { TenantId = tenantId })
    .QueryAsync<User>()
    .ToListAsync();
```

#### 방법 2: 동적 리포팅 DB 선택

```csharp
// 읽기 전용 리포트 DB를 동적으로 선택
public class ReportService(IDbContext db)
{
    private readonly string[] _readReplicas = 
    [
        "Server=replica1.db;Database=Analytics;...",
        "Server=replica2.db;Database=Analytics;...",
        "Server=replica3.db;Database=Analytics;..."
    ];

    public async Task<SalesReport> GenerateReportAsync(DateTime startDate, DateTime endDate)
    {
        // 부하 분산: 랜덤 리플리카 선택
        var connectionString = _readReplicas[Random.Shared.Next(_readReplicas.Length)];
        
        return await db.UseConnectionString(connectionString)
            .Procedure("dbo.usp_GenerateSalesReport")
            .With(new { StartDate = startDate, EndDate = endDate })
            .QuerySingleAsync<SalesReport>();
    }
}
```

#### 방법 3: 환경별 DB 전환 (코드 기반)

```csharp
// 환경 변수 또는 런타임 조건에 따라 DB 선택
public class ConfigService
{
    public string GetDbConnectionString(string environment)
    {
        return environment switch
        {
            "Development" => "Server=localhost;Database=DevDb;Integrated Security=True;",
            "Staging" => "Server=staging.db;Database=StagingDb;User Id=...;Password=...;",
            "Production" => "Server=prod.db;Database=ProdDb;User Id=...;Password=...;Encrypt=True;",
            _ => throw new ArgumentException($"Unknown environment: {environment}")
        };
    }
}

// 사용
var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var connectionString = configService.GetDbConnectionString(env);

int affected = await db.UseConnectionString(connectionString)
    .Sql("UPDATE Settings SET MaintenanceMode = @IsEnabled")
    .With(new { IsEnabled = true })
    .ExecuteAsync();
```

> [!WARNING]
> **보안 주의사항**
> - 연결 문자열에 **암호를 하드코딩하지 마세요**
> - Azure Key Vault, AWS Secrets Manager 등 보안 저장소 사용 권장
> - 프로덕션 환경에서는 Managed Identity 또는 AAD 인증 사용

> [!TIP]
> **성능 최적화**
> - `UseConnectionString()`은 매번 새로운 연결 풀을 생성하지 않습니다
> - 동일한 연결 문자열은 자동으로 캐시되어 재사용됩니다
> - 하지만 가능하면 `appsettings.json`에 미리 정의하고 `UseInstance()`를 사용하는 것을 권장합니다

> [!NOTE]
> **IDbContext 인터페이스**
> ```csharp
> public interface IDbContext
> {
>     IProcedureStage UseInstance(string instanceName);
>     IProcedureStage UseConnectionString(string connectionString);
>     IProcedureStage Default { get; }
> }
> ```
> - `UseInstance("Main")`: appsettings.json에 정의된 연결 문자열 사용
> - `UseConnectionString(connectionString)`: 직접 지정한 연결 문자열 사용
> - `Default`: "Default" 인스턴스 사용 (`UseInstance("Default")`와 동일)

---

### 5-3. 대량 데이터 마이그레이션

```csharp
// 1억 건 데이터를 안전하게 이동
var sourceData = db.Source.QueryResumableAsync(
    queryBuilder: (long lastId) => 
        $"SELECT TOP 50000 * FROM OldTable WHERE Id > {lastId} ORDER BY Id",
    cursorSelector: (row) => row.Id,
    initialCursor: 0L
);

var channel = Channel.CreateBounded<SourceRow>(10000);

// Producer: Resumable Query로 읽기
var produceTask = Task.Run(async () =>
{
    await foreach (var row in sourceData)
    {
        await channel.Writer.WriteAsync(row);
    }
    channel.Writer.Complete();});

// Consumer: Pipeline으로 쓰기
var consumeTask = db.Destination.BulkInsertPipelineAsync(
    "NewTable",
    channel.Reader,
    batchSize: 5000
);

await Task.WhenAll(produceTask, consumeTask);
```

### 5-3. 동적 쿼리 생성

```csharp
// 사용자 입력 기반 동적 필터
public async Task<List<Product>> Search SearchFilter filter)
{
    var conditions = new List<string>();
    var parameters = new Dictionary<string, object>();

    if (!string.IsNullOrEmpty(filter.Name))
    {
        conditions.Add("Name LIKE @Name");
        parameters["Name"] = $"%{filter.Name}%";
    }

    if (filter.MinPrice.HasValue)
    {
        conditions.Add("Price >= @MinPrice");
        parameters["MinPrice"] = filter.MinPrice.Value;
    }

    var whereClause = conditions.Any() 
        ? "WHERE " + string.Join(" AND ", conditions)
        : "";

    var sql = $"SELECT * FROM Products {whereClause}";

    return await db.Default
        .Sql(sql)
        .With(parameters)
        .QueryAsync<Product>()
        .ToListAsync();
}
```

### 5-4. 성능 튜닝 팁

```csharp
// ❌ 비효율: 전체 로드 후 필터링
var allUsers = await db.Default
    .Sql("SELECT * FROM Users")
    .QueryAsync<User>()
    .ToListAsync();

var activeUsers = allUsers.Where(u => u.IsActive);

// ✅ 효율: DB에서 필터링
var activeUsers = await db.Default
    .Sql("SELECT * FROM Users WHERE IsActive = 1")
    .QueryAsync<User>()
    .ToListAsync();

// ✅ 스트리밍 처리 (메모리 절약)
await foreach (var user in db.Default.Sql("SELECT * FROM Users").QueryAsync<User>())
{
    await ProcessUserAsync(user);  // 1건씩 처리
}
```

---

## 📚 전체 메서드 요약

### IProcedureStage (1단계)
- `Procedure(string)` - 저장 프로시저
- `Sql(string)` - 일반 SQL
- `Sql(FormattableString)` - 보간 SQL
- `Sql(string, params ReadOnlySpan<object?>)` - Span 최적화
- `BulkInsertAsync<T>()` - 대량 삽입
- `BulkUpdateAsync<T>()` - 대량 업데이트
- `BulkDeleteAsync<T>()` - 대량 삭제
- `BulkInsertPipelineAsync<T>()` - 파이프라인 삽입
- `BulkUpdatePipelineAsync<T>()` - 파이프라인 업데이트
- `BulkDeletePipelineAsync<T>()` - 파이프라인 삭제
- `QueryResumableAsync<TCursor, TResult>()` - 복구형 쿼리

### IParameterStage (2단계)
- `With<TParams>(TParams)` - 파라미터 설정
- `WithTimeout(int)` - 타임아웃 설정

### IExecutionStage (3단계)
- `QueryAsync<TResult>()` - 스트림 조회
- `QuerySingleAsync<TResult>()` - 단일 행 조회
- `ExecuteScalarAsync<TScalar>()` - 스칼라 값 조회
- `QueryMultipleAsync()` - 다중 결과 조회
- `ExecuteAsync()` - 명령 실행

### IMultipleResultReader
- `ReadAsync<T>()` - 결과셋 전체 읽기
- `ReadSingleAsync<T>()` - 결과셋 단일 행 읽기

---

**모든 API에 대한 예제를 포함하여 개발자가 문서만으로 완전히 사용 가능하도록 작성되었습니다.**

---


<p align="center">
  ⬅️ <a href="./02_configuration_and_di.md">이전: 설치 및 구성</a>
  &nbsp;|&nbsp;
  <a href="./04_tvp_and_aot.md">다음: TVP & AOT ➡️</a>
</p>

<p align="center">
  🏠 <a href="../README.md">홈으로</a>
</p>
