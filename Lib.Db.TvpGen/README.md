# Lib.Db.TvpGen

**고성능 TVP 및 DbDataReader 매핑 코드 자동 생성기 (Track 5 Optimized)**

[![.NET](https://img.shields.io/badge/.NET-10%2B-512BD4)](https://dotnet.microsoft.com/)
[![SQL Server](https://img.shields.io/badge/SQL%20Server-2008%2B-CC2927)](https://www.microsoft.com/sql-server)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## 개요

`Lib.Db.TvpGen`은 **Roslyn Source Generator**를 활용하여 SQL Server Table-Valued Parameters (TVP)와 DbDataReader 결과 매핑 코드를 컴파일 타임에 자동 생성합니다. 런타임 오버헤드를 제로화하고, **Track 5 하이브리드 알고리즘**을 통해 극한의 성능을 제공합니다.

### 핵심 기능

1. **Track 5 Hybrid Algorithm**:
    - **Small (12컬럼 이하)**: `Span.SequenceEqual` 기반 `else-if` 분기
    - **Large (12컬럼 초과)**: `FNV-1a` 해시 기반 `switch` 분기 (O(1) 근접)
2. **Zero Runtime Overhead**: 리플렉션, 박싱/언박싱, 딕셔너리 룩업 완전 제거
3. **TypeMappingRegistry (SSOT)**: C# <-> SQL 타입 매핑 규칙을 단일 진실 원천으로 통합
4. **SharedHashUtils**: TvpAccessorGenerator와 ResultAccessorGenerator가 공유하는 FNV-1a 해시 및 식별자 정리 유틸리티
5. **FullyQualified Reliability**: 모든 타입 참조에 `global::` 강제로 네임스페이스 충돌 원천 차단
6. **Modern .NET 10**: `DateOnly`, `TimeOnly`, `Half`, Primary Constructors, Collection Expressions 지원

---

## v2 DbResult 연동

v2에서 모든 실행 메서드는 `DbResult<T>`를 반환합니다. TvpGen이 생성하는 매핑 코드는 이 패턴과 자연스럽게 통합됩니다.

```csharp
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;

// 1. 입력: TVP 정의
[TvpRow(TypeName = "dbo.T_Product_V2", UseDatetime2 = true)]
public record ProductRow
{
    public int ProductId { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public DateTime CreatedAt { get; init; }
}

// 2. 출력: 결과 매핑 정의
[DbResult]
public partial record ProductDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public ProductDto() { }
}

// 3. 실행 (DbResult<T> 패턴)
public sealed class ProductService(IDbSession session)
{
    public async Task<List<ProductDto>> InsertAndGetAsync(List<ProductRow> products)
    {
        // TVP 전송 → DbResult<int>
        DbResult<int> insertResult = await session.Default
            .Procedure("dbo.usp_InsertProducts")
            .With(new { Products = products })
            .ExecuteAsync();

        if (!insertResult.IsSuccess) return [];

        // 결과 매핑 → DbResult<IAsyncEnumerable<ProductDto>>
        DbResult<IAsyncEnumerable<ProductDto>> queryResult = await session.Default
            .Procedure("dbo.usp_GetProducts")
            .QueryAsync<ProductDto>();

        if (!queryResult.IsSuccess) return [];

        List<ProductDto> result = [];
        await foreach (ProductDto dto in queryResult.Value!)
        {
            result.Add(dto);
        }
        return result;
    }
}
```

---

## 아키텍처

### TypeMappingRegistry (통합 SSOT)

`TypeMappingRegistry`는 모든 Generator의 타입 매핑 규칙을 중앙화합니다.

- `TvpAccessorGenerator`와 `ResultAccessorGenerator`가 동일한 매핑 규칙 사용
- `FullyQualifiedFormat`: 모든 타입이 `global::System.Int32`와 같이 완전 수식명으로 처리
- `DateTime2 Policy`: `UseDatetime2 = true` 명시 시에만 `DATETIME2` 매핑

### SharedHashUtils (통합 유틸리티)

기존에 TvpAccessorGenerator와 ResultAccessorGenerator에 중복되어 있던 유틸리티를 단일 클래스로 통합했습니다.

- **FNV-1a 해시**: Generator-Side 컴파일 타임 해시 계산
- **식별자 정리**: 컬럼명 정규화 및 안전한 C# 식별자 변환
- Generator 내부 전용이며, 런타임 코드에 임베딩되는 해시 함수와는 별개

### UnsafeAccessor & Serialization

`private` 필드나 `init-only` 프로퍼티에 접근하기 위해 .NET 8+의 `[UnsafeAccessor]` 기능을 사용합니다. 리플렉션 없이 고속 데이터 주입이 가능하며, `record` 타입의 불변성을 유지합니다.

---

## 타입 레퍼런스

### 기본 타입

| JSON Type | SQL Server Type | C# Type | Reader API |
|---|---|---|---|
| `Bit` | BIT | `bool` | `GetBoolean()` |
| `TinyInt` | TINYINT | `byte` | `GetByte()` |
| `SmallInt` | SMALLINT | `short` | `GetInt16()` |
| `Int` | INT | `int` | `GetInt32()` |
| `BigInt` | BIGINT | `long` | `GetInt64()` |
| `Real` | REAL | `float` | `GetFloat()` |
| `Float` | FLOAT | `double` | `GetDouble()` |
| `Decimal` | DECIMAL(18,2) | `decimal` | `GetDecimal()` |
| `NVarChar` | NVARCHAR(MAX) | `string` | `GetString()` |
| `VarBinary` | VARBINARY(MAX) | `byte[]` | - |

### 날짜/시간 타입

| JSON Type | SQL Server Type | C# Type | .NET 버전 |
|---|---|---|---|
| `DateTime` | DATETIME | `DateTime` | 기본 (SQL 2005+) |
| `DateTime2` | DATETIME2(7) | `DateTime` | 권장 (SQL 2008+) |
| `DateTimeOffset` | DATETIMEOFFSET | `DateTimeOffset` | 타임존 포함 |
| `Date` | DATE | `DateOnly` | .NET 6+ |
| `Time` | TIME | `TimeOnly` / `TimeSpan` | .NET 6+ |

### 고급 타입

| JSON Type | SQL Server Type | C# Type |
|---|---|---|
| `UniqueIdentifier` | UNIQUEIDENTIFIER | `Guid` |
| `Real` (Half) | REAL | `Half` |

---

## DB-First DTO 자동 생성

### 1. libdb.schema.json 작성

```json
{
  "Tvps": {
    "dbo.T_Product": [
      { "Name": "Id", "Type": "Int" },
      { "Name": "Name", "Type": "NVarChar" },
      { "Name": "Price", "Type": "Decimal", "Precision": 18, "Scale": 2 },
      { "Name": "CreatedAt", "Type": "DateTime2" }
    ]
  }
}
```

### 2. DTO 마킹

```csharp
[GenerateTvpFromDb(TvpName = "dbo.T_Product")]
public partial class ProductRow { }
```

`.csproj`에서 `AdditionalFiles`로 등록 필요:

```xml
<ItemGroup>
    <AdditionalFiles Include="libdb.schema.json" />
</ItemGroup>
```

---

## 생성 코드 위치

| Generator | 경로 |
|---|---|
| TvpAccessor | `obj/Debug/net10.0/generated/Lib.Db.TvpGen/Lib.Db.TvpGen.TvpAccessorGenerator/` |
| ResultAccessor | `obj/Debug/net10.0/generated/Lib.Db.TvpGen/Lib.Db.TvpGen.ResultAccessorGenerator/` |

### 스냅샷 토큰

```csharp
// <auto-generated/>
// TVPGEN:TVP:TRACK5              -- Track 5 알고리즘 사용 여부
// TVPGEN:ALGO:2025-12-18          -- 알고리즘 버전
// TVPGEN:DATETIME_TYPE:DateTime2  -- DateTime 매핑 전략
```

---

## 트러블슈팅

| Error ID | 원인 | 해결 |
|---|---|---|
| **TVP001** | 제네릭 타입 사용 | 구체적 클래스로 변경 |
| **TVP004** | 지원되지 않는 타입 | TypeMappingRegistry 지원 타입만 사용 |
| **RES001** | `partial` 누락 | DTO 클래스에 `partial` 키워드 추가 |
| **RES007** | `struct` 사용 | `class` 또는 `record class`로 변경 |

빌드 문제 시:

```bash
dotnet clean && dotnet build
```

`obj` 및 `bin` 폴더 삭제 후 재빌드가 필요할 수 있습니다.

---

## 관련 문서

- **[Lib.Db 가이드](../docs/01_guide.md)** -- v2 아키텍처, 설정, Fluent API
- **[고급 기능](../docs/02_advanced.md)** -- TVP 심층 가이드, AOT 호환성
- **[API 레퍼런스](../docs/03_api_reference.md)** -- DbResult, IExecutionStage 등 전체 API

---

<p align="center">
  Generated by <strong>Lib.Db.TvpGen</strong> for <strong>Productivity & Performance</strong>
</p>
