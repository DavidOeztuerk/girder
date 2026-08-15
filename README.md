# Girder

A shared foundation for .NET microservices: a CQRS pipeline, security and
identity primitives, caching, messaging, health probes, resilience and
observability — built once, reused by every service.

Targets `net10.0`.

## Projects

| Project | Contents | References |
|---|---|---|
| `Girder.Core` | Entities, domain exceptions, identity primitives, compliance ports | — |
| `Girder.Contracts` | Boundary DTOs: paging, contract versioning | — |
| `Girder.Application` | Mediator, pipeline behaviours, base handlers, ports | `Core` |
| `Girder.Infrastructure` | Security, caching, messaging, health checks, telemetry | `Core`, `Application` |

Dependencies point inward, as Clean Architecture requires: infrastructure is the
outermost layer and implements the ports the application declares.

**`Girder.Core` and `Girder.Contracts` have no package references at all** — a
domain project can depend on either without inheriting a framework.

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

## Permissions

Girder ships no permissions of its own. An application declares which roles
grant what, and role inheritance comes from the same place:

```csharp
builder.Services.AddPermissionCatalog(c => c
    .Role("User", "profile:read_own")
    .Role("Admin", "users:view_all", "users:delete")
    .RoleInherits("Admin", "User"));

builder.Services.AddGirderAuthorization();   // after the catalogue
```

Every permission in the catalogue also becomes a policy of the same name, so
`[Authorize(Policy = "users:delete")]` works without declaring anything else.

An endpoint states its requirement with `[RequirePermission("users:delete")]`.
Applications that would rather keep the map in one place can register a policy
instead:

```csharp
builder.Services.AddEndpointAccessPolicy(p => p
    .Public("/health", "/swagger")
    .Require("users:view_all", "/admin/users", "GET")
    .Require("users:delete", "/admin/users", "DELETE"));
```

The attribute wins over the policy. `users:*` satisfies any permission in that
category, and `*` satisfies everything.

## Resources

Resource-based authorization needs to know which resource a request is about.
An application declares that once:

```csharp
builder.Services.AddResourceMap(m => m
    .RouteParameter("Job", "jobId")
    .PathSegment("Job", "jobs", "postings")
    .PathSegment("Company", "companies")
    .IdParameters("Job", "jobId"));
```

Resolution order is: an explicit `[ResourceAuthorize]`, then the route
parameters, then the path segments. **Ambiguity fails closed** — when two
distinct resource types can be inferred from one request, nothing is inferred
and the request is denied rather than guessed at.

Permissions per resource and action are registered with `IPermissionResolver`,
which likewise starts empty.

## Digital sovereignty

Girder is built so an application on top of it can run without depending on
infrastructure outside the operator's control. Outbound destinations are
declared and undeclared calls fail; the running configuration reports what it
points at; only secret stores you can run yourself ship in the box.

See [SOVEREIGNTY.md](SOVEREIGNTY.md) for what that covers, what it does not,
and which backends keep a deployment sovereign.

## Ownership model

Services own their persistence and their domain. Girder provides interfaces,
middleware and generic extension points constrained on `TContext : DbContext` —
it defines no `DbContext` itself and holds no domain types.

## Roadmap

- **Domain names outside the authorization path.** Authorization no longer
  contains any, but a sweep of the whole library found more elsewhere. None of
  it affects correctness for a different domain — the names are defaults,
  metric names and comments — but they do not belong in a reusable library:
  - `Girder.Cqrs/Models/SkillSummary.cs` — a domain record in the CQRS package
  - `Girder.Cqrs/Behaviors/CacheInvalidationBehavior.cs` — maps cache patterns
    containing `skill`, `appointment` or `matchmaking` to hardcoded API paths,
    citing another project's `ocelot.json`
  - `Observability/PerformanceMetrics.cs`, `Observability/TelemetryExtensions.cs`
    — counters named `girder.skills.managed`, `girder.appointments.scheduled`
  - `Extensions/ServiceCollectionExtensions.cs` — assembly name to service name
    for `SkillService`, `MatchmakingService`, `AppointmentService`,
    `VideocallService`, plus hardcoded hub paths
  - `Communication/ServiceCommunicationManager.cs` — default URLs for those same
    services
  - `Caching/Http/CachePolicyProvider.cs`,
    `Security/Headers/SecurityHeadersMiddleware.cs` — path lists naming
    `/videocall` and `/calls`
  - `Security/Authorization/ResourceAuthorizationService.cs` — condition strings
    such as `"user is participant in appointment"`
- **Two `RequirePermissionAttribute` types.** One in
  `Girder.Infrastructure.Middleware` drives the middleware; one in
  `Girder.Infrastructure.Authorization` drives the policy provider. They should
  be a single attribute.
- **Two `CacheStatistics` types.** One in `Girder.Application.Abstractions`, one
  in `Girder.Infrastructure.Communication.Caching`. Different shapes, same name.
- **German error messages.** `Girder.Core/Exceptions/ErrorMessageService.cs`
  returns user-facing text in German. It should either be neutral or come from
  a resource file.
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
