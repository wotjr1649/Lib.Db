# Lib.Db.TvpGen Guide

Use this file when work touches `Lib.Db.TvpGen`, `[TvpRow]`, `[DbResult]`, generated source text, or generator tests.

## Generator Responsibilities

`Lib.Db.TvpGen` owns compile-time generation for:

- TVP row binding from `[TvpRow]`
- result row mapping from `[DbResult]`
- SQL Server type mapping for supported CLR types
- generated source diagnostics and compatibility shims

The runtime library owns execution, connection policy, diagnostics, and fallback reflection mapping.

## TVP Rules

- Keep generated TVP binding deterministic.
- Preserve column order expected by SQL Server TVP types.
- Keep nullable handling explicit.
- Support modern CLR types already covered by the runtime, including `DateOnly` and `TimeOnly`.
- Do not introduce runtime reflection into generated hot paths unless there is no compile-time alternative.

## DbResult Rules

Generated `[DbResult]` mappers must emit `Map(DbDataReader)` as the primary overload and `Map(SqlDataReader)` as a shim.

Generated code should prefer reader APIs selected by the type mapping registry. If provider behavior differs between mock readers and SQL Server readers, add both generated-source assertions and runtime tests.

## Compatibility

Do not remove existing generated public members without a deliberate breaking-change note.

If runtime behavior changes, update:

- generator source
- generator unit tests that assert generated source
- runtime mapper tests
- README/API docs that describe generated contracts

## Review Checklist

- Generated source compiles without warnings.
- Generated source does not require a concrete `SqlDataReader` when a `DbDataReader` wrapper is passed.
- Generated code has no hidden dependency on full connection strings or runtime secrets.
- Type mappings for `date` and `time` remain aligned with runtime binding.
