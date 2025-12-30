# 완전한 API 레퍼런스 (Complete API Reference)

<!-- AI_CONTEXT: START -->
<!-- ROLE: API_REFERENCE -->
<!-- TARGET: All public interfaces and classes -->
<!-- AI_CONTEXT: END -->

`Lib.Db`의 모든 Public API를 한 곳에서 확인할 수 있는 완전한 레퍼런스입니다.

---

## 목차

1. [핵심 인터페이스](#1-핵심-인터페이스)
2. [Fluent API 인터페이스](#2-fluent-api-인터페이스)
3. [Extension Methods](#3-extension-methods)
4. [LibDbOptions](#4-libdboptions)
5. [Exception 타입](#5-exception-타입)
6. [어트리뷰트](#6-어트리뷰트)

---

## 1. 핵심 인터페이스

### IDbContext

**위치**: `Lib.Db.Contracts.Entry.DbEntryContracts`

라이브러리의 **메인 진입점** 인터페이스입니다. 모든 DB 작업은 이 인터페이스로부터 시작되며, DI 컨테이너를 통해 주입받습니다.

```csharp
public interface IDbContext
{
    // 기본 인스턴스 사용
    IProcedureStage Default { get; }
    
    // 명명된 인스턴스 사용 (appsettings.json에 정의)
    IProcedureStage UseInstance(string instanceName);
    
    // Ad-hoc 연결 문자열 사용 (멀티테넌트, 동적 DB 선택)
    IProcedureStage UseConnectionString(string connectionString);
    
    // 트랜잭션 시작
    Task<IDbTransactionScope> BeginTransactionAsync(
        string instanceName,
        IsolationLevel isoLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);
}
```

**사용 예시**:
```csharp
public class UserRepository(IDbContext db)
{
    public async Task<User?> GetUserAsync(int id)
    {
        // 기본 인스턴스
        return await db.Default
            .Sql($"SELECT * FROM Users WHERE Id = {id}")
            .QuerySingleAsync<User>();
    }
    
    public async Task<List<Order>> GetOrdersAsync()
    {
        // 명명된 인스턴스 (appsettings.json의 "Reporting" 연결 문자열)
        return await db.UseInstance("Reporting")
            .Procedure("dbo.usp_GetOrders")
            .QueryAsync<Order>()
            .ToListAsync();
    }
    
    public async Task<List<TenantData>> GetTenantDataAsync(string tenantId)
    {
        // 동적 연결 문자열
        var connString = GetTenantConnectionString(tenantId);
        return await db.UseConnectionString(connString)
            .Sql("SELECT * FROM TenantData")
            .QueryAsync<TenantData>()
            .ToListAsync();
    }
}
```

---

## 2. Fluent API 인터페이스

### IProcedureStage

명령 정의 단계.

```csharp
public interface IProcedureStage
{
    // 저장 프로시저
    IParameterStage Procedure(string procedureName);
    
    // SQL 문자열
    IParameterStage Sql(string sql);
    IExecutionStage<Dictionary<string, object?>> Sql([InterpolatedStringHandlerArgument("")] ref Fluent.SqlInterpolatedStringHandler handler);
    IExecutionStage<Dictionary<string, object?>> Sql(FormattableString sql);
    IExecutionStage<Dictionary<string, object?>> Sql(string format, params ReadOnlySpan<object?> args);
    
    // Bulk 작업
    Task<int> BulkInsertAsync<T>(string tableName, IEnumerable<T> data, CancellationToken ct = default);
    Task<int> BulkUpdateAsync<T>(string tableName, IEnumerable<T> data, string[] keyColumns, string[] updateColumns, CancellationToken ct = default);
    Task<int> BulkDeleteAsync<T>(string tableName, IEnumerable<T> data, string[] keyColumns, CancellationToken ct = default);
    
    // Pipeline (Channel 기반)
    Task BulkInsertPipelineAsync<T>(string tableName, ChannelReader<T> reader, int batchSize = 5000, CancellationToken ct = default);
    Task BulkUpdatePipelineAsync<T>(string tableName, ChannelReader<T> reader, string[] keyColumns, string[] updateColumns, int batchSize = 5000, CancellationToken ct = default);
    Task BulkDeletePipelineAsync<T>(string tableName, ChannelReader<T> reader, string[] keyColumns, int batchSize = 5000, CancellationToken ct = default);
    
    // Resumable Query
    IAsyncEnumerable<TResult> QueryResumableAsync<TCursor, TResult>(
        Func<TCursor, string> queryBuilder,
        Func<TResult, TCursor> cursorSelector,
        TCursor initialCursor,
        CancellationToken ct = default);
}
```

---

### IParameterStage

파라미터 설정 단계.

```csharp
public interface IParameterStage
{
    // 파라미터 바인딩
    IExecutionStage<TParams> With<TParams>(TParams parameters);
    
    // 타임아웃 설정
    IParameterStage WithTimeout(int timeoutSeconds);
}
```

---

### IExecutionStage<TParams>

실행 단계.

```csharp
public interface IExecutionStage<TParams>
{
    // 조회
    IAsyncEnumerable<TResult> QueryAsync<TResult>(CancellationToken ct = default);
    Task<TResult?> QuerySingleAsync<TResult>(CancellationToken ct = default);
    
    // 스칼라
    Task<TScalar> ExecuteScalarAsync<TScalar>(CancellationToken ct = default);
    
    // 다중 결과
    Task<IMultipleResultReader> QueryMultipleAsync(CancellationToken ct = default);
    
    // 명령 실행
    Task<int> ExecuteAsync(CancellationToken ct = default);
}
```

---

### IMultipleResultReader

다중 결과셋 읽기.

```csharp
public interface IMultipleResultReader : IAsyncDisposable
{
    // 현재 결과셋 전체
    Task<List<T>> ReadAsync<T>(CancellationToken ct = default);
    
    // 현재 결과셋 첫 행
    Task<T?> ReadSingleAsync<T>(CancellationToken ct = default);
    
    // 다음 결과셋으로 이동
    Task<bool> NextResultAsync(CancellationToken ct = default);
}
```

---

## 3. Extension Methods

### DI 등록

**LibDbServiceCollectionExtensions**

```csharp
public static class LibDbServiceCollectionExtensions
{
    // appsettings.json 자동 바인딩
    public static IServiceCollection AddHighPerformanceDb(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "LibDb");
    
    // 코드 기반 설정
    public static IServiceCollection AddHighPerformanceDb(
        this IServiceCollection services,
        Action<LibDbOptions> configure);
}
```

---

### 초기화

**LibDbHostExtensions**

```csharp
public static class LibDbHostExtensions
{
    // Host 초기화 (공유 메모리 리더 선출)
    public static async Task<IHost> UseHighPerformanceDbAsync(
        this IHost host,
        CancellationToken ct = default);
}
```

---

### HybridCache

**HybridCacheExtensions**

```csharp
public static class HybridCacheExtensions
{
    // L1 캐시 설정
    public static IServiceCollection AddL1Cache(
        this IServiceCollection services,
        int maxEntries = 10000);
    
    // L2 캐시 설정
    public static IServiceCollection AddL2SharedMemoryCache(
        this IServiceCollection services,
        Action<SharedMemoryCacheOptions> configure);
}
```

---

## 4. LibDbOptions

### 전체 속성 목록

```csharp
public class LibDbOptions
{
    // ━━━━ 연결 ━━━━
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();
    
    // ━━━━ 스키마 캐싱 ━━━━
    public bool EnableSchemaCaching { get; set; } = true;
    public int SchemaRefreshIntervalSeconds { get; set; } = 60;
    public List<string> WatchedInstances { get; set; } = new();
    public List<string> PrewarmSchemas { get; set; } = new() { "dbo" };
    public List<string> PrewarmIncludePatterns { get; set; } = new();
    public List<string> PrewarmExcludePatterns { get; set; } = new();
    public int PrewarmMaxConcurrency { get; set; } = 0;
    
    // ━━━━ 실행 정책 ━━━━
    public bool EnableDryRun { get; set; } = false;
    public bool StrictRequiredParameterCheck { get; set; } = true;
    
    // ━━━━ 타임아웃 ━━━━
    public int DefaultCommandTimeoutSeconds { get; set; } = 30;
    public int BulkCommandTimeoutSeconds { get; set; } = 600;
    public int BulkBatchSize { get; set; } = 5000;
    
    // ━━━━ 리소스 관리 ━━━━
    public long TvpMemoryWarningThresholdBytes { get; set; } = 10 * 1024 * 1024;
    public int ResumableQueryMaxRetries { get; set; } = 5;
    public int ResumableQueryBaseDelayMs { get; set; } = 100;
    public int ResumableQueryMaxDelayMs { get; set; } = 5000;
    
    // ━━━━ Resilience ━━━━
    public bool EnableResilience { get; set; } = true;
    public ResilienceOptions Resilience { get; set; } = new();
    
    // ━━━━ 캐시 ━━━━
    public int MaxCacheSize { get; set; } = 10000;
    public int SchemaSnapshotWarningThreshold { get; set; } = 5000;
    
    // ━━━━ 공유 메모리 ━━━━
    public bool? EnableSharedMemoryCache { get; set; } = null;
    public bool EnableEpochCoordination { get; set; } = true;
    public int EpochCheckIntervalSeconds { get; set; } = 5;
    public SharedMemoryCacheOptions SharedMemoryCache { get; set; } = new();
    
    // ━━━━ Chaos ━━━━
    public ChaosOptions Chaos { get; set; } = new();
    
    // ━━━━ Observability ━━━━
    public bool EnableObservability { get; set; } = false;
    public bool EnableOpenTelemetry { get; set; } = false;
    public bool IncludeParametersInTrace { get; set; } = false;
    public int HealthCheckThrottleSeconds { get; set; } = 1;
    public int HealthCheckTimeoutSeconds { get; set; } = 2;
}
```

### ResilienceOptions

```csharp
public class ResilienceOptions
{
    public int MaxRetryCount { get; set; } = 3;
    public int BaseRetryDelayMs { get; set; } = 100;
    public int MaxRetryDelayMs { get; set; } = 2000;
    public bool UseRetryJitter { get; set; } = true;
    public string RetryBackoffType { get; set; } = "Exponential";
    
    public int CircuitBreakerThreshold { get; set; } = 5;
    public int CircuitBreakerSamplingDurationMs { get; set; } = 30000;
    public int CircuitBreakerBreakDurationMs { get; set; } = 30000;
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;
}
```

### ChaosOptions

```csharp
public class ChaosOptions
{
    public bool Enabled { get; set; } = false;
    public double ExceptionRate { get; set; } = 0.01;
    public double LatencyRate { get; set; } = 0.05;
    public int MinLatencyMs { get; set; } = 100;
    public int MaxLatencyMs { get; set; } = 500;
}
```

---

## 5. Exception 타입

### SqlException (Microsoft.Data.SqlClient)

**주요 속성**:
```csharp
public class SqlException : DbException
{
    public int Number { get; }  // 오류 번호
    public byte Class { get; }  // 심각도
    public byte State { get; }
    public string Server { get; }
    public string Procedure { get; }
    public int LineNumber { get; }
}
```

### Lib.Db 커스텀 예외

```csharp
// 파라미터 누락
public class RequiredParameterMissingException : ArgumentException
{
    public string ParameterName { get; }
}

// Schema 캐시 오류
public class SchemaCacheException : InvalidOperationException
{
    public string ProcedureName { get; }
}

// Resilience 오류
public class BrokenCircuitException : Exception  // Polly 제공
{
}

public class TimeoutRejectedException : Exception  // Polly 제공
{
}
```

---

## 6. 어트리뷰트

### [TvpRow]

TVP 타입 정의.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class TvpRowAttribute : Attribute
{
    public string TypeName { get; set; }  // 필수: "dbo.Tvp_User"
}
```

---

### [TvpLength]

NVARCHAR/VARCHAR 크기 지정.

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class TvpLengthAttribute : Attribute
{
    public int Length { get; }
    
    public TvpLengthAttribute(int length);
}
```

---

### [TvpPrecision]

DECIMAL/NUMERIC 정밀도 지정.

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class TvpPrecisionAttribute : Attribute
{
    public byte Precision { get; }
    public byte Scale { get; }
    
    public TvpPrecisionAttribute(byte precision, byte scale);
}
```

---

### [TvpIgnore]

TVP 직렬화에서 제외.

```csharp
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class TvpIgnoreAttribute : Attribute
{
}
```

---

### [ColumnName]

결과 매핑 시 컬럼명 오버라이드.

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class ColumnNameAttribute : Attribute
{
    public string Name { get; }
    
    public ColumnNameAttribute(string name);
}
```

---

## 7. 주요 타입 요약

| 타입 | 네임스페이스 | 용도 |
|:---|:---|:---|
| `IDbContext` | `Lib.Db.Contracts.Entry` | 메인 진입점 |
| `IProcedureStage` | `Lib.Db.Contracts` | 명령 정의 |
| `IParameterStage` | `Lib.Db.Contracts` | 파라미터 설정 |
| `IExecutionStage<T>` | `Lib.Db.Contracts` | 실행 |
| `IMultipleResultReader` | `Lib.Db.Contracts` | 다중 결과 읽기 |
| `LibDbOptions` | `Lib.Db` | 설정 옵션 |
| `TvpRowAttribute` | `Lib.Db.Contracts` | TVP 정의 |
| `SqlException` | `Microsoft.Data.SqlClient` | SQL 오류 |

---

## 검증 규칙

### LibDbOptions 검증

```csharp
// DefaultCommandTimeoutSeconds
[Range(1, 600)]
public int DefaultCommandTimeoutSeconds { get; set; } = 30;

// BulkBatchSize
[Range(100, 100_000)]
public int BulkBatchSize { get; set; } = 5000;

// CircuitBreakerFailureRatio
[Range(0.1, 1.0)]
public double CircuitBreakerFailureRatio { get; set; } = 0.5;
```

**검증 실패 시**:
```csharp
throw new OptionsValidationException(
    "LibDb",
    typeof(LibDbOptions),
    new[] { "BulkBatchSize must be between 100 and 100,000" });
```

---

**이 API 레퍼런스는 `Lib.Db v1.0` 기준입니다. 향후 버전에서 변경될 수 있습니다.**

---

<p align="center">
  ⬅️ <a href="./08_process_coordination.md">이전: 프로세스 코디네이션</a>
  &nbsp;|&nbsp;
  <a href="./11_migration_guide.md">다음: 마이그레이션 ➡️</a>
</p>

<p align="center">
  🏠 <a href="../README.md">홈으로</a>
</p>
