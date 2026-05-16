# Lib.Db v2.2.1

**Extreme Performance Data Access Library for .NET 10+**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/badge/NuGet-Latest-blue)](https://www.nuget.org/packages/Lib.Db/)
[![AOT Ready](https://img.shields.io/badge/Native_AOT-Ready-green)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## v2.2.1 소개

`Lib.Db` v2.2.1은 .NET 10 애플리케이션을 위한 **고성능 SQL Server 데이터 액세스 라이브러리**입니다.

- **Fluent API Only**: `IDbSession` 단일 진입점에서 3-Stage Fluent API로 모든 쿼리를 실행합니다
- **DbResult\<T>**: 예외 대신 결과 타입으로 성공/실패를 구분합니다 (패턴 매칭 지원)
- **멀티 DB**: `ConnectionStringNames` 리스트로 N개 DB를 동시 지원합니다
- **Low-Allocation**: `Span<T>`, `ArrayPool`, Source Generator 기반 경로로 힙 할당을 줄입니다
- **AOT-First**: Source Generator 기반으로 리플렉션 없이 Native AOT를 완벽 지원합니다
- **Resilience**: Polly v8 파이프라인 내장으로 자동 재시도 및 Circuit Breaker를 제공합니다

---

## 빠른 시작

### 1. appsettings.json

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=MyDb;User Id=app_user;Password=***;Encrypt=True;TrustServerCertificate=False;"
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

## 주요 기능

| 기능 | 설명 |
|---|---|
| **3-Stage Fluent API** | `IProcedureStage` -> `IParameterStage` -> `IExecutionStage` 타입 안전 체이닝 |
| **DbResult\<T>** | 예외 없이 성공/실패를 구분하는 불변 결과 타입 (Deconstruct, 패턴 매칭) |
| **멀티 DB** | `session.Use("DB1")` / `session.Use("DB2")` 병렬 실행 |
| **트랜잭션** | `BeginTransactionAsync` -> CommitAsync/RollbackAsync (자동 롤백) |
| **보간 SQL 파라미터화** | `SqlInterpolated(...)`로 보간 값 인수를 `@pN` 파라미터로 바인딩 |
| **Source Generator** | `[TvpRow]`, `[DbResult]` 어노테이션으로 컴파일 타임 코드 생성 |
| **Native AOT** | 리플렉션 제로, Shadow DTO 패턴으로 AOT 완벽 호환 |
| **Polly v8 Resilience** | 자동 재시도, Circuit Breaker, Deadlock 처리 내장 |
| **L1+L2 캐시** | MemoryCache + SharedMemoryCache(MMF) 하이브리드 스키마 캐싱 |
| **스키마 워밍업** | 앱 시작 시 SP 메타데이터 사전 로딩 (Include/Exclude 패턴) |
| **OpenTelemetry** | ActivitySource/Meter 통합 메트릭 수집 |
| **MARS 정책** | `MarsPolicy` (Disabled/Auto/ForceEnable) — ConnectionString 자동 보정 |
| **Raw SQL 정책** | `RawSqlPolicy`로 Text 명령 전체 차단 또는 위험 토큰 기반 쓰기/권한/운영 계열 guardrail 적용 |
| **연결 보안 프로필** | `ConnectionSecurityProfile.Production`으로 암호화, 인증서 검증, 고권한 로그인 사용 검증 |

---

## 성능

| 시나리오 | Dapper | EF Core | Lib.Db v2.2.0 | 개선율 |
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
- **[03. API 레퍼런스](./docs/03_api_reference.md)** -- 전체 Public API (v2.2.0 반영)
- **[04. 운영 가이드](./docs/04_operations.md)** -- 트러블슈팅, 에러 코드, 체크리스트
- **[v2.1→v2.2 마이그레이션](./docs/01_guide.md#8-v21--v22-마이그레이션)** -- Breaking Changes, 신규 기능, 개선 사항

### Source Generator
- **[Lib.Db.TvpGen](./Lib.Db.TvpGen/README.md)** -- TVP 자동 생성, DbDataReader 매핑, Track 5 알고리즘

---

## NuGet 패키지

- `Lib.Db` -- 런타임 라이브러리 (Source Generator 내장)
- `Lib.Db.TvpGen` -- Source Generator 단독 패키지

---

## 라이선스

[MIT License](LICENSE)

<p align="center">
  Developed by <strong>김재석</strong>
</p>
