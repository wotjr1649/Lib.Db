# v2.4.0 MTP Release Scripts and CI Design

확인일: 2026-05-22
대상 브랜치: `v2.4.0-mtp-release-ci`
기준 브랜치: `v2.4.0-mtp-migration-spike`

## 배경

PR #9는 xUnit v3 native Microsoft.Testing.Platform(MTP) 실행 가능성을 spike로 검증했다. 다음 PR은 spike 결과를 release scripts와 CI에 정식 반영해서 v2.4.0 이후 공식 release verification이 VSTest 문법에 의존하지 않도록 만든다.

공식 자료 기준:

- .NET 10 SDK의 MTP native `dotnet test` mode는 `global.json`의 `test.runner`로 활성화하며, MTP 인자에 추가 `--`가 필요하지 않다.
  <https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test>
- VSTest에서 MTP로 전환할 때 `--filter`, `--logger`, `--collect`, `--settings` 같은 VSTest 인자를 MTP 대응 인자로 바꿔야 한다.
  <https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform>
- xUnit v3 MTP는 VSTest `--filter` 문법을 그대로 지원하지 않고 `--filter-class`, `--filter-method`, `--filter-trait`, `--filter-query`를 사용한다.
  <https://xunit.net/docs/getting-started/v3/microsoft-testing-platform>
  <https://xunit.net/docs/query-filter-language>
- MTP TRX는 `Microsoft.Testing.Extensions.TrxReport`와 `--report-trx`를 사용한다.
  <https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-extensions-test-reports>
- MTP Code Coverage는 `Microsoft.Testing.Extensions.CodeCoverage`와 `--coverage` 계열 옵션을 사용한다.
  <https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-code-coverage>

## 목표

1. `Invoke-Verification.ps1`의 공식 release gate를 MTP 인자로 전환한다.
2. `Invoke-Tests.ps1`를 MTP 친화 CLI wrapper로 바꿔 VSTest-only 인자 사용을 줄인다.
3. `Invoke-Coverage.ps1`를 MTP Code Coverage 경로로 전환하되 기존 Cobertura gate와 reportgenerator 흐름은 유지한다.
4. Microsoft Code Coverage와 Coverlet의 Cobertura class naming/branch 해석 차이를 gate와 테스트로 흡수한다.
5. GitHub Actions publish workflow가 MTP release gate 산출물을 업로드하고, secret/artifact guard를 유지한다.
6. zero-test 성공, verification 환경 guard 우회, artifact secret 노출을 release blocker로 취급한다.

## 비목표

- PR #9의 MTP runner opt-in 구조를 다시 설계하지 않는다.
- NuGet publish credential 방식이나 SQL Server service container 구조를 바꾸지 않는다.
- coverage threshold 자체를 낮추지 않는다. MTP coverage 산출 수치가 다르면 원인을 분석하고 threshold 조정은 별도 리뷰 대상으로 둔다.
- `TESTINGPLATFORM_EXITCODE_IGNORE=8` 또는 `--ignore-exit-code 8`로 zero-test를 성공 처리하지 않는다.

## 설계

### Release Verification

`Invoke-Verification.ps1`는 matrix DB test 단계에서 다음 전환을 적용한다.

- `dotnet test <project>`를 `dotnet test --project <project>`로 명시한다.
- `--filter FullyQualifiedName~Lib.Db.IntegrationTests.V230Matrix.V230TvpMatrixTests`를 `--filter-class *V230TvpMatrixTests*`로 바꾼다.
- `--logger trx;LogFileName=v230-matrix.trx`를 `--report-trx --report-trx-filename v230-matrix.trx`로 바꾼다.
- `--minimum-expected-tests 1`을 추가해 filter 번역 실수로 0개 테스트가 실행되는 상태를 막는다.
- matrix TRX 파일이 생성됐는지 명시 확인한다.

### Test Wrapper

`Invoke-Tests.ps1`는 MTP용 옵션을 직접 받는다.

- 새 옵션: `-FilterClass`, `-FilterMethod`, `-FilterTrait`, `-FilterQuery`, `-ReportTrx`, `-TrxFileName`
- `-Logger`는 backward compatible alias로 남기되, `trx` 계열만 MTP TRX로 번역한다.
- `-Filter`는 단순 `FullyQualifiedName~ClassName` 패턴만 `--filter-class *ClassName*`으로 번역한다. 복잡한 VSTest expression은 명시 오류로 막고 `-FilterQuery` 사용을 안내한다.
- filtered run에는 `--minimum-expected-tests 1`을 기본 추가한다.

### Coverage

`Invoke-Coverage.ps1`는 기존 Coverlet collector 경로를 MTP Code Coverage로 바꾼다.

- `--settings coverlet.runsettings --collect "XPlat Code Coverage"` 제거
- `--coverage --coverage-output-format cobertura --coverage-output <fixed path> --coverage-settings mtp-codecoverage.config.xml` 사용
- output 파일명을 `coverage.cobertura.xml`로 고정해 reportgenerator와 coverage gate가 같은 파일을 읽게 한다.
- reportgenerator와 `Assert-LibDbCoverage.ps1` 호출은 유지한다.
- `Assert-Coverage.ps1`는 generic class 이름을 ``Type`1``과 `Type<T>` 양쪽 형식으로 인식한다.
- Microsoft Code Coverage가 switch branch를 더 세밀하게 계산하는 경로는 테스트를 추가해 100% branch gate를 유지한다.

### CI

`.github/workflows/publish.yml`는 기존 publish 흐름을 유지하되 release verification 산출물을 보존한다.

- `Run v2.4.0 release gate`는 기존 `Invoke-Verification.ps1 -BenchmarkJob Short`를 유지하되 내부가 MTP로 동작한다.
- verification 후 `actions/upload-artifact@v4`를 `if: always()`로 추가한다.
- 업로드 경로는 `Verification/artifacts/**`로 제한한다.
- `if-no-files-found: warn`, `retention-days: 7`을 사용한다.

## 보안/릴리스 무결성 규칙

- release verification에서는 `LIBDB_SKIP_TEST_ENV_GUARD=true`를 사용하지 않는다.
- 환경 변수와 secret은 존재 여부만 출력한다.
- zero-test exit를 무시하지 않는다.
- artifact는 제한된 verification artifact root만 업로드한다.
- 생성 artifact는 `Scan-VerificationArtifacts.ps1`와 `Assert-GeneratedArtifactsUntracked.ps1`를 통해 검증한다.

## 검증 기준

로컬에서 다음 명령이 통과해야 한다.

```powershell
dotnet build .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --no-incremental -v:minimal
pwsh -NoProfile -File .\Verification\scripts\Invoke-MtpSpike.ps1 -NoRestore
pwsh -NoProfile -File .\Verification\scripts\Invoke-Coverage.ps1 -RestoreTools
pwsh -NoProfile -File .\Verification\scripts\Invoke-Verification.ps1 -BenchmarkJob Short
rg -n -e "xUnit1051|NoWarn" Verification/projects/Lib.Db.IntegrationTests
git diff --check
```

CI에서는 publish workflow가 tag에서 동일 release gate를 실행하고, 실패 시에도 verification artifact를 업로드해야 한다.
