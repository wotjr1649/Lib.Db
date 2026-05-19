# Lib.Db v2.3.0

**Extreme Performance Data Access Library for .NET 10+**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/badge/NuGet-Latest-blue)](https://www.nuget.org/packages/Lib.Db/)
[![AOT Ready](https://img.shields.io/badge/Native_AOT-Ready-green)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## v2.3.0 소개

`Lib.Db` v2.3.0은 .NET 10 애플리케이션을 위한 **고성능 SQL Server 데이터 액세스 라이브러리**입니다.

- **Fluent API Only**: `IDbSession` 단일 진입점에서 3-Stage Fluent API로 모든 쿼리를 실행합니다
- **DbResult\<T>**: 예외 대신 결과 타입으로 성공/실패를 구분합니다 (패턴 매칭 지원)
- **멀티 DB**: `ConnectionStringNames` 리스트로 N개 DB를 동시 지원합니다
- **Low-Allocation**: `Span<T>`, `ArrayPool`, runtime-first TVP fast path로 힙 할당을 줄입니다
- **AOT-First TVP**: static column-shape 등록 경로로 Native AOT 친화 TVP 바인딩을 제공합니다
- **Resilience**: Polly v8 파이프라인 내장으로 자동 재시도 및 Circuit Breaker를 제공합니다

---

## v2.3.0 최신 개선 사항

v2.3.0은 별도 `Lib.Db.TvpGen` 패키지 없이 단일 `Lib.Db` 런타임에서 TVP를 바인딩하는 경로를 추가합니다.

| 개선 | 내용 |
|---|---|
| Runtime TVP 기본 API | `LibDb.Tvp("dbo.TypeName", rows)`로 명시 TVP wrapper를 만들고 일반 스칼라 파라미터와 함께 전달합니다. |
| Static-shape AOT fast path | `options.Tvp.Map<T>(...).Column(...)`으로 SQL 메타데이터와 static getter를 등록하면 반복 호출에서 `IEnumerable<SqlDataRecord>` fast path를 사용합니다. |
| Schema-adaptive descriptor | `db.UseSchema(...).GetTvpAsync(...)`로 DB TVP descriptor를 조회한 뒤 `LibDb.Tvp(descriptor, rows, TvpBindingPolicy.Adaptive)`로 nullable/default-safe drift만 보정할 수 있습니다. |
| Benchmark matrix | narrow/wide TVP 모두 generated accessor baseline, runtime object streaming, runtime registered fast path를 비교합니다. 기본 row count는 `100`, `1_000`, `10_000`이고 `LIBDB_BENCHMARK_SCALE=Full`일 때 `100_000`을 추가합니다. |
| ResultSet 이름 convention | 기본 DTO 매퍼가 `CELL_NO`, `cell_no`, `CellNo`처럼 SQL Server에서 흔한 UPPER_SNAKE/snake_case 컬럼을 PascalCase 프로퍼티와 매핑합니다. exact match를 우선하고, 정규화 충돌은 자동 매핑하지 않습니다. |
| `[DbResult]` reader 호환성 | Source Generator가 `Map(DbDataReader)`를 생성하고 `Map(SqlDataReader)` 호환 overload를 유지합니다. Diagnostic monitor의 `MonitoredSqlDataReader` wrapper에서도 generated result mapper가 동작합니다. |
| `DateOnly`/`TimeOnly` 파라미터 | Raw SQL 및 SP 메타데이터 바인딩 모두에서 `DateOnly -> SQL date`, `TimeOnly -> SQL time` 경로를 명시 지원합니다. |
| 검증 DB 회귀 테스트 | `verify` 스키마에 전용 테이블/SP/test data를 배포해 위 세 동작과 `SET QUOTED_IDENTIFIER ON` 기반 computed column index 생성까지 실제 SQL Server에서 검증합니다. |
| 보안 검토 | 변경 diff 기준 Codex Security 리뷰 결과 P0/P1 security blocker 없음. Raw SQL 정책은 guardrail이며 DB 권한 분리와 함께 사용해야 합니다. |

상세 내용은 [v2.3 런타임 TVP 설계](./docs/superpowers/specs/2026-05-17-libdb-v2.3-runtime-tvp-design.md)와 [v2.2.1 blocker fix report](./docs/v2.2.1-blocker-fixes.md)를 참조하세요.

---

## 빠른 시작

### 1. appsettings.json

```json
{
  "ConnectionStrings": {
    "Default": "<provide through user secrets, environment, or a local untracked file>"
  },
  "LibDb": {
    "ConnectionStringNames": ["Default"],
    "ConnectionSecurityProfile": "Production",
    "RawSqlPolicy": "DenyWriteText",
    "Mars": "ForceEnable",
    "EnableSchemaCaching": true,
    "EnableResilience": true,
    "EnableObservability": false
  }
}
```

### 2. DI 등록

```csharp
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLibDb(builder.Configuration);

IHost host = builder.Build();
await host.RunAsync();
```

코드 기반 설정을 사용하는 운영 서비스는 보안 기본값 프리셋을 함께 적용할 수 있습니다.

```csharp
builder.Services
    .AddLibDbOptions(options =>
    {
        options.ConnectionStringNames = ["Default"];
        options.ConnectionStrings["Default"] = builder.Configuration.GetConnectionString("Default")!;
    })
    .UseProductionSecurityDefaults();
```

### 3. 사용

```csharp
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;

public sealed class UserRepository(IDbSession session)
{
    public record User(int Id, string Name, string Email);

    public async Task<User?> GetUserAsync(int id)
    {
        DbResult<User?> result = await session.Default
            .Procedure("dbo.usp_GetUser")
            .With(new { Id = id })
            .QuerySingleAsync<User>();

        return result.IsSuccess ? result.Value : null;
    }

    public async Task<int> RegisterAsync(string name, string email)
    {
        DbResult<int?> result = await session.Default
            .Procedure("dbo.usp_RegisterUser")
            .With(new { Name = name, Email = email })
            .ExecuteScalarAsync<int>();

        return result.IsSuccess ? result.Value ?? 0 : -1;
    }
}
```

---

## Runtime TVP (v2.3.0)

### B. 기본 명시 API

TVP는 일반 파라미터 객체 안에서 스칼라 값과 함께 전달합니다. `LibDb.Tvp(...)`는 TVP type name을 `TvpTypeName.Parse`로 검증한 뒤 명시 wrapper를 만들며, 등록되지 않은 임의 `IEnumerable<T>`를 TVP로 추측하지 않습니다.

```csharp
await db.Exec("dbo.SaveOrder", new
{
    OrderId = orderId,
    RequestedBy = requestedBy,
    Rows = LibDb.Tvp("dbo.T_OrderItem", orderItems)
});
```

### Runtime schema-adaptive descriptor API

DB TVP shape가 nullable/default-safe 범위에서 바뀔 수 있는 경로는 descriptor를 명시합니다. `Adaptive`는 컬럼 누락, 타입 불일치, 필수값 누락을 숨기는 모드가 아니며, 안전하다고 정의된 drift만 보정합니다.

```csharp
TvpSchemaDescriptor descriptor = await db
    .UseSchema("Verification")
    .GetTvpAsync("dbo.T_OrderItem", ct);

await db.Use("Verification")
    .Procedure("dbo.SaveOrder")
    .With(new
    {
        OrderId = orderId,
        Rows = LibDb.Tvp(descriptor, orderItems, TvpBindingPolicy.Adaptive)
    })
    .ExecuteAsync(ct);
```

### C. 등록 fast path

반복 호출과 Native AOT 대상 서비스는 static column-shape를 등록합니다. 이 경로는 TVP binding 중 런타임 property discovery나 expression compile에 의존하지 않는 고성능 경로입니다.

```csharp
builder.Services.AddLibDb(options => options.Tvp
    .Map<OrderItem>("dbo.T_OrderItem")
    .Column("Id", SqlDbType.Int, static x => x.Id)
    .Column("Sku", SqlDbType.NVarChar, static x => x.Sku, size: 64)
    .Column("Qty", SqlDbType.Int, static x => x.Qty)
    .Column("Price", SqlDbType.Decimal, static x => x.Price, precision: 18, scale: 2));

await db.Exec("dbo.SaveOrder", new
{
    OrderId = orderId,
    RequestedBy = requestedBy,
    Rows = orderItems
});
```

Reflection 기반 object streaming fallback은 로컬 개발과 JIT-first 앱을 위한 편의 경로입니다. Native AOT release gate에서는 static-shape fast path와 AOT smoke 결과를 기준으로 판단하고, fallback 경로의 trim/AOT warning은 숨기지 않습니다.

### Mixed parameters, targeted flush, benchmarks

스칼라, output/return, provider raw parameter, TVP는 같은 파라미터 객체에서 함께 사용할 수 있습니다. TVP schema cache가 drift 뒤 stale하다고 확인되면 `db.Schema.FlushTvpAsync("dbo.T_OrderItem", cancellationToken)` 또는 `db.UseSchema("Verification").FlushTvpAsync("dbo.T_OrderItem", cancellationToken)`로 해당 TVP를 갱신합니다. 로컬 프로세스에서는 TVP 이름 단위로 HybridCache 항목, snapshot, negative cache, descriptor row-accessor cache를 정리합니다.

벤치마크 실행 전 연결 문자열 값은 출력하지 말고 `ConnectionStrings:Benchmark` 또는 `LIBDB_BENCHMARK_CONNECTION` 존재 여부만 확인합니다.

```powershell
pwsh -NoProfile -File .\Tools\verification\Invoke-LibDbV230Verification.ps1 -BenchmarkJob Dry
pwsh -NoProfile -File .\Tools\coverage\Invoke-LibDbCoverage.ps1
pwsh -NoProfile -File .\Tools\benchmark\Invoke-LibDbBenchmarks.ps1 -Job Short -Filter '*TvpBenchmarks*'
```

세부 release gate와 산출물 위치는 `docs/v2.3.0-verification.md`를 기준으로 관리합니다.

---

## 주요 기능

| 기능 | 설명 |
|---|---|
| **3-Stage Fluent API** | `IProcedureStage` -> `IParameterStage` -> `IExecutionStage` 타입 안전 체이닝 |
| **DbResult\<T>** | 예외 없이 성공/실패를 구분하는 불변 결과 타입 (Deconstruct, 패턴 매칭) |
| **멀티 DB** | `session.Use("DB1")` / `session.Use("DB2")` 병렬 실행 |
| **트랜잭션** | `BeginTransactionAsync` -> CommitAsync/RollbackAsync (자동 롤백) |
| **보간 SQL 파라미터화** | `SqlInterpolated(...)`로 보간 값 인수를 `@pN` 파라미터로 바인딩 |
| **Runtime TVP** | `LibDb.Tvp(...)`, `TvpShape<T>`, registered fast path로 TVP를 단일 런타임 패키지에서 바인딩 |
| **Native AOT** | static column-shape 등록 경로를 AOT 기준 fast path로 사용하고 reflection fallback warning은 명시 관리 |
| **Polly v8 Resilience** | 자동 재시도, Circuit Breaker, Deadlock 처리 내장 |
| **L1+L2 캐시** | MemoryCache + SharedMemoryCache(MMF) 하이브리드 스키마 캐싱 |
| **스키마 워밍업** | 앱 시작 시 SP 메타데이터 사전 로딩 (Include/Exclude 패턴) |
| **OpenTelemetry** | ActivitySource/Meter 통합 메트릭 수집 |
| **MARS 정책** | `MarsPolicy` (Disabled/Auto/ForceEnable) — ConnectionString 자동 보정 |
| **Raw SQL 정책** | `RawSqlPolicy`로 Text 명령 전체 차단 또는 위험 토큰 기반 쓰기/권한/운영 계열 guardrail 적용 |
| **연결 보안 프로필** | `ConnectionSecurityProfile.Production`으로 암호화, 인증서 검증, 고권한 로그인 사용 검증 |
| **SQL 이름 convention 매핑** | `CELL_NO`/`cell_no` 컬럼을 `CellNo` 프로퍼티에 보수적으로 매핑 |
| **DateOnly/TimeOnly 바인딩** | SQL Server `date`/`time` 파라미터를 Raw SQL/SP 경로에서 일관 처리 |

---

## 성능

| 시나리오 | Dapper | EF Core | Lib.Db v2.2.1 | 개선율 |
|---|---:|---:|---:|:---:|
| 단순 조회 (1,000건) | 12.3ms | 18.7ms | **8.1ms** | **+34%** |
| 메모리 사용량 | 1.23MB | 2.45MB | **0.78MB** | **-37%** |
| GC Gen0 수집 | 150회 | 320회 | **28회** | **-81%** |

*위 수치는 예시이며, 실제 환경에 따라 다를 수 있습니다.*

---

## v2.2.0 변경 요약

### v2.1 → v2.2 주요 변경

| 항목 | v2.1 | v2.2 |
|---|---|---|
| MARS 정책 | 수동 설정 필수 | `MarsPolicy` (Auto/ForceEnable/Disabled) |
| OTel 설정 | `EnableOpenTelemetry` + `EnableObservability` 이중 | `EnableObservability` 단일화 |
| Nullable 매핑 | DB NULL → 기본값(0) 버그 | DB NULL → `null` 정상 설정 |
| date/time 매핑 | `DateTime`/`TimeSpan` | `DateOnly`/`TimeOnly` |
| BulkInsert | Reflection `GetValue` | Expression Tree 컴파일 getter |
| Generator 성능 | Compilation 전체 전달 | bool 추출로 재실행 최소화 |
| HealthCheck | 1초 하드코딩 | `HealthCheckThrottleSeconds` 옵션 반영 |

### v1 → v2 주요 변경

| 항목 | v1 | v2 |
|---|---|---|
| 진입점 | `IDbContext` | `IDbSession` |
| 실행 경로 | 13개 | 5개 (통합) |
| 에러 처리 | `throw` 예외 | `DbResult<T>` 결과 타입 |
| DB 연결 | `ConnectionStringName` (단수) | `ConnectionStringNames` (복수 리스트) |
| 트랜잭션 결과 | void / 예외 | `DbResult<bool>` |
| API 스타일 | 다중 인터페이스 | Fluent API Only |

---

## 호환성

| Platform | .NET | SQL Server | Status |
|---|---|---|:---:|
| Windows | .NET 10 | SQL Server 2016+ | 지원 |
| Linux | .NET 10 | SQL Server 2017+ | 지원 |
| macOS | .NET 10 | SQL Server 2017+ | 지원 |
| Native AOT | .NET 10 | 모든 버전 | 지원 |

---

## 기술 문서

### 핵심 가이드
- **[01. 가이드](./docs/01_guide.md)** -- 아키텍처, 설정, Fluent API, 마이그레이션
- **[02. 고급 기능](./docs/02_advanced.md)** -- TVP, AOT, 성능, Resilience, 캐싱
- **[03. API 레퍼런스](./docs/03_api_reference.md)** -- 전체 Public API (v2.2.1 반영)
- **[04. 운영 가이드](./docs/04_operations.md)** -- 트러블슈팅, 에러 코드, 체크리스트
- **[v2.2.1 blocker fix report](./docs/v2.2.1-blocker-fixes.md)** -- result mapping, generated mapper, DateOnly 회귀 검증 상세
- **[v2.1→v2.2 마이그레이션](./docs/01_guide.md#8-v21--v22-마이그레이션)** -- Breaking Changes, 신규 기능, 개선 사항

### Runtime TVP
- **[v2.3 런타임 TVP 설계](./docs/superpowers/specs/2026-05-17-libdb-v2.3-runtime-tvp-design.md)** -- source generator 없이 `Lib.Db` 단일 패키지에서 TVP를 바인딩하는 설계
- **[v2.3 AOT/TVP 보안 risk ledger](./docs/security/libdb-v2.3-aot-tvp-risk-ledger.md)** -- identifier, artifact, AOT fallback, schema drift, flush 권한 위험 추적

---

## NuGet 패키지

- `Lib.Db` -- 런타임 라이브러리와 runtime-first TVP 바인딩을 제공하는 단일 NuGet 패키지

---

## 라이선스

[MIT License](LICENSE)

<p align="center">
  Developed by <strong>김재석</strong>
</p>
