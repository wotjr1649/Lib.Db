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

### Task 0: Pre-Implementation Baseline Evidence

**Files:**
- No planned source changes.

Run this task before implementing the runtime changes. If a command fails here,
stop and classify it as a pre-existing baseline issue before touching source.
Do not hide baseline failures inside feature implementation work.

This is the single pre-source baseline gate for the integrated plan and it
satisfies the bulk sub-plan baseline requirement when Task 3 starts in the same
session with no intervening source changes. If bulk work starts standalone, or
if any source file changed after this task and before Task 3, rerun the bulk
sub-plan's baseline gate and record the newer evidence.

- [ ] **Step 1: Capture current Native AOT baseline**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Aot.ps1
```

Expected: the current baseline has no new Lib.Db trim/AOT warnings. Record the
warning count summary and whether only the known provider warnings are present in
the implementation notes or PR body.

- [ ] **Step 2: Capture current release-verification baseline with durable log**

Run:

```powershell
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = "Verification/artifacts/logs/v240-preimplementation-release-verification-$stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$verificationOutput = & pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short *>&1
$exitCode = $LASTEXITCODE
$verificationOutput | Tee-Object -FilePath $log
$postLogExitCode = 0
if (-not (Test-Path -LiteralPath $log) -or (Get-Item -LiteralPath $log).Length -eq 0) {
    Write-Warning "Release verification log was not created or is empty: $log"
    $postLogExitCode = 1
}
pwsh -NoProfile -File Verification/scripts/Scan-VerificationArtifacts.ps1 -Paths $log
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
pwsh -NoProfile -File Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
if ($exitCode -ne 0) { exit $exitCode }
if ($postLogExitCode -ne 0) { exit $postLogExitCode }
```

Expected: release verification passes, a non-empty log is created under
`Verification/artifacts/logs/`, the log-specific artifact scan passes, and the
generated-artifact tracking gate still passes. Record the log path without
copying secret values into notes.

### Task 1: HybridCache Tags Overload

**Files:**
- Modify: `Lib.Db/Extensions/QueryCacheExtensions.cs`
- Modify: `Verification/projects/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs`

- [ ] **Step 1: Write failing tag-forwarding test**

Add a test that proves tags are passed to `HybridCache.GetOrCreateAsync`.

Use the existing in-memory/AOT HybridCache test style in `QueryCacheExtensionsCoverageTests`. Add `using Lib.Db.Caching;` if the test file does not already import it. If direct tag inspection is easier through `LibDbAotHybridCache`, use it. The behavior to prove:

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
[InlineData(null)]
[InlineData("")]
[InlineData(" ")]
[InlineData(" tag")]
[InlineData("tag ")]
[InlineData("*")]
public async Task WithHybridCacheAsync_ShouldRejectInvalidTags(string? tag)
{
    var cache = new LibDbAotHybridCache();
    string[] tags = tag is null ? [null!] : [tag];

    Func<Task> act = () => Task.FromResult(DbResult<string?>.Ok("value"))
        .WithHybridCacheAsync(
            cache,
            "hybrid:invalid-tag",
            TimeSpan.FromMinutes(1),
            tags,
            TestContext.Current.CancellationToken);

    await act.Should().ThrowAsync<ArgumentException>();
}

[Fact]
public async Task WithHybridCacheAsync_ShouldTreatNullTagsAsNoTags()
{
    var cache = new LibDbAotHybridCache();

    DbResult<string?> result = await Task.FromResult(DbResult<string?>.Ok("value"))
        .WithHybridCacheAsync(
            cache,
            "hybrid:null-tags",
            TimeSpan.FromMinutes(1),
            tags: null,
            TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue();
    result.Value.Should().Be("value");
}

[Fact]
public async Task WithHybridCacheAsync_ShouldRejectTooManyDistinctTags()
{
    var cache = new LibDbAotHybridCache();
    string[] tags = Enumerable.Range(0, 33)
        .Select(static index => $"tag:{index}")
        .ToArray();

    Func<Task> act = () => Task.FromResult(DbResult<string?>.Ok("value"))
        .WithHybridCacheAsync(
            cache,
            "hybrid:too-many-tags",
            TimeSpan.FromMinutes(1),
            tags,
            TestContext.Current.CancellationToken);

    await act.Should().ThrowAsync<ArgumentException>()
        .WithMessage("*32*tags*");
}

[Fact]
public async Task WithHybridCacheAsync_ShouldCountDistinctTagsWhenEnforcingLimit()
{
    var cache = new LibDbAotHybridCache();
    string[] tags = Enumerable.Range(0, 32)
        .Select(static index => $"tag:{index}")
        .Concat(["tag:0", "tag:0"])
        .ToArray();

    DbResult<string?> result = await Task.FromResult(DbResult<string?>.Ok("value"))
        .WithHybridCacheAsync(
            cache,
            "hybrid:duplicate-tags",
            TimeSpan.FromMinutes(1),
            tags,
            TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
}
```

Update the existing failure-path test instead of leaving the old raw-message expectation in place:

```csharp
[Fact]
public async Task WithHybridCacheAsync_ShouldThrowGenericMessageWhenResultFails()
{
    var cache = new LibDbAotHybridCache();

    Func<Task> act = () => Task
        .FromResult(DbResult<CachedUser?>.Fail(CreateError("hybrid failed: SELECT * FROM dbo.SecretTenant")))
        .WithHybridCacheAsync(
            cache,
            "hybrid:failure",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

    InvalidOperationException exception = (await act.Should()
        .ThrowAsync<InvalidOperationException>()).Which;

    exception.Message.Should().Be("DB query failed.");
    exception.Message.Should().NotContain("hybrid failed");
    exception.Message.Should().NotContain("SecretTenant");
    exception.Message.Should().NotContain("SELECT");
}
```

Add a second failure-path test for a faulted query task. This closes the case
where the task faults before producing a failed `DbResult<T>`:

```csharp
[Fact]
public async Task WithHybridCacheAsync_ShouldThrowGenericMessageWhenResultTaskFaults()
{
    var cache = new LibDbAotHybridCache();
    InvalidOperationException rawFailure = new("raw provider failure: SELECT * FROM dbo.SecretTenant");

    Func<Task> act = () => Task
        .FromException<DbResult<CachedUser?>>(rawFailure)
        .WithHybridCacheAsync(
            cache,
            "hybrid:faulted",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

    InvalidOperationException exception = (await act.Should()
        .ThrowAsync<InvalidOperationException>()).Which;

    exception.Message.Should().Be("DB query failed.");
    exception.InnerException.Should().BeNull();
    exception.ToString().Should().NotContain("raw provider failure");
    exception.ToString().Should().NotContain("SecretTenant");
    exception.ToString().Should().NotContain("SELECT");
}
```

The current `WithHybridCacheAsync_ShouldThrowWhenResultFails` test in
`QueryCacheExtensionsCoverageTests.cs` must be renamed or rewritten to this
contract. Do not keep a duplicate test that still asserts the raw
`DbError.Message` value.

- [ ] **Step 3: Run targeted tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*QueryCacheExtensionsCoverageTests*"
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
    IEnumerable<string>? tags,
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
            DbResult<T?> result;
            try
            {
                result = await resultTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw CreateHybridCacheFactoryFailure();
            }

            if (!result.IsSuccess)
                throw CreateHybridCacheFactoryFailure();

            return result.Value;
        },
        entryOptions,
        tags,
        ct).ConfigureAwait(false);

    return DbResult<T?>.Ok(cachedValue);
}
```

Add:

```csharp
private static InvalidOperationException CreateHybridCacheFactoryFailure()
    => new("DB query failed.");
```

Do not copy `DbError.Message`, SQL text, object names, provider details, row values, cache payloads, connection-string fragments, raw faulted-task exception messages, inner exceptions, or tenant/user identifiers into cache factory exception messages. The cache wrapper's public failure path must use a generic message and rely on the existing redacted diagnostics path for details. The generic exception must not retain the raw provider exception as `InnerException`, because `Exception.ToString()` would expose it.

Add normalization:

```csharp
private const int MaxHybridCacheTagsPerEntry = 32;

private static string[] NormalizeHybridCacheTags(IEnumerable<string>? tags)
{
    if (tags is null)
        return [];

    HashSet<string> seen = new(StringComparer.Ordinal);
    List<string> normalized = [];
    foreach (string? rawTag in tags)
    {
        if (rawTag is null)
            throw new ArgumentException("HybridCache tags cannot contain null values.", nameof(tags));

        string trimmed = rawTag.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("HybridCache tags cannot be empty or whitespace.", nameof(tags));

        if (!string.Equals(rawTag, trimmed, StringComparison.Ordinal))
            throw new ArgumentException("HybridCache tags cannot have leading or trailing whitespace.", nameof(tags));

        if (rawTag == "*")
            throw new ArgumentException("HybridCache tag '*' is reserved for wildcard invalidation and cannot be assigned to entries.", nameof(tags));

        if (seen.Add(rawTag))
        {
            normalized.Add(rawTag);
            if (normalized.Count > MaxHybridCacheTagsPerEntry)
                throw new ArgumentException($"HybridCache entries cannot have more than {MaxHybridCacheTagsPerEntry} distinct tags.", nameof(tags));
        }
    }

    return [.. normalized];
}
```

Use `tags: normalized.Length == 0 ? null : normalized` if the compiler or analyzer prefers null for no tags.
Do not silently truncate tags. A silently dropped tag creates an invalidation expectation that the cache entry will not actually honor.

- [ ] **Step 5: Run targeted tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*QueryCacheExtensionsCoverageTests*"
```

Expected: PASS.

### Task 2: Typed QueryMultiple Helper

**Files:**
- Create: `Lib.Db/Extensions/MultipleResultExtensions.cs`
- Create: `Verification/projects/Lib.Db.IntegrationTests/Unit/MultipleResultExtensionsTests.cs`

- [ ] **Step 1: Write failing success test**

Create tests using a small fake `IMultipleResultReader`:

The test file must include the extension namespace explicitly because `ReadMultipleAsync` is defined outside the test namespace:

```csharp
using FluentAssertions;
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Execution;
using Lib.Db.Extensions;
using Xunit;

namespace Lib.Db.IntegrationTests.Unit;

public sealed class MultipleResultExtensionsTests
{
    // Place the tests below in this class with local row records and fake reader helpers.
}
```

```csharp
[Fact]
public async Task ReadMultipleAsync_Arity2_ShouldReadAndDisposeReader()
{
    var reader = new FakeMultipleResultReader(
        new object[] { new List<UserRow> { new(1) }, new List<OrderRow> { new(7) } });

    DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.First.Should().ContainSingle(row => row.Id == 1);
    result.Value.Second.Should().ContainSingle(row => row.Id == 7);
    reader.DisposeCount.Should().Be(1);
}

[Fact]
public async Task ReadMultipleAsync_Arity3_ShouldReadInOrderAndDisposeReader()
{
    var reader = new FakeMultipleResultReader(
        new object[]
        {
            new List<UserRow> { new(1) },
            new List<OrderRow> { new(7) },
            new List<SummaryRow> { new(2) }
        });

    DbResult<DbMultiple<UserRow, OrderRow, SummaryRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow, SummaryRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.First.Should().ContainSingle(row => row.Id == 1);
    result.Value.Second.Should().ContainSingle(row => row.Id == 7);
    result.Value.Third.Should().ContainSingle(row => row.Count == 2);
    reader.DisposeCount.Should().Be(1);
}

[Fact]
public async Task ReadMultipleAsync_Arity4_ShouldReadInOrderAndDisposeReader()
{
    var reader = new FakeMultipleResultReader(
        new object[]
        {
            new List<UserRow> { new(1) },
            new List<OrderRow> { new(7) },
            new List<SummaryRow> { new(2) },
            new List<AuditRow> { new(9) }
        });

    DbResult<DbMultiple<UserRow, OrderRow, SummaryRow, AuditRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow, SummaryRow, AuditRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeTrue(result.Error?.Message);
    result.Value.First.Should().ContainSingle(row => row.Id == 1);
    result.Value.Second.Should().ContainSingle(row => row.Id == 7);
    result.Value.Third.Should().ContainSingle(row => row.Count == 2);
    result.Value.Fourth.Should().ContainSingle(row => row.Id == 9);
    reader.DisposeCount.Should().Be(1);
}
```

The fake reader should implement `ReadAsync<T>`, `ReadSingleAsync<T>`, and `DisposeAsync`, plus explicit failure helpers such as `ThrowOnSecondRead(...)` and `ThrowOnFourthRead(...)`. Add small local records such as `UserRow(int Id)`, `OrderRow(int Id)`, `SummaryRow(int Count)`, and `AuditRow(int Id)` so the samples compile. The arity 3/4 tests are required because the public API exposes those overloads; a passing arity 2 test alone is not enough to prove ordering, disposal, missing-result handling, and redacted failure mapping for every shipped helper.

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

[Fact]
public async Task ReadMultipleAsync_ShouldReturnFailureWhenExpectedResultSetIsMissing()
{
    var reader = new FakeMultipleResultReader(
        new object[] { new List<UserRow> { new(1) } });

    DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
    reader.DisposeCount.Should().Be(1);
}

[Fact]
public async Task ReadMultipleAsync_ShouldDisposeReaderWhenReadFails()
{
    var reader = FakeMultipleResultReader.ThrowOnSecondRead(new InvalidOperationException("mapper failed"));

    DbResult<DbMultiple<UserRow, OrderRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
    result.Error.Value.Message.Should().NotContain("mapper failed");
    reader.DisposeCount.Should().Be(1);
}

[Fact]
public async Task ReadMultipleAsync_Arity3_ShouldReturnFailureWhenExpectedResultSetIsMissing()
{
    var reader = new FakeMultipleResultReader(
        new object[]
        {
            new List<UserRow> { new(1) },
            new List<OrderRow> { new(7) }
        });

    DbResult<DbMultiple<UserRow, OrderRow, SummaryRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow, SummaryRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
    result.Error.Value.Message.Should().NotContain("Missing");
    result.Error.Value.Message.Should().NotContain("missing");
    reader.DisposeCount.Should().Be(1);
}

[Fact]
public async Task ReadMultipleAsync_Arity4_ShouldDisposeReaderWhenReadFails()
{
    var reader = FakeMultipleResultReader.ThrowOnFourthRead(new InvalidOperationException("mapper failed"));

    DbResult<DbMultiple<UserRow, OrderRow, SummaryRow, AuditRow>> result = await Task
        .FromResult(DbResult<IMultipleResultReader>.Ok(reader))
        .ReadMultipleAsync<UserRow, OrderRow, SummaryRow, AuditRow>(TestContext.Current.CancellationToken);

    result.IsSuccess.Should().BeFalse();
    result.Error!.Value.Message.Should().Be("Reading multiple result sets failed.");
    result.Error.Value.Message.Should().NotContain("mapper failed");
    reader.DisposeCount.Should().Be(1);
}
```

- [ ] **Step 3: Run tests and confirm RED**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*MultipleResultExtensionsTests*"
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
        try
        {
            DbResult<IMultipleResultReader> result = await readerTask.ConfigureAwait(false);
            if (!result.IsSuccess)
                return DbResult<DbMultiple<T1, T2>>.Fail(result.Error!.Value);

            await using IMultipleResultReader reader = result.Value!;
            List<T1> first = await reader.ReadAsync<T1>(ct).ConfigureAwait(false);
            List<T2> second = await reader.ReadAsync<T2>(ct).ConfigureAwait(false);
            return DbResult<DbMultiple<T1, T2>>.Ok(new DbMultiple<T1, T2>(first, second));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToReadFailure<DbMultiple<T1, T2>>(ex);
        }
    }

    public static async Task<DbResult<DbMultiple<T1, T2, T3>>> ReadMultipleAsync<T1, T2, T3>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        try
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToReadFailure<DbMultiple<T1, T2, T3>>(ex);
        }
    }

    public static async Task<DbResult<DbMultiple<T1, T2, T3, T4>>> ReadMultipleAsync<T1, T2, T3, T4>(
        this Task<DbResult<IMultipleResultReader>> readerTask,
        CancellationToken ct = default)
    {
        try
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToReadFailure<DbMultiple<T1, T2, T3, T4>>(ex);
        }
    }

    private static DbResult<T> ToReadFailure<T>(Exception ex)
        => DbResult<T>.Fail(new DbError
        {
            Kind = DbErrorKind.Unknown,
            Message = "Reading multiple result sets failed."
        });
}
```

Use the exact local `DbResult<T>.Fail` and error property style. Cancellation must still propagate; mapper/result-set failures should return failed `DbResult<T>` so the helper behaves like the rest of the fluent execution surface. Do not copy raw exception messages or inner exceptions into public errors for this helper; if diagnostic details are needed, they must flow through the existing redacted diagnostics path.

- [ ] **Step 5: Run targeted tests and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*MultipleResultExtensionsTests*"
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
- `DeleteMatched` is rejected when combined with update or insert actions.
- `BulkMergeOptions.Validate()` rejects `DeleteNotMatchedBySource` even through a `BulkWriteOptions` reference.
- staged mutation operations create a unique stage index to reject duplicate source key tuples before target DML.
- staged update/delete/upsert/merge failure and cancellation tests prove rollback after target DML has started.
- staged update/delete/upsert/merge reject `UseTransaction = false` before opening a connection.
- bulk insert `UseTransaction = false` is documented and tested as a non-atomic opt-out, not as a rollback-capable mode.
- rollback failure preserves the original public failure and is diagnostic-only.
- final bulk commit uses `CancellationToken.None` so caller cancellation cannot create a false canceled result after commit starts.
- target key uniqueness remains an explicit database schema contract, not a default per-call metadata probe.
- delete uses key-only stage columns and matching reader/mapping shape.
- table and column identifiers reject malformed bracket syntax and identifier parts longer than 128 characters.
- destination-column mapping is tested with CLR member names that differ from SQL destination column names.
- success tests assert final target row values and missing rows, not only affected-row counts.
- `DateOnly`, `TimeOnly`, enum, `Guid`, `decimal`, `byte[]`, and nullable values are normalized or passed through before `SqlBulkCopy` consumes rows.
- enum conversion is selected from shape metadata at shape-build time, not by row-time `value.GetType()` or `Enum.GetUnderlyingType(...)`.
- AOT smoke includes an enum column, verifies the enum is normalized through shape metadata, and roots public bulk overload/executor setup for publish-time analysis without requiring a live database.
- `BulkShapeDataReader<T>` tracks `IsClosed`, implements idempotent `Close()`/`Dispose(bool)`, clears current row state when `Read()` reaches EOF, throws on missing `GetOrdinal` names, reports `HasRows` as result-set presence without skipping the first row, and disposes the underlying row enumerator exactly once.
- new AOT-safe bulk options default to `CheckConstraints = true`.
- SQL/general failures return failed `DbResult<T>` after rollback and redaction; cancellation attempts rollback before rethrow.
- New shape APIs have no `RequiresUnreferencedCode`.
- AOT verification references the shape reader path and public bulk overload/executor reachability path.

- [ ] **Step 3: Run bulk targeted gates**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeDataReaderTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkSqlBuilderTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkMutationTests*"
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

This task owns the final public documentation and skill edits for all integrated
v2.4.0 scope items. Consume the bulk sub-plan's Task 10 documentation checklist
instead of re-editing content that was already committed elsewhere. If the bulk
sub-plan was executed standalone and already modified these public docs, merge
or de-duplicate those edits here; do not add duplicate cookbook sections or
history entries.

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
- duplicate source key rejection through stage unique indexes,
- target key uniqueness as a required application schema contract,
- key-only delete staging,
- `DateOnly`/`TimeOnly`/enum normalization,
- shape-metadata enum converter,
- reader enumerator disposal,
- `CheckConstraints = true` default,
- `DbResult<T>` failure behavior after rollback,
- `DeleteNotMatchedBySource` exclusion.

- [ ] **Step 4: Update skill guidance**

Update `.agents/skills/lib-db/SKILL.md` with compact consumer guidance for the new APIs:

- use tags overload for grouped HybridCache invalidation,
- use `ReadMultipleAsync<...>` for common multi-result SPs,
- use `BulkShape<T>` for AOT-safe bulk writes,
- treat bulk mutation keys as non-null unique database keys,
- do not use roadmap features as if implemented.

### Task 6: Security and Release Verification

**Files:**
- No planned source changes unless verification finds an issue.

- [ ] **Step 1: Static search for known risk markers**

Run:

```powershell
$secretKeys = 'ConnectionString|Password|Pwd|User Id'
Get-ChildItem -Path Lib.Db,Verification,docs,.agents -Recurse -File |
    Select-String -Pattern $secretKeys |
    ForEach-Object {
        [pscustomobject]@{
            Path = $_.Path
            Key = $_.Matches[0].Value
        }
    } |
    Sort-Object Path,Key -Unique

rg -n "MERGE|DeleteNotMatchedBySource|GetProperties|value\\.GetType\\(|RequiresUnreferencedCode|RequiresDynamicCode|IL3050|MakeGenericType|Expression\\.Compile|DynamicMethod|Reflection\\.Emit|RemoveByTagAsync|WithHybridCacheAsync|ReadMultipleAsync|BulkShape|CREATE UNIQUE INDEX|DateOnly|TimeOnly|DbResult<long>|BulkMergeOptions|CheckConstraints|Dispose\\(|Enum.GetUnderlyingType" Lib.Db Verification docs .agents
```

Expected:

- secret scan prints only file paths and key names, never values,
- `MERGE` not used as default bulk engine,
- `DeleteNotMatchedBySource` appears only as rejected/unsupported,
- new AOT-safe bulk APIs do not carry `RequiresUnreferencedCode` or `RequiresDynamicCode`,
- new AOT-safe bulk path does not use `MakeGenericType`, `Expression.Compile`, `DynamicMethod`, or `Reflection.Emit`,
- duplicate source-key rejection, value normalization, and polymorphic merge-option validation appear in tests/docs,
- cache tag behavior appears in tests/docs.

- [ ] **Step 2: Run targeted tests**

Run:

```powershell
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*QueryCacheExtensionsCoverageTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*MultipleResultExtensionsTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkShapeDataReaderTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkSqlBuilderTests*"
pwsh -NoProfile -File Verification/scripts/Invoke-Tests.ps1 -Target IntegrationTests -FilterClass "*BulkMutationTests*"
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
$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = "Verification/artifacts/logs/v240-release-verification-$stamp.log"
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
$verificationOutput = & pwsh -NoProfile -File Verification/scripts/Invoke-Verification.ps1 -BenchmarkJob Short *>&1
$exitCode = $LASTEXITCODE
$verificationOutput | Tee-Object -FilePath $log
$postLogExitCode = 0
if (-not (Test-Path -LiteralPath $log) -or (Get-Item -LiteralPath $log).Length -eq 0) {
    Write-Warning "Release verification log was not created or is empty: $log"
    $postLogExitCode = 1
}
pwsh -NoProfile -File Verification/scripts/Scan-VerificationArtifacts.ps1 -Paths $log
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
pwsh -NoProfile -File Verification/scripts/Assert-GeneratedArtifactsUntracked.ps1
if ($LASTEXITCODE -ne 0) { $postLogExitCode = $LASTEXITCODE }
if ($exitCode -ne 0) { exit $exitCode }
if ($postLogExitCode -ne 0) { exit $postLogExitCode }
```

Expected: release-grade verification completes successfully and leaves a durable
log under `Verification/artifacts/logs/`. The log is audit evidence only; it
must still pass the repository's secret-scan/redaction expectations and must not
contain connection-string, password, token, SQL parameter, row-value, or cache
payload values. Log creation, log non-emptiness, log-specific artifact scanning,
and generated-artifact tracking are hard failure gates.

- [ ] **Step 5: Final security review checklist**

Confirm:

- cache keys and tags are documented as non-sensitive and app-owned,
- cache tag collections cannot contain null elements,
- cache tag wildcard `*` cannot be assigned to entries,
- more than 32 distinct cache tags are rejected rather than silently truncated,
- duplicate cache tags are deduplicated using ordinal comparison before enforcing the 32-tag ceiling,
- the existing HybridCache failure test has been rewritten to expect `DB query failed.` and to reject raw `DbError.Message`, SQL text, object names, row values, cache payloads, and tenant/user identifiers,
- the faulted-task HybridCache failure path maps non-cancellation exceptions to `DB query failed.` without preserving the raw exception as `InnerException`,
- typed multi-result helper disposes readers and maps read failures to failed `DbResult<T>`,
- typed multi-result helper has ordering and disposal tests for arity 2, 3, and 4,
- typed multi-result helper has missing-result/read-failure redaction tests for arity 3 or 4, not only arity 2,
- bulk writes validate identifiers and run staged mutation in a transaction,
- staged bulk update/delete/upsert/merge reject `UseTransaction = false` in v2.4.0 instead of promising rollback without a transaction,
- bulk insert documents `UseTransaction = false` as an explicit non-atomic performance opt-out where partial rows can remain after failure or cancellation,
- malformed destination names with empty parts or whitespace around separators are rejected instead of normalized,
- bulk staged mutation rejects duplicate source keys before target DML,
- unsupported `SqlDbType` values are rejected before any database connection opens,
- target key uniqueness is documented as an application-owned schema contract,
- bulk delete uses key-only staging,
- bulk reader value normalization matches the existing TVP path,
- bulk enum conversion is shape-metadata based rather than row-time type inspection,
- AOT smoke verifies an enum column and public bulk overload reachability through the static-shape path,
- bulk readers track `IsClosed`, close/dispose idempotently, clear current row state at EOF, throw on missing `GetOrdinal` names, report `HasRows` as result-set presence while preserving first-row behavior, and dispose the underlying row enumerator exactly once,
- AOT-safe bulk defaults to `CheckConstraints = true`,
- non-cancellation bulk failures return redacted failed `DbResult<T>` after best-effort rollback,
- rollback failure cannot replace the original public bulk failure,
- cancellation attempts rollback before rethrow only before commit begins,
- final bulk commit is non-cancelable from the caller token to avoid ambiguous canceled results after commit starts,
- generated/future roadmap docs do not imply automatic production DDL/DML,
- docs do not overpromise L2 invalidation across all servers.

## Completion Criteria

The integrated additional scope is complete when:

- Pre-implementation AOT and release-verification baselines were captured before source changes.
- HybridCache tags overload is implemented and tested.
- Typed QueryMultiple helper is implemented and tested.
- AOT-safe bulk mutation sub-plan is implemented and tested.
- The document review findings from 2026-05-22 are closed: tag cap, duplicate tag dedupe, null tag rejection, leading/trailing tag whitespace rejection, cache factory generic error messages, faulted-task generic mapping, no raw inner exception retention, and the existing failure-test rewrite, QueryMultiple `Lib.Db.Extensions` using guidance, QueryMultiple redacted failure mapping, QueryMultiple arity 3/4 success and failure coverage, DateOnly/TimeOnly/Guid/decimal/byte-array/nullable normalization, unsupported `SqlDbType` pre-connection rejection, invalid batch/timeout option tests, polymorphic merge-option validation, invalid merge action combinations, malformed destination-name rejection, separator-whitespace rejection, 128-character identifier limit, duplicate source-key rejection, target key schema contract, key-only delete staging, DbResult failure mapping after rollback, rollback-failure primary-error preservation, final commit non-cancellation, staged-DML rollback/cancellation coverage, rollback-on-cancellation-before-commit, `UseTransaction = false` staged-mutation rejection, insert non-atomic opt-out documentation, redacted bulk general-error mapping, `CheckConstraints = true` default, reader lifecycle/disposal/idempotency, internal reader API surface, EOF current clearing, missing ordinal failure, first-row-safe `HasRows`, AOT enum smoke, public bulk AOT reachability, `RequiresDynamicCode`/IL3050 static gates, durable secret-safe verification log output with post-log scan/tracking gates, pre-implementation AOT/release baseline capture, AOT sub-plan parent linkage, and shape-metadata enum conversion.
- Roadmap document exists for generator/migration/change-tracking.
- Public docs and Lib.Db skill guidance are updated.
- Native AOT verification passes.
- Official release verification passes.
- Final security review has no blocking findings.

## Scope Reduction Gate

The approved v2.4.0 implementation scope is HybridCache tags, typed
QueryMultiple, and AOT-safe bulk insert/update/delete/upsert/merge. If bulk
implementation constraints force removing delete, merge, upsert, or any other
approved operation, stop before continuing release work. Update both integrated
and bulk specs/plans, revise docs/history/API promises, rerun review on the
reduced scope, and obtain explicit user approval for the new v2.4.0 scope.
