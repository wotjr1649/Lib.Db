# TypeMappingRegistry 아키텍처

**역할**: C# ↔ SQL Server 타입 매핑의 **단일 진실 원천** (Single Source of Truth)  
**버전**: v1.0  
**파일**: `Lib.Db.TvpGen/TypeMappingRegistry.cs`

---

## 🏗️ 설계 철학 (Design Philosophy)

### 1. SSOT (Single Source of Truth)
과거에는 `TvpAccessorGenerator`와 `ResultAccessorGenerator`가 각자 타입 매핑 로직을 가지고 있어 약 90줄 이상의 중복 코드와 정합성 문제가 발생했습니다. v1.0에서는 `TypeMappingRegistry`를 도입하여 모든 매핑 규칙을 한곳으로 통합했습니다. 이제 매핑 로직 변경 시 이 파일 하나만 수정하면 모든 제너레이터에 일관되게 반영됩니다.

### 2. FullyQualified Stability
`type.Name`에 의존하는 방식은 `System.Guid`와 `MyApp.Guid`를 구별하지 못하는 치명적인 결함이 있었습니다. v1.0은 `SymbolDisplayFormat.FullyQualifiedFormat`을 사용하여 모든 타입을 `global::System.Guid`와 같이 절대 경로로 식별합니다. 이는 네임스페이스 충돌을 100% 방지하고 컴파일 안정성을 보장합니다.

---

## 🔑 주요 기능 (Key Features)

### 1. GetSqlDbTypeName
C# 타입을 SQL Server `SqlDbType` 문자열로 변환합니다.

```csharp
public static string GetSqlDbTypeName(ITypeSymbol type, bool useDatetime2 = false)
{
    var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return fullName switch
    {
        "global::System.Int32" => "Int",
        "global::System.String" => "NVarChar",
        "global::System.DateTime" => useDatetime2 ? "DateTime2" : "DateTime", // 옵션 지원
        "global::System.Guid" => "UniqueIdentifier",
        "global::System.DateOnly" => "Date", // .NET 10
        // ...
        _ => "Variant" // 미지원 타입 (TVP004 에러 유발)
    };
}
```

### 2. GetReaderValueExpression
`DbDataReader`에서 값을 읽어오는 **최적의 C# 표현식**을 생성합니다. 성능을 위해 가능한 한 전용 메서드(`GetInt32` 등)를 사용하고, 불가능할 때만 제네릭 `GetFieldValue<T>`를 사용합니다.

```csharp
public static string GetReaderValueExpression(ITypeSymbol type, string ordVar)
{
    // 1. 기본형 최적화
    if (type.SpecialType == SpecialType.System_Int32) 
        return $"r.GetInt32({ordVar})";

    // 2. Enum 처리 (Cast)
    if (IsEnum(type)) 
        return $"(global::MyApp.Status)r.GetInt16({ordVar})";

    // 3. Fallback (FullyQualified)
    return $"r.GetFieldValue<{FullyQualifiedName}>({ordVar})";
}
```

---

## 🚀 전략적 결정 (Strategic Decisions)

### 왜 DateTime2가 기본값이 아닌가요?
하위 호환성을 위해서입니다. 많은 레거시 시스템이 여전히 `DateTime` (SQL Server `DATETIME`) 타입을 사용합니다. `UseDatetime2 = true` 옵션을 명시적으로 켠 경우에만 `DATETIME2`로 매핑하여, 개발자가 의도적으로 정밀도를 선택하도록 설계했습니다.

### 왜 Int128은 지원하지 않나요?
SQL Server에는 128비트 정수에 대응하는 네임티브 타입이 없습니다. `Decimal(38,0)` 등으로 우회할 수 있으나, 이는 암묵적인 성능 저하를 유발하므로 현재는 명시적으로 지원하지 않음(`Variant` 반환)으로써 개발자가 인지하도록 합니다.

---

## 🤖 AI 에이전트 참고 사항
*   **파일 수정 시**: 새로운 타입(예: `TimeSpan`)을 추가하려면 반드시 `GetSqlDbTypeName`과 `GetReaderValueExpression` 두 메서드 모두에 케이스를 추가해야 합니다.
*   **FullyQualified 필수**: `TypeMappingRegistry` 내부에서는 절대 단축형 이름(`List<int>`)을 사용하지 마십시오. 항상 `global::System.Collections.Generic.List<int>` 형식을 유지해야 합니다.
