# Lib.Db (v1.1)

**Extreme Performance Data Access Library for .NET 10+**

<!-- AI_CONTEXT: START -->
<!-- ROLE: PROJECT_PORTAL -->
<!-- GUIDELINES: docs/01~12 technical modules are the spokes, this is the hub. -->
<!-- AI_CONTEXT: END -->

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/badge/NuGet-1.1.0-blue)](https://www.nuget.org/packages/Lib.Db/)
[![AOT Ready](https://img.shields.io/badge/Native_AOT-Ready-green)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Zero Allocation](https://img.shields.io/badge/Performance-Zero_Allocation-orange)]()

---

## ⚡ 핵심 가치 (Core Values)

`Lib.Db`는 현대적인 .NET 애플리케이션을 위한 **성능 집약적 SQL Server 데이터 액세스 라이브러리**입니다.

1.  **Zero-Allocation**: `ArrayPool`, `Span<T>`, `SqlInterpolatedStringHandler`를 통해 힙 메모리 할당을 최소화합니다.
2.  **AOT-First**: **Dual-Layer Configuration Strategy(Shadow DTO)**를 적용하여 바인딩 경고 없이 Native AOT를 완벽 지원합니다.
3.  **Resilience-by-Design**: Polly v8 파이프라인과 카오스 엔지니어링 도구가 내장되어 있어 안정적인 프로덕션 운영이 가능합니다.
4.  **Process Coordination**: 공유 메모리(MMF) 기반의 멀티 프로세스 캐시 동기화 및 자동 리더 선출을 지원합니다.

---

## 📊 성능 벤치마크 (Performance)

> [!NOTE]
> **상세 벤치마크 데이터는 향후 `docs/10_performance_benchmarks.md`에서 제공 예정**

| 시나리오 | Dapper | EF Core | Lib.Db | 개선율 |
|:---|---:|---:|---:|:---:|
| **단순 조회 (1,000건)** | 12.3ms | 18.7ms | **8.9ms** | **+28%** |
| **Bulk Insert (100K건)** | N/A | 45s | **4.2s** | **10배↑** |
| **메모리 사용량** | 1.23MB | 2.45MB | **0.85MB** | **-31%** |
| **GC Gen0 수집** | 150회 | 320회 | **32회** | **-78%** |

*위 수치는 예시이며, 실제 측정 결과는 환경에 따라 다를 수 있습니다.*

---

## 🔧 호환성 (Compatibility)

| Platform | .NET Version | SQL Server | Status |
|:---|:---|:---|:---:|
| **Windows** | .NET 10 | SQL Server 2016+ | ✅ 지원 |
| **Linux** | .NET 10 | SQL Server 2017+ | ✅ 지원 |
| **macOS** | .NET 10 | SQL Server 2017+ | ✅ 지원 |
| **Docker** | .NET 10 | Azure SQL | ✅ 지원 |
| **Native AOT** | .NET 10 | 모든 버전 | ✅ 완벽 지원 |

**NuGet Packages**:
- `Lib.Db` - 런타임 라이브러리 (v1.1.0)
- `Lib.Db.TvpGen` - Source Generator (v1.1.0)
  - Table-Valued Parameters (TVP) 자동 생성
  - DbDataReader → DTO 고성능 매핑 (Track 5 알고리즘)
  - Native AOT 완벽 지원 (리플렉션 제로)
  - 📘 **[상세 가이드 →](./Lib.Db.TvpGen/README.md)**

---

## 🏎️ 라이브러리 비교 (Comparison)

| 기능 | Dapper | EF Core | ADO.NET | **Lib.Db** |
|:---|:---:|:---:|:---:|:---:|
| **Reflection** | ✅ 사용 | ✅ 사용 | ❌ | **❌ (Source Gen)** |
| **Native AOT** | ⚠️ 제한적 | ❌ | ✅ | **✅ 완벽 지원** |
| **Bulk Ops** | ❌ | ⚠️ 제한 | ❌ | **✅ 최적화 지원** |
| **Zero-Alloc** | ❌ | ❌ | ⚠️ 부분 | **✅ 핵심 설계** |
| **Process IPC** | ❌ | ❌ | ❌ | **✅ MMF 기반** |
| **Resilience** | ❌ | ❌ | ❌ | **✅ Polly v8 내장** |

---

## 🚀 5분 시작 가이드 (Quick Start)

### 1. 서비스 등록 (권장)
`WebApplication`뿐만 아니라 콘솔/워커 서비스에서도 사용 가능한 **범용 호스트(Generic Host)** 패턴입니다.

```csharp
// Program.cs
using Microsoft.Extensions.Hosting;
using Lib.Db.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// 1. 범용 호스트 빌더 생성 (appsettings.json 자동 로드)
// .NET 10 호스트 패턴 권장
builder.Services.AddLibDb(builder.Configuration);

var host = builder.Build();

// 2. 애플리케이션 실행
// 스키마 워밍업은 SchemaWarmupService에서 자동으로 수행됩니다.
await host.RunAsync();
```

> **Tip:** 별도의 `IConfiguration` 로드 없이 `builder.Configuration`을 바로 전달하면 됩니다.

### 2. appsettings.json 구성
```json
{
  "LibDb": {
    "ConnectionStrings": {
      "Default": "Server=User_Server;Database=User_Db;User Id=user_id;Password=user_password;TrustServerCertificate=True;Encrypt=False;"
    },
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [1] 스키마 캐싱 및 워밍업 (Schema Caching & Warmup)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "EnableSchemaCaching": true,                    // 스키마 캐싱 활성화 (필수 권장)
    "SchemaRefreshIntervalSeconds": 60,             // 캐시 갱신 주기 (기본값: 60초)
    "PrewarmSchemas": [                             // 앱 시작 시 미리 로드할 스키마 목록
      "dbo"
    ],
    "PrewarmIncludePatterns": [],                   // 워밍업 포함 패턴 (비어있으면 전체)
    "PrewarmExcludePatterns": [                     // 워밍업 제외 패턴 (와일드카드 지원)
      "*_Test*",
      "*_Legacy*"
    ],

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [2] 실행 정책 (Execution Policy)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "StrictRequiredParameterCheck": true,           // 필수 파라미터 누락 시 즉시 예외 발생
    "EnableGeneratedTvpBinder": true,               // Source Generator 기반 고성능 TVP 바인더 사용
    "EnableResilience": true                        // Polly 기반 자동 회복(Retry/CircuitBreaker) 활성화
  }
}
```

### 3. Repository 패턴 사용 (.NET 10 Primary Constructors)
```csharp
using Lib.Db.Contracts;

// C# 14 Primary Constructor
public class UserRepository(IDbContext db)
{
    // Record 타입 (Entity)
    public record User(int Id, string Name, string Email);

    public async Task<User?> GetUserAsync(int id)
    {
        // String Interpolation Handler로 SQL Injection 방지 및 Zero-Allocation
        return await db.Default
            .Sql($"SELECT * FROM Users WHERE Id = {id}")
            .QuerySingleAsync<User>();
    }
    
    public async Task<int> RegisterUserAsync(string name, string email)
    {
        return await db.Default
            .Sql($"INSERT INTO Users (Name, Email) VALUES ({name}, {email}); SELECT SCOPE_IDENTITY();")
            .ExecuteScalarAsync<int>();
    }
}
```

---

## 📖 기술 백서 (Technical Whitepapers)

라이브러리의 각 주제에 대한 심층적인 기술 문서는 아래 모듈에서 확인할 수 있습니다.

### 핵심 가이드
*   **[01. 아키텍처 개요](./docs/01_architecture_overview.md)**
    *   시스템 구조, 핵심/보호 레이어 분리 원칙, 형태-의미 동형성, 주요 컴포넌트 목록.
*   **[02. 설치 및 구성](./docs/02_configuration_and_di.md)**
    *   NuGet 설치, DI 컨테이너 등록, `appsettings.json` 전체 옵션 가이드 (176개 설정 항목).
*   **[03. Fluent API 레퍼런스](./docs/03_fluent_api_reference.md)**
    *   `DbRequestBuilder` 사용법, 파라미터 바인딩, 비동기 쿼리 패턴 (656줄 완전 레퍼런스).

### 고급 주제
*   **[04. TVP & AOT 심층 가이드](./docs/04_tvp_and_aot.md)**
    *   Source Generator 동작 원리, `[TvpRow]` 정의, 고급 시나리오 5개, 트러블슈팅 10개.
    *   📦 **[Lib.Db.TvpGen 상세 문서 →](./Lib.Db.TvpGen/README.md)** - JSON Schema, Track 5 알고리즘, DateTime2 마이그레이션
*   **[05. 성능 최적화 원리](./docs/05_performance_optimization.md)**
    *   Zero-Allocation, `Span<T>`, ArrayPool, HybridCache 전략, 벤치마킹 방법.
*   **[06. 회복력 및 카오스 엔지니어링](./docs/06_resilience_and_chaos.md)**
    *   Polly v8 파이프라인, Transient Error 목록, Circuit Breaker 상세, 카오스 시나리오.

### 운영 가이드
*   **[07. 트러블슈팅 및 FAQ](./docs/07_troubleshooting.md)**
    *   자주 묻는 질문 20개, SQL Exception 분석, 성능 문제 진단, Connection Pool 관리.
*   **[08. 프로세스 코디네이션](./docs/08_process_coordination.md)**
    *   공유 메모리 아키텍처, 리더 선출, Epoch 기반 동기화, 자동 유지보수 (591줄 상세 명세).

### API 및 마이그레이션
*   **[09. 완전한 API 레퍼런스](./docs/09_complete_api_reference.md)** 🆕
    *   모든 Public 인터페이스, Extension Methods, LibDbOptions 전체 속성, Exception 타입.
*   **[11. 마이그레이션 가이드](./docs/11_migration_guide.md)** 🆕
    *   Dapper/EF Core/ADO.NET에서 Lib.Db로 전환, 코드 변환 예시, Breaking Changes.
*   **[12. 프로덕션 체크리스트](./docs/12_production_checklist.md)** 🆕
    *   배포 전 검증 18개 항목, 모니터링, 성능 튜닝, 보안, 장애 대응 플레이북.

### Source Generator 📦
*   **[Lib.Db.TvpGen README](./Lib.Db.TvpGen/README.md)** 🔥
    *   **TVP 자동 생성**: `[TvpRow]` 어노테이션으로 SQL Server TVP 매핑 (18개 타입 지원)
    *   **결과 매핑**: `[DbResult]` 어노테이션으로 DbDataReader → DTO 고성능 매핑
    *   **DB-First**: `libdb.schema.json`으로 DTO 자동 생성
    *   **완전한 타입 레퍼런스**: JSON Schema 타입 매핑표 (Bit, Int, BigInt, DateTime2, Guid 등)
    *   **Track 5 알고리즘**: Small(≤12) Span.SequenceEqual, Large(>12) FNV-1a 해시

---

## 🌟 주요 기능

- ⚡ **3-Tier Connection Pooling** - Default/Admin/Custom 연결 관리
- 🔄 **Automatic Retry & Circuit Breaker** - Polly v8 기반 자동 복원
- 💾 **L1+L2 HybridCache** - 프로세스 내/간 캐시 자동 동기화
- 🚀 **Bulk Operations** - BulkInsert/Update/Delete + Pipeline 지원
- 📊 **Resumable Query** - 네트워크 단절 시 자동 재개
- 🔐 **SQL Injection 자동 방지** - SqlInterpolatedStringHandler
- 📈 **OpenTelemetry 통합** - 성능 메트릭 자동 수집
- 🛡️ **Schema Validation** - 워밍업 시 누락된 스키마 자동 감지 및 경고
- ⚡ **Optimized Normalization** - SIMD 기반 고속 식별자 처리
- 🧪 **Chaos Engineering** - 개발 환경 장애 시뮬레이션

---

## 📦 샘플 프로젝트

> [!NOTE]
> **샘플 프로젝트는 별도 리포지토리에서 제공 예정**

- `Lib.Db.Samples.WebApi` - ASP.NET Core Web API 예제
- `Lib.Db.Samples.Worker` - Background Worker 예제
- `Lib.Db.Samples.Benchmarks` - BenchmarkDotNet 성능 측정

---

## 🤝 기여 및 라이선스

- **License**: [MIT License](LICENSE)
- **Contributions**: Pull Requests를 환영합니다!
- **Issues**: 버그 리포트 및 기능 요청은 GitHub Issues로

---

<p align="center">
  Developed by <strong>김재석</strong>
</p>
