# Schema Maintenance

Use this file for `db.Schema`, `UseSchema`, TVP descriptors, and schema cache flush APIs.

## API Shape

`IDbSession` exposes:

- `Schema`: maintenance stage for the default instance.
- `UseSchema(string instanceName)`: maintenance stage for a named instance.

`ISchemaMaintenanceStage` exposes:

- `GetTvpAsync(string tvpName, CancellationToken ct = default)`
- `FlushTvpAsync(string tvpName, CancellationToken ct = default)`
- `FlushSchemaAsync(CancellationToken ct = default)`

## Get A TVP Descriptor

```csharp
TvpSchemaDescriptor descriptor = await db.Schema.GetTvpAsync("dbo.OrderLineTvp", ct);
```

Use descriptors when TVP binding must adapt to runtime schema metadata. For TVP details, read `tvp-source-generation.md`.

## Flush One TVP

```csharp
await db.Schema.FlushTvpAsync("dbo.OrderLineTvp", ct);
```

## Flush Current Instance Schema

```csharp
await db.UseSchema("Default").FlushSchemaAsync(ct);
```

## Operational Guidance

- Treat schema maintenance as an operational capability, not a normal request-path read.
- Keep authorization separate from application query authorization.
- Do not expose schema flush endpoints without strong access control.
- If schema changes happen outside the app, coordinate deployment and cache flush timing.
- SQL Server setup involving computed-column indexes must use `SET QUOTED_IDENTIFIER ON`.

## Hosted Coordination

`AddLibDbHostedServices()` registers schema warmup services. `AddSchemaFlushCoordination(...)` registers epoch-based coordination and watcher services. Read `operations-integration.md`.
