# Options And Registration

Use this file for dependency injection, `LibDbOptions`, option binding, and service registration.

## Namespaces

```csharp
using Lib.Db.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Some extensions live in other namespaces:

```csharp
using Lib.Db.Extensions;          // cache and JSON helpers
using Microsoft.Extensions.Hosting; // UseHighPerformanceDb
```

## Main Registration APIs

| API | Use |
| --- | --- |
| `services.AddLibDb(IConfiguration)` | Convenience registration from app configuration. Avoid for Native AOT-sensitive code. |
| `services.AddLibDb(Action<LibDbOptions>)` | Preferred explicit registration. |
| `services.AddHighPerformanceDb(Action<LibDbOptions>)` | Full registration behind `AddLibDb(Action<LibDbOptions>)`. |
| `services.RegisterLibDbCoreServices()` | Advanced modular registration; configure options separately. |
| `services.AddLibDbOptions(...)` | Register only options. |
| `services.AddLibDbOptionsFromConfiguration(...)` | Manual configuration binding helper. |
| `services.AddLibDbResilience()` | Register resilience pipeline pieces. |
| `services.AddLibDbHostedServices()` | Register Lib.Db hosted services. |
| `services.AddSchemaFlushCoordination(...)` | Register epoch-based schema flush coordination. |

## `LibDbOptions` High-Value Properties

- `ConnectionStrings`: key to connection string dictionary.
- `ConnectionStringNames`: names Lib.Db may use; first entry is the default.
- `Mars`: `Disabled`, `Auto`, or `ForceEnable` for `QueryMultipleAsync()`.
- `ConnectionSecurityProfile`: `Development` or `Production`.
- `AllowProductionTrustServerCertificateWaiver`, `AllowProductionSaLoginWaiver`: exceptional waivers; avoid by default.
- `RawSqlPolicy`: `Allow`, `DenyAllText`, or `DenyWriteText`.
- `StrictRequiredParameterCheck`: pre-call stored procedure required parameter check.
- `DefaultCommandTimeoutSeconds`, `BulkCommandTimeoutSeconds`, `BulkBatchSize`.
- `TvpValidationMode`, `EnableGeneratedTvpBinder`, `Tvp`.
- `EnableSchemaCaching`, `PrewarmSchemas`, `PrewarmIncludePatterns`, `PrewarmExcludePatterns`, `PrewarmMaxConcurrency`.
- `EnableResilience`, `Resilience`.
- `EnableSharedMemoryCache`, `EnableEpochCoordination`, `SharedMemoryCache`.
- `EnableObservability`, `IncludeParametersInTrace`.
- `HealthCheckThrottleSeconds`, `HealthCheckTimeoutSeconds`.
- `JsonOptions`.

## Explicit Production-Oriented Registration

```csharp
builder.Services.AddLibDb(options =>
{
    options.ConnectionStringNames = new[] { "Default" };
    options.ConnectionStrings["Default"] =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");

    options.UseProductionSecurityDefaults();
    options.RawSqlPolicy = RawSqlPolicy.DenyWriteText;
    options.DefaultCommandTimeoutSeconds = 30;
    options.EnableObservability = true;
});
```

Do not put full connection strings in examples. Use the application's configuration provider.

## Configuration Registration

```csharp
builder.Services.AddLibDb(builder.Configuration);
```

This reads the `LibDb` section and only copies top-level `ConnectionStrings` entries listed by `ConnectionStringNames`.

For Native AOT-sensitive code, prefer the explicit `Action<LibDbOptions>` overload or read `aot-trimming.md`.

## OptionsBuilder Pattern

```csharp
builder.Services
    .AddLibDbOptions(options =>
    {
        options.ConnectionStringNames = new[] { "Default" };
        options.ConnectionStrings["Default"] =
            builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string key 'Default' is missing.");
        options.UseProductionSecurityDefaults();
    })
    .WithValidation(o => o.ConnectionStringNames.Count > 0, "At least one DB instance is required.")
    .WithPostConfigure(o => o.EnableObservability = true);

builder.Services.RegisterLibDbCoreServices();
builder.Services.AddLibDbResilience();
builder.Services.AddLibDbHostedServices();
```

Use modular registration only when the application needs advanced control over service composition.
