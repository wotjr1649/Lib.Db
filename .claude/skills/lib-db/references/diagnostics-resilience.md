# Diagnostics And Resilience

Use this file for observability, parameter trace safety, resilience options, dry run, and chaos options.

## Observability

Enable:

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
    options.UseProductionSecurityDefaults();
    options.EnableObservability = true;
});
```

Keep `IncludeParametersInTrace = false` unless the user explicitly needs a controlled diagnostics environment and the data classification allows it.

## Resilience

Enable library resilience:

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
    options.UseProductionSecurityDefaults();
    options.EnableResilience = true;
    options.Resilience.MaxRetryCount = 3;
    options.Resilience.BaseRetryDelayMs = 100;
    options.Resilience.MaxRetryDelayMs = 2_000;
});
```

`Resilience` also exposes circuit-breaker settings:

- `CircuitBreakerThreshold`
- `CircuitBreakerSamplingDurationMs`
- `CircuitBreakerBreakDurationMs`
- `CircuitBreakerFailureRatio`
- `UseRetryJitter`
- `RetryBackoffType`

## Application Retry Decisions

Use `DbResult<T>.Error?.IsTransient` and `DbErrorKind` when deciding whether the application should retry, compensate, or surface a failure.

## Dry Run

`EnableDryRun` simulates data-changing work at the library layer. Treat it as an application behavior switch, not a permission boundary.

## Chaos Options

`Chaos.Enabled`, `ExceptionRate`, `LatencyRate`, `MinLatencyMs`, and `MaxLatencyMs` inject failures or latency for controlled resilience exercises. Do not enable in production examples.

## Logging

Log:

- `DbErrorKind`
- `SqlErrorCode`
- `IsTransient`
- safe object names
- correlation IDs from the application

Do not log:

- passwords
- full connection strings
- secret parameters
- raw SQL containing sensitive literals
