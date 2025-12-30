# DateTime → DateTime2 마이그레이션 가이드

**대상**: Lib.Db.TvpGen 사용자  
**버전**: 2025-12-19  
**SQL Server**: 2008 이상

---

## 개요

SQL Server 2008부터 도입된 `DateTime2` 타입은 기존 `DateTime`보다 **더 넓은 범위**와 **높은 정밀도**를 제공합니다. `Lib.Db.TvpGen`은 `UseDatetime2` 옵션을 통해 `DateTime2`로의 점진적 마이그레이션을 지원합니다.

---

## DateTime vs DateTime2 비교

| 특성 | DateTime | DateTime2 |
|------|----------|-----------|
| **범위** | 1753-01-01 ~ 9999-12-31 | 0001-01-01 ~ 9999-12-31 |
| **정밀도** | 3.33ms (1/300초) | 100ns (1/10,000,000초) |
| **저장 크기** | 8 bytes (고정) | 6-8 bytes (정밀도에 따라) |
| **C# 호환** | DateTime | DateTime |
| **권장** | Legacy (SQL Server 2005 이하) | **SQL Server 2008+** |

### 주요 차이점

**1. 범위 확장**
```sql
-- DateTime: 1753년 이전 날짜 불가
INSERT INTO Events (CreatedAt) VALUES ('1752-12-31');  -- ❌ 오류

-- DateTime2: 0001년부터 가능
INSERT INTO Events (CreatedAt) VALUES ('0001-01-01');  -- ✅ 정상
```

**2. 정밀도 향상**
```csharp
// DateTime: 3.33ms 정밀도 → 반올림 발생
var dt = new DateTime(2025, 1, 1, 12, 0, 0, 123);  // .123초
// SQL Server DateTime 저장 시: 12:00:00.123 → 12:00:00.123 또는 12:00:00.127 (반올림)

// DateTime2: 100ns 정밀도 → 정확한 저장
// SQL Server DateTime2 저장 시: 12:00:00.1230000 (정확)
```

**3. 저장 효율**
```sql
-- DateTime2(3): 7 bytes (밀리초 정밀도, DateTime보다 1 byte 절약)
-- DateTime2(7): 8 bytes (100ns 정밀도, DateTime과 동일)
```

---

## 마이그레이션 필요성

### 마이그레이션을 고려해야 하는 경우

✅ **1. 역사적 데이터 저장**
- 1753년 이전 날짜 (예: 생년월일, 역사적 이벤트)

✅ **2. 고정밀 타임스탬프**
- 금융 거래, 로그 분석, IoT 센서 데이터

✅ **3. .NET DateTime 완벽 호환**
- C# DateTime의 전체 범위 활용

✅ **4. 미래 호환성**
- Microsoft 권장 타입 (SQL Server 2008+)

### 마이그레이션이 불필요한 경우

❌ **1. SQL Server 2005 이하**
- DateTime2 미지원

❌ **2. 기존 시스템 호환성**
- 레거시 시스템이 DateTime 의존

❌ **3. 정밀도 불필요**
- 일/시간 단위 데이터만 사용

---

## 단계별 마이그레이션 절차

### Phase 1: 분석 및 계획

#### 1.1 영향 분석
```sql
-- 현재 DateTime 컬럼 목록 확인
SELECT 
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    t.name AS TableName,
    c.name AS ColumnName,
    TYPE_NAME(c.user_type_id) AS DataType
FROM sys.columns c
INNER JOIN sys.tables t ON c.object_id = t.object_id
WHERE TYPE_NAME(c.user_type_id) = 'datetime'
ORDER BY SchemaName, TableName, ColumnName;

-- TVP 타입 확인
SELECT 
    tt.name AS TypeName,
    c.name AS ColumnName,
    TYPE_NAME(c.user_type_id) AS DataType
FROM sys.table_types tt
INNER JOIN sys.columns c ON tt.type_table_object_id = c.object_id
WHERE TYPE_NAME(c.user_type_id) = 'datetime'
ORDER BY tt.name, c.name;
```

#### 1.2 우선순위 결정
```
High: 고정밀 타임스탬프 필요 (거래, 로그)
Mid: 역사적 데이터 (1753년 이전)
Low: 일반적인 날짜/시간
```

---

### Phase 2: TVP 스키마 변경

#### 2.1 TVP 타입 백업
```sql
-- 기존 TVP 정의 스크립트 생성 (백업)
SELECT OBJECT_DEFINITION(type_table_object_id) AS Definition
FROM sys.table_types
WHERE name = 'T_Event';
```

#### 2.2 TVP 재생성
```sql
-- ✅ 방법 1: DROP & CREATE (권장)
-- 주의: TVP를 사용하는 SP를 먼저 DROP해야 함

-- 1단계: 의존 SP 확인
SELECT 
    OBJECT_NAME(referencing_id) AS DependentSP
FROM sys.sql_expression_dependencies
WHERE referenced_id = TYPE_ID('dbo.T_Event');

-- 2단계: 의존 SP 백업 및 DROP
-- (각 SP마다 OBJECT_DEFINITION으로 스크립트 백업)

-- 3단계: TVP DROP
DROP TYPE dbo.T_Event;

-- 4단계: TVP 재생성 (DateTime2)
CREATE TYPE dbo.T_Event AS TABLE
(
    Id INT,
    CreatedAt DATETIME2,  -- ✅ DateTime → DateTime2
    UpdatedAt DATETIME2,
    Name NVARCHAR(255)
);

-- 5단계: 의존 SP 재생성
-- (백업한 스크립트로 재생성)
```

```sql
-- ⚠️ 방법 2: 새 TVP 생성 (호환성 유지)
-- 기존 T_Event는 유지, T_Event_V2 신규 생성

CREATE TYPE dbo.T_Event_V2 AS TABLE
(
    Id INT,
    CreatedAt DATETIME2,
    UpdatedAt DATETIME2,
    Name NVARCHAR(255)
);

-- 점진적 마이그레이션: SP별로 T_Event → T_Event_V2 전환
```

---

### Phase 3: C# 코드 변경

#### 3.1 TvpRow 속성 업데이트
```csharp
// ✅ Before (DateTime)
[TvpRow(TypeName = "dbo.T_Event")]
public class EventRow
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Name { get; set; } = "";
}

// ✅ After (DateTime2)
[TvpRow(TypeName = "dbo.T_Event", UseDatetime2 = true)]  // ← 옵션 활성화
public class EventRow
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }  // C# 타입은 동일
    public DateTime UpdatedAt { get; set; }
    public string Name { get; set; } = "";
}
```

#### 3.2 생성 코드 검증
```csharp
// 생성된 코드 확인: obj/Debug/net10.0/generated/
// 헤더 토큰 확인
// TVPGEN:DATETIME_TYPE:DateTime2  ← DateTime2 사용 확인

// StaticValidator 확인
// expectedType: SqlDbType.DateTime2  ← DateTime2 검증
```

#### 3.3 컴파일 및 배포
```powershell
# 1. 빌드
dotnet build -c Release

# 2. 테스트
dotnet test

# 3. 배포
# (CI/CD 파이프라인 또는 수동 배포)
```

---

### Phase 4: 검증 및 모니터링

#### 4.1 스키마 검증
```sql
-- TVP 스키마 확인
SELECT 
    tt.name AS TypeName,
    c.name AS ColumnName,
    TYPE_NAME(c.user_type_id) AS DataType,
    c.precision,
    c.scale
FROM sys.table_types tt
INNER JOIN sys.columns c ON tt.type_table_object_id = c.object_id
WHERE tt.name = 'T_Event'
ORDER BY c.column_id;

-- 결과:
-- TypeName | ColumnName | DataType  | precision | scale
-- T_Event  | CreatedAt  | datetime2 | 27        | 7
```

#### 4.2 런타임 검증
```csharp
// StaticValidator가 자동으로 스키마 검증
// 불일치 시 TvpSchemaValidationException 발생

var events = new[]
{
    new EventRow
    {
        Id = 1,
        CreatedAt = new DateTime(0001, 1, 1),  // ✅ DateTime2는 0001년 지원
        UpdatedAt = DateTime.UtcNow,
        Name = "Test Event"
    }
};

await session.From("dbo.PR_Insert_Events")
    .WithTvpParameter("@Events", events)
    .ExecuteAsync();
// ✅ 성공: StaticValidator가 DateTime2 스키마 확인
```

#### 4.3 데이터 검증
```sql
-- 정밀도 확인
SELECT 
    Id,
    CreatedAt,
    DATEPART(NANOSECOND, CreatedAt) AS Nanoseconds
FROM Events
WHERE Id = 1;

-- 범위 확인
SELECT MIN(CreatedAt), MAX(CreatedAt) FROM Events;
```

---

## 위험 관리

### 리스크 1: TVP 의존성
**문제**: TVP DROP 시 의존 SP 모두 영향  
**완화**: 
- 의존성 사전 확인 (`sys.sql_expression_dependencies`)
- SP 백업 스크립트 생성
- 트랜잭션 사용 (가능 시)

### 리스크 2: 데이터 손실
**문제**: DateTime → DateTime2 변환 시 기존 데이터 손실 가능성  
**완화**:
- ✅ **안전**: DateTime2가 DateTime 상위 호환 (범위/정밀도 모두 ≥)
- 데이터 백업 필수

### 리스크 3: 성능 영향
**문제**: DateTime2(7)은 DateTime과 동일한 8 bytes  
**완화**:
- DateTime2(3) 사용 시 1 byte 절약 (밀리초 정밀도면 충분한 경우)
- 인덱스 재구성 필요 시 계획

### 리스크 4: 애플리케이션 호환성
**문제**: 배포 타이밍 불일치 (DB 먼저 vs 앱 먼저)  
**완화**:
```
1. 기본값 DateTime 유지 → 호환성 보장
2. DB 스키마 변경 (DateTime2)
3. 앱 배포 (UseDatetime2 = true)
4. 검증
```

---

## 롤백 계획

### 롤백 시나리오 1: C# 코드만 배포된 경우
```csharp
// UseDatetime2 = true 제거
[TvpRow(TypeName = "dbo.T_Event")]  // ← 기본값 DateTime
public class EventRow { ... }

// 재빌드 및 재배포
```

### 롤백 시나리오 2: DB 스키마도 변경된 경우
```sql
-- FallbackType 개념 사용 (DateTime2 → DateTime 자동 변환)
-- ⚠️ 1753년 이전 데이터 손실 주의

-- TVP 재생성
DROP TYPE dbo.T_Event;

CREATE TYPE dbo.T_Event AS TABLE
(
    Id INT,
    CreatedAt DATETIME,  -- ← DateTime으로 복구
    UpdatedAt DATETIME,
    Name NVARCHAR(255)
);
```

---

## 성능 고려사항

### 저장 크기 최적화
```sql
-- DateTime2 precision 선택
DATETIME2(0)  -- 1초,  6 bytes
DATETIME2(3)  -- 1ms,  7 bytes (DateTime보다 1 byte 절약)
DATETIME2(7)  -- 100ns, 8 bytes (기본값, DateTime과 동일)
```

### 인덱스 영향
```sql
-- DateTime → DateTime2 변경 시 인덱스 재구성 권장
ALTER INDEX IX_Events_CreatedAt ON Events REBUILD;
```

### 쿼리 성능
```sql
-- DateTime2는 DateTime과 동일한 성능
-- 범위/정밀도 향상으로 인한 오버헤드 없음
```

---

## 모범 사례

✅ **1. 신규 프로젝트**: DateTime2 기본 사용  
✅ **2. 레거시 시스템**: 점진적 마이그레이션 (우선순위 기반)  
✅ **3. 정밀도 설정**: 필요한 만큼만 (DateTime2(3) 권장)  
✅ **4. 테스트**: 개발/스테이징 환경 검증 후 프로덕션 배포  
✅ **5. 모니터링**: 배포 후 성능/에러 로그 확인  

---

## FAQ

**Q: UseDatetime2를 설정하지 않으면 어떻게 되나요?**  
A: 기본값은 `false`이며, DateTime으로 매핑됩니다. Breaking Change 방지.

**Q: 기존 DateTime TVP와 혼용 가능한가요?**  
A: 가능합니다. DTO별로 UseDatetime2 옵션을 독립적으로 설정 가능.

**Q: SQL Server 2005에서도 사용 가능한가요?**  
A: 불가능합니다. DateTime2는 SQL Server 2008+ 전용.

**Q: C# DateTime 타입을 변경해야 하나요?**  
A: 불필요합니다. C# DateTime은 동일하게 사용.

**Q: 성능 차이가 있나요?**  
A: DateTime2(7)은 DateTime과 동일한 8 bytes이며 성능 차이 없음.

---

## 참조

- [Microsoft Docs: datetime2 (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetime2-transact-sql)
- [SQL Server Data Type Best Practices](https://learn.microsoft.com/en-us/sql/relational-databases/tables/data-types)
- Lib.Db.TvpGen Implementation Plan: `implementation_plan.md`
- Lib.Db.TvpGen Walkthrough: `walkthrough.md`
