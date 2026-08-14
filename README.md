# Girder

A shared foundation for .NET microservices: a CQRS pipeline, security and
identity primitives, caching, messaging, health probes, resilience and
observability — built once, reused by every service.

Targets `net10.0`.

## Projects

| Project | Contents |
|---|---|
| `Girder.Core` | Entities, exceptions, log sanitising, identity primitives, compliance ports |
| `Girder.Contracts` | Boundary DTOs: paging, error payloads, contract versioning |
| `Girder.Cqrs` | Mediator, pipeline behaviours, base handlers |
| `Girder.Infrastructure` | Security, caching, messaging, health checks, telemetry, resilience |

`Girder.Core` has no ASP.NET dependency and `Girder.Contracts` has no package
references at all, so both can be referenced from a domain layer.

## Getting started

```bash
dotnet restore Girder.slnx
dotnet build   Girder.slnx
dotnet test    Girder.slnx
```

Package versions are managed centrally in `Directory.Packages.props`.

## Identity and multi-tenancy

A principal has two independent axes, and neither is nullable:

```csharp
Principal.Subject   // SubjectId — who is acting
Principal.Acting    // Capacity  — in what capacity
```

`Capacity` is a closed hierarchy with exactly two cases, so matches over it are
exhaustive:

```csharp
if (principal.Acting is not Capacity.ForCompany company)
    return TypedResults.Problem(statusCode: 403);

var jobs = db.Jobs.Where(j => j.Tenant == company.Tenant);
```

`Capacity.ForCompany` carries no role: a token states which company a caller
acts for, never with what rights. Roles are read from the membership store per
operation.

### Wiring

```csharp
builder.Services.AddGirderIdentity();

app.UseAuthentication();
app.UseGirderPrincipal();   // translates the token once
app.UseAuthorization();
```

The middleware answers 401 when a token's claims cannot be translated, rather
than letting the request continue without a principal. Downstream code reads
`ICurrentPrincipal` instead of inspecting claims again.

Endpoints declare the capacity they need:

```csharp
[Authorize(Policy = GirderPolicies.ActingForCompany)]
[Authorize(Policy = GirderPolicies.ActingAsSelf)]
```

Issue a company token only after verifying membership:

```csharp
var claims = new UserClaims { /* ... */ };
claims.Acting = new Capacity.ForCompany(tenant);
```

### Query filtering

Multi-tenancy is opt-in **per entity**. An entity implementing `ITenantOwned`
gets a tenant query filter; everything else is untouched. Entities keyed by
`SubjectId` — profile, résumé, consent — deliberately do not implement it, so
they follow the person rather than the company.

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    => builder.AddGirderIdConverters();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Job>().HasQueryFilter("SoftDeletion", j => !j.IsDeleted);
    modelBuilder.ApplyTenantFilters(() => CurrentTenant);
}

private TenantId CurrentTenant => _principal.Current?.TenantForQueryFilter ?? TenantId.None;
```

The filter is named, so it coexists with others on the same entity and can be
lifted on its own with `IgnoreQueryFilters(["GirderTenant"])`.

Girder supplies the primitives and the model-builder extension. Each service
owns its own `DbContext`, as it does for the outbox and the processed-event
store.

## Ownership model

Services own their persistence and their domain. Girder provides interfaces,
middleware and generic extension points constrained on `TContext : DbContext` —
it defines no `DbContext` itself and holds no domain types.

## Roadmap

- **Permission catalogue.** `Security/Permissions.cs`, `RolePermissions.cs`,
  `Roles.cs` and `Authorization/IPermissionResolver.cs` still ship a fixed set of
  permissions. These will be replaced by an `IPermissionCatalog` supplied by the
  application, with an empty default.
- **Path-to-resource mapping.** `Middleware/PermissionMiddleware.cs` and
  `Security/Authorization/AuthorizationExtensions.cs` map request paths to
  resource types in `switch` blocks. These will move behind an
  `IResourceResolver`.
- **Layering.** `Girder.Cqrs` references `Girder.Infrastructure` for the caching
  ports. Moving those ports into `Girder.Core` removes the cycle in direction.
- **Package split.** `Girder.Infrastructure` is a single assembly pulling in
  RabbitMQ, Redis, PostgreSQL, Elasticsearch and OpenTelemetry. Planned split:
  `Girder.Caching.Redis`, `Girder.Messaging.MassTransit`,
  `Girder.Data.EntityFrameworkCore`, `Girder.Observability`.
- **Key derivation.** `Security/Secrets/Providers/FileBasedProvider.cs` derives
  keys with a constant salt and 10,000 PBKDF2 iterations. Both need to change:
  a per-installation salt and a current iteration count.
- **Secret providers.** `AzureKeyVaultProvider` and `AwsSecretsManagerProvider`
  reference no cloud SDK and keep secrets in an in-memory dictionary. They must
  either be implemented against the real services or removed.
- **Duplicate `IDomainEvent`.** One lives in `Girder.Cqrs.Interfaces`, a second
  in `Girder.Infrastructure.Caching`, because of the layering above.

## Package licensing

MediatR, MassTransit and FluentAssertions are pinned to their last Apache-2.0
releases. Later versions are commercially licensed, so upgrading them is a
licensing decision rather than a routine version bump. See
`Directory.Packages.props`.

Four OpenTelemetry contrib instrumentation packages have no stable release and
are pinned to prereleases.
