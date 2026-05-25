# Lib.Db Contract Tooling Design Spec

확인일: 2026-05-25

## 목적

Migration / Contract Tooling 후보는 `migration engine`보다 `contract validate/report` 도구로 시작하는 것이 맞다. Lib.Db의 포지션은 SQL Server/SP/TVP를 잘 아는 얇고 안전한 운영 라이브러리이며, 운영 DB를 자동 변경하는 도구가 아니다.

도구의 1차 목표는 저장 프로시저 parameter/result, TVP type, bulk target table/key/constraint를 SELECT metadata query 중심으로 검증하고 reviewable report를 생성하는 것이다.

v2.5.0 runtime-first 방향에서는 `Lib.Db.Tools`가 가장 먼저 깊게 설계되어야 한다. DB 변경은 consumer rebuild가 아니라 validate/report, CI exit code, runtime validation으로 드러나야 한다.

운영 DB와 명시적 runtime contract가 contract source of truth이며, generator/scaffold 산출물은 review용 draft 또는 검증 보조물일 뿐 schema 변경의 권위가 아니다.

## 공식 문서 확인

- `dotnet nuget push`: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push
- NuGet Trusted Publishing: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
- SQL Server Change Tracking 관련 문서는 adapter spec에 정리한다. 이 도구는 metadata query 중심으로만 사용한다.

## 냉정한 적합성 평가

추천: v2.5.0의 핵심 후보로 둔다. 단, 첫 구현은 DB 접속 없는 checked-in contract schema/report prototype으로 제한하고, 실제 SQL Server metadata inspect는 별도 후속 PR에서 SELECT-only로 추가한다.

이유:

- Runtime-first 운영 모델에서 DB drift를 앱 rebuild 없이 드러내려면 validate/report 도구가 필요하다.
- Generator가 live DB inspect를 하지 않으려면 checked-in metadata를 만드는 별도 도구가 필요하다.
- Migration engine으로 가면 운영 DB 변경 권한, transaction safety, rollback, drift resolution, DBA 승인 흐름까지 떠안게 된다. Lib.Db의 얇은 runtime 포지션과 맞지 않는다.
- Contract report는 안전하다. SELECT 중심이고, 운영 변경 없이 PR 리뷰에 올릴 수 있다.

## 패키지 및 책임 분리

권장 패키지:

- NuGet tool package: `Lib.Db.Tools`
- CLI command alias 후보: `dotnet-libdb`

책임:

- `Lib.Db.Tools`: inspect, validate, report, scaffold.
- `Lib.Db.Generator`: checked-in metadata를 소비해 compile-time helper를 생성.
- `Lib.Db`: runtime execution and contracts.

금지:

- 기본 실행에서 CREATE/ALTER/DROP/INSERT/UPDATE/DELETE/MERGE/TRUNCATE/EXEC 자동 수행.
- `apply`, `execute`, `migrate` 같은 운영 DB 변경 명령을 v2.5.0 stable scope에 포함.
- 운영 DB backup/restore 자동 수행.
- connection string 값 출력.
- tool 실행 결과에 secret/token/password 값 출력.

허용:

- SELECT metadata query.
- 사용자가 명시 opt-in한 deterministic script scaffold 파일 생성.
- scaffold 파일은 실행하지 않고 review 대상으로만 둔다.

## Public CLI/API 초안

CLI long-term shape:

```text
dotnet libdb inspect --connection <name-or-provider-key> --out libdb.contracts.json --include dbo.Customer_Get --include-tvp dbo.CustomerTvp
dotnet libdb validate --connection <name-or-provider-key> --contracts libdb.contracts.json --format markdown --out artifacts/libdb-contract-report.md
dotnet libdb report --contracts libdb.contracts.json --format markdown --out artifacts/libdb-contract-report.md
dotnet libdb scaffold --contracts libdb.contracts.json --kind tvp --out sql/review/libdb-scaffold.sql
```

v2.5.0 MVP CLI:

```text
dotnet run --project Lib.Db.Tools -- contract validate --expected expected.libdb.contracts.json --actual actual.libdb.contracts.json --format json --out artifacts/libdb-contract-report.json
dotnet run --project Lib.Db.Tools -- contract report --contracts libdb.contracts.json --format markdown --out artifacts/libdb-contract-report.md
```

`inspect`는 SELECT-only 후속 PR로 분리한다. `scaffold`는 v2.5.0 MVP에서 제외하고, deterministic review file generation 정책이 별도 승인된 뒤에만 다룬다.

Library shape:

```csharp
public sealed record LibDbToolOptions(
    string ConnectionName,
    string? ContractsPath,
    string? OutputPath,
    LibDbToolMode Mode);

public interface ILibDbMetadataReader
{
    Task<LibDbDatabaseContract> ReadAsync(LibDbInspectRequest request, CancellationToken cancellationToken);
}

public interface ILibDbContractValidator
{
    LibDbContractReport Validate(LibDbDatabaseContract expected, LibDbDatabaseContract actual);
}

public interface ILibDbScriptScaffolder
{
    LibDbScriptDraft Scaffold(LibDbDatabaseContract desired);
}
```

Metadata model:

```csharp
public sealed record LibDbDatabaseContract(
    IReadOnlyList<LibDbStoredProcedureContract> StoredProcedures,
    IReadOnlyList<LibDbTvpContract> TableValuedParameters,
    IReadOnlyList<LibDbBulkTargetContract> BulkTargets);
```

## Metadata query stance

후속 SELECT-only inspect tooling은 다음 계열의 metadata만 조회한다. v2.5.0 no-DB MVP는 운영 DB metadata를 조회하지 않는다.

- Objects, schemas, parameters, table types, columns, indexes, primary keys, foreign keys, computed/identity metadata.
- Stored procedure result metadata는 가능한 경우만 수집하고, 불확실하면 `Unknown` 또는 `RequiresManualContract`로 표시한다.

주의:

- 저장 프로시저 result shape는 SQL Server에서 항상 정적으로 안전하게 알 수 있는 것이 아니다.
- `EXEC` 기반 probing은 직접 SQL 실행 도구의 EXEC 금지와 충돌한다. 자동 실행하지 않는다.
- 도구가 앱/테스트 코드 경로에서 fixture DB를 다루는 경우는 별도 승인된 테스트 전략으로 분리한다.

## 보안 리스크

- Credential leakage: CLI args, logs, reports, artifacts에 connection string 값이 남을 수 있다. 입력은 named connection/provider key를 기본으로 하고 값은 redaction한다.
- Over-permission: metadata 조회 계정은 read-only metadata 권한으로 제한한다.
- Report poisoning: DB object names can contain unusual characters. Markdown/JSON output은 escaping한다.
- Scaffold misuse: script scaffold는 실행 명령을 제공하지 않고 파일 생성만 한다.

## AOT/trimming 리스크

- CLI tool은 runtime core와 분리한다. AOT 호환이 필수는 아니지만, shared metadata model은 trimming-safe하게 유지한다.
- Reflection-heavy command binding 라이브러리는 도구 내부로만 제한한다. Core runtime에 전파하지 않는다.

## SQL Server 운영 리스크

- Metadata query도 대형 DB에서 비용이 생길 수 있다. include filter를 기본으로 제공하고 전체 DB inspect는 명시 opt-in으로 한다.
- Stored procedure result discovery를 위해 실행을 시도하면 부작용이 생길 수 있다. 금지한다.
- 후속 SELECT-only inspect에서 bulk target validation은 key/constraint/index 존재를 확인하되, 자동 생성/변경하지 않는다.
- Report는 drift severity를 구분한다: breaking, warning, informational.

## 테스트/검증 전략

- 후속 SELECT-only inspect PR에서 Local SQL Server fixture 기반 metadata inspect integration test.
- Golden JSON/Markdown report snapshot.
- DDL/DML guard test: command planner가 SELECT 외 SQL을 자동 실행하지 않는지 확인.
- Redaction test: key 이름과 존재 여부만 출력하고 값은 출력하지 않는지 확인.
- Large schema smoke: many procedures/types/tables에서 runtime과 output size를 측정한다.
- 후속 generator PR에서 cross-package test: `Lib.Db.Generator`가 tool output JSON을 소비할 수 있는지 확인한다.

## 패키징/CI 영향

- `Lib.Db.Tools`는 dotnet tool package packaging이 필요하다.
- Publish workflow가 multi-package가 되면 accidental publish risk가 커진다. 먼저 infra hardening PR에서 pack/publish matrix, artifact scanner, tag/main guard를 검증한다.
- Trusted Publishing 전환 여부는 별도 release infra spec에서 결정한다.

## v2.5.0 포함 여부 추천

포함한다. 다만 범위는 no-DB prototype과 SELECT-only inspect plan으로 제한한다.

권장 prototype:

- DB 접속 없이 checked-in sample metadata JSON validate/report만 구현하는 no-DB prototype.
- 이후 별도 PR에서 SELECT metadata inspect를 추가한다.

Stable acceptance criteria:

- Tool output은 key 이름과 존재 여부만 표시하고 secret 값은 표시하지 않는다.
- v2.5.0 no-DB MVP의 기본 명령은 local contract file validate/report만 수행한다.
- SELECT metadata inspect는 별도 후속 PR에서만 추가하며 no-DB MVP acceptance에 포함하지 않는다.
- 운영 DB 변경 SQL scaffold는 MVP에서 제외한다.
- Report는 machine-readable JSON과 reviewable Markdown 중 최소 하나를 deterministic하게 생성한다.
- CI에서 drift를 실패로 만들 수 있는 exit code 정책을 문서화한다.
- Stored procedure result shape가 불확실하면 `Unknown` 또는 `RequiresManualContract`로 표시하고, tool/generator가 임의로 확정하지 않는다.
- DB object name, schema/table/procedure identifier는 quote/escape하고 report output은 Markdown/JSON injection을 방지한다.

## 구현 전 spec/plan 목록

- `libdb-contract-metadata-schema.md`: JSON schema와 versioning.
- `libdb-contract-tooling-select-query-plan.md`: 허용 SELECT metadata query 목록.
- `libdb-contract-report-format-plan.md`: Markdown/JSON report severity format.
- `libdb-contract-scaffold-policy.md`: script scaffold opt-in, non-execution, review flow.
- `libdb-tool-redaction-plan.md`: CLI/report/artifact secret redaction.
