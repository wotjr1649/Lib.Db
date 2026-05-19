# v2.3.0 Coverage AOT Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise the agreed v2.3.0 coverage targets to 100% for the named cache, hosting, query-cache, mapper, and TVP core areas while adding a real Native AOT publish/run verification gate.

**Architecture:** Keep public Lib.Db behavior unchanged and add only narrow internal seams for deterministic tests: a cache-maintenance tick source and a runtime-feature switch used by mapper selection. Native AOT validation lives in a separate console project that exercises AOT-safe TVP static-shape paths and fails on dynamic-code mode or AOT analyzer warnings.

**Tech Stack:** .NET 10, C# 14 preview, xUnit v3, FluentAssertions, Moq, Coverlet collector, ReportGenerator, Microsoft.Data.SqlClient, Native AOT (`PublishAot=true`).

---

## Source Basis

- Local spec: `docs/superpowers/specs/2026-05-18-v230-coverage-aot-design.md`
- Microsoft Learn Native AOT deployment, checked 2026-05-18: `https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/`
- Microsoft Learn unit test coverage with Coverlet and ReportGenerator, checked 2026-05-18: `https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-code-coverage`
- Microsoft Learn `dotnet test` VSTest/Microsoft Testing Platform integration, checked 2026-05-18: `https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-platform-integration-dotnet-test`
- Microsoft Learn Microsoft Testing Platform coverage and `coverlet.MTP`, checked 2026-05-18: `https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-code-coverage`
- Coverlet official repository, checked 2026-05-18: `https://github.com/coverlet-coverage/coverlet`

## Current Baseline

- Last verified run: `TestResults/CoverletFull13`
- Test result: `Passed: 487`, `Failed: 0`, `Skipped: 0`
- `Lib.Db` coverage: line `80.8%`, branch `70.9%`, method `87.0%`
- TVP runtime core: line, branch, and method already `100%`
- Remaining named gaps:
  - `Lib.Db.Caching.CacheMaintenanceService`: hosted timer loop false branch and stop path.
  - `Lib.Db.Hosting.SchemaWarmupService`: constructor null guards, null skip branches, diagnostic redaction fallback, and remaining logging/cancellation branches.
  - `Lib.Db.Extensions.QueryCacheExtensions`: `result.Error?.Message ?? "DB 쿼리 실패"` null-error branch.
  - `Lib.Db.Execution.Binding.GeneratedResultMapper<T>` and `ReflectionParameterMapper<T>`: dynamic-code false branch and output/default parameter branches.

## File Structure

- Modify: `Lib.Db/Caching/CacheMaintenanceService.cs`
  - Adds an internal tick-source abstraction used only by the hosted loop.
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`
  - Adds deterministic loop tests for tick `true`, tick `false`, disposal, and cycle exception handling.
- Create: `Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs`
  - Adds an internal testable wrapper around `RuntimeFeature.IsDynamicCodeSupported`.
- Modify: `Lib.Db/Execution/Binding/Mappers.cs`
  - Replaces direct `RuntimeFeature.IsDynamicCodeSupported` reads with `RuntimeFeatureSwitch.IsDynamicCodeSupported`.
- Modify: `Lib.Db/Lib.Db.csproj`
  - Keeps `<IsAotCompatible>true</IsAotCompatible>` and grants `InternalsVisibleTo` to the AOT verification project so the AOT executable can exercise internal mapper branches directly.
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`
  - Adds mapper branch tests for dynamic-code false, default input skip, nullable missing input, writable output, read-only output, and ignored input output mapping.
- Modify: `Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj`
  - Pins the coverage test project to the VSTest-compatible collector path with `IsTestingPlatformApplication=false` and `TestingPlatformDotnetTestSupport=false`.
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/SchemaWarmupServiceCoverageTests.cs`
  - Adds deterministic constructor, null option, and redaction fallback tests.
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs`
  - Adds a default `DbResult<T>` failure test to cover the null error object branch.
- Create: `Tests/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj`
  - AOT console project with `<PublishAot>true</PublishAot>` in the project file.
- Create: `Tests/Lib.Db.AotVerification/Program.cs`
  - AOT smoke executable that verifies `RuntimeFeature.IsDynamicCodeSupported == false`, TVP static-shape binding, generated result mapping, and reflection parameter mapping.
- Create: `Tools/coverage/Assert-LibDbCoverage.ps1`
  - Parses Cobertura XML and fails the build if v2.3.0 target coverage gates are not met.
- Modify: `Lib.Db.slnx`
  - Adds the AOT verification project under `/Tests/`.

## Security And Safety Guardrails

- Do not print connection strings, passwords, tokens, or SQL login values.
- Do not execute direct SQL DDL/DML through CLI tools in this plan.
- The AOT verification project must not connect to a database; it validates Lib.Db binding behavior in process.
- Existing `Tools/Lib.Db.AotSmoke` remains the DB-backed AOT smoke tool. The new `Tests/Lib.Db.AotVerification` project is the no-secret, no-DB release gate.
- Internal seams must remain `internal`; no new public toggles are added.
- `coverlet.collector` remains on the VSTest-compatible path. Do not migrate to Microsoft Testing Platform in this implementation.
- Do not suppress AOT or trimming warnings only to make the AOT gate green. New suppressions require a comment that names the exact safe API boundary.

---

### Task 1: Make CacheMaintenanceService Timer Loop Deterministic

**Files:**
- Modify: `Lib.Db/Caching/CacheMaintenanceService.cs`
- Test: `Tests/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`

- [ ] **Step 1: Add failing deterministic loop tests**

Modify `Tests/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`.

Add this using near the top:

```csharp
using System.Reflection;
```

Add these tests inside `CacheHostingCoverageTests`, after `CacheMaintenanceService_ShouldRunSuccessfulHostedCycle`:

```csharp
[Fact]
public async Task CacheMaintenanceService_ExecuteAsync_ShouldStopWhenTickSourceReturnsFalse()
{
    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
    using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
    var ticks = new ScriptedTickSource(false);
    var service = new CacheMaintenanceService(
        provider,
        loggerFactory.CreateLogger<CacheMaintenanceService>(),
        TimeSpan.FromMilliseconds(1),
        () => ticks);

    await ExecuteCacheMaintenanceAsync(service, TestContext.Current.CancellationToken);

    ticks.WaitCalls.Should().Be(1);
    ticks.DisposeCount.Should().Be(1);
}

[Fact]
public async Task CacheMaintenanceService_ExecuteAsync_ShouldContinueAfterCycleExceptionThenStop()
{
    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
    var ticks = new ScriptedTickSource(true, false);
    var service = new CacheMaintenanceService(
        new ThrowingScopeProvider(),
        loggerFactory.CreateLogger<CacheMaintenanceService>(),
        TimeSpan.FromMilliseconds(1),
        () => ticks);

    await ExecuteCacheMaintenanceAsync(service, TestContext.Current.CancellationToken);

    ticks.WaitCalls.Should().Be(2);
    ticks.DisposeCount.Should().Be(1);
}

[Fact]
public async Task CacheMaintenanceService_ExecuteAsync_ShouldTreatCancellationAsShutdown()
{
    using CancellationTokenSource cts = new();
    await cts.CancelAsync();

    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
    using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
    var ticks = new ScriptedTickSource(true);
    var service = new CacheMaintenanceService(
        provider,
        loggerFactory.CreateLogger<CacheMaintenanceService>(),
        TimeSpan.FromMilliseconds(1),
        () => ticks);

    await ExecuteCacheMaintenanceAsync(service, cts.Token);

    ticks.WaitCalls.Should().Be(0);
    ticks.DisposeCount.Should().Be(1);
}
```

Add these helpers before `ThrowingScopeProvider`:

```csharp
private static async Task ExecuteCacheMaintenanceAsync(
    CacheMaintenanceService service,
    CancellationToken cancellationToken)
{
    MethodInfo method = typeof(CacheMaintenanceService).GetMethod(
        "ExecuteAsync",
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    var task = (Task)method.Invoke(service, [cancellationToken])!;
    await task;
}

private sealed class ScriptedTickSource(params bool[] ticks) : ICacheMaintenanceTickSource
{
    private readonly Queue<bool> _ticks = new(ticks);

    public int WaitCalls { get; private set; }

    public int DisposeCount { get; private set; }

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();
        WaitCalls++;
        return ValueTask.FromResult(_ticks.Count > 0 && _ticks.Dequeue());
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Run the new cache tests and verify the expected compile failure**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~CacheHostingCoverageTests"
```

Expected:

```text
CS0246
ICacheMaintenanceTickSource
```

- [ ] **Step 3: Add the internal tick source seam**

Modify `Lib.Db/Caching/CacheMaintenanceService.cs`.

Add this field:

```csharp
private readonly Func<ICacheMaintenanceTickSource> _tickSourceFactory;
```

Replace the existing internal constructor with these two constructors:

```csharp
internal CacheMaintenanceService(
    IServiceProvider serviceProvider,
    ILogger<CacheMaintenanceService> logger,
    TimeSpan checkInterval)
    : this(serviceProvider, logger, checkInterval, null)
{
}

internal CacheMaintenanceService(
    IServiceProvider serviceProvider,
    ILogger<CacheMaintenanceService> logger,
    TimeSpan checkInterval,
    Func<ICacheMaintenanceTickSource>? tickSourceFactory)
{
    ArgumentNullException.ThrowIfNull(serviceProvider);
    ArgumentNullException.ThrowIfNull(logger);

    if (checkInterval <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(checkInterval), checkInterval, "Check interval must be greater than zero.");

    _serviceProvider = serviceProvider;
    _logger = logger;
    _checkInterval = checkInterval;
    _tickSourceFactory = tickSourceFactory ?? (() => new PeriodicTimerTickSource(checkInterval));
}
```

Replace the timer creation in `ExecuteAsync`:

```csharp
await using ICacheMaintenanceTickSource timer = _tickSourceFactory();
```

The loop remains:

```csharp
while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
```

Add these internal types at the bottom of the same namespace, after `CacheMaintenanceService`:

```csharp
internal interface ICacheMaintenanceTickSource : IAsyncDisposable
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken stoppingToken);
}

internal sealed class PeriodicTimerTickSource(TimeSpan interval) : ICacheMaintenanceTickSource
{
    private readonly PeriodicTimer _timer = new(interval);

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken stoppingToken)
        => _timer.WaitForNextTickAsync(stoppingToken);

    public ValueTask DisposeAsync()
    {
        _timer.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 4: Run cache tests and verify pass**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~CacheHostingCoverageTests"
```

Expected:

```text
Failed: 0
```

- [ ] **Step 5: Commit Task 1**

Run Git outside the sandbox per repository rules:

```powershell
git add Lib.Db/Caching/CacheMaintenanceService.cs Tests/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs
git commit -m "test: cover cache maintenance timer loop"
```

---

### Task 2: Cover SchemaWarmupService Remaining Branches

**Files:**
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/SchemaWarmupServiceCoverageTests.cs`

- [ ] **Step 1: Add constructor, null option, and redaction fallback tests**

Add these tests after `CreateDiagnosticRequestInfo_ShouldRedactInstanceIdAndUseWarmupShape`:

```csharp
[Fact]
public void SchemaWarmupService_ShouldValidateConstructorArguments()
{
    Mock<ISchemaService> schema = new();
    LibDbOptions options = new();
    ILogger<SchemaWarmupService> logger = NullLogger<SchemaWarmupService>.Instance;

    Action nullSchema = () => _ = new SchemaWarmupService(null!, options, logger);
    Action nullOptions = () => _ = new SchemaWarmupService(schema.Object, null!, logger);
    Action nullLogger = () => _ = new SchemaWarmupService(schema.Object, options, null!);

    nullSchema.Should().Throw<ArgumentNullException>().WithParameterName("schemaService");
    nullOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
    nullLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
}

[Fact]
public async Task ExecuteAsync_ShouldSkipWhenConnectionStringNamesAreNull()
{
    Mock<ISchemaService> schema = new();
    LibDbOptions options = new()
    {
        PrewarmSchemas = ["dbo"]
    };
    SetConnectionStringNames(options, null);
    SchemaWarmupService service = CreateService(schema.Object, options);

    await ExecuteAsync(service, TestContext.Current.CancellationToken);

    schema.Verify(
        x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public async Task ExecuteAsync_ShouldSkipWhenPrewarmSchemasAreNull()
{
    Mock<ISchemaService> schema = new();
    LibDbOptions options = new()
    {
        ConnectionStringNames = ["Primary"]
    };
    SetPrewarmSchemas(options, null);
    SchemaWarmupService service = CreateService(schema.Object, options);

    await ExecuteAsync(service, TestContext.Current.CancellationToken);

    schema.Verify(
        x => x.PreloadSchemaAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public void CreateDiagnosticRequestInfo_ShouldFallbackWhenRedactorReturnsNull()
{
    DbRequestInfo info = SchemaWarmupService.CreateDiagnosticRequestInfo(null!, schemaCount: 0);

    info.InstanceId.Should().BeNull();
    info.CorrelationId.Should().Be("warmup::0");
}
```

Replace `SetConnectionStringNames` with this nullable version:

```csharp
private static void SetConnectionStringNames(LibDbOptions options, IReadOnlyList<string>? value)
{
    FieldInfo field = typeof(LibDbOptions).GetField(
        "<ConnectionStringNames>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(options, value);
}
```

Add this helper after `SetConnectionStringNames`:

```csharp
private static void SetPrewarmSchemas(LibDbOptions options, List<string>? value)
{
    FieldInfo field = typeof(LibDbOptions).GetField(
        "<PrewarmSchemas>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(options, value);
}
```

- [ ] **Step 2: Run schema warmup coverage tests**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~SchemaWarmupServiceCoverageTests"
```

Expected:

```text
Failed: 0
```

- [ ] **Step 3: Commit Task 2**

Run Git outside the sandbox per repository rules:

```powershell
git add Tests/Lib.Db.IntegrationTests/Unit/SchemaWarmupServiceCoverageTests.cs
git commit -m "test: cover schema warmup branches"
```

---

### Task 3: Cover QueryCacheExtensions Null Error Branch

**Files:**
- Modify: `Tests/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs`

- [ ] **Step 1: Add the default failure test**

Add this test after `WithHybridCacheAsync_ShouldUseFallbackMessageWhenErrorMessageIsMissing`:

```csharp
[Fact]
public async Task WithHybridCacheAsync_ShouldUseFallbackMessageWhenErrorObjectIsMissing()
{
    using ServiceProvider provider = CreateHybridCacheProvider();
    HybridCache cache = provider.GetRequiredService<HybridCache>();
    DbResult<CachedUser?> failureWithoutError = default;

    Func<Task> act = () => Task
        .FromResult(failureWithoutError)
        .WithHybridCacheAsync(cache, "hybrid:failure:null-error", TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

    await act.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("DB 쿼리 실패");
}
```

- [ ] **Step 2: Run query cache coverage tests**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~QueryCacheExtensionsCoverageTests"
```

Expected:

```text
Failed: 0
```

- [ ] **Step 3: Commit Task 3**

Run Git outside the sandbox per repository rules:

```powershell
git add Tests/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs
git commit -m "test: cover hybrid cache null error fallback"
```

---

### Task 4: Add Testable Runtime Feature Switch For Mapper Branches

**Files:**
- Create: `Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs`
- Modify: `Lib.Db/Execution/Binding/Mappers.cs`
- Test: `Tests/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`

- [ ] **Step 1: Add failing mapper tests for dynamic-code false and parameter branches**

Modify `Tests/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`.

Add this using:

```csharp
using System.Reflection;
```

Add these tests after `ReflectionParameterMapper_ShouldThrowWhenRequiredPropertyIsMissing`:

```csharp
[Fact]
public void ReflectionParameterMapper_ShouldCoverDefaultMissingInputAndOutputBranches()
{
    var mapper = new ReflectionParameterMapper<ReflectionOutputCoverageDto>(strict: true);
    var dto = new ReflectionOutputCoverageDto();
    using var inputCommand = new SqlCommand();

    mapper.MapParameters(inputCommand, dto, CreateSchema(
        Param("@MissingDefaulted", SqlDbType.Int, nullable: false, hasDefault: true),
        Param("@MissingNullable", SqlDbType.NVarChar, nullable: true)));

    inputCommand.Parameters
        .Cast<SqlParameter>()
        .Should()
        .ContainSingle(p => p.ParameterName == "@MissingNullable");
    inputCommand.Parameters["@MissingNullable"].Value.Should().Be(DBNull.Value);

    using var outputCommand = new SqlCommand();
    outputCommand.Parameters.Add(new SqlParameter("@WritableValue", SqlDbType.Int)
    {
        Direction = ParameterDirection.InputOutput,
        Value = 5
    });
    outputCommand.Parameters.Add(new SqlParameter("@ReadOnlyValue", SqlDbType.Int)
    {
        Direction = ParameterDirection.Output,
        Value = 7
    });
    outputCommand.Parameters.Add(new SqlParameter("@IgnoredInput", SqlDbType.Int)
    {
        Direction = ParameterDirection.Input,
        Value = 9
    });

    mapper.MapOutputParameters(outputCommand, dto);

    dto.WritableValue.Should().Be(5);
    dto.ReadOnlyValue.Should().Be(0);
}

[Fact]
public void MapperFactory_ShouldUseReflectionMapperWhenDynamicCodeIsDisabled()
{
    using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);
    using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
    var factory = new MapperFactory(services, new LibDbOptions());

    ISqlMapper<RuntimeFeatureFallbackDto> mapper = factory.GetMapper<RuntimeFeatureFallbackDto>();

    mapper.Should().BeOfType<ReflectionParameterMapper<RuntimeFeatureFallbackDto>>();
}

[Fact]
public void GeneratedResultMapper_ShouldUseReflectionParameterMapperWhenDynamicCodeIsDisabled()
{
    using IDisposable _ = RuntimeFeatureSwitch.OverrideDynamicCodeSupportedForTests(false);

    var mapper = new GeneratedResultMapper<GeneratedDbDataReaderRow>(new LibDbOptions());

    GetPrivateField(mapper, "_parameterMapper")
        .Should()
        .BeOfType<ReflectionParameterMapper<GeneratedDbDataReaderRow>>();
}
```

Add this helper before `CreateSchema`:

```csharp
private static object GetPrivateField(object instance, string fieldName)
{
    FieldInfo field = instance.GetType().GetField(
        fieldName,
        BindingFlags.Instance | BindingFlags.NonPublic)!;

    return field.GetValue(instance)!;
}
```

Add these DTOs near the existing nested DTOs:

```csharp
private sealed class ReflectionOutputCoverageDto
{
    public int? WritableValue { get; set; }

    public int ReadOnlyValue => 0;
}

private sealed class RuntimeFeatureFallbackDto
{
    public int Id { get; set; }
}
```

- [ ] **Step 2: Run mapper tests and verify the expected compile failure**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~MapperCoverageTests"
```

Expected:

```text
CS0103
RuntimeFeatureSwitch
```

- [ ] **Step 3: Add RuntimeFeatureSwitch**

Create `Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs`:

```csharp
// ============================================================================
// File: Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs
// Role: Narrow internal seam for runtime capability decisions in tests
// ============================================================================

#nullable enable

using System.Runtime.CompilerServices;
using System.Threading;

namespace Lib.Db.Execution.Binding;

internal static class RuntimeFeatureSwitch
{
    private static readonly AsyncLocal<bool?> s_dynamicCodeSupportedOverride = new();

    internal static bool IsDynamicCodeSupported
        => s_dynamicCodeSupportedOverride.Value ?? RuntimeFeature.IsDynamicCodeSupported;

    internal static IDisposable OverrideDynamicCodeSupportedForTests(bool value)
    {
        bool? previous = s_dynamicCodeSupportedOverride.Value;
        s_dynamicCodeSupportedOverride.Value = value;
        return new ResetScope(previous);
    }

    private sealed class ResetScope(bool? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            s_dynamicCodeSupportedOverride.Value = previous;
            _disposed = true;
        }
    }
}
```

- [ ] **Step 4: Replace direct RuntimeFeature checks in Mappers**

Modify `Lib.Db/Execution/Binding/Mappers.cs`.

Replace:

```csharp
if (RuntimeFeature.IsDynamicCodeSupported)
```

with:

```csharp
if (RuntimeFeatureSwitch.IsDynamicCodeSupported)
```

Replace:

```csharp
_parameterMapper = RuntimeFeature.IsDynamicCodeSupported
```

with:

```csharp
_parameterMapper = RuntimeFeatureSwitch.IsDynamicCodeSupported
```

Keep the existing `using System.Runtime.CompilerServices;` because other attributes in the file still use it.

- [ ] **Step 5: Run mapper tests and verify pass**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~MapperCoverageTests"
```

Expected:

```text
Failed: 0
```

- [ ] **Step 6: Commit Task 4**

Run Git outside the sandbox per repository rules:

```powershell
git add Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs Lib.Db/Execution/Binding/Mappers.cs Tests/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs
git commit -m "test: cover mapper aot fallback branches"
```

---

### Task 5: Add Dedicated Native AOT Verification Project

**Files:**
- Modify: `Lib.Db/Lib.Db.csproj`
- Create: `Tests/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj`
- Create: `Tests/Lib.Db.AotVerification/Program.cs`
- Modify: `Lib.Db.slnx`

- [ ] **Step 1: Grant test-only internal access to the AOT verification assembly**

Modify `Lib.Db/Lib.Db.csproj`.

Inside the existing `InternalsVisibleTo` item group, add:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
  <_Parameter1>Lib.Db.AotVerification</_Parameter1>
</AssemblyAttribute>
```

The final item group must include:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>DynamicProxyGenAssembly2</_Parameter1>
  </AssemblyAttribute>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>Lib.Db.IntegrationTests</_Parameter1>
  </AssemblyAttribute>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>Lib.Db.AotVerification</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

- [ ] **Step 2: Create the AOT verification project file**

Create `Tests/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsPackable>false</IsPackable>
    <WarningsAsErrors>$(WarningsAsErrors);IL2026;IL3050;IL2070;IL2072;IL2067;IL2065;IL2090;IL2091</WarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Lib.Db\Lib.Db.csproj" />
    <TrimmerRootAssembly Include="Lib.Db" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add the AOT verification executable**

Create `Tests/Lib.Db.AotVerification/Program.cs`:

```csharp
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Lib.Db;
using Lib.Db.Configuration;
using Lib.Db.Contracts.Mapping;
using Lib.Db.Contracts.Models;
using Lib.Db.Execution.Binding;
using Lib.Db.Execution.Tvp;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;

try
{
    VerifyNativeAotRuntimeMode();
    VerifyExplicitStaticTvpShape();
    VerifyRegisteredStaticTvpShape();
    VerifyGeneratedMapperAndReflectionParameterMapper();

    Console.WriteLine("Lib.Db AOT verification passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    return 1;
}
finally
{
    DbBinder.ConfigureTvp(new LibDbOptions());
    DbBinder.ClearTvpCaches();
}

static void VerifyNativeAotRuntimeMode()
{
    if (RuntimeFeature.IsDynamicCodeSupported)
        throw new InvalidOperationException("Expected Native AOT runtime mode with dynamic code disabled.");
}

static void VerifyExplicitStaticTvpShape()
{
    LibDbTvpValue value = LibDb.Tvp("dbo.T_OrderItem", CreateRows(), CreateShape());

    using var command = new SqlCommand();
    DbBinder.BindRawParameter(command, "Rows", value);

    VerifyStructuredRows(command, "@Rows");
}

static void VerifyRegisteredStaticTvpShape()
{
    LibDbOptions options = new();
    options.Tvp
        .Map<AotOrderItem>("dbo.T_OrderItem")
        .Column("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Qty", SqlDbType.Int, static row => row.Qty);

    DbBinder.ConfigureTvp(options);
    DbBinder.ClearTvpCaches();

    using var command = new SqlCommand();
    DbBinder.BindRawParameter(command, "Rows", CreateRows());

    VerifyStructuredRows(command, "@Rows");
}

static void VerifyGeneratedMapperAndReflectionParameterMapper()
{
    DataTable table = new();
    table.Columns.Add("Id", typeof(int));
    table.Columns.Add("Name", typeof(string));
    table.Rows.Add(42, "aot-row");

    using DbDataReader reader = table.CreateDataReader();
    if (!reader.Read())
        throw new InvalidOperationException("Expected one generated-mapper row.");

    var generatedMapper = new GeneratedResultMapper<AotGeneratedRow>(new LibDbOptions());
    AotGeneratedRow row = generatedMapper.MapResult(reader);

    AssertEqual(42, row.Id, "generated row Id");
    AssertEqual("aot-row", row.Name, "generated row Name");

    var parameterMapper = new ReflectionParameterMapper<AotParameterDto>(strict: true);
    var dto = new AotParameterDto
    {
        Id = 7,
        Name = "input"
    };

    using var command = new SqlCommand();
    parameterMapper.MapParameters(command, dto, CreateSchema(
        Param("@Id", SqlDbType.Int, nullable: false),
        Param("@Name", SqlDbType.NVarChar, nullable: false),
        Param("@OutValue", SqlDbType.Int, direction: ParameterDirection.Output)));

    if (!command.Parameters.Contains("@Id"))
        throw new InvalidOperationException("Expected @Id parameter.");

    if (!command.Parameters.Contains("@Name"))
        throw new InvalidOperationException("Expected @Name parameter.");

    if (!command.Parameters.Contains("@OutValue"))
        throw new InvalidOperationException("Expected @OutValue parameter.");

    command.Parameters["@OutValue"].Value = 99;
    parameterMapper.MapOutputParameters(command, dto);

    AssertEqual(99, dto.OutValue, "output parameter");
}

static AotOrderItem[] CreateRows()
    => [new(1, "A100", 2)];

static TvpShape<AotOrderItem> CreateShape()
    => TvpShape.For<AotOrderItem>()
        .Column("Id", SqlDbType.Int, static row => row.Id)
        .Column("Sku", SqlDbType.NVarChar, static row => row.Sku, size: 64)
        .Column("Qty", SqlDbType.Int, static row => row.Qty)
        .Build();

static void VerifyStructuredRows(SqlCommand command, string parameterName)
{
    SqlParameter parameter = command.Parameters[parameterName];

    if (parameter.SqlDbType != SqlDbType.Structured)
        throw new InvalidOperationException($"Expected structured parameter for {parameterName}.");

    if (!string.Equals(parameter.TypeName, "dbo.T_OrderItem", StringComparison.Ordinal))
        throw new InvalidOperationException($"Unexpected TVP type name: {parameter.TypeName}");

    if (parameter.Value is not IEnumerable<SqlDataRecord> records)
        throw new InvalidOperationException("Expected static TVP shape to bind as SqlDataRecord sequence.");

    using IEnumerator<SqlDataRecord> enumerator = records.GetEnumerator();
    if (!enumerator.MoveNext())
        throw new InvalidOperationException("Expected one TVP row.");

    SqlDataRecord record = enumerator.Current;
    AssertEqual("Id", record.GetName(0), "column 0 name");
    AssertEqual("Sku", record.GetName(1), "column 1 name");
    AssertEqual("Qty", record.GetName(2), "column 2 name");
    AssertEqual(1, record.GetInt32(0), "Id");
    AssertEqual("A100", record.GetString(1), "Sku");
    AssertEqual(2, record.GetInt32(2), "Qty");

    if (enumerator.MoveNext())
        throw new InvalidOperationException("Expected exactly one TVP row.");
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
}

static SpSchema CreateSchema(params SpParameterMetadata[] parameters)
    => new()
    {
        Name = "dbo.usp_AotVerification",
        VersionToken = 1,
        LastCheckedAt = DateTime.UtcNow,
        Parameters = parameters
    };

static SpParameterMetadata Param(
    string name,
    SqlDbType dbType,
    ParameterDirection direction = ParameterDirection.Input,
    bool nullable = true,
    bool hasDefault = false)
    => new(
        name,
        UdtTypeName: null,
        Size: 0,
        dbType,
        direction,
        Precision: 0,
        Scale: 0,
        IsNullable: nullable,
        HasDefaultValue: hasDefault);

internal readonly record struct AotOrderItem(int Id, string Sku, int Qty);

internal sealed class AotGeneratedRow : IMapableResult<AotGeneratedRow>
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    public static AotGeneratedRow Map(DbDataReader reader)
        => new()
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1)
        };

    public static AotGeneratedRow Map(SqlDataReader reader)
        => throw new NotSupportedException("The AOT verification uses the DbDataReader overload.");
}

internal sealed class AotParameterDto
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int OutValue { get; set; }
}
```

- [ ] **Step 4: Add the project to Lib.Db.slnx**

Modify `Lib.Db.slnx` so the `/Tests/` folder contains both test projects:

```xml
<Folder Name="/Tests/">
  <Project Path="Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj" />
  <Project Path="Tests/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj" />
</Folder>
```

- [ ] **Step 5: Build the AOT verification project in normal JIT mode**

Run:

```powershell
dotnet build Tests\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj --nologo -v:minimal
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 6: Commit Task 5**

Run Git outside the sandbox per repository rules:

```powershell
git add Lib.Db/Lib.Db.csproj Tests/Lib.Db.AotVerification/Lib.Db.AotVerification.csproj Tests/Lib.Db.AotVerification/Program.cs Lib.Db.slnx
git commit -m "test: add native aot verification project"
```

---

### Task 6: Pin VSTest Coverage Mode And Add Coverage Gate Script

**Files:**
- Modify: `Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj`
- Create: `Tools/coverage/Assert-LibDbCoverage.ps1`

- [ ] **Step 1: Pin the integration test project to VSTest collector mode**

Modify `Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj`.

In the existing first `<PropertyGroup>`, keep the existing `IsTestingPlatformApplication` line and add `TestingPlatformDotnetTestSupport`:

```xml
<IsTestingPlatformApplication>false</IsTestingPlatformApplication>
<TestingPlatformDotnetTestSupport>false</TestingPlatformDotnetTestSupport>
```

The first property group must include:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>preview</LangVersion>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <OutputType>Exe</OutputType>
  <IsPackable>false</IsPackable>
  <IsTestProject>true</IsTestProject>
  <IsTestingPlatformApplication>false</IsTestingPlatformApplication>
  <TestingPlatformDotnetTestSupport>false</TestingPlatformDotnetTestSupport>
  <BuildInParallel>false</BuildInParallel>
</PropertyGroup>
```

- [ ] **Step 2: Add the automated coverage gate script**

Create `Tools/coverage/Assert-LibDbCoverage.ps1`:

```powershell
param(
    [Parameter(Mandatory = $true)]
    [string] $CoberturaPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoberturaPath)) {
    throw "Cobertura file not found: $CoberturaPath"
}

$culture = [System.Globalization.CultureInfo]::InvariantCulture
[xml] $document = Get-Content -LiteralPath $CoberturaPath
$coverage = $document.coverage

function Convert-CoverageRate {
    param([Parameter(Mandatory = $true)] [object] $Value)
    return [double]::Parse([string] $Value, $culture)
}

function Assert-AtLeast {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [Parameter(Mandatory = $true)] [double] $Actual,
        [Parameter(Mandatory = $true)] [double] $Expected
    )

    if ($Actual + 0.0000001 -lt $Expected) {
        throw "$Name expected >= $Expected but was $Actual"
    }
}

function Assert-TargetCoverage {
    param(
        [Parameter(Mandatory = $true)] [object[]] $Classes,
        [Parameter(Mandatory = $true)] [string] $DisplayName,
        [Parameter(Mandatory = $true)] [string] $Prefix
    )

    $targetClasses = @($Classes | Where-Object {
        $name = [string] $_.name
        $name -eq $Prefix -or $name.StartsWith("$Prefix/") -or $name.StartsWith("$Prefix+")
    })

    if ($targetClasses.Count -eq 0) {
        throw "$DisplayName target classes were not found with prefix '$Prefix'."
    }

    $lines = @($targetClasses | ForEach-Object { $_.lines.line })
    $uncoveredLines = @($lines | Where-Object { [int] $_.hits -eq 0 })
    if ($uncoveredLines.Count -gt 0) {
        $sample = ($uncoveredLines | Select-Object -First 8 | ForEach-Object { [string] $_.number }) -join ', '
        throw "$DisplayName line coverage expected 100%; uncovered lines: $sample"
    }

    $coveredBranches = 0
    $totalBranches = 0
    foreach ($line in $lines) {
        if ([string] $line.branch -ne 'True') {
            continue
        }

        $conditionCoverage = [string] $line.'condition-coverage'
        if ($conditionCoverage -match '\((\d+)/(\d+)\)') {
            $coveredBranches += [int] $Matches[1]
            $totalBranches += [int] $Matches[2]
        }
    }

    if ($coveredBranches -ne $totalBranches) {
        throw "$DisplayName branch coverage expected 100%; covered $coveredBranches of $totalBranches branches."
    }

    $methods = @($targetClasses | ForEach-Object { $_.methods.method })
    $uncoveredMethods = @($methods | Where-Object {
        (Convert-CoverageRate $_.'line-rate') -lt 1.0 -or
        ([string] $_.'branch-rate' -ne '' -and (Convert-CoverageRate $_.'branch-rate') -lt 1.0)
    })

    if ($uncoveredMethods.Count -gt 0) {
        $sample = ($uncoveredMethods | Select-Object -First 8 | ForEach-Object { [string] $_.name }) -join ', '
        throw "$DisplayName method coverage expected 100%; uncovered or partial methods: $sample"
    }

    Write-Host "$DisplayName coverage gate passed."
}

Assert-AtLeast 'Lib.Db overall line coverage' (Convert-CoverageRate $coverage.'line-rate') 0.80

$classes = @($coverage.packages.package.classes.class)
$targets = @(
    @{ DisplayName = 'CacheMaintenanceService'; Prefix = 'Lib.Db.Caching.CacheMaintenanceService' },
    @{ DisplayName = 'SchemaWarmupService'; Prefix = 'Lib.Db.Hosting.SchemaWarmupService' },
    @{ DisplayName = 'QueryCacheExtensions'; Prefix = 'Lib.Db.Extensions.QueryCacheExtensions' },
    @{ DisplayName = 'GeneratedResultMapper<T>'; Prefix = 'Lib.Db.Execution.Binding.GeneratedResultMapper`1' },
    @{ DisplayName = 'ReflectionParameterMapper<T>'; Prefix = 'Lib.Db.Execution.Binding.ReflectionParameterMapper`1' },
    @{ DisplayName = 'TVP ColumnarTvpReader'; Prefix = 'Lib.Db.Execution.Tvp.ColumnarTvpReader' },
    @{ DisplayName = 'TVP RuntimeTvpDataReader'; Prefix = 'Lib.Db.Execution.Tvp.RuntimeTvpDataReader' },
    @{ DisplayName = 'TVP RuntimeTvpRowShape'; Prefix = 'Lib.Db.Execution.Tvp.RuntimeTvpRowShape' },
    @{ DisplayName = 'TVP SqlDataRecordTvpEnumerable'; Prefix = 'Lib.Db.Execution.Tvp.SqlDataRecordTvpEnumerable' },
    @{ DisplayName = 'TVP TvpAccessorCache'; Prefix = 'Lib.Db.Execution.Tvp.TvpAccessorCache' },
    @{ DisplayName = 'TVP TvpAccessorRegistry'; Prefix = 'Lib.Db.Execution.Tvp.TvpAccessorRegistry' },
    @{ DisplayName = 'TVP TvpRowAccessorCache'; Prefix = 'Lib.Db.Execution.Tvp.TvpRowAccessorCache' }
)

foreach ($target in $targets) {
    Assert-TargetCoverage -Classes $classes -DisplayName $target.DisplayName -Prefix $target.Prefix
}

Write-Host 'All Lib.Db v2.3.0 coverage gates passed.'
```

- [ ] **Step 3: Run the test project file guardrail check**

Run:

```powershell
Select-String -Path Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj -Pattern "coverlet.collector|IsTestingPlatformApplication|TestingPlatformDotnetTestSupport"
```

Expected:

```text
coverlet.collector
<IsTestingPlatformApplication>false</IsTestingPlatformApplication>
<TestingPlatformDotnetTestSupport>false</TestingPlatformDotnetTestSupport>
```

- [ ] **Step 4: Commit Task 6**

Run Git outside the sandbox per repository rules:

```powershell
git add Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj Tools/coverage/Assert-LibDbCoverage.ps1
git commit -m "test: add coverage gate guardrails"
```

---

### Task 7: Run Coverage And Native AOT Gates

**Files:**
- Read: `TestResults/**/coverage.cobertura.xml`
- Read: `TestResults/CoverageReport-v230-coverage-aot/Summary.txt`
- Read: `Tools/coverage/Assert-LibDbCoverage.ps1`
- Read: `TestResults/AotVerificationPublish.log`
- Read: `Tests/Lib.Db.AotVerification/bin/Release/net10.0/win-x64/publish/Lib.Db.AotVerification.exe`

- [ ] **Step 1: Run the full Coverlet collector gate**

Run:

```powershell
dotnet test Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj --no-restore --nologo -v:minimal --collect:"XPlat Code Coverage" --results-directory TestResults\CoverletV230CoverageAot
```

Expected:

```text
Failed: 0
coverage.cobertura.xml
```

- [ ] **Step 2: Generate the coverage report**

Run:

```powershell
reportgenerator -reports:TestResults\CoverletV230CoverageAot\**\coverage.cobertura.xml -targetdir:TestResults\CoverageReport-v230-coverage-aot -reporttypes:"TextSummary;Cobertura" -assemblyfilters:"+Lib.Db;-Lib.Db.IntegrationTests"
```

Expected:

```text
Writing report file 'TestResults\CoverageReport-v230-coverage-aot\Summary.txt'
```

- [ ] **Step 3: Run the automated coverage gate**

Run:

```powershell
$coverage = Get-ChildItem -Path TestResults\CoverletV230CoverageAot -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
pwsh -NoProfile -File Tools\coverage\Assert-LibDbCoverage.ps1 -CoberturaPath $coverage.FullName
```

Expected:

```text
CacheMaintenanceService coverage gate passed.
SchemaWarmupService coverage gate passed.
QueryCacheExtensions coverage gate passed.
GeneratedResultMapper<T> coverage gate passed.
ReflectionParameterMapper<T> coverage gate passed.
All Lib.Db v2.3.0 coverage gates passed.
```

- [ ] **Step 4: Inspect the coverage summary**

Run:

```powershell
Get-Content TestResults\CoverageReport-v230-coverage-aot\Summary.txt
```

Expected coverage gates:

```text
Lib.Db line coverage >= 80%
Lib.Db.Caching.CacheMaintenanceService line/branch/method coverage = 100%
Lib.Db.Hosting.SchemaWarmupService line/branch/method coverage = 100%
Lib.Db.Extensions.QueryCacheExtensions line/branch/method coverage = 100%
Lib.Db.Execution.Binding.GeneratedResultMapper<T> line/branch/method coverage = 100%
Lib.Db.Execution.Binding.ReflectionParameterMapper<T> line/branch/method coverage = 100%
TVP runtime core line/branch/method coverage = 100%
```

- [ ] **Step 5: Publish the Native AOT executable**

Run on a Windows machine with Visual Studio C++ build tools installed:

```powershell
dotnet publish Tests\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:TreatWarningsAsErrors=true --nologo 2>&1 | Tee-Object TestResults\AotVerificationPublish.log
```

Expected:

```text
Publish succeeded.
Lib.Db.AotVerification.exe
```

The publish output must not contain:

```text
IL2026
IL3050
IL2070
IL2072
IL2067
IL2065
IL2090
IL2091
```

- [ ] **Step 6: Run the published Native AOT executable**

Run:

```powershell
.\Tests\Lib.Db.AotVerification\bin\Release\net10.0\win-x64\publish\Lib.Db.AotVerification.exe
```

Expected:

```text
Lib.Db AOT verification passed.
```

Expected process result:

```text
ExitCode: 0
```

- [ ] **Step 7: Commit verification-related adjustments**

Only run this commit when the coverage and AOT gates above pass:

```powershell
git status --short
git add Lib.Db Tests Lib.Db.slnx
git commit -m "test: verify v230 coverage and aot gates"
```

---

### Task 8: Final Review Checklist

**Files:**
- Read: `docs/superpowers/specs/2026-05-18-v230-coverage-aot-design.md`
- Read: `docs/superpowers/plans/2026-05-18-v230-coverage-aot-implementation.md`
- Read: `TestResults/CoverageReport-v230-coverage-aot/Summary.txt`
- Read: `TestResults/AotVerificationPublish.log`

- [ ] **Step 1: Confirm the spec requirements are mapped**

Verify each acceptance criterion from the spec has evidence:

```text
Full integration test suite passes.
Coverlet collector generates Cobertura output.
Overall Lib.Db line coverage is at least 80%.
TVP runtime core remains line/branch/method 100%.
Named target areas reach line/branch/method 100%.
AOT verification project contains <PublishAot>true</PublishAot>.
AOT verification project roots Lib.Db with <TrimmerRootAssembly Include="Lib.Db" />.
AOT verification executable covers static TVP shape, GeneratedResultMapper<T>, and ReflectionParameterMapper<T>.
AOT verification executable publishes successfully.
AOT verification executable exits with code 0.
Lib.Db keeps <IsAotCompatible>true</IsAotCompatible>.
VSTest-compatible coverlet.collector path is preserved.
Automated coverage gate script passes.
No secret values are printed.
```

- [ ] **Step 2: Confirm the test platform guardrail**

Run:

```powershell
Select-String -Path Tests\Lib.Db.IntegrationTests\Lib.Db.IntegrationTests.csproj -Pattern "coverlet.collector|IsTestingPlatformApplication|TestingPlatformDotnetTestSupport"
```

Expected:

```text
coverlet.collector
<IsTestingPlatformApplication>false</IsTestingPlatformApplication>
<TestingPlatformDotnetTestSupport>false</TestingPlatformDotnetTestSupport>
```

- [ ] **Step 3: Confirm the AOT project property**

Run:

```powershell
Select-String -Path Tests\Lib.Db.AotVerification\Lib.Db.AotVerification.csproj -Pattern "PublishAot|WarningsAsErrors|TrimmerRootAssembly"
```

Expected:

```text
<PublishAot>true</PublishAot>
<WarningsAsErrors>$(WarningsAsErrors);IL2026;IL3050;IL2070;IL2072;IL2067;IL2065;IL2090;IL2091</WarningsAsErrors>
<TrimmerRootAssembly Include="Lib.Db" />
```

- [ ] **Step 4: Confirm AOT warning inventory is clean**

Run:

```powershell
Select-String -Path TestResults\AotVerificationPublish.log -Pattern "IL2026|IL3050|IL2070|IL2072|IL2067|IL2065|IL2090|IL2091"
```

Expected:

```text
No matches.
```

- [ ] **Step 5: Confirm no broad coverage exclusions were introduced**

Run:

```powershell
rg -n "ExcludeFromCodeCoverage" Lib.Db Tests
```

Expected:

```text
No new ExcludeFromCodeCoverage entries in the changed files.
```

- [ ] **Step 6: Final status command**

Run Git outside the sandbox per repository rules:

```powershell
git status --short
```

Expected:

```text
clean working tree after the final commit, or only intentionally uncommitted local artifacts under TestResults/bin/obj
```

## Subagent Ownership

- Worker 1 owns Task 1 and only edits `Lib.Db/Caching/CacheMaintenanceService.cs` plus `Tests/Lib.Db.IntegrationTests/Unit/CacheHostingCoverageTests.cs`.
- Worker 2 owns Tasks 2 and 3 and only edits `Tests/Lib.Db.IntegrationTests/Unit/SchemaWarmupServiceCoverageTests.cs` plus `Tests/Lib.Db.IntegrationTests/Unit/QueryCacheExtensionsCoverageTests.cs`.
- Worker 3 owns Task 4 and only edits `Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs`, `Lib.Db/Execution/Binding/Mappers.cs`, and `Tests/Lib.Db.IntegrationTests/Unit/MapperCoverageTests.cs`.
- Worker 4 owns Task 5 and only edits `Lib.Db/Lib.Db.csproj`, `Tests/Lib.Db.AotVerification/**`, and `Lib.Db.slnx`.
- Worker 5 owns Task 6 and only edits `Tests/Lib.Db.IntegrationTests/Lib.Db.IntegrationTests.csproj` plus `Tools/coverage/Assert-LibDbCoverage.ps1`.
- The parent agent owns Task 7 and Task 8 verification, resolves integration conflicts, and does the final summary.

## Implementation Notes

- The cache tick seam is deliberately not public API.
- `RuntimeFeatureSwitch` is an internal test seam for selecting the same branch that Native AOT takes naturally; the real AOT executable still proves the actual runtime mode.
- `default(DbResult<T>)` is the correct null-error failure value for `QueryCacheExtensions`; the public `Fail(DbError)` factory always supplies an error object.
- The AOT project validates static TVP shapes and mapper/parameter binding paths, while reflection-based TVP convenience APIs remain annotated as dynamic-code or unreferenced-code paths.
- `Tools/Lib.Db.AotSmoke` remains the optional DB-backed smoke project. `Tests/Lib.Db.AotVerification` remains the required no-DB release gate.
