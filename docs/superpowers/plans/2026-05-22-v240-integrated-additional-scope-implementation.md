# Lib.Db v2.4.0 Integrated Additional Scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the five adopted v2.4.0 additional-scope items without expanding Lib.Db core into an ORM, migration engine, or change tracker.

**Architecture:** Implement three additive runtime features in v2.4.0: HybridCache tags overload, typed QueryMultiple helper, and AOT-safe bulk mutations. Document generator, migration, and SQL Server Change Tracking as v2.5.0 candidates only. Bind all features through docs, security checks, Native AOT verification, and the official release gate.

**Tech Stack:** .NET 10, C# 14 preview syntax already used by the repo, Microsoft.Extensions.Caching.Hybrid, Microsoft.Data.SqlClient, SQL Server local verification DB, xUnit v3, FluentAssertions, existing `DbResult<T>` and fluent execution patterns.

---

## Reviewed Spec

Spec: `docs/superpowers/specs/2026-05-22-v240-integrated-additional-scope-design.md`

Sub-spec:

- `docs/superpowers/specs/2026-05-22-v240-aot-safe-bulk-mutations-design.md`

Sub-plan:

- `docs/superpowers/plans/2026-05-22-v240-aot-safe-bulk-mutations-implementation.md`

## Scope Rules

- Runtime implementation in v2.4.0: HybridCache tags overload, typed QueryMultiple helper, AOT-safe bulk mutations.
- Docs-only in v2.4.0: generator, migration tooling, SQL Server Change Tracking adapter.
- Do not add automatic DDL/DML migration execution.
- Do not add SQL Server `MERGE` as the default bulk engine.
- Do not add a core ORM or change tracker.
- Do not print secrets, raw connection strings, raw cache payloads, or row values.

## File Structure

Create:

- `Lib.Db/Extensions/MultipleResultExtensions.cs`
  Public extension helpers that convert `Task<DbResult<IMultipleResultReader>>` into typed `DbMultiple<...>` records and dispose the reader.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/MultipleResultExtensionsTests.cs`
  Unit coverage for success, failed `DbResult`, missing result set, and reader disposal.

- `docs/roadmap/generator-migration-change-tracking.md`
  Public roadmap document for v2.5.0 candidates. If `docs/roadmap` does not exist, create it.

Modify:

- `Lib.Db/Extensions/QueryCacheExtensions.cs`
  Add `WithHybridCacheAsync` tags overload and delegate existing overload to it.

- `Verification/projects/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs`
  Add tag forwarding, tag validation, and existing overload compatibility tests.

- `docs/superpowers/plans/2026-05-22-v240-aot-safe-bulk-mutations-implementation.md`
  Keep as the authoritative detailed implementation plan for bulk. Do not duplicate all bulk implementation in this integrated plan.

- `docs/02_advanced.md`
  Document HybridCache tags, typed QueryMultiple helper, AOT-safe bulk, and roadmap boundaries.

- `docs/03_api_reference.md`
  Add public API entries.

- `docs/05_fluent_api_reference.md`
  Add fluent helper usage.

- `docs/06_cookbook.md`
  Add recipes.

- `docs/history.md`
  Add v2.4.0 release history entry.

- `.agents/skills/lib-db/SKILL.md`
  Update consumer guidance if the skill currently omits the new public APIs.

---

### Task 1: HybridCache Tags Overload

**Files:**
- Modify: `Lib.Db/Extensions/QueryCacheExtensions.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs`

- [ ] **Step 1: Write failing tag-forwarding test**

Add a test that proves tags are passed to `HybridCache.GetOrCreateAsync`.

Use the existing in-memory/AOT HybridCache test style in `QueryCacheExtensionsCoverageTests`. If direct tag inspection is easier through `LibDbAotHybridCache`, use it. The behavior to prove:

```csharp
[Fact]
public async Task WithHybridCacheAsync_ShouldStoreHybridCacheEntryWithTags()
{
    var cache = new LibDbAotHybridCache();
    int queryCalls = 0;

    Task<DbResult<string?>> Query()
    {
        queryCalls++;
        return Task.FromResult(DbResult<string?>.Ok("before"));
    }

    DbResult<string?> first = await Query()
        .WithHybridCacheAsync(
            cache,
            "hybrid:tagged",
            TimeSpan.FromMinutes(5),
            tags: ["product", "tenant:hash"],
            TestContext.Current.CancellationToken);

    await cache.RemoveByTagAsync("product", TestContext.Current.CancellationToken);

    DbResult<string?> second = await Task.FromResult(DbResult<string?>.Ok("after"))
        .WithHybridCacheAsync(
            cache,
            "hybrid:tagged",
            TimeSpan.FromMinutes(5),
            tags: ["product", "tenant:hash"],
            TestContext.Current.CancellationToken);

    first.Value.Should().Be("before");
    second.Value.Should().Be("after");
    queryCalls.Should().Be(1);
}
```

Expected: without the tags overload this test does not compile. After implementation, the second call must return `"after"` because `RemoveByTagAsync("product")` invalidates the tagged cache entry. The `queryCalls` assertion documents that the current task-based API starts a caller-created task before cache lookup; do not change that behavior in this task.

- [ ] **Step 2: Write failing tag-validation tests**

Add:

```csharp
[Theory]
[InlineData("")]
[InlineData(" ")]
[InlineData("*")]
public async Task WithHybridCacheAsync_ShouldRejectInvalidTags(string tag)
{
    var cache = new LibDbAotHybridCache();

    Func<Task> act = () => Task.FromResult(DbResult<string?>.Ok("value"))
        .WithHybridCacheAsync(
            cache,
            "hybrid:invalid-tag",
            TimeSpan.FromMinutes(1),
            tags: [tag],
            TestContext.Current.CancellationToken);

    await act.Should().ThrowAsync<ArgumentException>();
}
```

- [ ] **Step 3: Run targeted tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~QueryCacheExtensionsCoverageTests"
```

Expected: build or test failure because the tags overload does not exist.

- [ ] **Step 4: Add overload and normalization helper**

In `QueryCacheExtensions.cs`, add:

```csharp
public static Task<DbResult<T?>> WithHybridCacheAsync<T>(
    this Task<DbResult<T?>> resultTask,
    HybridCache hybridCache,
    string cacheKey,
    TimeSpan duration,
    IEnumerable<string> tags,
    CancellationToken ct = default)
    => WithHybridCacheCoreAsync(resultTask, hybridCache, cacheKey, duration, NormalizeHybridCacheTags(tags), ct);
```

Change the existing overload to:

```csharp
public static Task<DbResult<T?>> WithHybridCacheAsync<T>(
    this Task<DbResult<T?>> resultTask,
    HybridCache hybridCache,
    string cacheKey,
    TimeSpan duration,
    CancellationToken ct = default)
    => WithHybridCacheCoreAsync(resultTask, hybridCache, cacheKey, duration, tags: null, ct);
```

Add private core method:

```csharp
private static async Task<DbResult<T?>> WithHybridCacheCoreAsync<T>(
    Task<DbResult<T?>> resultTask,
    HybridCache hybridCache,
    string cacheKey,
    TimeSpan duration,
    IEnumerable<string>? tags,
    CancellationToken ct)
{
    HybridCacheEntryOptions entryOptions = new()
    {
        Expiration = duration,
        LocalCacheExpiration = duration
    };

    T? cachedValue = await hybridCache.GetOrCreateAsync(
        cacheKey,
        async token =>
        {
            DbResult<T?> result = await resultTask.ConfigureAwait(false);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.Error?.Message ?? "DB query failed.");

            return result.Value;
        },
        entryOptions,
        tags,
        ct).ConfigureAwait(false);

    return DbResult<T?>.Ok(cachedValue);
}
```

Add normalization:

```csharp
private static string[] NormalizeHybridCacheTags(IEnumerable<string>? tags)
{
    if (tags is null)
        return [];

    string[] normalized = tags
        .Select(static tag => tag.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    foreach (string tag in normalized)
    {
        if (tag.Length == 0)
            throw new ArgumentException("HybridCache tags cannot be empty or whitespace.", nameof(tags));

        if (tag == "*")
            throw new ArgumentException("HybridCache tag '*' is reserved for wildcard invalidation and cannot be assigned to entries.", nameof(tags));
    }

    return normalized;
}
```

Use `tags: normalized.Length == 0 ? null : normalized` if the compiler or analyzer prefers null for no tags.

- [ ] **Step 5: Run targeted tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~QueryCacheExtensionsCoverageTests"
```

Expected: PASS.

### Task 2: Typed QueryMultiple Helper

**Files:**
- Create: `Lib.Db/Extensions/MultipleResultExtensions.cs`
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/MultipleResultExtensionsTests.cs`

- [ ] **Step 1: Write failing success test**

Create tests using a small fake `IMultipleResultReader`:

```csharp
[Fact]
public async Task ReadMultipleAsync_Arity2_ShouldReadAndDisposeReader()
{
    var reader = new FakeMultipleResultReader(
        new object[] { new List<UserRow> { new(1) }, new List<OrderRow> { new(7) } });

    DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    result.Value.First.Should().ContainSingle(row => row.Id == 1);
    result.Value.Second.Should().ContainSingle(row => row.Id == 7);
    reader.DisposeCount.Should().Be(1);
}
```

The fake reader should implement `ReadAsync<T>`, `ReadSingleAsync<T>`, and `DisposeAsync`.

- [ ] **Step 2: Write failing failure propagation test**

```csharp
[Fact]
public async Task ReadMultipleAsync_ShouldPropagateFailedDbResult()
{
    DbError error = new()
    {
        Kind = DbErrorKind.Unknown,
        SqlErrorCode = 0,
        Severity = 0,
        IsTransient = false,
        Message = "QueryMultiple failed"
    };

    DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Fail(error))
        .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    result.Error.Should().Be(error);
}
```

- [ ] **Step 3: Run tests and confirm RED**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MultipleResultExtensionsTests"
```

Expected: build failure because `MultipleResultExtensions` and `DbMultiple<...>` do not exist.

- [ ] **Step 4: Implement arity 2, 3, and 4 helpers**

Create `Lib.Db/Extensions/MultipleResultExtensions.cs`:

```csharp
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Execution;

namespace Lib.Db.Extensions;

public readonly record struct DbMultiple<T1, T2>(List<T1> First, List<T2> Second);

public readonly record struct DbMultiple<T1, T2, T3>(List<T1> First, List<T2> Second, List<T3> Third);

public readonly record struct DbMultiple<T1, T2, T3, T4>(
    List<T1> First,
    List<T2> Second,
    List<T3> Third,
    List<T4> Fourth);

public static class MultipleResultExtensions
{
    public static async Task<DbResult<DbMultiple<T1, T2>>> ReadMultipleAsync<T1, T2>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
        if (!result.IsSuccess)
            return DbResult<DbMultiple<T1, T2>>.Fail(result.Error!.Value);

        await using IMultipleResultReader reader = result.Value!;
        List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
        List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);
        return DbResult<DbMultiple<T1, T2>>.Ok(new DbMultiple<T1, T2>(first, second));
    }

    public static async Task<DbResult<DbMultiple<T1, T2, T3>>> ReadMultipleAsync<T1, T2, T3>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
        if (!result.IsSuccess)
            return DbResult<DbMultiple<T1, T2, T3>>.Fail(result.Error!.Value);

        await using IMultipleResultReader reader = result.Value!;
        List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
        List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);
        List<T3> third = await reader.ReadAsync<T3>(ct).ConfigureAwait(false);
        return DbResult<DbMultiple<T1, T2, T3>>.Ok(new DbMultiple<T1, T2, T3>(first, second, third));
    }

    public static async Task<DbResult<DbMultiple<T1, T2, T3, T4>>> ReadMultipleAsync<T1, T2, T3, T4>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
        if (!result.IsSuccess)
            return DbResult<DbMultiple<T1, T2, T3, T4>>.Fail(result.Error!.Value);

        await using IMultipleResultReader reader = result.Value!;
        List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
        List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);
        List<T3> third = await reader.ReadAsync<T3>(ct).ConfigureAwait(false);
        List<T4> fourth = await reader.ReadAsync<T4>(ct).ConfigureAwait(false);
        return DbResult<DbMultiple<T1, T2, T3, T4>>.Ok(new DbMultiple<T1, T2, T3, T4>(first, second, third, fourth));
    }
}
```

Use the exact local `DbResult<T>.Fail` and error property style.

- [ ] **Step 5: Run targeted tests and confirm GREEN**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~MultipleResultExtensionsTests"
```

Expected: PASS.

### Task 3: AOT-Safe Bulk Mutation Suite

**Files:**
- Follow: `docs/superpowers/plans/2026-05-22-v240-aot-safe-bulk-mutations-implementation.md`

- [ ] **Step 1: Execute the existing AOT-safe bulk sub-plan**

Run the sub-plan task-by-task. Keep commits scoped to the task groups in that plan.

- [ ] **Step 2: Preserve integrated scope decisions**

While executing the sub-plan, verify:

- `BulkInsertAsync`, `BulkUpdateAsync`, `BulkDeleteAsync`, `BulkUpsertAsync`, and `BulkMergeAsync` shape APIs exist.
- SQL Server `MERGE` is not used as the default implementation.
- `DeleteNotMatchedBySource` is rejected.
- New shape APIs have no `RequiresUnreferencedCode`.
- AOT verification references the shape reader path.

- [ ] **Step 3: Run bulk targeted gates**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~BulkShapeTests|FullyQualifiedName~BulkShapeDataReaderTests|FullyQualifiedName~BulkSqlBuilderTests|FullyQualifiedName~BulkMutationTests"
```

Expected: PASS.

### Task 4: v2.5.0 Roadmap Document

**Files:**
- Create: `docs/roadmap/generator-migration-change-tracking.md`
- Modify: `docs/history.md`

- [ ] **Step 1: Create roadmap document**

Create `docs/roadmap/generator-migration-change-tracking.md`:

```markdown
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
```

- [ ] **Step 2: Add history note**

In `docs/history.md`, add:

```markdown
- Documented generator, migration/contract tooling, and SQL Server Change Tracking adapter as v2.5.0-or-later roadmap items. v2.4.0 keeps these out of the core runtime implementation.
```

### Task 5: Public Documentation and Skill Update

**Files:**
- Modify: `docs/02_advanced.md`
- Modify: `docs/03_api_reference.md`
- Modify: `docs/05_fluent_api_reference.md`
- Modify: `docs/06_cookbook.md`
- Modify: `docs/history.md`
- Modify: `.agents/skills/lib-db/SKILL.md`

- [ ] **Step 1: Update HybridCache docs**

Document:

- tags overload,
- invalid tag rejection,
- logical invalidation,
- local-only behavior without provider-backed L2,
- app-owned cache key and tag dimensions.

- [ ] **Step 2: Update QueryMultiple docs**

Document:

```csharp
DbResult<DbMultiple<UserRow, OrderRow>> result = await session
    .Use("Default")
    .Procedure("dbo.usp_UserDashboard")
    .QueryMultipleAsync(ct)
    .ReadMultipleAsync<UserRow, OrderRow>(ct);
```

Explain that result sets are consumed in stored-procedure order and that the helper disposes the reader.

- [ ] **Step 3: Update bulk docs**

Link to the bulk mutation docs and document:

- `BulkShape<T>`,
- insert/update/delete/upsert/merge,
- staged DML,
- AOT compatibility,
- transaction behavior,
- `DeleteNotMatchedBySource` exclusion.

- [ ] **Step 4: Update skill guidance**

Update `.agents/skills/lib-db/SKILL.md` with compact consumer guidance for the new APIs:

- use tags overload for grouped HybridCache invalidation,
- use `ReadMultipleAsync<...>` for common multi-result SPs,
- use `BulkShape<T>` for AOT-safe bulk writes,
- do not use roadmap features as if implemented.

### Task 6: Security and Release Verification

**Files:**
- No planned source changes unless verification finds an issue.

- [ ] **Step 1: Static search for known risk markers**

Run:

```powershell
rg -n "ConnectionString|Password|Pwd|User Id|MERGE|DeleteNotMatchedBySource|GetProperties|RequiresUnreferencedCode|RemoveByTagAsync|WithHybridCacheAsync|ReadMultipleAsync|BulkShape" Lib.Db Verification docs .agents
```

Expected:

- no secrets printed,
- `MERGE` not used as default bulk engine,
- `DeleteNotMatchedBySource` appears only as rejected/unsupported,
- new AOT-safe bulk APIs do not carry `RequiresUnreferencedCode`,
- cache tag behavior appears in tests/docs.

- [ ] **Step 2: Run targeted tests**

Run:

```powershell
dotnet test Verification/projects/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj --filter "FullyQualifiedName~QueryCacheExtensionsCoverageTests|FullyQualifiedName~MultipleResultExtensionsTests|FullyQualifiedName~BulkShapeTests|FullyQualifiedName~BulkShapeDataReaderTests|FullyQualifiedName~BulkSqlBuilderTests|FullyQualifiedName~BulkMutationTests"
```

Expected: PASS.

- [ ] **Step 3: Run Native AOT verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: no new Lib.Db trim/AOT warnings.

- [ ] **Step 4: Run official release verification**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short
```

Expected: release-grade verification completes successfully.

- [ ] **Step 5: Final security review checklist**

Confirm:

- cache keys and tags are documented as non-sensitive and app-owned,
- cache tag wildcard `*` cannot be assigned to entries,
- typed multi-result helper disposes readers,
- bulk writes validate identifiers and run staged mutation in a transaction,
- generated/future roadmap docs do not imply automatic production DDL/DML,
- docs do not overpromise L2 invalidation across all servers.

## Completion Criteria

The integrated additional scope is complete when:

- HybridCache tags overload is implemented and tested.
- Typed QueryMultiple helper is implemented and tested.
- AOT-safe bulk mutation sub-plan is implemented and tested.
- Roadmap document exists for generator/migration/change-tracking.
- Public docs and Lib.Db skill guidance are updated.
- Native AOT verification passes.
- Official release verification passes.
- Final security review has no blocking findings.
