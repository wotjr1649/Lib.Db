# v2.3.0 Coverage and AOT Verification Design

## Goal

Bring the remaining v2.3.0 coverage gaps to the agreed target without weakening the library design: target areas should reach line, branch, and method 100%, while overall `Lib.Db` line coverage remains at or above 80%.

Native AOT/JIT fixed branches must not be hidden with broad coverage exclusions. They must be verified through a dedicated Native AOT verification project that publishes and runs a real AOT executable.

## Scope

This work is limited to branch `v2.3.0`.

The target areas are:

- `Lib.Db.Caching.CacheMaintenanceService`
- `Lib.Db.Hosting.SchemaWarmupService`
- `Lib.Db.Extensions.QueryCacheExtensions`
- mapper branches in `Lib.Db.Execution.Binding.Mappers`
- the already-covered TVP runtime core must remain at 100% line, branch, and method coverage

Out of scope:

- unrelated API redesign
- unrelated cache, schema, or mapper refactoring
- direct SQL DDL/DML through command-line SQL tools
- hiding reachable production code behind broad `[ExcludeFromCodeCoverage]`

## Current Findings

The latest verified Coverlet run was `TestResults/CoverletFull13`.

It passed:

```text
Passed: 487
Failed: 0
Skipped: 0
Lib.Db line coverage: 80.8%
Lib.Db branch coverage: 70.9%
Lib.Db method coverage: 87.0%
```

Remaining target gaps:

- `CacheMaintenanceService`: hosted timer loop has a branch that is difficult to make deterministic with a real `PeriodicTimer`.
- `SchemaWarmupService`: constructor guard, skip paths, cancellation logging, missing schema warning, exception logging, and diagnostic redaction fallback need deterministic coverage.
- `QueryCacheExtensions`: hybrid cache failure fallback message branch is partially covered.
- `GeneratedResultMapper<T>` and `ReflectionParameterMapper<T>`: generated mapper wrapper and runtime feature selection branches need targeted tests.

## Design

### Coverage Strategy

Use ordinary `dotnet test --collect:"XPlat Code Coverage"` for normal line, branch, and method coverage.

For code paths that are reachable in a JIT test process, prefer behavioral tests over coverage exclusions.

For paths whose behavior depends on runtime mode, introduce narrow internal seams that make the decision testable in ordinary unit tests while preserving the public API. The seam must represent a runtime capability decision only; it must not become a general configuration system.

### Native AOT Verification

Add a dedicated console project:

```text
Tests/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj
```

The project will:

- reference `Lib.Db`
- publish with `PublishAot=true`
- run as a real native executable
- verify `RuntimeFeature.IsDynamicCodeSupported == false`
- execute AOT-safe Lib.Db paths, including static TVP shape binding and mapper/parameter binding paths that avoid runtime code generation
- return exit code `0` on success and non-zero on failure

This AOT verification result is a separate gate from Coverlet coverage because Native AOT publish output is a native executable and is not expected to contribute directly to the Coverlet Cobertura XML from `dotnet test`.

### CacheMaintenanceService

Keep the public hosted service behavior unchanged.

Introduce a small internal tick-loop seam so tests can deterministically cover:

- timer tick returns `true`
- timer tick returns `false`
- cancellation
- maintenance cycle exception handling

The production path continues to use `PeriodicTimer`.

### SchemaWarmupService

Keep the public service behavior unchanged.

Add or reuse focused internal helpers only where necessary to make branch coverage deterministic:

- constructor null guard tests
- empty `ConnectionStringNames`
- empty `PrewarmSchemas`
- all blank schema names
- successful preload with missing schemas
- per-target exception path
- cancellation path
- top-level unexpected exception path
- diagnostic instance redaction fallback

The tests must not require a real database.

### QueryCacheExtensions

Add focused tests for the remaining hybrid cache branch:

- result failure with explicit error message
- result failure with missing error message, expecting fallback message
- result failure with missing error object if supported by the `DbResult` contract

No production behavior change is expected unless the current API cannot express the missing-error case safely.

### Mapper Branches

Cover reachable branches with focused tests:

- input parameter with default value
- strict required missing input parameter
- nullable input parameter
- output and input-output parameter mapping
- generated mapper receiving `SqlDataReader`
- generated mapper receiving non-`SqlDataReader` wrapper and throwing the intended exception

For runtime capability selection, use a narrow internal runtime feature abstraction. Unit tests verify both dynamic-code-supported and dynamic-code-not-supported selection. The Native AOT verification executable validates the actual false runtime mode.

## Verification Gates

Primary coverage gate:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --collect:"XPlat Code Coverage"
```

Coverage report gate:

```powershell
reportgenerator -reports:<coverage.cobertura.xml> -targetdir:TestResults\CoverageReport-<run> -reporttypes:"TextSummary;Cobertura" -assemblyfilters:"+Lib.Db;-Lib.Db.IntegrationTests"
```

Native AOT gate:

```powershell
dotnet publish Tests\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj -c Release -r win-x64 -p:PublishAot=true
```

Then run the produced executable and require exit code `0`.

## Security and Safety

Connection strings, passwords, and tokens must not be printed.

The AOT verification project must not require direct SQL DDL/DML through command-line SQL tools. If database-backed verification is later added, it must use application/test code paths already allowed by project rules.

Coverage seams must be internal and deterministic. They must not expose security-sensitive controls to library consumers.

## Acceptance Criteria

- Full integration test suite passes.
- Coverlet collector still generates Cobertura output.
- Overall `Lib.Db` line coverage is at least 80%.
- TVP runtime core remains line, branch, and method 100%.
- Remaining named target areas reach line, branch, and method 100%, except for branches explicitly proven to require Native AOT runtime behavior.
- A dedicated AOT verification project publishes with `PublishAot=true`.
- The published AOT executable runs and exits with code `0`.
- No secret values are printed in test logs or final output.
