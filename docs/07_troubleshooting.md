# 트러블슈팅 및 FAQ (Troubleshooting)

<!-- AI_CONTEXT: START -->
<!-- ROLE: SUPPORT_GUIDE -->
<!-- AI_CONTEXT: END -->

`Lib.Db` 사용 중 발생할 수 있는 일반적인 문제와 해결 방법을 안내합니다.

---

## 목차

1. [빌드 및 컴파일 오류](#1-빌드-및-컴파일-오류)
2. [런타임 오류](#2-런타임-오류)
3. [성능 문제 진단](#3-성능-문제-진단)
4. [Connection Pool 문제](#4-connection-pool-문제)
5. [로깅 및 진단](#5-로깅-및-진단)
6. [FAQ (자주 묻는 질문)](#6-faq-자주-묻는-질문)

---

## 1. 빌드 및 컴파일 오류

### Q1. `Lib.Db.TvpGen` 관련 "Metadata not found" 오류

**증상**: 빌드 시 소스 제너레이터가 DTO 메타데이터를 찾지 못함.

```
error CS0246: The type or namespace name 'UserDtoTvpBuilder' could not be found
```

**원인**: 패키지 참조가 누락되었거나 IDE 캐시 문제.

**해결**:
1. `Lib.Db` 패키지 설치 확인 (TvpGen 내장).
   ```bash
   dotnet list package | grep Lib.Db
   ```

2. 솔루션 정리(Clean) 후 다시 빌드(Rebuild).
   ```bash
   dotnet clean
   dotnet build
   ```

3. VS Code/Visual Studio 재시작.

4. `obj` 및 `bin` 폴더 수동 삭제.
   ```bash
   rm -rf obj bin
   dotnet build
   ```

---

### Q2. "Type is not partial" 경고

**증상**: DTO 클래스에 부분(partial) 키워드가 없음.

**해결**: Source Generator 최적화를 위해 `partial` 키워드를 추가해 주세요.

```csharp
[TvpRow(...)]
public partial record UserDto  // ✅ partial 추가
{
    // ...
}
```

---

### Q3. Native AOT 빌드 시 IL 경고

**증상**:
```
IL2026: Using member 'System.Reflection.PropertyInfo.GetValue(object)' 
which has 'RequiresUnreferencedCodeAttribute' can break functionality when trimming.
```

**원인**: `Lib.Db` 내부에서는 발생하지 않아야 함. 사용자 코드에서 리플렉션 사용 가능성.

**해결**:
- `Lib.Db` Source Generator 사용 (리플렉션 제거)
- Dynamic Type 사용 금지
- 경고 파일 경로 확인하여 사용자 코드 수정

---

## 2. 런타임 오류

### Q4. "Required Parameter Missing" 예외

**증상**:
```csharp
System.ArgumentException: Required parameter '@UserId' was not provided.
```

**원인**: `StrictRequiredParameterCheck` 옵션이 켜져 있고, SQL에서 `@Param`을 썼는데 `.With()`로 값을 안 넘김.

**해결**:
- 누락된 파라미터를 `.With(new { UserId = ... })`로 전달.
- 또는 `options.StrictRequiredParameterCheck = false`로 설정 (비권장).

```json
{
  "LibDb": {
    "StrictRequiredParameterCheck": false
  }
}
```

---

### Q5. Linux/Docker에서 "Named Mutex" 오류

**증상**:
```
System.UnauthorizedAccessException: Access to the path '/var/run/...' is denied.
```

**원인**: 리눅스 컨테이너 환경에서 `/tmp` 또는 공유 메모리 영역(`Global\`)에 대한 권한 부족.

**해결**:
1. `appsettings.json`에서 `"EnableSharedMemoryCache": false` 설정.

```json
{
  "LibDb": {
    "EnableSharedMemoryCache": false
  }
}
```

2. 또는 Docker 실행 시 `--shm-size` 옵션 조정 및 권한 부여.

```bash
docker run --shm-size=256m -v /dev/shm:/dev/shm myapp
```

---

### Q6. "SqlException: Connection Timeout Expired"

**증상**:
```
Microsoft.Data.SqlClient.SqlException (0x80131904): 
Timeout expired. The timeout period elapsed prior to completion of the operation.
```

**원인**: 쿼리 실행 시간이 타임아웃 설정을 초과.

**해결**:
1. 타임아웃 증가:
```csharp
await db.Default
    .Sql("...")
    .WithTimeout(120)  // 2분
    .ExecuteAsync();
```

2. 쿼리 최적화 (인덱스, 실행 계획 확인).

3. 네트워크 문제 확인:
```bash
ping your-sql-server.database.windows.net
```

---

### Q7. "Cannot open database ... requested by the login"

**증상**:
```
SqlException (4060): Cannot open database "MyDatabase" requested by the login.
```

**원인**: 
- 데이터베이스가 존재하지 않음
- 로그인 사용자에게 권한 없음
- Initial Catalog가 잘못됨

**해결**:
1. 연결 문자열 확인:
```json
{
  "ConnectionStrings": {
    "Main": "Server=...;Database=MyDatabase;..."
  }
}
```

2. SQL Server에서 확인:
```sql
SELECT name FROM sys.databases WHERE name = 'MyDatabase';
```

3. 권한 부여:
```sql
CREATE LOGIN [myuser] WITH PASSWORD = '...';
USE MyDatabase;
CREATE USER [myuser] FOR LOGIN [myuser];
ALTER ROLE db_datareader ADD MEMBER [myuser];
ALTER ROLE db_datawriter ADD MEMBER [myuser];
```

---

### Q8. "BrokenCircuitException"

**증상**:
```
Polly.CircuitBreaker.BrokenCircuitException: 
The circuit is now open and is not allowing calls.
```

**원인**: Circuit Breaker가 작동하여 일시적으로 모든 요청 차단.

**해결**:
1. 로그 확인하여 원인 파악:
```json
{
  "Logging": {
    "LogLevel": {
      "Lib.Db.Infrastructure.Resilience": "Debug"
    }
  }
}
```

2. Circuit Breaker 설정 조정:
```json
{
  "LibDb": {
    "Resilience": {
      "CircuitBreakerFailureRatio": 0.7,  // 더 관대하게
      "CircuitBreakerBreakDurationMs": 10000  // 복구 시간 단축
    }
  }
}
```

3. DB 서버 상태 확인:
```sql
SELECT @@SERVERNAME, GETDATE();
```

---

### Q9. "TVP Type 'dbo.Tvp_User' does not exist"

**증상**:
```sql
Msg 2715, Level 16, State 7
Cannot find type 'dbo.Tvp_User' in database.
```

**원인**: SQL Server에 User-Defined Table Type이 생성되지 않음.

**해결**:
1. SQL Server에서 Type 생성:
```sql
CREATE TYPE dbo.Tvp_User AS TABLE
(
    Name NVARCHAR(100),
    Age INT
);
```

2. 스키마 확인:
```sql
SELECT name, type_desc 
FROM sys.types 
WHERE is_table_type = 1;
```

---

### Q10. "Deadlock" 오류 반복 발생

**증상**:
```
SqlException (1205): Transaction was deadlocked on lock resources.
```

**원인**: 여러 트랜잭션이 서로의 락을 기다리며 교착 상태.

**해결**:
1. Deadlock 그래프 수집:
```sql
DBCC TRACEON(1222, -1);  -- 교착 상태 정보 로그에 기록
```

2. 쿼리 순서 통일:
```csharp
// ❌ 순서가 다르면 Deadlock 가능
Task.Run(() => UpdateUserThenOrder(userId, orderId));
Task.Run(() => UpdateOrderThenUser(orderId, userId));

// ✅ 순서 통일
Task.Run(() => UpdateOrderThenUser(orderId, userId));
Task.Run(() => UpdateOrderThenUser(orderId2, userId2));
```

3. 트랜잭션 범위 최소화:
```csharp
// ❌ 트랜잭션이 너무 김
using (var scope = new TransactionScope())
{
    await db.Default.Sql("UPDATE Users ...").ExecuteAsync();
    await Task.Delay(5000);  // 불필요한 지연
    await db.Default.Sql("UPDATE Orders ...").ExecuteAsync();
    scope.Complete();
}

// ✅ 트랜잭션 짧게
using (var scope = new TransactionScope())
{
    await db.Default.Sql("UPDATE Users ...").ExecuteAsync();
    await db.Default.Sql("UPDATE Orders ...").ExecuteAsync();
    scope.Complete();
}
```

---

## 3. 성능 문제 진단

### 체크리스트

실행 시간이 느리거나 메모리 사용량이 높을 때:

- [ ] **SQL 쿼리 실행 계획 확인**
  ```sql
  SET STATISTICS TIME ON;
  SET STATISTICS IO ON;
  -- 쿼리 실행
  ```

- [ ] **Missing Index 확인**
  ```sql
  SELECT * FROM sys.dm_db_missing_index_details;
  ```

- [ ] **Connection Pool 상태 확인**
  ```csharp
  SqlConnection.ClearAllPools();  // 풀 리셋 후 재테스트
  ```

- [ ] **SELECT * 사용 여부**
  ```csharp
  // ❌ 불필요한 컬럼 전송
  .Sql("SELECT * FROM Users")
  
  // ✅ 필요한 컬럼만
  .Sql("SELECT Id, Name FROM Users")
  ```

- [ ] **Bulk 작업 사용 여부**
  ```csharp
  // ❌ 10,000번 INSERT
  foreach (var user in users)
      await db.Default.Sql("INSERT ...").ExecuteAsync();
  
  // ✅ BulkInsert (1회)
  await db.Default.BulkInsertAsync("Users", users);
  ```

- [ ] **캐시 히트율 확인** (80% 이상 목표)
  ```json
  {
    "Logging": { "LogLevel": { "Lib.Db.Caching": "Debug" } }
  }
  ```

### 성능 프로파일링

```bash
# dotMemory (JetBrains)
dotMemory attach <PID>

# PerfView (Microsoft, 무료)
PerfView.exe collect -MaxCollectSec:60

# BenchmarkDotNet
dotnet run -c Release --project MyBenchmarks.csproj
```

---

## 4. Connection Pool 문제

### Q11. "Timeout expired. The connection pool is exhausted"

**증상**:
```
System.InvalidOperationException: 
Timeout expired. The pool is exhausted and max pool size was reached.
```

**원인**: 연결이 제대로 반환되지 않거나 Pool 크기 부족.

**해결**:
1. Pool 크기 증가:
```json
{
  "ConnectionStrings": {
    "Main": "Server=...;Max Pool Size=200;..."
  }
}
```

2. 연결 누수 확인:
```csharp
// ❌ 연결 미해제
var conn = new SqlConnection(...);
await conn.OpenAsync();
// conn.Dispose() 호출 안 함 → 누수!

// ✅ Lib.Db는 자동 관리 (걱정 불필요)
await db.Default.Sql("...").ExecuteAsync();
```

3. 동시 요청 수 확인:
```bash
# 활성 연결 수 모니터링
SELECT COUNT(*) FROM sys.dm_exec_connections;
```

---

### Q12. "Connection was not closed. The connection's current state is open"

**증상**: 연결이 열린 상태로 반환됨.

**원인**: `Lib.Db` 내부 버그 (보고 필요).

**임시 해결**:
```json
{
  "ConnectionStrings": {
    "Main": "Server=...;Pooling=false;..."
  }
}
```

---

## 5. 로깅 및 진단

### DiagnosticSource 활용

`Lib.Db`는 `System.Diagnostics.DiagnosticSource`를 통해 상세한 텔레메트리를 방출합니다.

```csharp
// OpenTelemetry 연동 예시
builder.Services.AddOpenTelemetry()
    .WithTracing(tracer => tracer
        .AddSource("Lib.Db")  // 소스 이름
        .AddConsoleExporter());
```

### 로그 레벨 조정

`appsettings.json`의 Logging 섹션에서 `Lib.Db` 네임스페이스의 레벨을 `Debug` 또는 `Trace`로 낮추면 내부 동작(SQL 생성, 캐시 Hit/Miss)을 상세히 볼 수 있습니다.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Lib.Db": "Debug",
      "Lib.Db.Execution": "Trace"
    }
  }
}
```

**출력 예시**:
```
[Trace] Lib.Db.Execution.SqlDbExecutor: Preparing SQL: SELECT * FROM Users WHERE Id = @p0
[Debug] Lib.Db.Infrastructure.DbBinder: Binding parameter @p0 (Int32) = 123
[Debug] Lib.Db.Caching.SharedMemoryCache: Cache Hit (L2): schema:dbo.usp_GetUsers
[Information] Lib.Db.Execution.SqlDbExecutor: Query executed in 45ms
```

### 스택 트레이스 분석

**예외 발생 시**:
```csharp
try
{
    await db.Default.Sql("...").ExecuteAsync();
}
catch (Exception ex)
{
    // 전체 스택 트레이스 출력
    Console.WriteLine(ex.ToString());
    
    // InnerException 확인
    while (ex.InnerException != null)
    {
        ex = ex.InnerException;
        Console.WriteLine("Inner: " + ex.Message);
    }
}
```

**분석 방법**:
1. 가장 안쪽 InnerException이 실제 원인
2. `SqlException`의 `Number` 속성 확인
3. `Lib.Db` vs 사용자 코드 스택 구분

```
at Lib.Db.Execution.SqlDbExecutor.ExecuteAsync(...)  ← Lib.Db 내부
   at MyApp.UserRepository.GetUserAsync(...)          ← 사용자 코드
```

---

## 6. FAQ (자주 묻는 질문)

### Q13. Dapper와 Lib.Db를 함께 사용할 수 있나요?

**답변**: 네, 가능합니다.

```csharp
// Dapper
using (var conn = new SqlConnection(connectionString))
{
    var users = await conn.QueryAsync<User>("SELECT * FROM Users");
}

// Lib.Db
var users = await db.Default.Sql("SELECT * FROM Users").QueryAsync<User>().ToListAsync();
```

**주의**: 동일한 Connection Pool을 사용하므로 Max Pool Size 조정 필요.

---

### Q14. Entity Framework Core와 함께 사용할 수 있나요?

**답변**: 네, 보완적으로 사용 가능합니다.

```csharp
// EF Core: 복잡한 도메인 로직
var orders = await dbContext.Orders
    .Include(o => o.Items)
    .Where(o => o.Status == OrderStatus.Pending)
    .ToListAsync();

// Lib.Db: 대량 작업, 성능 중시
await db.Default.BulkInsertAsync("OrderItems", orderItems);
```

---

### Q15. 비동기가 아닌 동기 메서드가 있나요?

**답변**: 아니요, `Lib.Db`는 **비동기 전용**입니다.

동기 호출이 필요하면:
```csharp
var result = db.Default
    .Sql("SELECT COUNT(*) FROM Users")
    .ExecuteScalarAsync<int>()
    .GetAwaiter()
    .GetResult();  // 동기 대기
```

---

### Q16. Transaction 명시적 제어가 가능한가요?

**답변**: 현재 버전에서는 `TransactionScope` 사용을 권장합니다.

```csharp
using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    await db.Default.Sql("INSERT ...").ExecuteAsync();
    await db.Default.Sql("UPDATE ...").ExecuteAsync();
    
    scope.Complete();  // Commit
}
// Dispose 시 Rollback (Complete 미호출 시)
```

---

### Q17. 여러 데이터베이스에 동시 연결할 수 있나요?

**답변**: 네, `appsettings.json`에 여러 연결 문자열 정의.

```json
{
  "LibDb": {
    "ConnectionStrings": {
      "Main": "Server=...;Database=MyDb;...",
      "LogDb": "Server=...;Database=LogDb;...",
      "ReportDb": "Server=...;Database=ReportDb;..."
    }
  }
}
```

```csharp
// 사용
await db["Main"].Sql("...").ExecuteAsync();
await db["LogDb"].Sql("...").ExecuteAsync();
await db["ReportDb"].Sql("...").ExecuteAsync();
```

---

### Q18. SQL Injection 방지는 어떻게 하나요?

**답변**: `Lib.Db`는 자동으로 파라미터화합니다.

```csharp
// ✅ 안전 (자동 파라미터화)
int userId = Request.Query["id"];
await db.Default.Sql($"SELECT * FROM Users WHERE Id = {userId}").QueryAsync<User>();

// 내부적으로 변환:
// SQL: "SELECT * FROM Users WHERE Id = @p0"
// Params: { @p0 = userId }
```

**절대 금지**:
```csharp
// ❌ 위험! (문자열 연결)
string sql = "SELECT * FROM Users WHERE Name = '" + userName + "'";
await db.Default.Sql(sql).ExecuteAsync();
```

---

### Q19. 동적 정렬/필터링은 어떻게 하나요?

**답변**: 문자열 빌더 사용.

```csharp
public async Task<List<Product>> SearchAsync(SearchFilter filter)
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

---

### Q20. 페이징은 어떻게 구현하나요?

**답변**: `OFFSET` / `FETCH NEXT` 사용 (SQL Server 2012+).

```csharp
public record PagedResult<T>(List<T> Items, int TotalCount);

public async Task<PagedResult<User>> GetUsersPagedAsync(int page, int pageSize)
{
    int offset = (page - 1) * pageSize;

    var items = await db.Default
        .Sql($@"
            SELECT * FROM Users 
            ORDER BY Id 
            OFFSET {offset} ROWS 
            FETCH NEXT {pageSize} ROWS ONLY
        ")
        .QueryAsync<User>()
        .ToListAsync();

    var totalCount = await db.Default
        .Sql("SELECT COUNT(*) FROM Users")
        .ExecuteScalarAsync<int>();

    return new PagedResult<User>(items, totalCount);
}
```

---

## 추가 지원

문제가 해결되지 않으면:

1. **공식 문서**: `docs/` 디렉토리 전체 검토
2. **GitHub Issues**: 문제 리포팅
3. **Stack Overflow**: `lib-db` 태그 사용
4. **Community Discord**: (링크 예정)

---

**Happy Debugging! 🐛**

---

<p align="center">
  ⬅️ <a href="./06_resilience_and_chaos.md">이전: 회복력</a>
  &nbsp;|&nbsp;
  <a href="./08_process_coordination.md">다음: 프로세스 코디네이션 ➡️</a>
</p>

<p align="center">
  🏠 <a href="../README.md">홈으로</a>
</p>
