# 마이그레이션 가이드 (Migration Guide)

<!-- AI_CONTEXT: START -->
<!-- ROLE: MIGRATION_GUIDE -->
<!-- AI_CONTEXT: END -->

기존 ORM/데이터 액세스 라이브러리에서 `Lib.Db`로 마이그레이션하는 방법을 안내합니다.

---

## 목차

1. [Dapper → Lib.Db](#1-dapper--libdb)
2. [Entity Framework Core → Lib.Db](#2-entity-framework-core--libdb)
3. [ADO.NET → Lib.Db](#3-adonet--libdb)
4. [Breaking Changes 주의사항](#4-breaking-changes-주의사항)

---

## 1. Dapper → Lib.Db

### 1-1. 쿼리 패턴 변환표

| Dapper | Lib.Db | 설명 |
|:---|:---|:---|
| `conn.QueryAsync<T>(sql)` | `db.Default.Sql(sql).QueryAsync<T>()` | 비동기 조회 |
| `conn.QuerySingleAsync<T>(sql)` | `db.Default.Sql(sql).QuerySingleAsync<T>()` | 단일 행 조회 |
| `conn.ExecuteScalarAsync<T>(sql)` | `db.Default.Sql(sql).ExecuteScalarAsync<T>()` | 스칼라 값 조회 |
| `conn.ExecuteAsync(sql)` | `db.Default.Sql(sql).ExecuteAsync()` | 명령 실행 |

### 1-2. Connection 관리 차이

**Dapper**:
```csharp
using (var conn = new SqlConnection(connectionString))
{
    await conn.OpenAsync();
    var users = await conn.QueryAsync<User>("SELECT * FROM Users", param);
}  // 수동 Dispose
```

**Lib.Db**:
```csharp
// 연결 관리 자동
var users = await db.Default
    .Sql("SELECT * FROM Users")
    .QueryAsync<User>()
    .ToListAsync();
```

### 1-3. 파라미터 바인딩

**Dapper**:
```csharp
var param = new { UserId = 123, Name = "Alice" };
await conn.QueryAsync<User>("SELECT * FROM Users WHERE Id = @UserId AND Name = @Name", param);
```

**Lib.Db**:
```csharp
await db.Default
    .Sql("SELECT * FROM Users WHERE Id = @UserId AND Name = @Name")
    .With(new { UserId = 123, Name = "Alice" })
    .QueryAsync<User>();

// 또는 Interpolated String (권장)
await db.Default
    .Sql($"SELECT * FROM Users WHERE Id = {123} AND Name = {"Alice"}")
    .QueryAsync<User>();
```

### 1-4. TVP vs DynamicParameters

**Dapper**:
```csharp
var table = new DataTable();
table.Columns.Add("Id", typeof(int));
table.Columns.Add("Name", typeof(string));
// ... 수동 행 추가 ...

var param = new DynamicParameters();
param.Add("@Users", table.AsTableValuedParameter("dbo.Tvp_User"));
await conn.ExecuteAsync("dbo.usp_BulkInsert", param, commandType: CommandType.StoredProcedure);
```

**Lib.Db**:
```csharp
[TvpRow(TypeName = "dbo.Tvp_User")]
public record UserDto(int Id, string Name);

var users = new[] { new UserDto(1, "Alice"), new UserDto(2, "Bob") };

// 자동 TVP 변환
await db.Default
    .Procedure("dbo.usp_BulkInsert")
    .With(new { Users = users })
    .ExecuteAsync();
```

### 1-5. 다중 결과셋

**Dapper**:
```csharp
using var multi = await conn.QueryMultipleAsync("dbo.usp_GetDashboard");
var users = await multi.ReadAsync<User>();
var orders = await multi.ReadAsync<Order>();
```

**Lib.Db**:
```csharp
await using var multi = await db.Default
    .Procedure("dbo.usp_GetDashboard")
    .QueryMultipleAsync();
var users = await multi.ReadAsync<User>();
var orders = await multi.ReadAsync<Order>();
```

---

## 2. Entity Framework Core → Lib.Db

### 2-1. LINQ vs SQL

**EF Core**:
```csharp
var users = await dbContext.Users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .Take(10)
    .ToListAsync();
```

**Lib.Db**:
```csharp
var users = await db.Default
    .Sql(@"
        SELECT TOP 10 * 
        FROM Users 
        WHERE IsActive = 1 
        ORDER BY Name
    ")
    .QueryAsync<User>()
    .ToListAsync();
```

### 2-2. Include (Eager Loading)

**EF Core**:
```csharp
var orders = await dbContext.Orders
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .ToListAsync();
```

**Lib.Db**:
```csharp
// Option 1: JOIN
var orders = await db.Default
    .Sql(@"
        SELECT o.*, c.Name AS CustomerName, ...
        FROM Orders o
        INNER JOIN Customers c ON o.CustomerId = c.Id
        LEFT JOIN OrderItems i ON o.Id = i.OrderId
    ")
    .QueryAsync<OrderWithDetails>()
    .ToListAsync();

// Option 2: Multiple Result Sets
await using var multi = await db.Default
    .Procedure("dbo.usp_GetOrdersWithDetails")
    .QueryMultipleAsync();
var orders = await multi.ReadAsync<Order>();
var customers = await multi.ReadAsync<Customer>();
var items = await multi.ReadAsync<OrderItem>();
```

### 2-3. Change Tracking

**EF Core**:
```csharp
var user = await dbContext.Users.FindAsync(1);
user.Name = "Updated";
await dbContext.SaveChangesAsync();  // 자동 UPDATE
```

**Lib.Db**:
```csharp
// 명시적 UPDATE
await db.Default
    .Sql("UPDATE Users SET Name = @Name WHERE Id = @Id")
    .With(new { Id = 1, Name = "Updated" })
    .ExecuteAsync();
```

### 2-4. Migrations

**EF Core**:
```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

**Lib.Db**:
- 직접 SQL 스크립트 관리
- 또는 DbUp, FluentMigrator 같은 별도 도구 사용

### 2-5. 보완적 사용 (권장)

```csharp
// EF Core: 복잡한 도메인 모델
var order = new Order { Customer = customer, Items = items };
dbContext.Orders.Add(order);
await dbContext.SaveChangesAsync();

// Lib.Db: 대량 작업, 성능 중시
await db.Default.BulkInsertAsync("OrderItems", items);

// EF Core: 읽기 작업
var orders = await dbContext.Orders
    .Include(o => o.Customer)
    .Where(o => o.Date > DateTime.Today)
    .ToListAsync();
```

---

## 3. ADO.NET → Lib.Db

### 3-1. SqlCommand 패턴

**ADO.NET**:
```csharp
using var conn = new SqlConnection(connectionString);
await conn.OpenAsync();

using var cmd = new SqlCommand("SELECT * FROM Users WHERE Id = @Id", conn);
cmd.Parameters.AddWithValue("@Id", 123);

using var reader = await cmd.ExecuteReaderAsync();
var users = new List<User>();
while (await reader.ReadAsync())
{
    users.Add(new User
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1)
    });
}
```

**Lib.Db**:
```csharp
var users = await db.Default
    .Sql(sql:"SELECT * FROM Users WHERE Id = @Id")
    .With(new { Id = 123 })
    .QueryAsync<User>()
    .ToListAsync();
```

**개선점**:
- ✅ Connection 자동 관리
- ✅ Parameter 안전 바인딩
- ✅ 결과 자동 매핑
- ✅ Async/Await 네이티브 지원

### 3-2. 수동 매핑 제거

**ADO.NET**:
```csharp
while (await reader.ReadAsync())
{
    users.Add(new User
    {
        Id = reader.GetInt32(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        // ... 모든 필드 수동 매핑
    });
}
```

**Lib.Db**:
```csharp
// Source Generator가 자동 매핑
.QueryAsync<User>()
```

---

## 4. Breaking Changes 주의사항

### 4-1. 비동기 전용

`Lib.Db`는 동기 메서드를 제공하지 않습니다.

```csharp
// ❌ 불가능
var users = db.Default.Sql("...").QueryAsync<User>().Result;

// ✅ 비동기 사용
var users = await db.Default.Sql("...").QueryAsync<User>().ToListAsync();

// ✅ 동기가 필수이면 GetAwaiter().GetResult()
var users = db.Default.Sql("...").QueryAsync<User>().ToListAsync().GetAwaiter().GetResult();
```

### 4-2. Connection String 관리

**기존**:
```csharp
var connectionString = Configuration.GetConnectionString("Main");
var conn = new SqlConnection(connectionString);
```

**Lib.Db**:
```json
{
  "LibDb": {
    "ConnectionStrings": {
      "Main": "Server=...;Database=...;"
    }
  }
}
```

### 4-3. Transaction Scope

**기존 (ADO.NET)**:
```csharp
using var conn = new SqlConnection(connectionString);
await conn.OpenAsync();
using var transaction = conn.BeginTransaction();
try
{
    // 명령 실행
    transaction.Commit();
}
catch
{
    transaction.Rollback();
}
```

**Lib.Db**:
```csharp
using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    await db.Default.Sql("INSERT ...").ExecuteAsync();
    await db.Default.Sql("UPDATE ...").ExecuteAsync();
    scope.Complete();
}
```

### 4-4. Source Generator 필수

**기존**: 런타임 리플렉션으로 매핑.

**Lib.Db**: `Lib.Db.TvpGen` 패키지 필수 설치.

```xml
<PackageReference Include="Lib.Db.TvpGen" Version="2.0.0" />
```

---

## 마이그레이션 체크리스트

- [ ] `Lib.Db` 및 `Lib.Db.TvpGen` 패키지 설치
- [ ] `appsettings.json`에 `ConnectionStrings` 설정
- [ ] DI 컨테이너에 `AddHighPerformanceDb` 등록
- [ ] `UseHighPerformanceDbAsync()` 호출 추가 (초기화)
- [ ] TVP 사용 시 `[TvpRow]` 어트리뷰트 적용
- [ ] 동기 메서드 → 비동기 변환
- [ ] Connection 수동 관리 → 자동 관리로 변경
- [ ] Transaction 패턴 → `TransactionScope`로 변경
- [ ] 성능 벤치마크 수행 (마이그레이션 효과 측정)

---

**단계적 마이그레이션 권장**: 전체를 한 번에 마이그레이션하기보다는, 성능이 중요한 부분부터 점진적으로 적용하세요.

---

<p align="center">
  ⬅️ <a href="./09_complete_api_reference.md">이전: API 레퍼런스</a>
  &nbsp;|&nbsp;
  <a href="./12_production_checklist.md">다음: 프로덕션 체크리스트 ➡️</a>
</p>

<p align="center">
  🏠 <a href="../README.md">홈으로</a>
</p>
