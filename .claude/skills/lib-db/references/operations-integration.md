# Operations Integration

Use this file for health checks, hosted services, schema flush coordination, interceptors, and host-level integration.

## Health Checks

```csharp
builder.Services
    .AddHealthChecks()
    .AddLibDbHealthCheck("sql_db", "db", "ready");
```

Options:

- `HealthCheckThrottleSeconds`: minimum interval between actual DB checks.
- `HealthCheckTimeoutSeconds`: timeout for the health query.

The health check performs a lightweight DB check against the configured default instance.

## Hosted Services

```csharp
builder.Services.AddLibDbHostedServices();
```

This registers Lib.Db hosted services such as schema warmup. `AddHighPerformanceDb(...)` already calls it.

## Schema Flush Coordination

```csharp
builder.Services.AddSchemaFlushCoordination();
```

Optional base path:

```csharp
builder.Services.AddSchemaFlushCoordination(epochBasePath: configuredEpochPath);
```

Use this when multiple app processes need coordinated schema cache invalidation.

## Interceptors

Register:

```csharp
builder.Services.AddLibDbInterceptor<AuditInterceptor>();
```

Implement:

```csharp
public sealed class AuditInterceptor : IDbInterceptor
{
    public ValueTask<DbInterceptionResult> OnExecutingAsync(
        DbInterceptionContext context,
        CancellationToken ct)
    {
        return ValueTask.FromResult(DbInterceptionResult.Continue);
    }

    public ValueTask OnExecutedAsync(DbInterceptionContext context, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask OnErrorAsync(DbInterceptionContext context, CancellationToken ct)
        => ValueTask.CompletedTask;
}
```

Use `DiagnosticCommandText` for logging. Avoid `CommandText` when it may contain raw SQL.

`DbInterceptionResult.Suppress` skips DB execution; use only for deliberate infrastructure behavior.

## Host Hook

```csharp
IHost host = builder.Build();
host.UseHighPerformanceDb();
await host.RunAsync();
```

`UseHighPerformanceDb()` bridges legacy generated TVP accessor validation into DI. It uses reflection and is not Native AOT friendly. New AOT-sensitive TVP paths should prefer static runtime TVP shapes. Read `aot-trimming.md`.
