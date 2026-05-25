# Lib.Db SQL Server Change Tracking Adapter Design Spec

확인일: 2026-05-25

## 목적

SQL Server Change Tracking Adapter는 custom trigger/table system이 아니라 SQL Server native Change Tracking을 얇게 감싸는 provider/adapter package 후보이다. Core runtime에 넣지 않고, Change Tracking을 이미 운영 정책으로 선택한 팀이 Lib.Db 방식의 `DbResult<T>`, redaction, backpressure, checkpoint handling을 사용할 수 있게 하는 것을 목표로 한다.

v2.5.0에서 이 후보는 core 기능이 아니라 provider/read-adapter 설계로만 다룬다. Lib.Db가 Change Tracking을 켜거나 checkpoint 저장소를 자동 생성하면 runtime-first thin library 범위를 벗어난다.

## 공식 문서 확인

- About Change Tracking: https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/about-change-tracking-sql-server?view=sql-server-ver17
- CHANGETABLE: https://learn.microsoft.com/en-us/sql/relational-databases/system-functions/changetable-transact-sql?view=sql-server-2017
- CHANGE_TRACKING_CURRENT_VERSION: https://learn.microsoft.com/en-us/sql/relational-databases/system-functions/change-tracking-current-version-transact-sql?view=sql-server-ver16
- CHANGE_TRACKING_MIN_VALID_VERSION: https://learn.microsoft.com/en-us/sql/relational-databases/system-functions/change-tracking-min-valid-version-transact-sql?view=sql-server-ver17
- Work with Change Tracking: https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/work-with-change-tracking-sql-server?view=sql-server-ver16

확인한 핵심 사실:

- Change Tracking은 lightweight row-change mechanism이며 custom trigger/table 없이 동작한다.
- Tracked table의 primary key 값이 change information에 기록된다.
- `CHANGETABLE(CHANGES ...)`는 특정 version 이후 변경을 조회한다.
- `CHANGE_TRACKING_CURRENT_VERSION()`은 다음 enumerate 기준으로 쓸 현재 version을 반환한다.
- `CHANGE_TRACKING_MIN_VALID_VERSION()`은 last sync version이 retention cleanup 이후에도 유효한지 확인하는 데 필요하다.

## 냉정한 적합성 평가

추천: v2.5.0에 포함하지 않는다.

적합한 부분:

- Lib.Db의 SQL Server 전문성, operational safety, `DbResult<T>` 결과 모델과 잘 맞는다.
- Polling/checkpoint/report adapter는 runtime core를 크게 흔들지 않고 provider package로 분리할 수 있다.

위험한 부분:

- Change Tracking은 enablement 자체가 DDL 운영 결정이다. Lib.Db가 자동으로 켜면 안 된다.
- Retention window를 놓치면 checkpoint invalid 상태가 된다. 이때 자동으로 full resync를 시작하면 데이터 폭주/중복/누락 위험이 있다.
- Delete row는 base table join 결과가 없으므로 별도 모델링이 필요하다.
- Composite key, tenant/key exposure, high-churn table performance를 단순 API로 가리면 운영 장애가 된다.

## 패키지 및 책임 분리

권장 패키지: `Lib.Db.SqlServer.ChangeTracking`

책임:

- Change Tracking read/query/checkpoint helper.
- Invalid checkpoint detection.
- Per-table adapter with explicit key model.
- Optional hosted polling wrapper는 별도 subpackage 또는 later phase.

Core runtime 변경:

- 없어야 한다.
- 필요한 경우 shared `DbResult<T>`와 existing execution abstractions만 소비한다.

금지:

- `ALTER DATABASE ... SET CHANGE_TRACKING = ON` 자동 실행.
- `ALTER TABLE ... ENABLE CHANGE_TRACKING` 자동 실행.
- Custom trigger/table 생성.
- Checkpoint table 자동 생성.
- Tenant/key 값을 diagnostic에 그대로 출력.
- Runtime raw table/schema string을 query builder에 직접 전달.

## Public API 초안

```csharp
namespace Lib.Db.SqlServer.ChangeTracking;

public readonly record struct ChangeTrackingVersion(long Value);

public sealed record ChangeTrackingTable
{
    private ChangeTrackingTable(string schema, string name)
    {
        Schema = schema;
        Name = name;
    }

    public string Schema { get; }

    public string Name { get; }

    public static ChangeTrackingTable FromValidatedIdentifier(string schema, string name);
}

public sealed record ChangeTrackingReadOptions(
    int BatchSize = 1000,
    bool IncludeDeletes = true,
    bool ValidateMinVersion = true);

public enum ChangeTrackingOperation
{
    Insert,
    Update,
    Delete
}

public sealed record ChangeTrackingRow<TKey>(
    TKey Key,
    ChangeTrackingOperation Operation,
    ChangeTrackingVersion Version,
    IReadOnlySet<string>? ChangedColumns);

public sealed record ChangeTrackingBatch<TKey>(
    ChangeTrackingVersion From,
    ChangeTrackingVersion To,
    IReadOnlyList<ChangeTrackingRow<TKey>> Rows);

public interface IChangeTrackingReader<TKey>
{
    Task<DbResult<ChangeTrackingVersion>> GetCurrentVersionAsync(CancellationToken cancellationToken);

    Task<DbResult<ChangeTrackingBatch<TKey>>> ReadChangesAsync(
        ChangeTrackingVersion lastSyncVersion,
        ChangeTrackingReadOptions options,
        CancellationToken cancellationToken);
}
```

Invalid checkpoint:

```csharp
public sealed record ChangeTrackingInvalidCheckpoint(
    ChangeTrackingTable Table,
    ChangeTrackingVersion LastSyncVersion,
    ChangeTrackingVersion MinimumValidVersion,
    string Reason);
```

Composite key shape:

```csharp
public readonly record struct OrderLineKey(int OrderId, int LineNumber);
```

## Data flow

1. Caller stores checkpoint outside adapter.
2. Adapter reads `CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(...))`.
3. If checkpoint is lower than min valid version, return invalid checkpoint failure. Do not auto-resync.
4. Adapter reads `CHANGE_TRACKING_CURRENT_VERSION()` as upper bound.
5. Adapter queries `CHANGETABLE(CHANGES schema.table, @lastSyncVersion)`.
6. Adapter maps key, operation, version, changed columns.
7. Caller processes rows and persists checkpoint after successful downstream processing.

## 보안 리스크

- Key exposure: primary/composite key values may be customer identifiers. Logs and diagnostics must redact or hash keys by default.
- Tenant boundary: adapter must not infer tenant filters. Consumer must provide explicit tenant predicate strategy if needed.
- Permission leakage: insufficient permission can make min valid version return NULL. Error must distinguish not enabled/not found/permission unknown without exposing connection details.
- SQL injection: table/schema names are identifiers. Require configured, quoted identifiers and reject runtime raw table names unless prevalidated.

## AOT/trimming 리스크

- Adapter should be ordinary runtime code without reflection-heavy mapper discovery.
- Key mapper can use explicit delegates or generated helpers later.
- Avoid dynamic expression compilation.

## SQL Server 운영 리스크

- Retention window: if cleanup removes versions before checkpoint, changes may be invalid. Adapter returns a hard failure requiring explicit reinitialize/resync.
- Snapshot isolation: docs note cleanup interactions can leave expired rows in `sys.syscommittab` with open snapshot transactions. Adapter docs must call out long transaction risk.
- Delete rows: base table data may no longer exist. Delete events only carry key/change metadata.
- Composite keys: order and SQL type mapping must be explicit.
- Backpressure: batch size, max version span, and checkpoint commit policy must be caller-controlled.
- Performance: high-churn tables need index/plan validation. Consider `FORCESEEK` only as an explicit advanced option after query plan testing.

## 테스트/검증 전략

- Local SQL Server integration tests with Change Tracking enabled by fixture code path only.
- Tests for insert/update/delete semantics and changed columns.
- Retention invalid checkpoint test by controlled fixture or simulated metadata reader.
- Composite key mapping test.
- Redaction test for key values and connection details.
- Backpressure test with batch size and checkpoint persistence handoff.
- No DDL/DML direct SQL tool execution in manual workflows; fixture setup through test code is allowed.

## 패키징/CI 영향

- Separate provider package prevents core dependency growth.
- Integration tests need local SQL Server verification environment; CI should keep them behind existing guarded scripts.
- Package must document that it does not enable Change Tracking or create checkpoint tables.

## v2.5.0 포함 여부 추천

stable provider로 포함하지 않는다. Change Tracking adapter는 appealing하지만 운영 리스크가 높다. 먼저 design + simulated reader prototype plan을 작성하고, real SQL integration은 후속 PR로 둔다.

Stable acceptance criteria before any package:

- Identifier는 validated/allowlisted type을 통해서만 SQL query builder에 들어간다.
- `CHANGE_TRACKING_MIN_VALID_VERSION()` failure는 자동 resync가 아니라 hard failure로 반환한다.
- Adapter는 `ALTER DATABASE`, `ALTER TABLE`, checkpoint table create를 실행하지 않는다.
- Key/tenant value는 diagnostic에서 redacted 또는 hashed로만 나온다.
- Consumer가 checkpoint 저장, row materialization, retention 만료 복구를 명시적으로 책임진다.

## 구현 전 spec/plan 목록

- `libdb-change-tracking-api-plan.md`: reader/checkpoint/error API.
- `libdb-change-tracking-sql-plan.md`: SELECT-only query templates and identifier validation.
- `libdb-change-tracking-operational-guide.md`: retention, invalid checkpoint, isolation, delete row, composite key guidance.
- `libdb-change-tracking-test-plan.md`: fixture setup and no-direct-DDL manual rules.
