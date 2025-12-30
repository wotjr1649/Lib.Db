# Lib.Db.TvpGen 워크스루 (Walkthrough)

**버전**: v1.0 (Track 5 Optimized)  
**대상**: 신규 개발자 및 AI 에이전트

---

## 1. TVP 생성 및 전송 (Sending Data)

### 단계 1: DTO 정의
`[TvpRow]` 특성을 사용하여 DTO를 정의합니다. .NET 10의 `record`와 `collection expression`을 활용하면 더욱 간결합니다.

```csharp
using Lib.Db.Contracts.Models;

namespace MyApp.Models;

[TvpRow(TypeName = "dbo.T_UserBatch", UseDatetime2 = true)]
public record UserRow
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public DateTime JoinedAt { get; init; }
}
```

### 단계 2: 실행
`Lib.Db` 세션을 통해 데이터를 전송합니다.

```csharp
// .NET 10 Collection Expression
List<UserRow> users = 
[
    new() { Id = 101, Name = "Alice", JoinedAt = DateTime.UtcNow },
    new() { Id = 102, Name = "Bob", JoinedAt = DateTime.UtcNow }
];

// TVP 전송 (자동 매핑)
await db.Procedure("dbo.usp_ImportUsers")
        .WithTvp("Users", users)
        .ExecuteAsync();
```

---

## 2. 데이터 조회 (Reading Data)

### 단계 1: 결과 DTO 정의
`[DbResult]` 특성을 사용하며, **`partial` 키워드가 필수**입니다.

```csharp
using Lib.Db.Contracts.Mapping;

namespace MyApp.Models;

[DbResult]
public partial record UserDto
{
    public required int Id { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    
    // 기본 생성자 (필수)
    public UserDto() { }
}
```

### 단계 2: 실행

```csharp
var results = await db.Procedure("dbo.usp_GetUsers")
                      .QueryAsync<UserDto>();
                      
foreach (var user in results)
{
    Console.WriteLine($"User: {user.Username}");
}
```

---

## 3. 고급 기능: DateTime2 사용

SQL Server 2008 이상의 `DATETIME2` 타입을 사용하려면 옵션을 켜야 합니다.

```csharp
[TvpRow(TypeName = "dbo.T_Log", UseDatetime2 = true)] // <--- 옵션 활성화
public record LogRow
{
    public DateTime Timestamp { get; init; } // 이제 DATETIME2(7)로 매핑됨 (100ns 정밀도)
}
```

## 4. DB-First 개발 워크플로우 (JSON Schema)

DB 스키마가 먼저 정의된 경우, `libdb.schema.json`을 사용하여 DTO 코드를 자동 생성할 수 있습니다.

### 단계 1: 스키마 정의 (`libdb.schema.json`)

프로젝트 루트에 파일을 생성하고 Build Action을 `AdditionalFiles`로 설정해야 합니다.

```json
{
  "Tvps": {
    "dbo.T_OrderItem": [
      { "Name": "OrderId", "Type": "BigInt" },
      { "Name": "ProductId", "Type": "Int" },
      { "Name": "Price", "Type": "Decimal", "Precision": 18, "Scale": 2 }
    ]
  }
}
```

### 단계 2: DTO 선언

```csharp
using Lib.Db.Contracts.Models;

namespace MyApp.Models;

// "dbo.T_OrderItem" 스키마를 사용하여 속성 자동 생성
[GenerateTvpFromDb(TvpName = "dbo.T_OrderItem", UsePascalCase = true)]
public partial class OrderItemRow
{
    // 비워두면 자동 생성됨:
    // public long OrderId { get; set; }
    // public int ProductId { get; set; }
    // public decimal Price { get; set; }
}
```

---

## 5. 트러블슈팅

*   **컴파일 에러 `RES001`**: "Partial keyword missing" -> 클래스에 `partial` 키워드가 있는지 확인하세요.
*   **런타임 에러 `TvpSchemaValidationException`**: DB의 TVP 스키마 컬럼 순서/타입과 C# DTO 속성이 정확히 일치하는지 확인하세요. (순서 중요!)

---

## 🧭 다음 조치
이제 `Li.Db.TvpGen`을 사용하여 보일러플레이트 코드 없이 고성능 데이터 액세스 계층을 구축할 수 있습니다. 추가적인 성능 튜닝이 필요하다면 `docs/typemapping_architecture.md`를 참조하여 내부 동작을 이해하십시오.
