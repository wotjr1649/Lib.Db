# Lib.Db Operations

트러블슈팅, 에러 코드 매핑, 현재 검증 절차, 프로덕션 체크리스트를 다루는 운영 가이드입니다.

---

## 1. DbResult 에러 분기 패턴

### 1-1. Kind별 대응 전략

```csharp
DbResult<User?> result = await session.Default
    .Procedure("dbo.usp_GetUser")
    .With(new { Id = 1 })
    .QuerySingleAsync<User>();

if (!result.IsSuccess)
{
    DbError error = result.Error!.Value;

    string action = error.Kind switch
    {
        // 재시도 가능 (IsTransient = true)
        DbErrorKind.ConnectionLost   => "연결 복구 대기 후 재시도",
        DbErrorKind.Timeout          => "타임아웃 증가 후 재시도",
        DbErrorKind.Deadlock         => "자동 재시도됨 (Polly)",
        DbErrorKind.CloudTransient   => "잠시 후 재시도",
        DbErrorKind.ResourceExhausted => "리소스 확인 후 재시도",
        DbErrorKind.TransactionAborted => "트랜잭션 재시작",

        // 코드 수정 필요
        DbErrorKind.SchemaNotFound      => $"SP 확인: {error.ObjectName}",
        DbErrorKind.ParameterMismatch   => "파라미터 타입/이름 확인",
        DbErrorKind.QuerySyntax         => "SQL 구문 수정",
        DbErrorKind.DataConversion      => "데이터 타입 확인",
        DbErrorKind.ConstraintViolation => "PK/FK/UNIQUE 제약 확인",

        // 인프라 조치 필요
        DbErrorKind.AuthenticationFailed => "연결 문자열/자격 증명 확인",
        DbErrorKind.PermissionDenied     => "DB 권한 부여 필요",

        // 사용자 정의
        DbErrorKind.UserDefined => $"비즈니스 오류: {error.Message}",

        _ => $"알 수 없는 오류: {error.Message}"
    };
}
```

### 1-2. Transient 판별

```csharp
if (result.Error is { IsTransient: true } transientError)
{
    // 재시도 가능한 오류 → Polly가 자동 처리 (EnableResilience = true 시)
    // 수동 처리가 필요한 경우에만 이 분기 사용
}
```

---

## 2. SqlException → DbError 매핑 테이블

주요 SQL Server 에러 코드와 DbErrorKind 매핑입니다.

### 2-1. 연결 및 인증

| SQL 에러코드 | 설명 | DbErrorKind | Transient |
|---:|---|---|:---:|
| 2 | 네트워크 경로 없음 | ConnectionLost | O |
| 53 | 서버 연결 실패 | ConnectionLost | O |
| 233 | 연결 끊김 | ConnectionLost | O |
| 10054 | 기존 연결 강제 종료 | ConnectionLost | O |
| 10060 | 연결 시도 시간 초과 | Timeout | O |
| 18456 | 로그인 실패 | AuthenticationFailed | X |
| 18452 | 신뢰할 수 없는 도메인 | AuthenticationFailed | X |

### 2-2. 실행 및 구문

| SQL 에러코드 | 설명 | DbErrorKind | Transient |
|---:|---|---|:---:|
| 102 | 구문 오류 | QuerySyntax | X |
| 156 | FROM 키워드 근처 오류 | QuerySyntax | X |
| 207 | 잘못된 열 이름 | SchemaNotFound | X |
| 208 | 잘못된 개체 이름 | SchemaNotFound | X |
| 2812 | SP를 찾을 수 없음 | SchemaNotFound | X |
| 8144 | 프로시저 매개변수 과다 | ParameterMismatch | X |
| 8145 | 프로시저 매개변수 미지정 | ParameterMismatch | X |

### 2-3. 제약 조건 및 데이터

| SQL 에러코드 | 설명 | DbErrorKind | Transient |
|---:|---|---|:---:|
| 245 | 데이터 형식 변환 오류 | DataConversion | X |
| 544 | IDENTITY_INSERT OFF 위반 | ConstraintViolation | X |
| 547 | FK 제약 조건 위반 | ConstraintViolation | X |
| 2601 | UNIQUE INDEX 중복 | ConstraintViolation | X |
| 2627 | PK/UNIQUE 제약 위반 | ConstraintViolation | X |
| 8152 | 문자열 데이터 잘림 | DataConversion | X |

### 2-4. 동시성 및 리소스

| SQL 에러코드 | 설명 | DbErrorKind | Transient |
|---:|---|---|:---:|
| 1205 | Deadlock victim | Deadlock | O |
| -2 | 타임아웃 (클라이언트) | Timeout | O |
| 1222 | 잠금 요청 시간 초과 | Timeout | O |
| 701 | 메모리 부족 | ResourceExhausted | O |
| 1101 | 디스크 공간 부족 | ResourceExhausted | O |
| 3960 | 스냅샷 격리 충돌 | TransactionAborted | O |

### 2-5. 권한 및 사용자 정의

| SQL 에러코드 | 설명 | DbErrorKind | Transient |
|---:|---|---|:---:|
| 229 | EXECUTE 권한 거부 | PermissionDenied | X |
| 262 | CREATE 권한 거부 | PermissionDenied | X |
| 50000+ | RAISERROR/THROW | UserDefined | X |

---

## 3. 연결 문자열 검증

Lib.Db는 options validation 단계에서 연결 문자열 이름, 키 매핑, 문자열 형식, 프로덕션 보안 프로필을 검증합니다. 실제 DB 연결 확인은 health check 경로에서 수행합니다.

| 단계 | 검증 내용 | 실패 시 |
|---|---|---|
| 1 | `ConnectionStringNames`가 비어있지 않은지 확인 | 시작 실패 |
| 2 | 이름 공백 및 중복 확인 | 시작 실패 |
| 3 | 각 이름에 대응하는 `ConnectionStrings` 키 존재 및 값 공백 확인 | 시작 실패 |
| 4 | 연결 문자열 형식 유효성 (`SqlConnectionStringBuilder` 파싱) | 시작 실패 |
| 5 | `ConnectionSecurityProfile.Production` 사용 시 암호화, 인증서 신뢰, 고권한 SQL 로그인 waiver 확인 | 시작 실패 |

`SELECT 1` 기반 연결성 확인은 startup options validation이 아니라 `AddLibDbHealthChecks(...)`로 등록한 health check에서 수행됩니다.

---

## 4. 프로덕션 체크리스트

### 4-1. 빌드 및 테스트

- [ ] `dotnet build` 경고/에러 0건
- [ ] maintainer verification gate 전체 통과
- [ ] AOT 빌드 시 Lib.Db-owned IL 트리밍/AOT 경고 0건, provider-owned warning은 verification 문서의 accepted-warning 정책에 따라 별도 검토
- [ ] `[DbResult]` generated result mapper를 쓰는 DTO에 `partial` 키워드 확인
- [ ] TVP 고빈도/Native AOT 경로는 `options.Tvp.Map<T>()` 또는 `TvpShape.For<T>()` static shape 등록 확인

### 4-2. 설정 검증

- [ ] `ConnectionStringNames`에 사용할 모든 DB 키 나열
- [ ] 각 키에 대응하는 `ConnectionStrings` 값 존재
- [ ] 운영 서비스는 `ConnectionSecurityProfile = Production` 또는 `UseProductionSecurityDefaults()` 적용
- [ ] Raw SQL Text가 필요한 경우에도 운영 기본값은 `RawSqlPolicy = DenyWriteText`, 보안 경계가 필요하면 `DenyAllText`
- [ ] `Encrypt=True;TrustServerCertificate=False` (프로덕션)
- [ ] sa 계정 대신 최소 권한 Application User 사용
- [ ] TVP 사용 계정은 대상 routine의 `EXECUTE`와 user-defined table type, schema, 또는 database의 `REFERENCES` 권한만 최소 부여
- [ ] **MARS 정책 설정**: `"Mars": "ForceEnable"` (권장) 또는 `"Mars": "Auto"` (기본값)
  - `ForceEnable`: `AddLibDb()` 등록 시 ConnectionString에 `MultipleActiveResultSets=True` 자동 주입
  - `Auto`: `QueryMultipleAsync` 사용 시 MARS 미설정이면 경고 로그 후 예외 (수동 설정 필요)
  - `Disabled`: MARS 미사용 (`QueryMultipleAsync` 사용 불가)

### 4-3. Connection Pool / Resilience / 캐싱

- [ ] `Min Pool Size` / `Max Pool Size` 설정 (Max = CPU x 2 + 동시 요청)
- [ ] `EnableResilience = true`, `MaxRetryCount`: 3
- [ ] `EnableSchemaCaching = true`, `SchemaRefreshIntervalSeconds`: 60~300
- [ ] `PrewarmExcludePatterns`: `*_Test*`, `*_Legacy*` 등 제외
- [ ] `DefaultCommandTimeoutSeconds`: 30 (OLTP), 120+ (배치)
- [ ] 사용자 지정 cache key에는 비밀값, PII, 원문 SQL, 조건값 원문을 넣지 않음
  - cache key는 Activity/log 태그로 전달될 수 있으므로 안정적인 non-sensitive 식별자 또는 해시 사용

### 4-4. HealthCheck

- [ ] `HealthCheckThrottleSeconds`: 1~10 (기본값: 1초)
  - 이 설정은 실제 HealthCheck 최소 실행 간격으로 적용됩니다
- [ ] HealthCheck 응답의 캐시 진단 데이터 확인
  - `libdb.cache.mode`: `shared-memory`, `fallback`, `unregistered` 중 하나
  - `libdb.cache.fallback_active`: SharedMemory 사용이 기대되는 환경에서 `true`면 degraded 상태로 취급
  - HealthCheck를 외부 공개 엔드포인트로 노출하는 경우 내부 캐시 모드가 운영 정보가 될 수 있으므로 접근 제어를 적용

---

## 5. Verification

The canonical verification root is `Verification/`. It contains maintainer-only integration tests, SQL setup/verify scripts, coverage gates, AOT checks, BenchmarkDotNet projects, and chaos harness assets.

Use the internal verification runbook before publishing. Consumer applications do not need these scripts to use `Lib.Db`.

Past regression details are summarized in [History](./history.md).

### 5-1. Security Notes

- 검증 DB에서는 DDL 배포를 허용하지만, 프로덕션 DB에서 테스트 초기화 코드를 실행하지 마세요.
- `RawSqlPolicy.DenyWriteText`는 SQL 파서가 아니라 guardrail입니다. 운영 보안 경계는 최소 권한 DB 계정, `DenyAllText`, SP 권한 분리로 구성하세요.
- `SET QUOTED_IDENTIFIER ON`은 computed column index, indexed view, filtered index 등 SQL Server 기능에서 요구될 수 있으므로 DDL 스크립트에 명시하세요.

---

## 6. 성능 튜닝 가이드

### 6-1. 스키마 캐시 TTL

| 환경 | 권장 SchemaRefreshIntervalSeconds |
|---|---|
| 개발 | 10~30 (빈번한 스키마 변경) |
| 스테이징 | 60 (기본값) |
| 프로덕션 | 300~600 (안정적 스키마) |

### 6-2. SharedMemoryCache 스트라이프

128개 Mutex 스트라이프가 기본이며, 대부분의 워크로드에 적합합니다.
동시 프로세스 수가 매우 많은 경우 `BasePath` 격리를 확인하세요.

### 6-3. Polly 재시도 설정

| 시나리오 | MaxRetryCount | BaseRetryDelayMs |
|---|---|---|
| 빠른 응답 (API) | 2 | 50 |
| 일반 (기본값) | 3 | 100 |
| 배치 처리 | 5 | 500 |
| 장시간 작업 | 3 | 1000 |

### 6-4. 진단 활성화

`EnableObservability`를 `true`로 설정합니다.

> ⚠️ `EnableOpenTelemetry`는 **Deprecated**되었습니다. `EnableObservability`로 대체하세요. 향후 breaking release에서 제거 예정입니다.

`IncludeParametersInTrace`는 보안상 개발 환경에서만 `true`로 설정하세요.
