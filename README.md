# Lib.Db

Lib.Db is a SQL Server data access library for .NET applications that prefer stored procedures, typed results, conservative SQL defaults, and explicit runtime configuration.

The README is the current-user entry point. Release history, verification details, and security notes live in the linked docs so this page stays short enough to scan during implementation.

[![NuGet](https://img.shields.io/nuget/v/Lib.Db.svg)](https://www.nuget.org/packages/Lib.Db)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/wotjr1649/Lib.Db/blob/main/LICENSE)

## Install

Install the `Lib.Db` package through your normal NuGet workflow.

```xml
<ItemGroup>
  <PackageReference Include="Lib.Db" Version="x.y.z" />
</ItemGroup>
```

Pin the package version in the application or through central package management. Do not commit real connection strings, tokens, or credentials. Use redacted placeholders in samples.

## Quick Start

### Configure

Keep connection string values in user secrets, environment variables, deployment configuration, or another untracked local source.

```json
{
  "ConnectionStrings": {
    "Default": "<provided outside source control>"
  },
  "LibDb": {
    "ConnectionStringNames": ["Default"],
    "ConnectionSecurityProfile": "Production",
    "RawSqlPolicy": "DenyWriteText",
    "Mars": "ForceEnable",
    "EnableSchemaCaching": true,
    "EnableResilience": true,
    "EnableObservability": false
  }
}
```

### Register

Register Lib.Db once during application startup.

```csharp
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLibDb(builder.Configuration);

IHost app = builder.Build();
await app.RunAsync();
```

### Query

Use `IDbSession` to select a configured database, call a stored procedure, pass parameters, and read a `DbResult<T>`.

```csharp
using Lib.Db.Contracts.Core;
using Lib.Db.Contracts.Entry;

public sealed class UserRepository(IDbSession session)
{
    public sealed record User(int Id, string Name, string Email);

    public async Task<User?> GetUserAsync(int id, CancellationToken ct)
    {
        DbResult<User?> result = await session.Default
            .Procedure("dbo.usp_GetUser")
            .With(new { Id = id })
            .QuerySingleAsync<User>(ct);

        return result.IsSuccess ? result.Value : null;
    }

    public async Task<int> RegisterAsync(string name, string email, CancellationToken ct)
    {
        DbResult<int?> result = await session.Default
            .Procedure("dbo.usp_RegisterUser")
            .With(new { Name = name, Email = email })
            .ExecuteScalarAsync<int>(ct);

        return result.IsSuccess ? result.Value ?? 0 : -1;
    }
}
```

## Runtime TVP

Runtime TVP support lets a stored procedure receive scalar parameters and table-valued parameters in the same parameter object.

```csharp
DbResult<int> result = await session.Default
    .Procedure("dbo.usp_UpsertProducts")
    .With(new
    {
        RequestedBy = userId,
        Products = LibDb.Tvp("dbo.T_Product", products)
    })
    .ExecuteAsync(ct);
```

The SQL principal that executes a TVP procedure needs `EXECUTE` permission on the routine and `REFERENCES` permission on the user-defined table type, its schema, or its database.

For repeated calls or Native AOT targets, register a static shape at startup.

```csharp
using System.Data;

builder.Services.AddLibDb(options =>
{
    options.Tvp.Map<ProductRow>("dbo.T_Product")
        .Column("ProductId", SqlDbType.Int, static row => row.ProductId)
        .Column("Name", SqlDbType.NVarChar, static row => row.Name, size: 100)
        .Column("Price", SqlDbType.Decimal, static row => row.Price, precision: 18, scale: 2);
});
```

See [Configuration](https://github.com/wotjr1649/Lib.Db/blob/main/docs/02_configuration.md) for setup-oriented options and [Advanced Features](https://github.com/wotjr1649/Lib.Db/blob/main/docs/02_advanced.md) for schema-adaptive descriptors, cache flush behavior, AOT guidance, and TVP versus bulk insert tradeoffs.

## Key Features

| Feature | Summary |
|---|---|
| Fluent stored procedure API | Stage-based call flow for procedure, parameters, and execution. |
| `DbResult<T>` | Immutable success/failure result type without forcing exception-driven control flow. |
| Multiple databases | `session.Default` and `session.Use("Name")` route calls through configured connection names. |
| Transactions | Async transaction helpers support commit, rollback, and automatic rollback on failure paths. |
| Runtime TVP | `LibDb.Tvp(...)` and `options.Tvp.Map<T>()` support explicit TVP binding in the runtime package. |
| Native AOT path | Static TVP shapes keep hot paths explicit and reduce reflection dependency. |
| Resilience | Retry, circuit breaker, and deadlock handling are configurable through application options. |
| Schema caching | Schema metadata can be cached and warmed up for stored procedure-heavy services. |
| Observability | Activity and metric hooks support OpenTelemetry-oriented monitoring. |
| Raw SQL guardrails | Raw text execution can be restricted by policy for operational safety. |
| Connection security profile | Production defaults enforce conservative SQL Server connection behavior. |
| SQL name mapping | Conservative column-to-property mapping supports common SQL naming conventions. |

## Documentation

- [Guide](https://github.com/wotjr1649/Lib.Db/blob/main/docs/01_guide.md) introduces setup, configuration, and day-to-day usage.
- [Configuration](https://github.com/wotjr1649/Lib.Db/blob/main/docs/02_configuration.md) covers stable setup and option entry points.
- [Advanced Features](https://github.com/wotjr1649/Lib.Db/blob/main/docs/02_advanced.md) covers Runtime TVP, AOT, resilience, caching, JSON mapping, bulk insert, and pool metrics.
- [API Reference](https://github.com/wotjr1649/Lib.Db/blob/main/docs/03_api_reference.md) lists the public API surface and extension points.
- [Operations](https://github.com/wotjr1649/Lib.Db/blob/main/docs/04_operations.md) covers production configuration and operational guardrails.
- [Fluent API Reference](https://github.com/wotjr1649/Lib.Db/blob/main/docs/05_fluent_api_reference.md) documents the staged procedure call API.
- [Cookbook](https://github.com/wotjr1649/Lib.Db/blob/main/docs/06_cookbook.md) provides task-focused examples.
- [Verification](https://github.com/wotjr1649/Lib.Db/blob/main/docs/verification.md) explains maintainer-only release validation and generated artifact policy.
- [History](https://github.com/wotjr1649/Lib.Db/blob/main/docs/history.md) contains release notes and migration history.

## License

[MIT License](https://github.com/wotjr1649/Lib.Db/blob/main/LICENSE)
