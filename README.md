# Girder

A shared foundation for .NET microservices: a CQRS pipeline, security and
identity primitives, caching, messaging, health probes, resilience and
observability — built once, reused by every service.

Targets `net10.0`.

## Packages

Girder is split so that a service takes only what it runs. The engine names no
driver: choosing where data lives is the operator's decision, and it is made in
the application's composition root.

| Package | Contents | Provider dependency |
|---|---|---|
| `Girder.Core` | Entities, domain exceptions, identity primitives, compliance ports | none |
| `Girder.Contracts` | Boundary DTOs: paging, contract versioning | none |
| `Girder.Abstractions` | **Every port**, plus `IGirderBuilder` | none |
| `Girder.Application` | Mediator, pipeline behaviours, base handlers | none |
| `Girder.Infrastructure` | Middleware, builder, telemetry, resilience, headers, input sanitisation | none |
| `Girder.Redis` | Cache, rate counters, secrets, keys, audit trail, resource permissions | StackExchange.Redis |
| `Girder.InMemory` | The same ports, in process | none |
| `Girder.Messaging.MassTransit` | Event bus, correlation filters, broker health | MassTransit 8, RabbitMQ |
| `Girder.Data.EntityFrameworkCore` | Id converters, tenant filters, readiness probe, exception mapping | EF Core (no database provider) |

Dependencies point inward, as Clean Architecture requires. `Girder.Core`,
`Girder.Contracts` and `Girder.Abstractions` are held to that by a build
target — adding an infrastructure package to any of them fails the build with
`GIRDER0001`.

The reasoning is in [ADR-0001](docs/adr/0001-souveraenitaet-durch-portschnitt.md);
what sovereignty means in practice is in [SOVEREIGNTY.md](SOVEREIGNTY.md).

## Getting started

```bash
dotnet restore Girder.slnx
dotnet build   Girder.slnx
dotnet test    Girder.slnx
```

Package versions are managed centrally in `Directory.Packages.props`.

### A minimal service

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, "identity-service");

var app = builder.Build();

app.UseSharedInfrastructure(builder.Environment, "identity-service", pipeline => pipeline
    .UseExceptionHandling()
    .UseCorrelationId()
    .UseSecurityHeaders()
    .UseCors()
    .UseAuth()
    .UseHealthCheckEndpoints());

app.Run();
```

`AddSharedInfrastructure` registers the engine. It registers **no** cache, no
secret store, no audit sink and no message bus — those come from provider
packages, so that adding Girder never adds a server you have to run.

### Choosing providers

```csharp
// Redis, Valkey, Garnet or KeyDB — the same wire protocol
builder.Services
    .AddRedisConnection(connectionString, instanceName: "identity")
    .AddRedisCache("identity")
    .AddRedisSecretManager(builder.Configuration, builder.Environment)
    .AddRedisSecurityAudit()
    .AddRedisResourceAuthorization()
    .AddRedisRateLimiting()
    .AddRedisEncryption();

// or, for a single instance and for tests
builder.Services
    .AddInMemoryCache("identity")
    .AddInMemorySecretManager()
    .AddInMemorySecurityAudit()
    .AddInMemoryResourceAuthorization()
    .AddInMemoryRateLimiting();
```

Every in-memory registration documents what it costs: state is invisible to
other instances, so a rate limit counts per process and an audit trail does not
survive a restart. That is sound for tests and a single replica, and stated
rather than implied.

### Selective wiring

`AddSharedInfrastructure` has a second form for services that want only part of
the engine:

```csharp
builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, "jobs-service", infra => infra
        .AddJwtAuthentication()
        .AddAuthorization()
        .AddSecurityHeaders()
        .AddObservability()
        .AddHealthChecks());
```

Available modules: `AddJwtAuthentication`, `AddAuthorization`,
`AddResourceAuthorization`, `AddSecretManagement`, `AddEncryption`,
`AddAuditLogging`, `AddSecurityMonitoring`, `AddSecurityHeaders`,
`AddInputSanitization`, `AddDistributedRateLimiting`, `AddCaching`,
`AddCommunication`, `AddResilience`, `AddHealthChecks`, `AddObservability`.

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

### Conditional permissions

A permission may depend on the resource itself — "the author may edit". What
such a condition *means* is the application's to say:

```csharp
builder.Services.AddPermissionConditions(c => c
    .Condition("user is the author", ctx =>
        ctx.ResourceData is Posting p && p.AuthorId == ctx.UserId));
```

An undeclared condition denies and logs. Girder has no vocabulary of its own
here, and inventing one would mean granting access on a sentence nobody wrote.

## Token revocation

A JWT is valid until it expires; there is nothing to delete. A revocation list
is what makes "sign out" take effect before then.

```csharp
builder.Services.AddRedisConnection(connectionString, "identity");
// the store implements both the read and the write side
app.UseTokenRevocation();       // after UseAuthentication()
```

`UseTokenRevocation()` throws at startup when no evaluator is registered. There
is no silent default: a revocation check that always answers "not revoked" is
indistinguishable from one that works, and the difference only shows when
someone needs a token to stop working. Turning it off is explicit and requires
a stated reason:

```csharp
builder.Services.AddNoTokenRevocation(
    "access tokens live 15 minutes; revocation happens at the refresh path");
```

Three things can revoke a token:

```csharp
await writer.RevokeTokenAsync(jti, expiresAt, "user requested");
await writer.RevokeSessionAsync(subjectId, sessionId, expiresAt, "device lost");
await writer.RevokeSubjectBeforeAsync(subjectId, DateTimeOffset.UtcNow, "password changed");
```

`RevokeSubjectBeforeAsync` is the one that scales: it refuses every token
issued before a cutoff, including tokens the store has never seen. It is
idempotent and monotonic — two concurrent revocations cannot undo one another.

**A cutoff alone closes only the access-token window.** Whatever issues refresh
tokens has to revoke those in the same operation, or the holder simply
refreshes into a new access token issued after the cutoff.

### Behaviour during an outage

Neither answer is right on its own: refusing everything turns a brief store
outage into a full sign-out; honouring everything lifts every revocation for
as long as it lasts. The decorator keeps the last known verdict for a stated
span, then closes:

```csharp
new DegradingTokenRevocationEvaluator(inner, new RevocationDegradationOptions
{
    MaxStaleness = TimeSpan.FromSeconds(30),
    OnUnknown    = UnknownStatePolicy.Deny,
    Budget       = TimeSpan.FromMilliseconds(50)
}, logger);
```

A stale verdict is marked as such in `RevocationVerdict.IsStale` and logged —
a system running on stale revocation data works, but not as well as it looks.

### Sharing the store between languages

`TokenRevocationKeys` fixes the key layout so services written in different
languages can share one store: prefix `girder:revocation:`, cutoffs as Unix
**seconds** in decimal ASCII, compared strictly less-than. During a migration a
Python service may write the cutoff that a .NET service reads, and
`1755264720` and `1755264720.0` are not the same string.

## Rate limiting

```csharp
builder.Services.AddDistributedRateLimiting(builder.Configuration);
builder.Services.AddRedisCache("identity");     // brings the shared counter

app.UseDistributedRateLimiting();
```

The store is a separate choice from the rules, because it decides how much
traffic actually gets through: with a shared counter all instances draw from
one budget, in process each counts for itself, so the effective limit is
multiplied by the replica count.

`IDistributedRateLimitStore.SlidingWindowIncrementAsync` counts and decides in
one indivisible step. Implementations that cannot guarantee that are not valid
implementations of the port — a conformance suite asserts it with fifty
concurrent callers racing for ten slots.

## Messaging

```csharp
builder.Services.AddMessaging(builder.Configuration, typeof(Program).Assembly);
```

`IEventBus` has one method and is fire-and-forget by design. It promises that
the transport accepted the event — **not** a transactional outbox, and not that
the event survives a crash between the database commit and the publish.

Where that guarantee is needed it belongs to the service that owns the
transaction: it writes the intent in the same transaction as the domain change
and publishes from there. A delivery guarantee that appears or disappears
depending on the configured transport would be worse than none, because callers
would rely on it.

## Persistence

```csharp
builder.Services.AddDatabaseContext<AppDbContext>(
    builder.Configuration, "identity",
    (options, connectionString) => options.UseNpgsql(connectionString));

builder.Services.AddEntityFrameworkExceptionMapping();
```

Girder resolves *where* — `ConnectionStrings__identity` from the environment,
then `ConnectionStrings:identity`, then `ConnectionStrings:DefaultConnection`,
and it throws when none is set. The application binds the provider, because the
provider package is the application's dependency.

`AddEntityFrameworkExceptionMapping()` teaches the exception handler about EF's
persistence failures. Without it a concurrency conflict surfaces as a plain 500
instead of a 409.

## Health checks

```csharp
builder.Services.AddGirderHealthChecks();

app.UseHealthCheckEndpoints();   // /health, /health/live, /health/ready
```

`/health/live` answers 200 even when a dependency is down: a liveness probe
tied to the database makes the orchestrator restart the process because the
*database* is gone, which lengthens the outage instead of ending it.
`/health/ready` answers 503, because a pod that cannot reach its store should
not take traffic.

Provider packages contribute their own checks; the aggregator names no driver.

## Telemetry

```csharp
builder.Services.AddTelemetry("identity-service", "1.0.0", t => t
    .AddTracing(tracing => tracing.AddGirderEntityFrameworkInstrumentation())
    .AddMetrics()
    .AddLogging());
```

Girder emits **OTLP** and nothing else — the neutral protocol, aimed at a
self-hostable OpenTelemetry Collector that fans out to whatever you run. A
backend-specific exporter is the application's choice and goes through the
`configure` callback.

`AddGirderEntityFrameworkInstrumentation(captureStatements: false)` is off by
default: a SQL statement carries table names and parameter values, and spans
travel to wherever telemetry is collected.

### Logging

```csharp
LoggingConfiguration.ConfigureSerilog(configuration, environment, "identity-service");
```

Enrichment, filtering and exception shaping are Girder's. **Where the logs go is
the application's**: declare `Serilog:WriteTo` in configuration and install the
sink package alongside naming it. Declaring even one sink replaces the built-in
console and file sinks completely, so list every destination you want.

## Digital sovereignty

Outbound destinations are declared, and undeclared calls fail:

```csharp
builder.Services.AddGirderEgressPolicy(p => p
    .Allow("openbao.internal")
    .AllowSubdomainsOf("example.eu")
    .AllowLoopback());
```

With nothing declared everything is allowed — adding the package must not
change behaviour on its own. Once anything is declared, the policy is
enforcing.

```csharp
builder.Services.AddGirderSovereigntyReport();
```

The report reads the running configuration and says what it points at,
classifying each host as self-hosted, third-country provider, or undetermined.
A region in Frankfurt does not change who operates a service or which law
reaches it, so `*.amazonaws.com` is a third-country provider whatever the
endpoint says. Not matching the list is reported as *undetermined*, never as
sovereign — the difference between a report and a rubber stamp.

Credentials never reach the report: it is something people paste into tickets.

## Secrets and keys

```csharp
builder.Services.AddSecretManagement(builder.Configuration, builder.Environment);
builder.Services.AddRedisSecretManager(builder.Configuration, builder.Environment);
```

Girder never generates a master key — a key it invented would be a key nobody
chose to trust. Supply one:

```csharp
builder.Services.AddConfiguredMasterKey();    // from Encryption:MasterKey
builder.Services.AddSecretStoreMasterKey();   // from an ISecretProvider
```

`ISecretProvider` is the sovereignty seam: OpenBao (MPL-2.0, Linux Foundation)
rather than Vault (BSL since 2023).

## Testing against the contracts

Ports carry promises their interfaces cannot express — that a revocation takes
effect on the next check, that counting and deciding are indivisible, that a
cutoff never moves backwards. Those live in conformance suites that every
implementation inherits:

```csharp
public class MyStoreConformanceTests : TokenRevocationEvaluatorConformance
{
    protected override (ITokenRevocationEvaluator, ITokenRevocationWriter) CreateStore()
        => /* your implementation */;
}
```

The Redis suites run against a real server in a container and **fail** rather
than skip when no container runtime is reachable: a silently skipped
integration suite is indistinguishable from a passing one.

## Roadmap

- **Three ways to require a permission.** `PermissionMiddleware`,
  `PermissionPolicyProvider` and the per-permission policies
  `AddGirderAuthorization` registers all answer the same question, and not
  alike: the first two consult the role catalogue, the third only the
  `permission` claim, so a user holding a role but no explicit claim is allowed
  by two of them and refused by the third. One attribute now drives the first
  two; the third has no attribute and is reachable only as
  `[Authorize(Policy = "users:read")]`. Which one survives is still open.
- **Duplicate type names.** Three areas carry two or three types of the same
  name, which the package split made visible: `CacheStatistics`
  (`Girder.Abstractions.Caching` and `Girder.Infrastructure.Communication.Caching`),
  `RateLimitResult` (`Girder.Abstractions.Security.RateLimiting` and
  `Girder.Infrastructure.Models`), and a whole second audit system —
  `ISecurityAuditLogger` with its own `SecurityAuditEvent` and
  `SecurityEventSeverity` beside `ISecurityAuditService`. Where they collide
  the code now names them in full; merging them is separate work.
- **German error messages.** `Girder.Core/Exceptions/ErrorMessageService.cs`
  returns user-facing text in German. It should either be neutral or come from
  a resource file.
- **Key derivation.** `Security/Secrets/Providers/FileBasedProvider.cs` derives
  keys with a constant salt and 10,000 PBKDF2 iterations. Both need to change:
  a per-installation salt and a current iteration count.
- **`ISecretProvider` implementations still sit in `Girder.Infrastructure`.**
  The port moved to `Girder.Abstractions`, the OpenBao and file-based
  implementations did not. They belong in `Girder.Secrets.*` packages.
- **Duplicate `IDomainEvent`.** One lives in `Girder.Cqrs.Interfaces`, a second
  in `Girder.Infrastructure.Caching`, because of the layering above.

## Package licensing

MediatR, MassTransit and FluentAssertions are pinned to their last Apache-2.0
releases. Later versions are commercially licensed, so upgrading them is a
licensing decision rather than a routine version bump. See
`Directory.Packages.props`.

Four OpenTelemetry contrib instrumentation packages have no stable release and
are pinned to prereleases.
