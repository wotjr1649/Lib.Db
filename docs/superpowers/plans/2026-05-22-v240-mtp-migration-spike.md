# v2.4.0 MTP migration spike

확인일: 2026-05-22
브랜치: `v2.4.0-mtp-migration-spike`
기준 브랜치: `v2.4.0-provider-neutral-caching`

## 목적

이번 작업은 v2.4.0 본 릴리스 PR에 Microsoft.Testing.Platform(MTP)을 섞어 넣기 위한 정식 전환이 아니다. 다음 PR에서 release scripts/CI까지 전환할 수 있는지 판단하기 위해 xUnit v3 native MTP 실행, filter 번역, TRX, coverage, verification 환경 guard, artifact secret scan의 리스크를 격리 검증하는 spike다.

## 공식 자료 기준

- Microsoft: VSTest에서 Microsoft.Testing.Platform으로 migration 시 혼합 구성이 지원되지 않고, `dotnet test` 인자와 CI task를 전환해야 한다.
  <https://learn.microsoft.com/dotnet/core/testing/migrating-vstest-microsoft-testing-platform>
- Microsoft: .NET 10 SDK 이상은 `global.json`의 `test.runner`로 MTP native `dotnet test` 모드를 사용할 수 있다.
  <https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test>
- xUnit.net v3: MTP v2 선택은 `xunit.v3.mtp-v2` 패키지와 `UseMicrosoftTestingPlatformRunner`로 명시한다.
  <https://xunit.net/docs/getting-started/v3/microsoft-testing-platform>
- xUnit.net v3: VSTest의 `--filter` 문법 대신 `--filter-class`, `--filter-method`, `--filter-trait`, `--filter-query`를 사용한다.
  <https://xunit.net/docs/query-filter-language>
- Microsoft: MTP TRX는 `Microsoft.Testing.Extensions.TrxReport`와 `--report-trx` 계열 옵션으로 생성한다.
  <https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-test-reports>
- Microsoft: MTP Code Coverage는 `Microsoft.Testing.Extensions.CodeCoverage`와 Microsoft Code Coverage XML 설정을 사용하며, 기존 Coverlet runsettings와 설정 체계가 다르다.
  <https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-code-coverage>
  <https://github.com/microsoft/codecoverage/blob/main/docs/configuration.md>

## Spike 변경 사항

- 루트 `global.json`에 MTP runner를 명시했다.
- `Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj`에서 xUnit v3 MTP v2 runner를 opt-in했다.
- MTP TRX와 Microsoft Code Coverage extension 패키지를 추가했다.
- 기존 VSTest/MSBuild guard 우회를 막기 위해 `VerificationEnvironmentGuard` assembly fixture를 추가했다.
- `Verification/scripts/Invoke-MtpSpike.ps1`을 추가해 guard, runner, filter, DB matrix, TRX, coverage, artifact scan을 분리 실행할 수 있게 했다.
- `mtp-codecoverage.config.xml`을 추가해 기존 `coverlet.runsettings`를 정식 MTP coverage 경로와 섞지 않았다.

## 검증 결과

| 항목 | 결과 |
| --- | --- |
| Build | `dotnet build .\Verification\projects\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --no-incremental -v:minimal` 통과, 경고 0개, 오류 0개 |
| Guard | verification DB 환경이 없을 때 MTP 실행을 런타임 assembly fixture가 차단 |
| Runner | MTP runner help에서 `Microsoft.Testing.Platform`, `--filter-class`, `--report-trx`, `--coverage` 확인 |
| Filter | `--filter-class *CacheHostingCoverageTests*` 실행, 32개 통과 |
| DB matrix | `--filter-class *V230TvpMatrixTests*` 실행, 5개 통과 |
| TRX | `--report-trx`로 `Verification/artifacts/mtp-spike/trx/mtp-matrix.trx` 생성 |
| Coverage | `--coverage --coverage-output-format cobertura`로 `coverage.cobertura.xml` 생성 |
| Artifact scan | secret pattern path 없음, 생성 artifact는 ignored/untracked 상태 |

최종 실행 명령:

```powershell
pwsh -NoProfile -File .\Verification\scripts\Invoke-MtpSpike.ps1 -NoRestore
```

## Codex Security 관점 리스크 평가

### P1: 기존 MSBuild `BeforeTargets="VSTest"` guard는 MTP native에서 충분하지 않다

초기 spike 실행에서 verification 환경 변수를 제거했는데도 guard 시나리오가 성공했다. 이는 .NET 10 MTP native runner 경로에서 기존 `VSTest` target 기반 guard를 릴리스 보안 경계로 볼 수 없다는 뜻이다.

보완: xUnit assembly fixture인 `VerificationEnvironmentGuard`를 추가했다. 이 guard는 테스트 프로세스 내부에서 동작하므로 `dotnet test`, `dotnet run`, 직접 test executable 실행 경로에 모두 적용된다. `LIBDB_SKIP_TEST_ENV_GUARD=true`는 non-DB 테스트 spike 전용으로만 사용한다.

정식 전환 게이트: CI/release script 전환 전, guard 실패 시나리오가 clean 환경에서 반드시 실패해야 한다.

### P2: VSTest filter 문법과 xUnit v3 MTP filter 문법이 호환되지 않는다

기존 release verification은 `--filter FullyQualifiedName~...` 계열에 의존한다. xUnit v3 native MTP에서는 `--filter-class`, `--filter-method`, `--filter-trait`, `--filter-query`로 번역해야 한다.

보완: spike script에서 class filter를 사용하고, 각 filtered 실행에 `--minimum-expected-tests 1`을 추가해 오타나 번역 실수로 0개 테스트가 실행되는 상태를 릴리스 성공으로 오해하지 않게 했다.

정식 전환 게이트: `Invoke-Verification.ps1`, `Invoke-Tests.ps1`, `Invoke-Coverage.ps1`의 모든 filter를 xUnit MTP 문법으로 번역하고 테스트 개수 기대치를 둔다.

### P2: 기존 Coverlet runsettings는 Microsoft Code Coverage MTP 설정으로 재사용할 수 없다

기존 `coverlet.runsettings`를 `--coverage-settings`에 넘기면 MTP coverage 단계가 `invalid settings`로 실패했다. 기존 파일은 VSTest `XPlat Code Coverage` data collector 설정이고, MTP Microsoft Code Coverage는 `<Configuration>` 루트의 XML 설정을 기대한다.

보완: `mtp-codecoverage.config.xml`을 별도로 추가했다. 정식 전환에서는 Microsoft Code Coverage로 갈지, Coverlet semantics를 유지하기 위해 `coverlet.MTP`를 쓸지 별도 결정이 필요하다.

정식 전환 게이트: coverage 산출물 형식, 제외 규칙, deterministic report, CI artifact 업로드 경로를 기존 VSTest 결과와 비교한다.

### P2: TRX와 coverage artifact가 새 위치에 생성된다

MTP extension은 TRX와 coverage를 만들 수 있지만, 기존 CI가 기대하는 logger 이름, 결과 디렉터리, 파일명과 다를 수 있다.

보완: spike script는 TRX와 coverage 파일명을 고정하고, artifact secret scan 및 untracked/ignored gate를 통과시켰다.

정식 전환 게이트: GitHub Actions 또는 release script에서 업로드 경로와 retention 정책을 MTP artifact 경로로 맞춘다.

### P3: 이번 spike를 정식 migration으로 오해하면 release PR의 blast radius가 커진다

현재 작업은 integration test project와 spike script에 한정된다. 공식 release verification scripts와 CI는 아직 VSTest 흐름이다.

정식 전환 게이트: v2.4.0 릴리스 PR에는 이 spike를 직접 섞지 않고, 다음 PR에서 MTP migration spike 결과를 리뷰한 뒤 release scripts/CI 전환 PR을 별도로 진행한다.

## 결론

MTP 전환 가능성은 있다. 다만 정식 전환 조건은 "runner가 돈다"가 아니라 다음 네 가지를 모두 만족하는 것이다.

1. verification 환경 guard가 runner 경로와 무관하게 실패해야 할 때 실패한다.
2. 모든 filter가 xUnit v3 MTP 문법으로 번역되고 zero-test 성공을 허용하지 않는다.
3. TRX와 coverage artifact가 기존 릴리스 소비자와 CI 업로드 경로에 맞게 유지된다.
4. artifact scan과 clean full verification이 새 runner에서 통과한다.

이 spike는 1-3의 대표 경로를 통과시켰고, 4는 다음 정식 전환 PR에서 release scripts/CI를 실제로 바꾼 뒤 전체 verification으로 닫아야 한다.
