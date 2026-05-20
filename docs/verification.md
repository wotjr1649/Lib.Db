# Lib.Db Verification Policy

Updated: 2026-05-21

This is an internal maintainer policy, not consumer API documentation. Consumer applications do not need this workflow to install or use `Lib.Db`.

## Public Contract

- Consumer applications do not need the internal verification scripts to install or use `Lib.Db`.
- Release validation uses disposable local SQL Server databases, not cloud, staging, production, or customer databases.
- Automation must not print connection string values, tokens, SQL passwords, or secret values. It may report key names and whether expected keys are present.
- Direct SQL DDL/DML/EXEC verification remains an explicit opt-in maintainer activity scoped to disposable verification databases.
- Server-level chaos validation is excluded from the default release gate and requires explicit opt-in, separate setup, separate harness execution, and mandatory teardown.

## Verification Areas

Release-grade maintainer validation covers:

1. Build and packaging readiness.
2. Local SQL Server integration tests across the verification database matrix.
3. Runtime TVP shape, scalar plus TVP mixed-parameter execution, and registered fast-path behavior.
4. Coverage gates for agreed high-risk areas, with overall `Lib.Db` line coverage held above the release threshold.
5. Native AOT publish/run validation and review of remaining provider-owned warnings.
6. BenchmarkDotNet comparison between the generated-accessor baseline, runtime object streaming, and registered runtime fast-path.
7. Secret-pattern scanning of generated verification artifacts before preserving or sharing reports.
8. Generated artifact tracking gates so benchmark, test, coverage, and AOT outputs are not committed as source.

## Official References

- Microsoft Learn documents Coverlet-style `dotnet test` coverage collection and Cobertura reporting for .NET test projects: <https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-code-coverage>
- Coverlet's official repository documents the `coverlet.collector` VSTest integration: <https://github.com/coverlet-coverage/coverlet>
- BenchmarkDotNet documents exporter output and artifact path configuration: <https://benchmarkdotnet.org/articles/configs/exporters.html>
- SQL Server `CREATE TYPE` documentation covers table types and memory-optimized table type requirements: <https://learn.microsoft.com/en-us/sql/t-sql/statements/create-type-transact-sql>
- Microsoft Learn documents memory-optimized table variables, TVP usage, and filegroup requirements: <https://learn.microsoft.com/en-us/sql/relational-databases/in-memory-oltp/faster-temp-table-and-table-variable-by-using-memory-optimization>
- Microsoft Learn documents Native AOT warning handling and the need to validate remaining warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/fixing-warnings>
- Microsoft Learn documents `IL3053` as an aggregate third-party AOT analysis warning: <https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3053>

## AOT Warning Policy

Lib.Db-owned Native AOT and trimming warnings are release blockers. Provider-owned aggregate warnings can remain only when the release gate publishes successfully, the produced executable runs successfully, and the warning set is reviewed after provider package changes.

If a new Lib.Db-owned warning appears, or the provider warning set changes materially, update the risk ledger and resolve the release decision before publishing.

## AOT warning baseline

Native AOT publish must have zero Lib.Db-owned IL warnings. The AOT gate keeps `TrimmerSingleWarn=false`, captures detailed publish output, and compares parsed warning id plus assembly against `Verification/baselines/aot-warnings.json`.

Provider-owned warnings are accepted only when the id, assembly, source package, and package version match the baseline. Stale baseline entries also fail the gate, so a provider warning disappearing is an intentional review event rather than silent drift. When provider packages are upgraded, rerun AOT and update the baseline only after reviewing owner and impact.

## Artifact Policy

Generated verification artifacts are internal maintainer evidence. They are not source, they are not part of the package, and they must not contain secret values. Benchmark, test, coverage, and AOT artifacts must be scanned for secret-pattern paths before they are retained or shared. Generated artifact directories must remain ignored/untracked.

## Related Documents

- [AOT/TVP Risk Ledger](./security/aot-tvp-risk-ledger.md)
- [History](./history.md)
