# Lib.Db Release Infra Hardening Design Spec

확인일: 2026-05-25

## 목적

MTP / release scripts / CI / Node 24 hardening은 v2.5.0 기능 PR과 섞지 않고 별도 infra PR로 분리하는 것이 맞다. 목표는 publish guard, artifact redaction, MTP reporting/filtering, GitHub Actions runtime drift를 점검해 기능성 패키지 추가 전에 release surface를 안정화하는 것이다.

v2.5.0 브랜치에서는 main merge, tag, release, NuGet publish를 실행하지 않는다. Release infra hardening은 먼저 검증 가능한 guard와 dry-run을 추가하고, 실제 publish 경로 변경은 별도 승인 이후에만 진행한다.

## 공식 문서 확인

- `dotnet test`: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test
- Testing with `dotnet test`: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-platform-integration-dotnet-test
- Test platforms overview: https://learn.microsoft.com/en-us/dotnet/core/testing/test-platforms-overview
- actions/checkout: https://github.com/actions/checkout
- actions/setup-dotnet: https://github.com/actions/setup-dotnet
- actions/upload-artifact releases: https://github.com/actions/upload-artifact/releases
- NuGet Trusted Publishing: https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
- `dotnet nuget push`: https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-push

확인한 핵심 사실:

- .NET 10 SDK부터 `dotnet test` runner selection이 `global.json`의 `test.runner`로 가능하며 MTP는 `Microsoft.Testing.Platform` 1.7 이상이 필요하다.
- 현재 repo는 `global.json`에서 `Microsoft.Testing.Platform` runner를 사용한다.
- `actions/checkout@v6`, `actions/setup-dotnet@v5`, `actions/upload-artifact@v6`는 Node 24 runtime 계열이다. self-hosted runner는 최소 runner version 검토가 필요하다.
- NuGet Trusted Publishing은 long-lived API key 없이 GitHub Actions OIDC 기반 short-lived credential을 사용하는 모델이지만 rollout 상태와 NuGet 계정 정책 확인이 필요하다.
- `dotnet nuget push`는 `--api-key`, `--source`, `--skip-duplicate` 등 명시 옵션을 제공한다.

## 냉정한 적합성 평가

추천: 별도 infra PR로 v2.5.0 전에 처리하거나, 최소한 기능 PR과 분리한다.

적합한 이유:

- Generator/tool/provider 패키지가 늘어나기 전에 publish workflow가 multi-package 오작동에 강해야 한다.
- v2.4.1이 release hygiene patch였으므로 후속 작업은 기능보다 release safety를 먼저 고정하는 것이 자연스럽다.
- MTP와 Node 24는 최신성이 있고 CI failure mode가 기능 코드와 독립적이다.

주의:

- Infra PR이 publish workflow를 잘못 바꾸면 NuGet release 사고가 난다.
- Trusted Publishing 전환은 계정/정책 상태를 요구한다. 문서상 더 안전하지만 즉시 전환 가능한지는 NuGet.org UI/owner 권한 확인이 필요하다.
- MTP filtering/reporting 변경은 local/CI test invocation을 동시에 깨뜨릴 수 있다.

## 현재 상태 메모

- Workflows: `.github/workflows/release-verification.yml`, `.github/workflows/publish.yml`, `.github/workflows/native-aot.yml`
- 현재 action versions: `actions/checkout@v6`, `actions/setup-dotnet@v5`, `actions/upload-artifact@v6`
- Publish workflow는 tag ref에서만 job을 수행하고, tag target commit이 `origin/main`에 포함되는지 확인한다.
- Test project는 `IsTestingPlatformApplication`, `UseMicrosoftTestingPlatformRunner`, `xunit.v3.mtp-v2`, `Microsoft.Testing.Extensions.TrxReport`, `Microsoft.Testing.Extensions.CodeCoverage`를 사용한다.
- Secret key names observed in project/workflow context: `NUGET_API_KEY`, `LIBDB_TEST_SQL_PASSWORD`, `ConnectionStrings__*`, `LIBDB_TEST_CONNECTION_*`. Values were not inspected or printed.

## Public surface / script API 초안

Infra work should expose scripts rather than runtime API.

```powershell
Verification/scripts/Invoke-Tests.ps1
  -Filter <string>
  -Configuration <Debug|Release>
  -CollectCoverage
  -ReportTrx
  -SkipTestEnvGuard

Verification/scripts/Test-PublishGuards.ps1
  -Workflow .github/workflows/publish.yml
  -Scenario NonTag
  -Scenario TagNotOnMain
  -Scenario MissingNuGetApiKey

Verification/scripts/Test-ArtifactRedaction.ps1
  -ArtifactsPath artifacts
  -FailOnSecretLikeValue
```

Publish guard model:

```text
Allowed:
- push tag refs/tags/v*
- tag target commit is contained in origin/main
- package version matches tag SemVer after v prefix

Denied:
- branch push
- pull_request
- tag whose commit is not contained in origin/main
- missing package artifact
- missing publish credential
- secret-like content in public artifact
```

## 보안 리스크

- Accidental publish: publish workflow must be tag-only and main-contained.
- Secret exposure: artifact scanner must check logs/reports/nupkg metadata for secret-like values. It must report key names and presence, not values.
- Trusted Publishing: safer than long-lived API keys when available, but policy owner/repo/workflow/environment must match exactly.
- GitHub token scope: checkout/setup actions should run with `contents: read` unless publish step needs more.
- Shell quoting: publish scripts must avoid echoing command lines that include secrets.

## AOT/trimming 리스크

- Infra work should preserve existing Native AOT verification. If generator/provider prototypes arrive later, CI should add AOT smoke per package only when package contains runtime code.
- Analyzer/tool packages should not be included in Native AOT runtime verification as app dependencies unless intentionally consumed by a sample.

## SQL Server 운영 리스크

- CI/test scripts must not execute direct SQL DDL/DML through `sqlcmd`.
- Integration fixture setup through application/test code remains allowed.
- Test environment guards should continue to fail early when required local SQL Server configuration is missing.
- Artifact redaction must cover connection string names and values; final reports should only state key presence.

## MTP hardening

Recommended checks:

- Ensure `global.json` MTP runner is intentional and documented.
- Verify `dotnet test` command lines use MTP-compatible filter/report options.
- Keep `TestingPlatformDotnetTestSupport=false` unless a VSTest compatibility scenario is explicitly needed.
- Add documented commands for:
  - smoke test
  - integration test with environment guard
  - TRX report output
  - coverage output

## Node 24 hardening

Recommended checks:

- Document minimum self-hosted runner requirement for Node 24 actions.
- Keep official actions pinned to major versions currently used, or decide whether SHA pinning is worth the maintenance cost.
- Add scheduled/manual workflow dry run for release verification without publish.
- Ensure all third-party actions, if added later, declare Node 24 compatibility or are isolated.

## NuGet publish hardening

Recommended checks:

- Add negative tests for publish guard logic outside GitHub Actions where possible.
- Validate `.nupkg` version equals tag.
- Validate package ID allowlist before push when multi-package support is introduced.
- Keep `dotnet nuget push` command explicit with `--source`.
- Consider Trusted Publishing as a follow-up only after owner/policy/environment readiness is confirmed.

## 테스트/검증 전략

- Static workflow tests: parse YAML and assert triggers, permissions, action versions, publish conditions.
- Publish guard negative tests: simulate `GITHUB_REF`, `GITHUB_REF_TYPE`, `GITHUB_REF_NAME`, and git ancestry inputs.
- Artifact scanner tests: seeded fake artifacts with secret-like values must fail without printing the value.
- MTP command tests: local dry run for filter/report/coverage commands.
- Release dry run: pack only, no push, no tag, no release.

## 패키징/CI 영향

- No runtime package changes.
- No NuGet publish in this PR.
- Future multi-package support should be introduced only after package allowlist and artifact scanner pass.
- Trusted Publishing would require workflow permission `id-token: write` and NuGet.org policy setup; do not add until ready.

## v2.5.0 포함 여부 추천

별도 infra PR로 분리한다. v2.5.0 기능 후보와 한 PR에 섞지 않는다.

Recommended PR order:

1. `.gitignore` and tracked v2.5.0 specs PR.
2. Release infra hardening PR.
3. Core/package boundary guard PR.
4. Contract metadata schema/tooling design PR.
5. `Lib.Db.Tools` no-DB validate/report prototype PR.
6. Generator rebuild-boundary and mapper-registration spec PR.
7. Change Tracking simulated adapter design/prototype PR.

Acceptance criteria:

- No workflow in this branch publishes to NuGet during validation.
- Publish guard negative tests cover branch ref, tag-not-on-main, missing credential, package ID mismatch, and version/tag mismatch.
- Multi-package support requires package ID allowlist before wildcard push.
- Artifact scanner reports key names/presence only and never prints secret-like values.
- MTP/release commands are version-neutral and do not keep v2.4.0-only gate names.
- Release dry run is pack-only and never performs push, tag, or release.
- Acceptance includes NonTag, TagNotOnMain, MissingCredential, secret-like artifact, package ID mismatch, version/tag mismatch, and no-direct-DDL/DML/EXEC negative tests.

## 구현 전 spec/plan 목록

- `libdb-release-publish-guard-plan.md`: publish guard negative tests.
- `libdb-artifact-redaction-plan.md`: artifact scanner rules and fixtures.
- `libdb-mtp-command-plan.md`: MTP filter/report/coverage command matrix.
- `libdb-node24-actions-plan.md`: Node 24 action compatibility and runner requirements.
- `libdb-trusted-publishing-readiness-plan.md`: NuGet.org policy readiness, no immediate migration.
