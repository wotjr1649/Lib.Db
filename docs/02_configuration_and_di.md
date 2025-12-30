# 설치 및 구성 (Configuration & DI)

<!-- AI_CONTEXT: START -->
<!-- ROLE: TECHNICAL_GUIDE -->
<!-- AI_CONTEXT: END -->

`Lib.Db`를 프로젝트에 설치하고, 의존성 주입(DI) 컨테이너에 올바르게 등록하는 방법을 안내합니다.

---

## 1. 패키지 설치 (Installation)

NuGet 패키지 관리자 또는 .NET CLI를 통해 패키지를 설치합니다.

```bash
# Core Library
dotnet add package Lib.Db

# Source Generator (필수)
dotnet add package Lib.Db.TvpGen
```

> [!CAUTION]
> **Native AOT 필수**: `Lib.Db`는 리플렉션을 사용하지 않으므로, `Lib.Db.TvpGen` 없이는 데이터 매핑이 작동하지 않습니다.

---

## 2. 의존성 주입 (Dependency Injection)

### Generic Host (.NET 10 권장)
`Program.cs`에서 `AddHighPerformanceDb` 메서드를 사용합니다.

```csharp
using Lib.Db.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// appsettings.json의 "LibDb" 섹션을 자동으로 바인딩
builder.Services.AddLibDb(builder.Configuration);
```

### 수동 구성 (Manual Configuration)
특정 섹션을 수동으로 지정하거나 코드로 옵션을 설정할 때 사용합니다.

```csharp
builder.Services.AddHighPerformanceDb(options =>
{
    options.ConnectionStrings["Main"] = "Server=...";
    options.DefaultCommandTimeoutSeconds = 60;
    options.EnableSharedMemoryCache = false; // 격리 모드
});
```

---

## 3. 구성 옵션 상세 (Configuration Options)

`appsettings.json`의 **완전한 스키마**입니다. 모든 옵션은 선택사항이며, 명시하지 않으면 기본값이 사용됩니다.

```json
{
  "LibDb": {
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [1] 연결 및 인프라 설정
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "ConnectionStrings": {
      "Main": "Server=localhost;Database=LibDb;Trusted_Connection=True;TrustServerCertificate=True;",  // [필수]
      "LogDb": "..."  // [선택] 추가 연결 문자열
    },
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [2] 스키마 캐싱 및 워밍업
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "EnableSchemaCaching": true,                    // 스키마 캐싱 사용 (기본값: true)
    "SchemaRefreshIntervalSeconds": 60,             // 스키마 갱신 주기 (1~86400초, 기본값: 60)
    "WatchedInstances": [],                         // 감시 대상 인스턴스 목록 (비면 모든 연결 감시)
    "PrewarmSchemas": ["dbo"],                      // 앱 시작 시 로드할 스키마 (기본값: ["dbo"])
    "PrewarmIncludePatterns": [                     // 워밍업 포함 패턴 (* 와일드카드 사용 가능)
      "usp_User*",                                  // 예: usp_User로 시작하는 모든 SP
      "*_Order*"                                    // 예: _Order를 포함하는 모든 객체
    ],
    "PrewarmExcludePatterns": [                     // 워밍업 제외 패턴
      "*_Test*",                                    // 예: _Test를 포함하는 객체 제외
      "*_Legacy*"
    ],
    "PrewarmMaxConcurrency": 0,                     // 워밍업 동시 작업 수 (0=자동, 기본값: 0)
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [3] 쿼리 실행 정책
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "EnableDryRun": false,                          // Dry Run 모드 (INSERT/UPDATE 미실행, 로그만 기록)
    "StrictRequiredParameterCheck": true,           // [권장] 필수 파라미터 누락 시 즉시 예외
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [4] 데이터 직렬화 및 검증
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // JsonOptions: 코드에서 직접 설정 (Shadow DTO 적용으로 인해 appsettings.json 바인딩 불가)
    "TvpValidationMode": "Strict",                  // TVP 검증 모드: Strict | Loose
    "EnableGeneratedTvpBinder": true,               // Source Generator 바인더 사용 (기본값: true)
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [5] 타임아웃 및 성능 튜닝
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "DefaultCommandTimeoutSeconds": 30,             // 일반 쿼리 타임아웃 (1~600초, 기본값: 30)
    "BulkCommandTimeoutSeconds": 600,               // 대량 작업 타임아웃 (1~3600초, 기본값: 600)
    "BulkBatchSize": 5000,                          // Bulk 배치 크기 (100~100,000, 기본값: 5000)
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [6] 리소스 관리 및 메모리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "TvpMemoryWarningThresholdBytes": 10485760,     // TVP 메모리 경고 임계값 (바이트, 기본값: 10MB)
    "ResumableQueryMaxRetries": 5,                  // Resumable 쿼리 재시도 횟수 (0~20, 기본값: 5)
    "ResumableQueryBaseDelayMs": 100,               // Resumable 재시도 기본 지연 (ms, 기본값: 100)
    "ResumableQueryMaxDelayMs": 5000,               // Resumable 재시도 최대 지연 (ms, 기본값: 5000)
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [7] 회복 탄력성 (Resilience - Polly v8)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "EnableResilience": true,                       // Polly 활성화
    "Resilience": {
      // --- Retry 정책 ---
      "MaxRetryCount": 3,                           // 최대 재시도 횟수 (0=비활성, 기본값: 3)
      "BaseRetryDelayMs": 100,                      // 재시도 기본 지연 (ms, 기본값: 100)
      "MaxRetryDelayMs": 2000,                      // 재시도 최대 지연 (ms, 기본값: 2000)
      "UseRetryJitter": true,                       // Jitter 사용 (Thunder Herd 방지, 기본값: true)
      "RetryBackoffType": "Exponential",            // 백오프 유형: Exponential | Linear | Constant
      
      // --- Circuit Breaker 정책 ---
      "CircuitBreakerThreshold": 5,                 // 최소 처리량 임계값 (기본값: 5)
      "CircuitBreakerSamplingDurationMs": 30000,    // 샘플링 기간 (ms, 기본값: 30초)
      "CircuitBreakerBreakDurationMs": 30000,       // 회로 열린 후 유지 시간 (ms, 기본값: 30초)
      "CircuitBreakerFailureRatio": 0.5             // 실패 비율 임계값 (0.0~1.0, 기본값: 0.5)
    },
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [8] 캐시 관리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "MaxCacheSize": 10000,                          // 매퍼/팩토리 캐시 최대 항목 수 (1,000~1,000,000, 기본값: 10,000)
    "SchemaSnapshotWarningThreshold": 5000,         // 스키마 스냅샷 경고 임계값 (기본값: 5,000)
    "SchemaLockCleanupThreshold": 1000,             // 락 정리 임계값 (기본값: 1,000)
    "SchemaLockCleanupIntervalMs": 60000,           // 락 정리 주기 (ms, 기본값: 60초)
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [9] L2 캐시 및 공유 메모리
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "EnableSharedMemoryCache": true,                // 공유 메모리 캐시 사용 (null=자동 감지)
    "EnableEpochCoordination": true,                // 에포크 조정 활성화
    "EpochCheckIntervalSeconds": 5,                 // 에포크 확인 주기 (초, 기본값: 5)
    "SharedMemoryCache": {
      "BasePath": "C:/Temp/LibDbCache",             // 캐시 파일 저장 경로
      "Scope": "User",                              // 공유 범위: User | Machine
      "MaxCacheSizeBytes": 1073741824               // 최대 캐시 크기 (바이트, 기본값: 1GB)
      // FallbackCache: 코드에서 직접 설정
      // IsolationKey: 내부용 (자동 설정됨)
    },
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [10] 카오스 엔지니어링 (테스트용)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "Chaos": {
      "Enabled": false,                             // [주의] 절대 프로덕션에서 true 금지!
      "ExceptionRate": 0.01,                        // 예외 발생 확률 (0.0~1.0, 기본값: 1%)
      "LatencyRate": 0.05,                          // 지연 발생 확률 (0.0~1.0, 기본값: 5%)
      "MinLatencyMs": 100,                          // 최소 지연 시간 (ms, 기본값: 100)
      "MaxLatencyMs": 500                           // 최대 지연 시간 (ms, 기본값: 500)
    },
    
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // [11] 관측 가능성 및 헬스 체크 (Observability & Health)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    "EnableObservability": false,                   // 관측 가능성 마스터 스위치
    "EnableOpenTelemetry": false,                   // OpenTelemetry 추적/메트릭 활성화
    "IncludeParametersInTrace": false,              // [주의] SQL 파라미터를 Trace에 포함 (보안 위험)
    "HealthCheckThrottleSeconds": 1,                // 헬스 체크 스로틀링 (초, 기본값: 1)
    "HealthCheckTimeoutSeconds": 2                  // 헬스 체크 타임아웃 (초, 기본값: 2)
  }
}
```

---

## 4. 주요 옵션 설명

### 4-1. 스키마 워밍업 (Prewarm) 패턴

`PrewarmIncludePatterns`와 `PrewarmExcludePatterns`는 **와일드카드 문법**(`*`, `?`)을 사용하여 대상을 필터링합니다.

| 사용자 패턴 | SQL LIKE 변환 | 예시 |
|---|---|---|
| `usp_User*` | `usp_User%` | `usp_UserGet`, `usp_UserCreate` |
| `*_Order*` | `%_Order%` | `usp_GetOrderList`, `Tvp_OrderDetail` |
| `usp_Auth?` | `usp_Auth_` | `usp_AuthN`, `usp_AuthZ` (1글자만) |

**우선순위 규칙**:
1. Include와 Exclude 모두 비어있으면 → 모든 객체 로드
2. Include만 → Include 매칭만 로드
3. Exclude만 → Exclude 제외한 모든 것 로드
4. 둘 다 → Include이면서 Exclude 아닌 것만 로드

### 4-2. Resilience (회복 탄력성) 정책

#### Retry Backoff Type

- **Exponential** (권장): `100ms → 200ms → 400ms → 800ms → ...`
- **Linear**: `100ms → 200ms → 300ms → 400ms → ...`
- **Constant**: `100ms → 100ms → 100ms → ...`

#### Circuit Breaker 동작

1. **Closed**: 정상 상태. 모든 요청 통과.
2. **Open**: 실패율이 `CircuitBreakerFailureRatio` 초과 시 열림. 모든 요청 즉시 차단.
3. **Half-Open**: `CircuitBreakerBreakDurationMs` 경과 후 제한적 재연결 시도.

### 4-3. 공유 메모리 캐시 (Shared Memory Cache)

#### Scope 옵션

- **User** (기본값): 동일한 Windows 사용자 계정 내에서만 공유.
- **Machine**: 머신 전체에서 공유 (관리자 권한 필요할 수 있음).

#### EnableSharedMemoryCache 자동 감지

- `null` (기본값): Windows에서는 `true`, Linux/macOS에서는 `false`로 자동 설정됨.
- `true`/`false`: OS 상관없이 강제 활성화/비활성화.

> [!CAUTION]
> Linux/Docker 환경에서 `true`로 강제 설정 시 Named Mutex 권한 문제로 실패할 수 있습니다.

### 4-4. 관측 가능성 (Observability)

#### OpenTelemetry 연동

`EnableOpenTelemetry: true` 설정 시 다음 소스를 통해 텔레메트리 데이터가 생성됩니다:

```csharp
// OpenTelemetry 연동 예시
builder.Services.AddOpenTelemetry()
    .WithTracing(tracer => tracer
        .AddSource("Lib.Db")        // ActivitySource
        .AddConsoleExporter())
    .WithMetrics(meter => meter
        .AddMeter("Lib.Db")         // Meter
        .AddConsoleExporter());
```

#### IncludeParametersInTrace 보안 주의

`true` 설정 시 SQL 파라미터 값이 Trace에 포함되어 민감 정보(비밀번호, 개인정보)가 노출될 수 있습니다.

**권장 설정**:
- **프로덕션**: `false` (보안 준수)

### 4-5. 설정 유효성### 3-1. Strict Validation (C# 14 Field Syntax)

`Lib.Db`는 v1.0부터 **C# 14 `field` 키워드**를 활용한 강력한 설정 검증을 도입했습니다.
잘못된 설정 값(예: 음수 Time-to-Live, 0 이하의 Batch Size)이 입력되면 애플리케이션 시작 즉시 예외(`ArgumentOutOfRangeException`)를 발생시켜 **Fail-Fast**를 보장합니다.NullException`이 발생하며 실행이 중단됩니다 (Fail-Fast).

**주요 검증 규칙**:
*   **Resilience**:
    *   `MaxRetryCount`: 0 이상 (음수 불가)
    *   `CircuitBreakerFailureRatio`: 0.0 ~ 1.0 (범위 초과 시 예외)
*   **Timeout**:
    *   `DefaultCommandTimeoutSeconds`: 1초 이상 600초 이하
*   **Batch**:
    *   `BulkBatchSize`: 100 이상 100,000 이하

```text
[오류 예시]
Unhandled exception. System.ArgumentOutOfRangeException: 실패 비율은 0.0 ~ 1.0 사이여야 합니다. (Parameter 'value')
Actual value was 1.5.
```

---

## 5. 고급 사용 예시

### 5-1. 대규모 워밍업 최적화

대량의 SP/TVP가 있는 환경에서 선택적 워밍업:

```json
{
  "LibDb": {
    "PrewarmSchemas": ["dbo", "app"],
    "PrewarmIncludePatterns": ["usp_*", "Tvp_*"],  // 프로시저와 TVP만
    "PrewarmExcludePatterns": ["*_Test*", "*_Deprecated*"],
    "PrewarmMaxConcurrency": 8  // 8개 병렬 로드
  }
}
```

### 5-2. 고가용성 환경 설정

Circuit Breaker를 더 민감하게 조정:

```json
{
  "LibDb": {
    "EnableResilience": true,
    "Resilience": {
      "CircuitBreakerThreshold": 10,
      "CircuitBreakerFailureRatio": 0.3,  // 30% 실패 시 즉시 차단
      "CircuitBreakerBreakDurationMs": 10000  // 10초 후 재시도
    }
  }
}
```

### 5-3. 카오스 엔지니어링 시나리오 테스트

```json
{
  "LibDb": {
    "Chaos": {
      "Enabled": true,
      "ExceptionRate": 0.1,   // 10% 확률로 예외
      "LatencyRate": 0.2,     // 20% 확률로 지연
      "MinLatencyMs": 500,
      "MaxLatencyMs": 2000    // 0.5~2초 랜덤 지연
    }
  }
}
```

---

## 4. 런타임 초기화 (Runtime Initialization)

애플리케이션 시작 시 스키마 워밍업은 `SchemaWarmupService` Hosted Service에서 자동으로 수행됩니다.

```csharp
var host = builder.Build();

// 스키마 워밍업은 백그라운드에서 자동으로 실행됩니다.
// LibDbOptions.PrewarmSchemas가 설정되어 있으면 SchemaWarmupService가 동작합니다.
await host.RunAsync();
```

> **참고**: `EnableSchemaCaching = true`이고 `PrewarmSchemas`에 스키마가 지정되어 있으면,
> `SchemaWarmupService`가 앱 시작 시 자동으로 메타데이터를 로드합니다.


---

<p align="center">
  ⬅️ <a href="./01_architecture_overview.md">이전: 아키텍처 개요</a>
  &nbsp;|&nbsp;
  <a href="./03_fluent_api_reference.md">다음: Fluent API ➡️</a>
</p>

<p align="center">
  🏠 <a href="../README.md">홈으로</a>
</p>
