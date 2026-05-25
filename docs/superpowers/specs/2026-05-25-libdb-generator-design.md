# Lib.Db Generator Design Spec

확인일: 2026-05-25

## 목적

Lib.Db.Generator는 Lib.Db core runtime을 더 무겁게 만들지 않고, 반복적인 SQL Server 매핑 코드를 빌드 타임에 생성하는 별도 analyzer/source generator 패키지 후보이다. 목표는 AOT-safe mapper, `BulkShape<T>`, TVP shape, 저장 프로시저 parameter/result contract helper를 단계적으로 검증하는 것이다.

이 문서는 구현 지시가 아니라 v2.5.0 후보 적합성 평가와 prototype boundary를 고정하기 위한 설계 초안이다.

## 공식 문서 확인

- Microsoft Learn, `IIncrementalGenerator`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.iincrementalgenerator?view=roslyn-dotnet-4.14.0
- Roslyn source generator cookbook: https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md
- NuGet analyzer packaging conventions: https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions

확인한 핵심 사실:

- `IIncrementalGenerator`는 compiler가 수명주기를 관리하므로 generator instance에 state를 저장하지 않아야 한다.
- Analyzer/source generator NuGet은 `analyzers/dotnet/cs` 같은 analyzer asset 경로로 배치하는 관례를 따른다.
- Incremental generator는 IDE/build 성능이 제품 품질의 일부다. 입력은 syntax/semantic/additional files로 좁히고 deterministic output을 보장해야 한다.

## Runtime-first 결정

Lib.Db.Generator는 Lib.Db의 DB contract source of truth가 아니다. Lib.Db의 기본 모델은 runtime-first이다. 운영 DB schema, SP, TVP가 앱 배포와 독립적으로 바뀔 수 있으므로, DB 변경 대응의 최종 방어선은 runtime validation과 `Lib.Db.Tools` report여야 한다.

Generator는 compile-time metaprogramming이므로 generator 입력이 바뀌면 consumer rebuild가 필요하다. 따라서 DB metadata 변경을 generator의 정상 경로로 만들면 Lib.Db의 런타임 운영 라이브러리 포지션과 충돌한다.

Acceptance criteria:

- Generator는 정상 runtime 사용 경로의 필수 dependency가 아니다.
- Generator는 live DB에 접속하지 않는다.
- Generator는 DB schema/SP/TVP 변경을 자동 반영하는 주체가 아니다.
- Generator 산출물이 오래되어도 runtime validation 또는 Tools report가 최종 방어선으로 남아야 한다.
- v2.5.0 stable scope에는 DB metadata 기반 generator를 포함하지 않는다.

## 냉정한 적합성 평가

추천: v2.5.0에서 stable generator 제품을 구현하지 않는다. Generator는 optional AOT/performance preview 후보로 낮추고, v2.5.0의 중심은 `Lib.Db.Tools` contract validate/report로 둔다.

적합한 부분:

- Lib.Db가 이미 `DbResultAttribute`, `BulkShape<T>`, TVP 모델을 갖고 있어 generator가 붙을 소비 지점은 있다.
- Native AOT 방향과 잘 맞는다. reflection scan이나 expression compile 의존을 줄일 수 있다.
- Runtime API를 늘리지 않고 별도 `Lib.Db.Generator` 또는 `Lib.Db.Analyzers` 패키지로 제공할 수 있다.

부적합하거나 위험한 부분:

- Compile-time live DB inspect는 금지한다. 빌드가 네트워크/DB 권한/운영 DB 상태에 의존하면 재현성과 보안성이 무너진다.
- 저장 프로시저 result shape는 SQL Server metadata만으로 완전하지 않을 수 있다. 동적 SQL, 임시 테이블, conditional result set은 generator가 확정하면 안 된다.
- Generator가 core runtime public surface를 안정화하기 전에 과도한 attribute/API를 노출하면 v2.x 호환성 부담이 커진다.
- 현재 `IGeneratedMapper<T>`와 `ISqlMapper<T>`는 internal 계약이다. 별도 NuGet generator가 consumer project에 생성한 코드로 이 계약을 직접 구현하는 예시는 현재 구조와 맞지 않는다.

## 패키지 및 책임 분리

권장 패키지명: `Lib.Db.Generator`

대안:

- `Lib.Db.Analyzers`: analyzer 진단 중심 이름이다. Source generator가 주 기능이면 이름이 좁지 않다.
- `Lib.Db.Generator`: generator-first 패키지로 명확하다. Analyzer diagnostic/code fix를 포함해도 자연스럽다.

책임:

- `Lib.Db`: runtime contracts only. 가능한 변경은 public attribute/marker 안정화 정도로 제한한다.
- `Lib.Db.Generator`: compile-time code generation and diagnostics. DB metadata consumption은 preview/opt-in AdditionalFiles로만 검토한다.
- `Lib.Db.Tools`: DB metadata validate/report. Generator가 아니라 runtime/report 흐름의 source of truth를 맡는다.

금지:

- Generator package가 `Microsoft.Data.SqlClient`를 참조하고 DB에 접속하는 설계.
- Generator output이 현재 시각, 머신 경로, 랜덤 값, DB 상태에 따라 달라지는 설계.
- Runtime package가 generator 구현 assembly에 의존하는 설계.

## Public API 초안

이 API는 스케치이며 v2.5.0에서 바로 public으로 고정하지 않는다.

```csharp
namespace Lib.Db.Generator;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class LibDbGenerateMapperAttribute : Attribute
{
    public string? ContractName { get; init; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class LibDbBulkShapeAttribute : Attribute
{
    public string Table { get; }
    public LibDbBulkShapeAttribute(string table);
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class LibDbBulkColumnAttribute : Attribute
{
    public string? Name { get; init; }
    public bool IsKey { get; init; }
    public int Order { get; init; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class LibDbTvpShapeAttribute : Attribute
{
    public string TypeName { get; }
    public LibDbTvpShapeAttribute(string typeName);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class LibDbStoredProcedureContractAttribute : Attribute
{
    public string Procedure { get; }
    public LibDbStoredProcedureContractAttribute(string procedure);
}
```

생성물 후보:

```csharp
// Generated, partial name illustrative only.
internal sealed class CustomerRowLibDbMapper
{
    public CustomerRow Map(IDataRecord record);
}

internal static partial class CustomerRowLibDbBulk
{
    public static BulkShape<CustomerRow> Shape { get; }
}

internal static partial class CustomerRowLibDbTvp
{
    public static TvpShape Shape { get; }
}
```

중요한 boundary:

- Attribute type을 core에 둘지 generator package에 둘지는 별도 결정이 필요하다.
- Core runtime이 generator package에 의존하지 않으려면 attribute는 core에 둘 수 있다. 단, attribute가 늘어날수록 runtime surface가 커진다.
- Prototype은 consumer project에 marker attribute source를 post-initialization으로 주입하는 방식과 core attribute를 참조하는 방식을 비교한다.
- Mapper 자동 통합은 public registration API 또는 public minimal mapping contract를 먼저 설계한 뒤에만 진행한다. 현재 internal mapper interface 직접 구현은 v2.5.0 prototype에서 제외한다.

## Metadata 입력

허용하되 v2.5.0 stable 범위에서는 제외:

- Checked-in JSON/YAML metadata or non-executable SQL metadata fixtures.
- `AdditionalFiles`로 들어오는 `libdb.contracts.json`.
- deterministic parser와 schema version.

금지:

- 빌드 중 DB 접속.
- connection string, secret, token을 generator diagnostic에 출력.
- schema drift를 자동 수정하는 생성.

예상 JSON 모양:

```json
{
  "version": 1,
  "storedProcedures": [
    {
      "name": "dbo.Customer_Get",
      "parameters": [
        { "name": "@CustomerId", "sqlType": "int", "nullable": false }
      ],
      "results": [
        { "name": "CustomerId", "sqlType": "int", "nullable": false }
      ]
    }
  ],
  "tvps": [
    {
      "name": "dbo.CustomerTvp",
      "columns": [
        { "name": "CustomerId", "sqlType": "int", "nullable": false, "ordinal": 0 }
      ]
    }
  ]
}
```

## 보안 리스크

- Secret exposure: AdditionalFiles나 analyzer config에 connection string이 들어오면 diagnostic/output에 값이 노출될 수 있다. 값 출력은 금지하고 key 존재 여부만 진단한다.
- Supply chain: generator는 consumer 빌드에서 실행된다. 별도 패키지로 분리하고 최소 dependency, deterministic pack, analyzer asset 검사, signed/provenance 검토가 필요하다.
- SQL injection: generator가 SQL text를 합성하는 경우 identifier quoting 규칙을 반드시 core와 공유하거나 생성하지 않는다.
- DB 권한 오용: live inspect 금지로 차단한다.

## AOT/trimming 리스크

- 목표는 AOT 개선이지만 generator 자체는 빌드 도구다. 생성된 runtime code가 reflection, dynamic method, `Expression.Compile`, assembly scan에 의존하면 실패다.
- 현재 `IGeneratedMapper<T>` 검색 경로에 assembly scan이 있다면 prototype에서 registration model을 함께 검토한다.
- Generated mapper는 direct ordinal/name lookup과 static registration을 우선한다.

## SQL Server 운영 리스크

- Generator 자체는 SQL Server에 접속하지 않는다.
- SP/result contract는 metadata source의 정확성에 의존한다. 운영 DB drift 검증은 `Lib.Db.Tools`의 validate/report 책임이며 generator가 대체하지 않는다.
- TVP/bulk shape 생성은 SQL type/nullable/ordinal/key mismatch가 운영 장애로 이어질 수 있어 analyzer diagnostic severity를 신중하게 설계한다.

## 테스트/검증 전략

- Roslyn generator unit tests: marker attribute, partial type, invalid model, duplicate columns, nullable mapping.
- Snapshot/golden tests: generated source deterministic 비교. 줄바꿈은 LF로 고정한다.
- Analyzer packaging test: `.nupkg` 내부 `analyzers/dotnet/cs` asset 확인.
- Consumer compile test: generated mapper/bulk/tvp shape가 Lib.Db consumer project에서 컴파일되는지 확인.
- AOT smoke: generated path가 Native AOT publish에서 trim/AOT warning을 만들지 않는지 확인.
- Performance guard: large solution synthetic input으로 incremental rerun 범위를 측정한다.

## 패키징/CI 영향

- 새 패키지가 생기면 release workflow의 pack/push 대상이 늘어난다. v2.5.0에서 publish workflow를 같이 바꾸지 않는 것이 안전하다.
- 별도 infra PR에서 multi-package pack, artifact scanner, publish guard negative test를 먼저 정리한다.
- Analyzer dependency는 `PrivateAssets=all`을 기본으로 문서화한다.

## v2.5.0 포함 여부 추천

stable 기능으로 포함하지 않는다. v2.5.0에는 다음 설계/계획만 포함한다.

1. `Lib.Db.Generator` prototype plan 작성.
2. Core attribute 위치 결정.
3. public registration 또는 public minimal mapping contract boundary 결정.
4. generator rebuild boundary 결정.
5. `Lib.Db.Tools` metadata validate/report와 generator opt-in 입력 파일 관계 결정.

## 구현 전 spec/plan 목록

- `libdb-generator-prototype-plan.md`: mapper-only prototype.
- `libdb-generator-packaging-plan.md`: analyzer NuGet layout and CI pack validation.
- `libdb-metadata-contract-schema.md`: shared JSON schema for generator/tools.
- `libdb-aot-mapper-registration-plan.md`: generated mapper discovery without runtime assembly scanning.
- `libdb-generator-rebuild-boundary.md`: DB 변경이 consumer rebuild를 강제하지 않는 조건과 non-goals.
