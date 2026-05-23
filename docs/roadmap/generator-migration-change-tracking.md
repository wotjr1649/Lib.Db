# Lib.Db Generator, Migration, and Change Tracking Roadmap

Status: Roadmap for v2.5.0 or later
Last reviewed: 2026-05-22

## Scope Boundary

Lib.Db v2.4.0 does not implement generator, migration, or SQL Server Change Tracking adapters. These are documented as future candidates only.

## `Lib.Db.Generator`

- Optional incremental source generator.
- Additive only; it must not rewrite user code.
- No live database access during normal compilation.
- Schema inputs must be explicit checked-in additional files when schema-aware generation is added.
- Packaged as analyzer assets under `analyzers/dotnet/cs`, not as a runtime dependency.

## Migration / Contract Tooling

- SQL Server object contract validation and script scaffolding only.
- No automatic production DDL from Lib.Db core.
- No EF-style model snapshot engine in core.
- Scripts must be deterministic, reviewable, and opt-in.

## SQL Server Change Tracking Adapter

- Adapter over SQL Server Change Tracking, not custom triggers.
- Requires database/table Change Tracking to be enabled by the application/operator.
- Exposes changed keys and versions; consumers fetch and apply current row values.
- Must handle retention-window expiration and invalid stored versions.

## Security Rules

- Never print connection strings or generated secrets.
- Generated SQL must separate identifiers from values.
- DDL/DML execution must remain explicit and reviewable.
- Future tooling must not silently mutate production schema.
