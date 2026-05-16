# Lib.Db v2 API 레퍼런스

v2 Public API 전체 목록입니다.

---

## 1. IDbSession

DB 작업의 유일한 진입점입니다. DI 컨테이너에서 `Scoped`로 등록됩니다.

| 멤버 | 시그니처 | 설명 |
|---|---|---|
| `Use` | `IProcedureStage Use(string instanceName)` | 등록된 DB 인스턴스로 작업 시작 |
| `UseConnectionString` | `IProcedureStage UseConnectionString(string connectionString)` | Ad-hoc 연결 문자열로 작업 시작 |
| `Default` | `IProcedureStage Default { get; }` | 기본 인스턴스("Default")로 작업 시작 |
| `BeginTransactionAsync` | `Task<IDbTransactionScope> BeginTransactionAsync(string instanceName, CancellationToken ct)` | 인스턴스별 독립 트랜잭션 시작 (ReadCommitted) |
| `BeginTransactionAsync` | `Task<IDbTransactionScope> BeginTransactionAsync(string instanceName, IsolationLevel isolationLevel, CancellationToken ct)` | 격리 수준 지정 트랜잭션 시작 |
| `BulkInsertAsync<T>` | `Task<DbResult<long>> BulkInsertAsync<T>(string instanceName, string destinationTable, IEnumerable<T> records, BulkInsertOptions? options, CancellationToken ct)` | SqlBulkCopy 대량 INSERT (Reflection, AOT 비호환) |
| (상속) | `IAsyncDisposable` | 비동기 리소스 해제 |

---

## 2. IProcedureStage

1단계: 실행할 명령(SP/SQL)을 선택합니다.

| 멤버 | 시그니처 | 설명 |
|---|---|---|
| `Procedure` | `IParameterStage Procedure(string spName)` | 저장 프로시저 지정 |
| `Sql` | `IParameterStage Sql(string sqlText)` | Raw SQL 텍스트 지정. 사용자 입력은 문자열 결합하지 말고 파라미터로 전달 |
| `Sql` | `IParameterStage Sql(FormattableString sql)` | 보간 SQL (값 인수 파라미터 자동 추출) |
| `SqlInterpolated` | `IParameterStage SqlInterpolated(FormattableString sql)` | 보간 SQL을 명시적으로 선택하는 파라미터화 API |

---

## 2-1. Raw SQL 보안 옵션

| 옵션 | 기본값 | 설명 |
|---|---|---|
| `RawSqlPolicy` | `Allow` | `CommandType.Text` 실행 정책. `DenyAllText`는 모든 Raw SQL 텍스트를 차단하고, `DenyWriteText`는 주석/문자열/식별자를 건너뛰며 쓰기/DDL/권한/운영 계열 위험 토큰을 차단합니다. |
| `ConnectionSecurityProfile` | `Development` | `Production` 설정 시 암호화 비활성, `TrustServerCertificate=True`, 고권한 기본 SQL 로그인 사용을 검증합니다. |

`DenyWriteText`는 SQL 파서가 아니라 전환기 보조 guardrail입니다.
복잡한 T-SQL 문법 전체를 증명하는 보안 경계로 간주하면 안 됩니다.
운영에서 Raw SQL 자체를 금지하려면 `DenyAllText`와 DB 권한 분리를 함께 사용하세요.

## 3. IParameterStage

2단계: 파라미터와 실행 옵션을 설정합니다. `IExecutionStage<object>`를 상속하므로 파라미터 없이 바로 실행할 수 있습니다.

| 멤버 | 시그니처 | 설명 |
|---|---|---|
| `With<T>` | `IExecutionStage<T> With<T>(T parameters)` | 파라미터 객체 설정 (DTO, 익명 타입, Dictionary) |
| `WithTimeout` | `IParameterStage WithTimeout(int timeoutSeconds)` | 명령 타임아웃 오버라이드 (초) |

---

## 4. IExecutionStage\<TParams>

3단계: 최종 실행 및 결과 조회입니다. 모든 반환값은 `DbResult<T>`로 래핑됩니다.

| 메서드 | 시그니처 | 설명 |
|---|---|---|
| `QueryAsync<T>` | `Task<DbResult<IAsyncEnumerable<T>>> QueryAsync<T>(CancellationToken ct)` | 다건 비동기 스트림 조회 |
| `QuerySingleAsync<T>` | `Task<DbResult<T?>> QuerySingleAsync<T>(CancellationToken ct)` | 단건 조회 (없으면 null 성공) |
| `ExecuteScalarAsync<T>` | `Task<DbResult<T?>> ExecuteScalarAsync<T>(CancellationToken ct)` | 스칼라 값 (1행 1열) |
| `QueryMultipleAsync` | `Task<DbResult<IMultipleResultReader>> QueryMultipleAsync(CancellationToken ct)` | 다중 결과 셋 |
| `ExecuteAsync` | `Task<DbResult<int>> ExecuteAsync(CancellationToken ct)` | NonQuery 실행 (영향 행 수) |

---

## 5. IDbTransactionScope

트랜잭션 수명 관리 인터페이스입니다. `IProcedureStage`를 상속하여 트랜잭션 내에서 바로 Fluent 체이닝이 가능합니다.

| 멤버 | 시그니처 | 설명 |
|---|---|---|
| `CommitAsync` | `Task<DbResult<bool>> CommitAsync(CancellationToken ct)` | 명시적 커밋 |
| `RollbackAsync` | `Task<DbResult<bool>> RollbackAsync(CancellationToken ct)` | 명시적 롤백 |
| (상속) | `IProcedureStage` | Fluent API 체이닝 지원 |
| (상속) | `IAsyncDisposable` | 커밋 없이 Dispose 시 자동 롤백 |

---

## 6. DbResult\<T>

DB 작업 성공/실패 결과를 나타내는 불변 구조체입니다 (`readonly record struct`).

| 속성 | 타입 | 설명 |
|---|---|---|
| `IsSuccess` | `bool` | 작업 성공 여부 |
| `Value` | `T?` | 성공 시 반환 값 (실패 시 default) |
| `Error` | `DbError?` | 실패 시 오류 정보 (성공 시 null) |
| `AffectedRows` | `int` | 영향받은 행 수 (INSERT/UPDATE/DELETE) |

| 팩토리 메서드 | 시그니처 | 설명 |
|---|---|---|
| `Ok` | `static DbResult<T> Ok(T value, int affectedRows = 0)` | 성공 결과 생성 |
| `Fail` | `static DbResult<T> Fail(DbError error)` | 실패 결과 생성 |

| 메서드 | 시그니처 | 설명 |
|---|---|---|
| `Deconstruct` | `void Deconstruct(out bool success, out T? value, out DbError? error)` | 패턴 매칭용 분해 |

---

## 7. DbError

DB 오류 정보를 담는 불변 구조체입니다 (`readonly record struct`).

| 속성 | 타입 | 설명 |
|---|---|---|
| `Kind` | `DbErrorKind` | 오류 종류 |
| `SqlErrorCode` | `int` | SQL Server 오류 번호 |
| `Severity` | `byte` | SQL Server 심각도 (0~25) |
| `IsTransient` | `bool` | 일시적 오류 여부 (true면 재시도 대상) |
| `Message` | `string` (required) | 사용자 표시 오류 메시지 |
| `Hint` | `string?` | 해결 힌트 메시지 |
| `ObjectName` | `string?` | 오류 발생 DB 객체 이름 |
| `InnerException` | `Exception?` | 원본 예외 (로깅/디버깅용) |

---

## 8. DbErrorKind

DB 오류 분류 열거형입니다 (16개 값).

| 값 | 설명 | Transient |
|---|---|:---:|
| `None` | 미지정 | - |
| `SchemaNotFound` | SP/테이블 등 스키마 객체 미발견 | X |
| `AuthenticationFailed` | DB 인증 실패 (로그인 오류) | X |
| `ConnectionLost` | DB 연결 끊김 | O |
| `Timeout` | 쿼리 실행 제한 시간 초과 | O |
| `Deadlock` | 교착 상태 감지 | O |
| `ConstraintViolation` | 제약 조건 위반 (PK, FK, UNIQUE) | X |
| `DataConversion` | 데이터 형식 변환 오류 | X |
| `ParameterMismatch` | SP 매개변수 불일치 | X |
| `PermissionDenied` | 권한 부족 | X |
| `ResourceExhausted` | 리소스 부족 (메모리, 디스크) | O |
| `TransactionAborted` | 트랜잭션 중단 | O |
| `QuerySyntax` | 쿼리 구문 오류 | X |
| `UserDefined` | 사용자 정의 (RAISERROR/THROW) | X |
| `CloudTransient` | 클라우드 일시적 오류 | O |
| `Unknown` | 분류 불가 | - |

---

## 9. LibDbOptions 주요 속성

| 속성 | 타입 | 기본값 | 설명 |
|---|---|---|---|
| `ConnectionStrings` | `Dictionary<string, string>` | `[]` | DB 연결 문자열 목록 |
| `ConnectionStringNames` | `IReadOnlyList<string>` | `["Default"]` | 사용 대상 키 목록 (첫 번째 = Default) |
| `EnableSchemaCaching` | `bool` | `true` | 스키마 캐싱 활성화 |
| `SchemaRefreshIntervalSeconds` | `int` | `60` | 스키마 갱신 주기 (1~86400초) |
| `PrewarmSchemas` | `List<string>` | `["dbo"]` | 시작 시 로드할 스키마 |
| `PrewarmIncludePatterns` | `List<string>` | `[]` | 워밍업 포함 패턴 |
| `PrewarmExcludePatterns` | `List<string>` | `[]` | 워밍업 제외 패턴 |
| `DefaultCommandTimeoutSeconds` | `int` | `30` | 기본 타임아웃 (1~600초) |
| `StrictRequiredParameterCheck` | `bool` | `true` | 필수 파라미터 검사 |
| `EnableGeneratedTvpBinder` | `bool` | `true` | SG 기반 TVP 바인더 |
| `EnableResilience` | `bool` | `false` | Polly 회복 탄력성 |
| `Resilience` | `ResilienceOptions` | (내부 기본값) | 재시도/Circuit Breaker 설정 |
| `EnableSharedMemoryCache` | `bool?` | `null` (자동) | L2 공유 메모리 캐시 |
| `EnableEpochCoordination` | `bool?` | `null` | 프로세스 간 Epoch 동기화 |
| `EnableDryRun` | `bool` | `false` | 모의 실행 모드 |
| `EnableObservability` | `bool` | `false` | 관측 가능성(Logging, Metrics, Tracing) 마스터 스위치 |
| `EnableOpenTelemetry` | `bool` | `false` | ⚠️ **Deprecated** — `EnableObservability`를 사용하세요 (v3.0 제거 예정) |
| `Mars` | `MarsPolicy` | `Auto` | MARS 정책 (`Disabled`/`Auto`/`ForceEnable`) |
| `HealthCheckThrottleSeconds` | `int` | `1` | HealthCheck 최소 실행 간격 (초) |

### 9-1. MarsPolicy 열거형

| 값 | 설명 |
|---|---|
| `Disabled` | MARS 미사용. `QueryMultipleAsync` 호출 시 `InvalidOperationException` 발생 |
| `Auto` | 자동 감지 (기본값). `QueryMultipleAsync` 사용 시 MARS 미설정이면 경고 로그 후 예외 |
| `ForceEnable` | `AddLibDb()` 등록 시 ConnectionString에 `MultipleActiveResultSets=True` 자동 주입 |

---

## 10. IMultipleResultReader

다중 결과 셋을 순차적으로 읽기 위한 인터페이스입니다.

| 메서드 | 시그니처 | 설명 |
|---|---|---|
| `ReadAsync<T>` | `Task<List<T>> ReadAsync<T>(CancellationToken ct)` | 현재 ResultSet 전체를 리스트로 |
| `ReadSingleAsync<T>` | `Task<T?> ReadSingleAsync<T>(CancellationToken ct)` | 현재 ResultSet에서 단건 |
| (상속) | `IAsyncDisposable` | 리소스 해제 |

---

## 11. Extension Methods

| 메서드 | 대상 | 설명 |
|---|---|---|
| `AddLibDb(IConfiguration)` | `IServiceCollection` | appsettings.json 기반 일괄 등록 |
| `AddHighPerformanceDb(Action<LibDbOptions>)` | `IServiceCollection` | 수동 구성 일괄 등록 |
| `RegisterLibDbCoreServices()` | `IServiceCollection` | 핵심 서비스만 등록 (테스트용) |
| `AddLibDbResilience()` | `IServiceCollection` | Polly 파이프라인 등록 |
| `AddLibDbHostedServices()` | `IServiceCollection` | 워밍업 Hosted Service 등록 |
| `AddSchemaFlushCoordination(string?)` | `IServiceCollection` | Epoch 기반 분산 스키마 캐시 조정 |
| `AddLibDbInterceptor<T>()` | `IServiceCollection` | 쿼리 인터셉터 등록 (다중 가능) |

---

## 12. IDbInterceptor (쿼리 인터셉터)

DB 명령 실행 전후를 가로채는 사용자 수준 인터셉터입니다.

| 메서드 | 시그니처 | 설명 |
|---|---|---|
| `OnExecutingAsync` | `ValueTask<DbInterceptionResult> OnExecutingAsync(DbInterceptionContext context, CancellationToken ct)` | 명령 실행 직전 호출 |
| `OnExecutedAsync` | `ValueTask OnExecutedAsync(DbInterceptionContext context, CancellationToken ct)` | 명령 실행 성공 직후 호출 |
| `OnErrorAsync` | `ValueTask OnErrorAsync(DbInterceptionContext context, CancellationToken ct)` | 명령 실행 실패 시 호출 |

### DbInterceptionResult

| 값 | 설명 |
|---|---|
| `Continue` | 실행을 계속합니다 |
| `Suppress` | 실행을 억제합니다 (DB 호출 건너뜀) |

### DbInterceptionContext

| 속성 | 타입 | 설명 |
|---|---|---|
| `CommandText` | `string` (required) | SP 이름 또는 SQL 텍스트 |
| `DiagnosticCommandText` | `string?` | 진단/로그용 명령 텍스트. Raw SQL 원문 노출을 피하려면 이 값을 우선 사용 |
| `CommandType` | `CommandType` (required) | 명령 유형 |
| `InstanceName` | `string` (required) | 대상 인스턴스 이름 |
| `StartTime` | `DateTime` | 실행 시작 시각 (UTC, 기본값: UtcNow) |
| `ElapsedMs` | `long?` | 실행 소요 시간 (밀리초) |
| `Result` | `object?` | 실행 결과 |
| `Exception` | `Exception?` | 발생한 예외 |
| `State` | `Dictionary<string, object?>` | 인터셉터 간 데이터 전달 |

---

## 13. 캐시 확장 메서드 (QueryCacheExtensions)

| 메서드 | 시그니처 | 설명 |
|---|---|---|
| `WithCacheAsync<T>` | `Task<DbResult<T?>> WithCacheAsync<T>(this Task<DbResult<T?>> resultTask, IDistributedCache cache, string cacheKey, TimeSpan duration, JsonSerializerOptions? jsonOptions, CancellationToken ct)` | 단건 결과 캐싱 |
| `WithCacheListAsync<T>` | `Task<DbResult<List<T>>> WithCacheListAsync<T>(this Task<DbResult<IAsyncEnumerable<T>>> resultTask, IDistributedCache cache, string cacheKey, TimeSpan duration, JsonSerializerOptions? jsonOptions, CancellationToken ct)` | 다건 스트림 -> List 캐싱 |
| `WithHybridCacheAsync<T>` | `Task<DbResult<T?>> WithHybridCacheAsync<T>(this Task<DbResult<T?>> resultTask, HybridCache hybridCache, string cacheKey, TimeSpan duration, CancellationToken ct)` | HybridCache L1+L2 캐싱 |
| `InvalidateCacheAsync` | `Task InvalidateCacheAsync(this IDistributedCache cache, string cacheKey, CancellationToken ct)` | 캐시 무효화 |

---

## 14. JSON 매핑 확장 메서드 (JsonMappingExtensions)

| 메서드 | 시그니처 | 설명 |
|---|---|---|
| `MapJsonColumn<T>` | `T? MapJsonColumn<T>(this Dictionary<string, object?> row, string columnName, JsonSerializerOptions? options)` | Dictionary 행에서 JSON 컬럼 역직렬화 |
| `WithJsonColumnAsync<T>` | `IAsyncEnumerable<(Dictionary<string, object?> Row, T? Json)> WithJsonColumnAsync<T>(this IAsyncEnumerable<Dictionary<string, object?>> rows, string columnName, JsonSerializerOptions? options)` | 스트림 전체에 JSON 매핑 |
| `FromJson<T>` | `T? FromJson<T>(this string? json, JsonSerializerOptions? options)` | JSON 문자열 역직렬화 |
| `ToJson<T>` | `string ToJson<T>(this T value, JsonSerializerOptions? options)` | 객체를 JSON 문자열로 직렬화 |

---

## 15. BulkInsertOptions

SqlBulkCopy 기반 대량 INSERT 옵션 클래스입니다.

| 속성 | 타입 | 기본값 | 설명 |
|---|---|---|---|
| `BatchSize` | `int` | 5,000 | 배치당 행 수 |
| `TimeoutSeconds` | `int` | 600 | 명령 타임아웃 (초) |
| `EnableStreaming` | `bool` | `true` | 스트리밍 활성화 |
| `FireTriggers` | `bool` | `false` | INSERT 트리거 실행 여부 |
| `CheckConstraints` | `bool` | `false` | 제약 조건 검사 여부 |
| `KeepIdentity` | `bool` | `false` | IDENTITY 값 유지 여부 |

---

## 16. 텔레메트리 (LibDbTelemetry)

OpenTelemetry 기반 관측 가능성을 위한 정적 클래스입니다.

| 상수/필드/메서드 | 타입 | 설명 |
|---|---|---|
| `SourceName` | `const string` | `"Lib.Db"` — ActivitySource/Meter 공통 이름 |
| `Version` | `const string` | `"2.2.0"` — ActivitySource/Meter 버전 |
| `ActivitySource` | `ActivitySource` | 트레이스 데이터 생성 |
| `Meter` | `Meter` | 메트릭 데이터 생성 |
| `RecordBytesFreed(long)` | `static void` | 캐시 정리 시 해제된 바이트 누적 기록 |

### 메트릭 목록

| 메트릭 | 타입 | 단위 | 설명 |
|---|---|---|---|
| `libdb.db_requests_total` | `Counter<long>` | - | DB 요청 총 횟수 |
| `libdb.db_request_duration_ms` | `Histogram<double>` | ms | DB 요청 소요 시간 |
| `libdb.connection.acquire_duration_ms` | `Histogram<double>` | ms | 연결 획득 소요 시간 |
| `libdb.connection.pool_waits` | `Counter<long>` | - | 연결 풀 대기 횟수 |
| `libdb.connection.pool_timeouts` | `Counter<long>` | - | 연결 풀 타임아웃 횟수 |
| `libdb.cache_requests_total` | `Counter<long>` | - | 캐시 연산 총 횟수 |
| `libdb.cache_op_duration_ms` | `Histogram<double>` | ms | 캐시 연산 소요 시간 |
| `libdb.cache_cleanup_total` | `Counter<long>` | - | 캐시 정리 사이클 수 |
| `libdb.cache_bytes_freed` | `ObservableGauge<long>` | bytes | 캐시 정리 시 해제된 바이트 |
