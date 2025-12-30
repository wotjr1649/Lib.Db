# 프로덕션 체크리스트 (Production Checklist)

<!-- AI_CONTEXT: START -->
<!-- ROLE: OPERATIONAL_GUIDE -->
<!-- AI_CONTEXT: END -->

`Lib.Db`를 프로덕션 환경에 배포하기 전 반드시 확인해야 할 사항들을 정리한 체크리스트입니다.

---

## 배포 전 검증 (Pre-Deployment)

### ✅ 1. 연결 문자열 보안

- [ ] **암호화된 연결 문자열** 사용
  ```json
  {
    "ConnectionStrings": {
      "Main": "Server=...;Encrypt=True;TrustServerCertificate=False;"
    }
  }
  ```

- [ ] **Azure Key Vault** 또는 **AWS Secrets Manager** 연동
  ```csharp
  builder.Configuration.AddAzureKeyVault(
      new Uri("https://myvault.vault.azure.net/"),
      new DefaultAzureCredential());
  ```

- [ ] **Application User 사용** (sa 금지)
  ```sql
  -- 최소 권한 원칙
  GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [AppUser];
  ```

---

### ✅ 2. Connection Pool 설정

- [ ] **Pool 크기 조정**
  ```
  Min Pool Size=10;Max Pool Size=100;
  ```
  - 계산식: `Max Pool Size = (CPU 코어 수 × 2) + 예상 동시 요청 수`

- [ ] **Connection Timeout 설정**
  ```
  Connection Timeout=15;
  ```

- [ ] **Pool 누수 모니터링 설정**
  ```csharp
  SqlConnection.ClearAllPools();  // 정기 Pool 리셋 스케줄링
  ```

---

### ✅ 3. 타임아웃 정책

- [ ] **적절한 Command Timeout**
  ```json
  {
    "LibDb": {
      "DefaultCommandTimeoutSeconds": 30,
      "BulkCommandTimeoutSeconds": 600
    }
  }
  ```

- [ ] **Resilience Timeout 설정**
  ```json
  {
    "LibDb": {
      "Resilience": {
        "MaxRetryCount": 3,
        "BaseRetryDelayMs": 100
      }
    }
  }
  ```

---

### ✅ 4. 카오스 엔지니어링 비활성화

- [ ] **Chaos.Enabled = false 확인**
  ```json
  {
    "LibDb": {
      "Chaos": {
        "Enabled": false  // 🚨 필수!
      }
    }
  }
  ```

- [ ] **환경별 설정 파일 분리**
  ```
  appsettings.json           # 기본
  appsettings.Development.json
  appsettings.Production.json  # Chaos 비활성화
  ```

---

### ✅ 5. 로깅 설정

- [ ] **적절한 로그 레벨**
  ```json
  {
    "Logging": {
      "LogLevel": {
        "Default": "Information",
        "Lib.Db": "Warning",  // 프로덕션에서는 Warning 이상만
        "Lib.Db.Execution": "Error"
      }
    }
  }
  ```

- [ ] **민감 정보 로깅 비활성화**
  ```json
  {
    "LibDb": {
      "IncludeParametersInTrace": false  // 🚨 필수!
    }
  }
  ```

- [ ] **구조화된 로깅 (Serilog, NLog)**
  ```csharp
  Log.Logger = new LoggerConfiguration()
      .WriteTo.File("logs/libdb-.log", rollingInterval: RollingInterval.Day)
      .CreateLogger();
  ```

---

### ✅ 6. 성능 최적화

- [ ] **스키마 워밍업**
  ```json
  {
    "LibDb": {
      "PrewarmSchemas": ["dbo", "app"],
      "PrewarmIncludePatterns": ["usp_*", "Tvp_*"]
    }
  }
  ```

- [ ] **공유 메모리 캐시 활성화**
  ```json
  {
    "LibDb": {
      "EnableSharedMemoryCache": true,
      "SharedMemoryCache": {
        "MaxCacheSizeBytes": 1073741824  // 1GB
      }
    }
  }
  ```

- [ ] **Circuit Breaker 조정**
  ```json
  {
    "LibDb": {
      "Resilience": {
        "CircuitBreakerFailureRatio": 0.3,
        "CircuitBreakerBreakDurationMs": 10000
      }
    }
  }
  ```

---

## 모니터링 설정 (Monitoring)

### ✅ 7. 메트릭 수집

- [ ] **OpenTelemetry 연동**
  ```csharp
  builder.Services.AddOpenTelemetry()
      .WithMetrics(metrics => metrics
          .AddMeter("Lib.Db")
          .AddPrometheusExporter());
  ```

- [ ] **주요 메트릭 대시보드**
  - `lib_db_query_duration_ms` (쿼리 실행 시간)
  - `lib_db_cache_hit_ratio` (캐시 히트율, 목표 80% 이상)
  - `lib_db_retry_count` (재시도 횟수)
  - `lib_db_circuit_breaker_state` (CB 상태)
  - `lib_db_connection_pool_active` (활성 연결 수)

---

### ✅ 8. Health Check

- [ ] **Health Check 엔드포인트**
  ```csharp
  builder.Services.AddHealthChecks()
      .AddCheck<LibDbHealthCheck>("libdb");
  
  app.MapHealthChecks("/health");
  ```

- [ ] **Liveness/Readiness Probe** (Kubernetes)
  ```yaml
  livenessProbe:
    httpGet:
      path: /health
      port: 5000
    initialDelaySeconds: 30
    periodSeconds: 10
  readinessProbe:
    httpGet:
      path: /health
      port: 5000
    initialDelaySeconds: 5
    periodSeconds: 5
  ```

---

### ✅ 9. 알람 설정

- [ ] **Circuit Breaker Open 알람**
  ```
  Alert: lib_db_circuit_breaker_state == 1
  Severity: Critical
  Action: Slack/PagerDuty 알림
  ```

- [ ] **Cache Hit Rate 저하 알람**
  ```
  Alert: lib_db_cache_hit_ratio < 0.7 (70%)
  Severity: Warning
  ```

- [ ] **Connection Pool 고갈 알람**
  ```
  Alert: lib_db_connection_pool_active > max_pool_size * 0.9
  Severity: Critical
  ```

---

## 성능 튜닝 (Performance Tuning)

### ✅ 10. 인덱스 최적화

- [ ] **Missing Index 확인**
  ```sql
  SELECT 
      migs.avg_user_impact,
      migs.avg_total_user_cost,
      mid.statement,
      mid.equality_columns,
      mid.inequality_columns,
      mid.included_columns
  FROM sys.dm_db_missing_index_groups AS mig
  INNER JOIN sys.dm_db_missing_index_group_stats AS migs ON mig.index_group_handle = migs.group_handle
  INNER JOIN sys.dm_db_missing_index_details AS mid ON mig.index_handle = mid.index_handle
  ORDER BY migs.avg_user_impact DESC;
  ```

- [ ] **Unused Index 제거**
  ```sql
  SELECT 
      OBJECT_NAME(s.object_id) AS TableName,
      i.name AS IndexName,
      s.user_seeks,
      s.user_scans,
      s.user_lookups,
      s.user_updates
  FROM sys.dm_db_index_usage_stats AS s
  INNER JOIN sys.indexes AS i ON s.object_id = i.object_id AND s.index_id = i.index_id
  WHERE s.user_seeks = 0 AND s.user_scans = 0 AND s.user_lookups = 0
  ORDER BY s.user_updates DESC;
  ```

---

### ✅ 11. 쿼리 최적화

- [ ] **실행 계획 분석**
  ```sql
  SET STATISTICS TIME ON;
  SET STATISTICS IO ON;
  ```

- [ ] **Table Scan 제거**
  ```sql
  -- ❌ 비효율
  SELECT * FROM Users WHERE YEAR(CreatedAt) = 2024;
  
  -- ✅ 효율
  SELECT * FROM Users WHERE CreatedAt >= '2024-01-01' AND CreatedAt < '2025-01-01';
  ```

- [ ] **SELECT * 금지**
  ```csharp
  // ❌ 불필요한 컬럼 전송
  .Sql("SELECT * FROM Users")
  
  // ✅ 필요한 컬럼만
  .Sql("SELECT Id, Name, Email FROM Users")
  ```

---

## 보안 (Security)

### ✅ 12. SQL Injection 방지

- [ ] **파라미터화 사용**
  ```csharp
  // ✅ 안전 (자동 파라미터화)
  await db.Default.Sql($"SELECT * FROM Users WHERE Id = {userId}").QueryAsync<User>();
  
  // ❌ 위험
  string sql = $"SELECT * FROM Users WHERE Id = {userId}";  // 문자열 보간
  await db.Default.Sql(sql).ExecuteAsync();
  ```

---

### ✅ 13. 권한 관리

- [ ] **최소 권한 원칙**
  ```sql
  -- 읽기 전용 사용자
  CREATE USER [ReadOnlyUser] FOR LOGIN [ReadOnlyLogin];
  ALTER ROLE db_datareader ADD MEMBER [ReadOnlyUser];
  
  -- Application 사용자 (SELECT, INSERT, UPDATE, DELETE만)
  GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [AppUser];
  ```

- [ ] **DDL 권한 분리**
  - 애플리케이션 계정: DML만 (SELECT, INSERT, UPDATE, DELETE)
  - 마이그레이션 계정: DDL (CREATE, ALTER, DROP)

---

## 장애 대응 (Incident Response)

### ✅ 14. Runbook 준비

**Circuit Breaker Open 시**:
1. 로그 확인: `Lib.Db.Infrastructure.Resilience` 네임스페이스
2. DB 서버 상태 확인: `SELECT @@SERVERNAME, GETDATE()`
3. 활성 연결 수 확인: `SELECT COUNT(*) FROM sys.dm_exec_connections`
4. 대기 중인 쿼리 확인: `sp_who2`

**Connection Pool 고갈 시**:
1. Pool 리셋: `SqlConnection.ClearAllPools()`
2. 연결 누수 확인: Application Insights / dotMemory
3. Pool 크기 임시 증가: `Max Pool Size=200`
4. 장기 Running 쿼리 Kill: `KILL <SPID>`

**성능 저하 시**:
1. 캐시 히트율 확인
2. Missing Index 추가
3. Query Store 분석 (SQL Server 2016+)
4. 실행 계획 수집

---

### ✅ 15. 백업 및 복구

- [ ] **자동 백업 설정**
  ```sql
  BACKUP DATABASE [MyDb] TO DISK = 'D:\Backups\MyDb.bak' WITH COMPRESSION;
  ```

- [ ] **백업 주기 설정**
  - 전체 백업: 매일 오전 2시
  - 증분 백업: 4시간마다
  - 트랜잭션 로그 백업: 15분마다

- [ ] **복구 테스트**
  - 월 1회 복구 테스트 수행
  - RTO (Recovery Time Objective): 1시간
  - RPO (Recovery Point Objective): 15분

---

## 배포 후 검증 (Post-Deployment)

### ✅ 16. Smoke Test

```csharp
// 헬스 체크
bool isHealthy = await db.HealthCheckAsync();
Assert.True(isHealthy);

// 간단한 쿼리
int count = await db.Default.Sql("SELECT COUNT(*) FROM Users").ExecuteScalarAsync<int>();
Assert.True(count >= 0);

// TVP 테스트
var testUsers = new[] { new UserDto(1, "Test") };
await db.Default.Procedure("dbo.usp_TestBulkInsert").With(new { Users = testUsers }).ExecuteAsync();
```

---

### ✅ 17. 성능 베이스라인 수립

- [ ] **응답 시간 측정**
  - P50: 중앙값
  - P95: 95th percentile
  - P99: 99th percentile

- [ ] **처리량 측정**
  - RPS (Requests Per Second)
  - TPS (Transactions Per Second)

---

### ✅ 18. 로그 모니터링

- [ ] **첫 24시간 집중 모니터링**
  - ERROR 로그 0건 목표
  - WARNING 로그 분석
  - 성능 이상 징후 확인

---

## 체크리스트 요약

| 카테고리 | 항목 수 | 필수 |
|:---|:---:|:---:|
| **배포 전 검증** | 6개 | ✅ |
| **모니터링 설정** | 3개 | ✅ |
| **성능 튜닝** | 2개 | ⚠️ |
| **보안** | 2개 | ✅ |
| **장애 대응** | 2개 | ✅ |
| **배포 후 검증** | 3개 | ✅ |
| **총계** | **18개** | - |

---

**모든 항목을 완료한 후 프로덕션 배포를 진행하세요!**

---

<p align="center">
  ⬅️ <a href="./11_migration_guide.md">이전</a>
  &nbsp;|&nbsp;
  <a href="../README.md">홈으로 ➡️</a>
</p>
