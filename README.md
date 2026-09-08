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
| `Girder.Infrastructure` | Middleware, builder, telemetry, resilience, headers, input sanitisation, sessions, password hashing | none |
| `Girder.Redis` | Cache, rate counters, secrets, keys, audit trail, resource permissions | StackExchange.Redis |
| `Girder.InMemory` | The same ports, in process | none |
| `Girder.Passwords.BCrypt` | bcrypt, to write or to read what a system already has | BCrypt.Net-Next |
| `Girder.Passwords.Argon2` | Argon2id, where custom hardware is part of the threat | Konscious |
| `Girder.Messaging.MassTransit` | Event bus, correlation filters, broker health | MassTransit 8, RabbitMQ |
| `Girder.Data.EntityFrameworkCore` | Id converters, tenant filters, readiness probe, exception mapping, **refresh token store** | EF Core (no database provider) |

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

Fifteen lines, and a service that runs:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGirder(
    builder.Configuration, builder.Environment, "identity-service",
    girder => girder.UseDefaults());

var app = builder.Build();

app.UseGirder(builder.Environment, "identity-service", pipeline => pipeline
    .UseExceptionHandling()
    .UseCorrelationId()
    .UseSecurityHeaders()
    .UseCors()
    .UseAuth()
    .UseHealthCheckEndpoints());

app.Run();
```

`UseDefaults()` is a call you can see and delete. Delete it and you get nothing —
the same shape as Entity Framework without a provider, and for the same reason:
what infrastructure a service runs is a decision that belongs to whoever operates
it, and a decision made silently is one nobody can review.

It registers **no** cache, no secret store, no audit sink and no message bus.
Those come from provider packages, so that adding Girder never adds a server you
have to run.

### The modules

| Module | What it does | What it needs | In `UseDefaults()` |
|---|---|---|---|
| `Logging` | Serilog, configured from this service's configuration | — | ✅ |
| `HttpContextAccess` | the ambient `HttpContext` several modules read | — | ✅ |
| `JsonOptions` | camelCase in and out, indented while developing | — | ✅ |
| `Jwt` | `IJwtService`, `ITotpService`, error messages | — | ✅ |
| `SecurityMonitoring` | failed sign-ins, lockouts, alerts | `IDistributedCache` (from `Caching`) | ✅ |
| `Resilience` | circuit breakers and retries for outgoing calls | — | ✅ |
| `SecretManagement` | reading and rotating secrets | — | ✅ |
| `Audit` | the security audit trail | — | ✅ |
| `InputSanitization` | refusing requests that carry injection *syntax* | — | ✅ |
| `RateLimiting` | counting requests, refusing the ones over the line | — (brings an in-process counter; a provider replaces it) | ✅ |
| `HealthChecks` | liveness and readiness | — | ✅ |
| `Caching` | the two in-process caches other modules build on | — | ✅ |
| `Observability` | traces, metrics, the telemetry pipeline | — | ✅ |
| `SecurityHeaders` | the response headers a browser enforces | — | ✅ |
| `Authorization` | permission policies — what answers `[RequirePermission]` | — | ✅ |
| `PermissionEnforcement` | the pipeline step that refuses a request no permission covers | — (registers nothing) | ✅ |
| `CorrelationPropagation` | carries the correlation id out on every HTTP call | — | ✅ |
| `ApiDocumentation` | Swagger, in development only | — | ✅ |
| `Cors` | cross-origin rules from the configured origins | — | ✅ |
| `HttpResponseCaching` | ETags and conditional responses | `IDistributedCacheService` | ❌ |
| `Communication` | calling other services, with caching and deduplication | `IEventBus` | ❌ |
| `ResourceAuthorization` | resource and ownership policies | `IResourceAuthorizationService` | ❌ |
| `Encryption` | encrypting values at rest | a master key | ❌ |
| `PasswordHashing` | hashing and verifying passwords | a hashing provider | ❌ |
| `TokenSessions` | refresh tokens and sessions | a refresh token store | ❌ |
| `Principal` | the current principal, read from the request | — | ❌ |

The line between the two halves is one rule: **everything in `UseDefaults()`
starts with nothing else registered** — checked by building the container with
`ValidateOnBuild`, so a module that registers a consumer without its dependency
fails there rather than on the first request that needs it. The seven that are
not in it each need a decision Girder must not make on anyone's behalf — which
cache, which broker, which key, which hashing algorithm, which store. Including
them would produce a default set that refuses to start, which is not a default.

The rule now holds for the **pipeline** too, and did not before. `RateLimiting`
was in the default set and registered no counter, so the default chain refused
to compose — `UseRateLimiting() needs IDistributedRateLimitStore` — and the
shortest documented way to stand a service up died at startup. It brings an
in-process counter now, the same shape as the framework's own
`AddDistributedMemoryCache()` that `Caching` already registers, and
`AddRedisCache(prefix)` or `AddInMemoryCache(prefix)` replaces it because the
last registration of a service is the one that wins. **A counter in one process
counts per replica**, so a shared store belongs in place before a second
instance runs.

`Authorization` and `PermissionEnforcement` are two modules because they are two
things. The first supplies the policy provider that answers
`[RequirePermission]` on the endpoints carrying it; the second is the pipeline
step that refuses *everything else* a caller has no permission for. A service
with any public surface at all — a sign-in page, a careers page, a door behind a
shared secret rather than a token — leaves the second one out and keeps the
first:

```csharp
girder.UseDefaults()
      .Without(GirderModule.PermissionEnforcement,
               "we have a public surface, and it is declared at the endpoints");
```

### What input sanitization actually looks at

`UseInputSanitization()` inspects the **query string**, **form bodies**, the
**`X-Forwarded-For` and `X-Real-IP` headers**, and every **string value in a
JSON body**. Not the path, not other headers, and not binary content. A promise
wider than its effect is more dangerous than none, because people rely on it.

It matches **syntax, never words**. A keyword carries no information about
intent: `Union-Investment` is a fund manager, `Drop-In-Zentrum` is a place, and
`O'Brien` is a name. What it matches is a quote that ends a literal and starts
an operator, a comment introducer after a quote, a statement terminator followed
by a keyword, a tautology, an LDAP filter breakout, a shell metacharacter
followed by a command, markup, and path traversal.

Until 4.1 it matched the bare keyword at a word boundary — and, on its own,
every semicolon, pipe, backtick, apostrophe, quote, bracket and brace. A hyphen
is a word boundary and an underscore is not, so `delete-account` was an attack
and `delete_account` was not. It also treated `Referer` as input, so every
request from a page whose own URL contained such a word was refused, including
the one asking who is signed in. Meanwhile JSON bodies were exempt — so for an
application whose whole write surface is JSON, it inspected nowhere anything
arrives and refused ordinary traffic everywhere else.

**A refused request is refused; an accepted one is passed on untouched.** The
JSON body used to be re-serialised on every request whether or not anything had
changed, which rebuilt property names and turned every number into a `decimal`.
Editing someone's text without saying so is not a weaker refusal, it is a
different and worse thing: nothing downstream can tell it happened.

The refusal is a **problem document** (`application/problem+json`) naming the
correlation id, like every other error Girder writes. What it cannot name is the
**field**: a request refused here never reached the validator that knows which
one. For an application whose own validation answers `422` with the field name
that is a loss — a precise answer replaced by a blunt one — so it can hand the
body back:

```csharp
services.Configure<InputSanitizationOptions>(o => o.InspectJsonBodies = false);
```

That is a decision about *who reports the error*, not about whether the value is
refused. Turning it off **without** validators is a decision to refuse nothing.

### When you want to differ

`Use` adds, `Without` removes, in any order, and the last mention of a module
wins. What order you write them in never changes what the service does: modules
register in Girder's order, because they read what earlier ones set up.

**No broker on this service.** `Communication` is not in the defaults, so this is
only worth writing if someone might expect it:

```csharp
girder.UseDefaults()
      .Without(GirderModule.Communication, "reads only; nothing to publish");
```

**Nothing may be cached, for legal reasons.** The reason is required, and this is
why: in a year the line is still there and still says what the auditor asked.

```csharp
girder.UseDefaults()
      .Without(GirderModule.Caching, "ADR-0013: consent decisions must not be cached");
```

**A brake of your own.** The two settings that decide whether a rate limit is a
limit at all — whom it counts, and whom it believes about who that is:

```csharp
girder.UseDefaults()
      .UseRateLimiting(rate => rate
          .TrustForwardedHeadersFrom("10.0.0.0/8")   // the load balancer, and nobody else
          .PerOrigin()                                // a sign-in route counts per origin
          .Allowing(perMinute: 5, perHour: 20, perDay: 100));
```

`TrustNoForwardedHeaders()` is the default written out loud. It changes nothing,
and it is worth writing: it tells the next reader that this service is meant to
be reached directly, so putting a proxy in front of it later is a change to that
line rather than a silent change in who gets counted.

**A service that reads tokens but must not be able to write them.** Every service
except the one that signs people in:

```csharp
girder.UseDefaults()
      .UseJwt(jwt => jwt.VerifyOnly(publicKey, keyId));
```

**Everything, for a single instance.** The provider packages extend the builder
themselves:

```csharp
girder.UseDefaults()
      .Use(GirderModule.HttpResponseCaching)
      .Use(GirderModule.TokenSessions)
      .UseInMemoryCache("identity-service")
      .UseInMemoryRefreshTokens();
```

**Nothing at all.** A legitimate thing to want, and a visible thing to have done:

```csharp
builder.Services.AddGirder(config, env, "jobs-service", _ => { });
```

### Choosing providers

```csharp
// Redis, Valkey, Garnet or KeyDB — the same wire protocol
builder.Services
    .AddRedisConnection(connectionString, instanceName: "identity")
    .AddRedisCache("identity")
    .AddRedisSecretManager(builder.Configuration, builder.Environment)
    .AddRedisSecurityAudit()
    .AddRedisResourceAuthorization()
    .AddRedisTokenRevocation(maxTokenLifetime: TimeSpan.FromHours(24))
    .AddRedisEncryption();

// or, for a single instance and for tests
builder.Services
    .AddInMemoryCache("identity")
    .AddInMemorySecretManager()
    .AddInMemorySecurityAudit()
    .AddInMemoryResourceAuthorization()
    .AddInMemoryTokenRevocation()
    .AddInMemoryRefreshTokens();
```

Rate limiting brings an in-process counter of its own, so it works from the
first line. `AddRedisCache` or `AddInMemoryCache` replaces it and decides whether
the counters are shared between instances.

Every in-memory registration documents what it costs: state is invisible to
other instances, so a rate limit counts per process and an audit trail does not
survive a restart. That is sound for tests and a single replica, and stated
rather than implied.

### Contributing a module from another package

Girder does not know what modules exist. `GirderModule` is a name, not an
enumeration, and `Use(module, register)` takes the registration along with it —
so a package Girder has never heard of extends a composition the way a database
provider extends Entity Framework's options builder:

```csharp
namespace Contoso.Billing;

public static class BillingGirderModules
{
    public static GirderModule Billing => new("Contoso.Billing");

    /// <summary>
    /// Sets up billing. Without it, the endpoints under /api/billing answer 404.
    /// </summary>
    public static GirderBuilder UseContosoBilling(this GirderBuilder girder, string apiKey) =>
        girder.Use(Billing, g => g.Services.AddContosoBilling(apiKey));
}
```

The module then behaves like any other: it appears in the composition, and it can
be left out with a reason. Prefix the name with your package so two packages
cannot collide.

### What this service is running

The composition is in the container, so a service can report it:

```csharp
var composition = app.Services.GetRequiredService<GirderComposition>();

foreach (var (module, reason) in composition.Excluded)
{
    app.Logger.LogInformation("Girder module {Module} left out: {Reason}", module, reason);
}
```

A module nobody mentioned is in neither list. Silence is not a decision, and
recording it as one would make the report longer and less true.

### What a module says when something is missing

Modules stand alone: each registers what it owns and nothing else. Where a
module genuinely needs something it cannot provide — a cache needs a cache
server — it says so **at startup**, naming the call that fixes it:

```
Girder is missing 1 provider registration(s):
  • AddCaching(), AddHttpResponseCaching() need IDistributedCacheService — call AddRedisCache(prefix) or AddInMemoryCache(prefix)
Provider packages: Girder.Redis, Girder.InMemory, Girder.Messaging.MassTransit, Girder.Data.EntityFrameworkCore.
```

The count is services to register, not modules that asked. Two modules needing
one cache is one line and one thing to do — but both are named, because you may
be removing one of them rather than adding the provider.

The pipeline side does the same while it is being composed. `UseHttpCaching()`
without the caching module, or `UseRateLimiting()` without a store, throws there
rather than on the first request that happens to reach the middleware — in
production, naming a Girder-internal type the reader never wrote.

### The CQRS pipeline asks for a cache only if you cache

`AddCQRS(assemblies)` reads what you hand it. Where nothing implements
`ICacheableQuery` or `ICacheInvalidatingCommand`, the two cache behaviours are
not put in the pipeline at all — no cache is needed, and your composition root
does not have to register one it never uses:

```csharp
services.AddCQRS(typeof(Program).Assembly);   // caches nothing, needs nothing
```

Where something does, the requirement follows the same rule as the modules
above, and names the type that caused it rather than the interface you would
have to go looking for:

```
Girder is missing 1 provider registration(s):
  • AddCQRS() (GetJobQuery implements ICacheableQuery) needs IDistributedCacheService — call AddRedisCache(prefix) or AddInMemoryCache(prefix)
Provider packages: Girder.Redis, Girder.InMemory, Girder.Messaging.MassTransit, Girder.Data.EntityFrameworkCore.
```

A cache, and nothing else. Commands that declare `ETagInvalidationPatterns`
clear stale ETags through that same cache, so a command pipeline never drags
HTTP response caching in behind it.

Any `IDistributedCacheService` satisfies the requirement. The check asks for the
interface, not for who registered it, so a provider you wrote yourself counts.

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

### Keys: who may issue, and who may only verify

With a shared secret the verification key **is** the signing key. Every service
able to check a token can mint one — for any subject, with any role. Compromise
the least important service and you can impersonate anyone at the most
important one.

A key pair separates the two, and `SigningKey` makes that a property of the
object rather than a rule someone has to remember:

```bash
# one line each, no PEM, no escaping
GIRDER_JWT_KID=2026-08
GIRDER_JWT_PRIVATE_KEY=MIGH…    # the issuing service, and nothing else
GIRDER_JWT_PUBLIC_KEY=MFkw…     # everyone who verifies
```

```csharp
// identity-service: issues and verifies
infra.AddJwtAuthentication(o =>
{
    o.SigningKey = SigningKey.FromEcdsaPrivateKey(config["GIRDER_JWT_PRIVATE_KEY"]!, kid);
    o.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(config["GIRDER_JWT_PUBLIC_KEY"]!, kid));
});

// every other service: verifies, and cannot issue
infra.AddJwtAuthentication(o =>
    o.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(config["GIRDER_JWT_PUBLIC_KEY"]!, kid)));
```

Asking the second one to issue throws, naming why. ES256 rather than RS256 by
default: both halves are a single base64 line that fits in an environment
variable, and signing happens on every sign-in.

Generate a pair with `SigningKey.GenerateKeyPair(kid)`, or in development let
several services derive the same one from a seed — which refuses to run outside
development, because everyone holding the seed can issue:

```csharp
o.SigningKey = SigningKey.DevelopmentFromSeed(config["GIRDER_DEV_KEY_SEED"]!, env);
```

**`ValidationKeys` is a list on purpose.** Rotation and the move off a shared
secret both need two live keys at once — the new `kid` starts issuing while
tokens under the previous one are still in circulation. With a single key,
either change signs every user out at the moment it takes effect:

```csharp
o.SigningKey = current;                    // ES256, kid 2026-08
o.ValidationKeys.Add(current);
o.ValidationKeys.Add(previous);            // ES256, kid 2026-07 — until they expire
o.ValidationKeys.Add(legacySharedSecret);  // HS256 — until the window closes
```

The accepted algorithms come from these keys, never from a token header.

Calling `AddJwtAuthentication()` with no options keeps the shared-secret path
from `JwtSettings:Secret` or `JWT_SECRET`, unchanged.

### Swapping Girder's own sign-in for a provider

A library whose authentication cannot be exchanged for Keycloak, Zitadel or
authentik is itself the dependency it claims to prevent. The same verification
path takes either source:

```csharp
// Girder's own keys
infra.AddJwtAuthentication(o =>
    o.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(publicKey, kid)));

// or a provider's published key set — discovery, JWKS, kid rotation
infra.AddJwtAuthentication(o => o.Authority = "https://keycloak.intern/realms/wt");
```

A service behind a provider issues nothing, so it needs no signing key and gets
no `IJwtService`. Both together is the shape a migration has, where the old and
the new issuer are live at once.

### Wiring

```csharp
builder.Services.AddSharedInfrastructure(
    builder.Configuration, builder.Environment, "jobs-service", infra => infra
        .AddJwtAuthentication()
        .AddPrincipal());

app.UseGirder(builder.Environment, "jobs-service", pipeline => pipeline
    .UseAuth()          // authentication, then authorization
    .UsePrincipal());   // translates the token once
```

`AddPrincipal` is separate from `AddJwtAuthentication` on purpose: a gateway
verifies tokens without ever building a principal, and a service may build one
from claims another scheme established.

The middleware answers 401 when a token's claims cannot be translated, rather
than letting the request continue without a principal. Downstream code reads
`ICurrentPrincipal` instead of inspecting claims again.

Endpoints declare the capacity they need:

```csharp
[Authorize(Policy = GirderPolicies.ActingForCompany)]
[Authorize(Policy = GirderPolicies.ActingAsSelf)]
```

Issue a company token only after verifying membership. `AddJwtAuthentication`
registers `IJwtService` for that — the same module that verifies tokens issues
them, so both read one `JwtSettings` and a service cannot mint a token it then
refuses:

```csharp
var issued = await jwt.GenerateTokenAsync(new UserClaims
{
    UserId = subject.ToString(),
    Email  = email,
    Acting = new Capacity.ForCompany(tenant)   // only after checking membership
});
```

A subject needs an identifier and an address, nothing more. Girder does not ask
for a name in two parts: that refuses tokens to mononyms, to names that do not
split that way, and to service accounts.

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

## Sessions: two tokens, and only one of them can be taken back

A JWT is valid until it expires; there is nothing to delete. That is what makes
it fast — every service verifies it from the signature alone, with no round
trip — and it is also why signing out is not simply a matter of forgetting it.

Girder answers that with two tokens whose properties are opposites:

| | Access token (JWT) | Refresh token |
|---|---|---|
| Who verifies it | **every** service, from the signature | only the issuing service |
| Lifetime | `JwtSettings:ExpireMinutes`, 60 by default | `TokenSessions:RefreshTokenLifetime`, 14 days |
| Stored | nowhere | one row, in **your** database |
| Taken back | only via a revocation store | by setting a column |

Signing out ends the session where the refresh token lives. Nothing else has to
be running for that to take effect, and the access token already issued stands
until it expires — a window you set as a number, not infrastructure you deploy.

```csharp
builder.Services.AddSharedInfrastructure(config, env, "identity", infra => infra
    .AddJwtAuthentication(o => { /* keys */ })
    .AddPasswordHashing()
    .AddTokenSessions());

builder.Services.AddInMemoryRefreshTokens();                       // day one
// builder.Services.AddEntityFrameworkRefreshTokens<AppDbContext>();  // in earnest
```

```csharp
var signIn = await sessions.SignInAsync(subject);       // session + first token
var again  = await sessions.RefreshAsync(presented);    // rotates, one token per use
await sessions.SignOutAsync(session);                   // this device
await sessions.SignOutEverywhereAsync(subject);         // all of them
var mine   = await sessions.ActiveSessionsAsync(subject);
```

### Rotation, and what it catches

Every refresh issues a new token and retires the old one. That is not
housekeeping: it is the only way a theft becomes visible.

Without rotation, a stolen refresh token works for its whole lifetime and
**nobody ever finds out** — attacker and owner use the same valid credential.
With rotation, whoever refreshes second presents a token that was already
consumed, which cannot happen honestly. Which of the two is the thief is
unknowable, so the whole session ends and the owner signs in again with a
password the attacker does not have.

The trap is the honest case that looks identical: two browser tabs both hit a
401 and both refresh. Treating that as theft signs out people who did nothing
wrong. `ReuseGracePeriod` (15 seconds) is the window in which a second
presentation is answered with a fresh token for the same session instead of an
alarm — reported as `RotatedWithinGrace`, so a run of them on one session is
still visible. It is a deliberate concession, and bounded: inside it a replay
genuinely cannot be told from a second tab.

`AbsoluteSessionLifetime` (30 days) is the ceiling. Without it, refreshing
forever keeps a session alive forever, which is exactly what an undetected
stolen token wants.

### Inside a transaction you already have

The Entity Framework store joins a transaction the caller has open and opens one
of its own only when there is none. So an application that writes an audit row in
the same transaction as the change that caused it can put a refresh in that
bracket like anything else:

```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);

var refreshed = await sessions.RefreshAsync(presented, ct);
db.AuditEvents.Add(AuditEvent.TokenRefresh(refreshed.Session));
await db.SaveChangesAsync(ct);

await transaction.CommitAsync(ct);   // the rotation and the record, or neither
```

Rolling back takes the rotation with it, and the presented token goes on working
— which is the point: an audit row that survives a rollback records something
that did not happen.

Rotation is still one atomic step whichever way it runs. The store never rolls a
caller's transaction back; where a concurrent refresh has already taken the row,
it says so and leaves the caller's work alone.

### Where the refresh token belongs in a browser

Not in `localStorage` and not in `sessionStorage`: script can read both, and a
refresh token is worth days where an access token is worth minutes. Send it as
an `HttpOnly`, `SameSite=Strict` cookie scoped to the refresh path, keep the
access token in memory, and fetch a new one on load. A cross-site scripting bug
then costs one short-lived token instead of the account.

### Cleaning up

`PurgeAsync(olderThan, batchSize)` removes rows that are finished. Girder never
calls it: retention is policy, the table is yours, and a background loop in a
library owns a schedule in your process. Call it from whatever already runs
your scheduled work.

In batches, and that is not a detail — an unbounded delete competes with
`TryConsumeAsync` for the same pages, and on a single-writer database that
blocks the sign-in path.

## Passwords

**Girder picks no algorithm.** It defines the port and ships three
implementations; which one writes is the deployment's decision, exactly like
which server its data lives on.

```csharp
infra.AddPasswordHashing();              // PBKDF2 unless something else registers
services.AddArgon2Passwords();           // ...or Argon2id writes
services.AddBCryptPasswords();           // ...or bcrypt
```

PBKDF2 is the fallback only because it needs no package and no licence. Argon2id
is OWASP's first choice and the reason is memory: PBKDF2 costs an attacker time,
which purpose-built hardware buys back cheaply, and Argon2id costs memory, which
it does not.

### Why a library does this at all

Because the alternative is that every service writes it, and there are five
standard ways to get it wrong — no work factor, no salt, a comparison that exits
early, a cost fixed in code with no way to raise it, and a construction someone
invented. None of that is domain knowledge; it is a pure function with
operational parameters, which is the same shape as everything else here.

What would **not** be acceptable is Girder deciding for you. Hence the port, the
three implementations, and the next section.

### Changing your mind, and arriving with someone else's entries

A deployment that cannot change its algorithm without asking everyone to reset
has not chosen one — it was given one. So every format ever written stays
readable while exactly one writes:

```csharp
infra.AddPasswordHashing();              // PBKDF2 writes
services.AddBCryptPasswordReader();      // bcrypt entries still verify
```

A successful sign-in against any other format answers
`SuccessRehashNeeded`. Rewrite the entry from the password you were just handed
— the one moment it exists — and that person is across. Nobody is asked to reset
anything, and the old format leaves as people return.

The same mechanism covers all of it: a migration from another system, a switch
from PBKDF2 to Argon2id, a raised cost. Each algorithm ships as both a writer
and a reader (`AddArgon2Passwords` / `AddArgon2PasswordReader`), and several
readers can be registered at once, which a long-lived system will need.

### Adding one

Implement three methods. `Hash` writes, `CanRead` says which entries are yours,
`Verify` answers `Success`, `SuccessRehashNeeded` or `Failed`:

```csharp
public sealed class ScryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => /* … */;
    public bool CanRead(string encoded) => encoded.StartsWith("$scrypt$");
    public PasswordVerification Verify(string password, string? encoded) => /* … */;
}

services.AddKeyedSingleton<IPasswordHasher>(PasswordHashing.PrimaryKey, new ScryptPasswordHasher());
```

Two rules the shipped ones follow and yours should. `encoded` may be **null** —
that means no such account, and the implementation still spends the work before
answering `Failed`, or how long an answer takes says whether someone has an
account here. And a damaged entry returns `Failed` rather than throwing: one bad
row must not break signing in for everyone.

## Token revocation

A JWT is valid until it expires; there is nothing to delete. A revocation list
is what makes "sign out" take effect before then.

```csharp
builder.Services
    .AddRedisConnection(connectionString, "identity")
    .AddRedisTokenRevocation(maxTokenLifetime: TimeSpan.FromHours(24));

app.UseGirder(builder.Environment, "identity", pipeline => pipeline
    .UseAuth()
    .UseTokenRevocation());     // after UseAuth, which establishes the claims
```

One registration serves both sides from one instance: the evaluator answers
from the state the writer records. `maxTokenLifetime` is how long a cutoff is
kept and must be at least the longest lifetime an access token can have, or a
cutoff expires while tokens it should refuse are still valid.

**This is the upgrade, not the entry price.** Ending a session is what
`AddTokenSessions()` does, in your own database, with no extra server. A
revocation store closes the remaining window — the access token already issued —
to zero. Add it when that window matters; most deployments shorten
`ExpireMinutes` instead.

`AddJwtAuthentication()` asks for no store, and a service without one issues and
verifies normally.

`UseTokenRevocation()` throws at startup when no evaluator is registered. There
is no silent default: a revocation check that always answers "not revoked" is
indistinguishable from one that works, and the difference only shows when
someone needs a token to stop working. Turning it off is explicit and requires
a stated reason:

```csharp
builder.Services.AddNoTokenRevocation(
    "access tokens live 15 minutes; revocation happens at the refresh path");
```

That also registers a writer, and the writer **throws**. A deployment that
declared it revokes nothing must not have a `RevokeTokenAsync` that quietly
succeeds: the caller believes it withdrew a token, and the path that tells
someone "signed out everywhere" cannot complete when nothing was withdrawn.

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
builder.Services.AddRedisCache("identity");     // replaces it with a shared one

app.UseDistributedRateLimiting();
```

`AddDistributedRateLimiting` registers a counter that lives in this process, so
the rules work from the first line. The store is still a separate choice from
the rules, because it decides how much traffic actually gets through: with a
shared counter all instances draw from one budget, in process each counts for
itself, so the effective limit is multiplied by the replica count.

**Per-path limits start empty.** They used to arrive holding seven paths from
the application Girder was extracted from — `/api/auth/login`, `/api/admin/*`
and the rest — and configuration adds to that dictionary rather than replacing
it, so they could not be removed from outside. Measured: a call to
`POST /api/auth/register` was refused at three per minute in an application that
has no such route.

**Loopback is exempt by default** (`WhitelistedIps`). It is not forgeable — the
origin comes from `Connection.RemoteIpAddress` and nothing else — but it applies
in exactly the place a rate limit gets tried out first, your own machine, where
it then looks as though nothing is counting. `Exempting(...)` replaces the list;
`Exempting()` empties it.

**The refusal is a problem document.** `application/problem+json`, carrying the
correlation id as well as the trace identifier — because the one answer a person
actually reports is *I am locked out*, and it used to be the one answer with no
id anybody could look up.

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

### Instrumentation Girder does not ship

`OpenTelemetry.Instrumentation.EntityFrameworkCore` and
`.Process` have never had a stable release, and a stable package must not drag
a prerelease into every consumer's tree. An application that wants them
installs the package and adds them through the same callback:

```csharp
builder.Services.AddTelemetry("identity-service", "1.0.0", t => t
    .AddTracing(tracing => tracing.AddEntityFrameworkCoreInstrumentation(o =>
    {
        // A SQL statement carries table names and parameter values, and spans
        // travel to wherever telemetry is collected. The instrumentation has no
        // option for this, so the tag is cleared after it is set.
        o.EnrichWithIDbCommand = (activity, _) =>
        {
            activity.SetTag("db.statement", null);
            activity.SetTag("db.query.text", null);
        };
    }))
    .AddMetrics(metrics => metrics.AddProcessInstrumentation()));
```

### What never reaches a log

**Girder logs no values.** Not sanitised values, not redacted values — none.
Two paths could write data into a log, the CQRS behaviour running a command and
the HTTP middleware handling a request, and both write the *shape*:

```
Shape of CreateTodoCommand: {Title: string(41), Note: string(26)}
Incoming Request: … Body = {displayName: string(12), email: string(15), amount: number}
```

Field names and value sizes. That is what a person debugging actually needs —
which fields arrived and whether they were empty — and it cannot leak, because
there is nothing in it to leak.

This replaced redaction by field name, which does not work and looked as though
it did. Redaction is enumeration: it removes what somebody thought of. A case
reference, a note to a doctor, the name of a company someone is leaving, a
title reading *"Termin bei Dr. Weber wegen der Kündigung"* — none of it is on
any list, however long the list gets.

What still is removed rather than described, because it is a credential in a
place everything logs: `Authorization`, `Cookie` and `Set-Cookie` headers, and
token-bearing query parameters, which is how a verification link becomes a log
entry.

**Request and response bodies are not logged at all by default**
(`Observability:EnableDetailedHttpLogging`). Turning it on gives you shapes, not
contents.

The diagnostic handle that survives everywhere is the pseudonymous id: the
logging scope carries `UserId`, which identifies a person to your database and
to nobody reading the log.

### If you log a payload yourself

Girder still ships `ILogSanitizer` and the vocabulary behind it —
`SensitiveFieldNames` (about a hundred names, matched exactly so `RequestName`
survives) and `SensitiveValuePatterns` (email, card number, IBAN, national
identifier, by shape wherever they appear). Girder no longer uses either
internally, and that is deliberate.

```csharp
logger.LogDebug("{@Payload}", sanitizer.Sanitize(payload));
```

Use it if you decide to log a payload anyway. Know what you are getting: it is
a net, not a guarantee, and the paragraph above says why.

### Logging

```csharp
LoggingConfiguration.ConfigureSerilog(configuration, environment, "identity-service");
```

Enrichment, filtering and exception shaping are Girder's. **Where the logs go is
the application's**: declare `Serilog:WriteTo` in configuration and install the
sink package alongside naming it. Declaring even one sink replaces the built-in
console and file sinks completely, so list every destination you want.

### Security headers

`AddSecurityHeaders()` plus `UseSecurityHeaders()` set the headers and then
check what actually went out. Three rules keep that check worth reading:

- **`X-XSS-Protection` is neither sent nor demanded.** It controlled a browser
  XSS auditor that was itself exploitable; browsers removed it and OWASP
  advises against sending it.
- **`frame-ancestors` counts as framing protection.** A CSP carrying it is not
  missing `X-Frame-Options` — that header is what it replaced.
- **Over plain HTTP, a missing HSTS header is not a finding.** RFC 6797 §8.1
  says a user agent must ignore an HSTS header received over a non-secure
  transport, so asking for one asks for something discarded.
- **A JSON response drops `X-Frame-Options` and states `frame-ancestors 'none'`
  instead.** Dropping the legacy header alone would leave nothing:
  `frame-ancestors` does not fall back to `default-src`.

Headers that do not apply are left out of the analysis entirely, not merely out
of the findings list — the score is a weighted average over that set, and
scoring against one set while reporting another produced the worst of both: a
warning naming nothing.

Each distinct finding is logged **once per process**, not once per response. A
misconfiguration is constant; a warning repeated on every request buries
everything else in the log and gets the whole check switched off.

## Digital sovereignty

### What Girder calls out to

Girder itself opens no connection you did not configure. Every outbound call has
a named cause:

| What calls out | When | Where to |
|---|---|---|
| `ServiceCommunicationManager` | `Communication`, on every `GetAsync` / `SendRequestAsync` | the services in `ServiceEndpoints`, or the gateway |
| `ServiceTokenProvider` | `Communication`, to get a machine token | the token endpoint in `ServiceCommunication:M2M` |
| `OpenBaoSecretProvider` | the OpenBao secret provider only | the address configured for it |
| `Girder.Redis` | whenever a Redis provider is registered | the connection string you passed |
| `Girder.Messaging.MassTransit` | `AddMessaging` | the broker you configured |
| OpenID Connect discovery | `UseJwt(jwt => jwt.From(authority))` only | the authority's published key set |

There is no telemetry to Girder, no licence check, no update ping. A service
with none of the above configured makes no outbound call because Girder is in it.

### Limiting it

Outbound destinations are declared, and undeclared calls fail:

```csharp
builder.Services.AddGirderEgressPolicy(p => p
    .Allow("openbao.internal")
    .AllowSubdomainsOf("example.eu")
    .AllowLoopback());
```

With nothing declared everything is allowed — adding the package must not
change behaviour on its own. Once anything is declared, the policy is
enforcing, and it applies to every `HttpClient` the factory builds, including
the ones Girder's own modules use.

A refused call throws where it was made, naming the host and the policy. That is
deliberate: a call that silently returned nothing would look like an empty
answer, and an empty answer is something an application acts on.

### Reading the report

```csharp
builder.Services.AddGirderSovereigntyReport();

// ...

var report = app.Services.GetRequiredService<ISovereigntyReport>().Assess();
```

The report reads the running configuration and says what it points at. Each
finding is one dependency:

```
Database   db.internal              SelfHosted     private network
Secrets    openbao.internal         SelfHosted     name reserved for internal use
Cache      cache.example.eu         Undetermined   public name; operator not known from the name
Storage    bucket.s3.amazonaws.com  ThirdCountry   provider subject to the US CLOUD Act
```

Three verdicts, and the middle one is the important one:

- **`SelfHosted`** — loopback, a private network, or a name reserved for internal
  use. Infrastructure the operator controls.
- **`Undetermined`** — a public name whose operator cannot be told from the name.
  Most third-party and most European providers land here. **This is not a pass**;
  it is the report saying the question is still open and an operator has to
  answer it.
- **`ThirdCountry`** — a domain belonging to a provider subject to third-country
  access laws. Recognised by name, not by contract: a region in Frankfurt does
  not change who operates a service or which law reaches it, so `*.amazonaws.com`
  is a third-country provider whatever the endpoint says.

Not matching the list is reported as *undetermined*, never as sovereign — the
difference between a report and a rubber stamp. A host name cannot prove a
jurisdiction, and no library can; what this does is make every configured
destination visible in one place, so the ones that need an answer can be seen.

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

- **Two `SecurityHeadersMiddleware` classes** — resolved. There used to be one
  in `Girder.Infrastructure.Middleware` (config-driven) and one in
  `Girder.Infrastructure.Security.Headers` (service-driven, with CSP scoring and
  separate script/style nonces). The builder pipeline silently used the first,
  so `AddSecurityHeaders()` registered nothing the running middleware needed and
  the maintained implementation was unreachable. The richer one survives.
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
- **`ISecretProvider` implementations still sit in `Girder.Infrastructure`.**
  The port moved to `Girder.Abstractions`, the OpenBao and file-based
  implementations did not. They belong in `Girder.Secrets.*` packages.
- **Duplicate `IDomainEvent`.** One lives in `Girder.Cqrs.Interfaces`, a second
  in `Girder.Infrastructure.Caching`, because of the layering above.
- **Password entries from another system.** `IPasswordHasher` is a port and
  entries say what they are, so a reader for bcrypt or Argon2id can be layered
  in front — but Girder ships neither, and a migration that has to re-hash
  everyone on first sign-in is the state today.
- **`DataProtectionSecretProvider` has no consumer.** Nothing registers it, so
  ASP.NET's key ring is created by the framework and used by nothing. Harmless
  where nothing is protected with it; a service that adds cookie authentication
  or antiforgery has to persist and protect those keys itself today.
- **`ILogSanitizer` has no consumer inside Girder** since logging moved to
  shapes. It stays as a tool for an application that logs a payload of its own,
  and whether that is enough reason to keep it is an open question.
- **No transactional outbox.** Recording an intent in the same transaction as
  the change that caused it is infrastructure and belongs here; the dispatcher
  that delivers it is a background loop and belongs to the application, the same
  split as `PurgeAsync`. Nothing is built yet.

## Upgrading to 4.2.3

`Shape.Of` and `LoggingBehavior` no longer fail a request they cannot describe.
A command carrying `ReadOnlyMemory<byte>` used to 500 before the handler ran:
reflection cannot invoke a getter that returns a ref struct (`Span`,
`ReadOnlySpan`), and the catch only covered `TargetInvocationException`. Those
getters are now named, not invoked, and a shape that still fails is omitted —
the handler still runs.

## Upgrading to 4.2.2

`X-RateLimit-Limit` and `X-RateLimit-Remaining` are now on the **429** as well.
They used to go on the allowed answer only, so the one response where a caller
most needs to read the limit — and see that nothing is left — was the one
without them.

## Upgrading to 4.2.1

Two defects that only showed up when an application stopped rebuilding the
limiter and actually used it.

**Nothing is exempt by default any more.** `WhitelistedIps` held loopback and
`WhitelistedEndpoints` held the health paths, and **neither could be removed from
configuration**: the .NET binder adds to a collection and never replaces it, and
an empty JSON array is indistinguishable from an absent key. Measured — with
`"WhitelistedIps": ["9.9.9.9"]` the bound value was `127.0.0.1, ::1, 9.9.9.9`. An
operator who wrote the list out deliberately still got the exemption, and nothing
in their own configuration would have told them. A default nobody can remove is a
trap, and this one hid in the place a rate limit is first tried out: your own
machine, where it then looks as though nothing counts.

**Health checks now run before rate limiting in the default chain.** They used to
run after, so the liveness probe went through the limiter and was saved only by
that unremovable whitelist entry. A braked liveness probe takes the container out
of the load balancer, which makes the limiter itself the outage. Order is the
honest place for that, not a path string.

If you relied on either default, name it yourself — and check that your own
chain puts the health endpoints first.

## Upgrading to 4.2

Nothing to rewrite. One new package and two things that were always possible and
never said.

### `Girder.Http` — take the pipeline without the engine

`Girder.Infrastructure` carries **44** transitive packages: Swashbuckle,
OpenTelemetry, nine Serilog packages, JWT bearer, TOTP, FluentValidation. Right
for a service running the whole default set; wrong for a gateway that only
routes and wants a correlation id.

`Girder.Http` carries **none**. It holds `CorrelationIdMiddleware`,
`DistributedRateLimitingMiddleware`, `ClientAddress`, `InProcessRateLimitStore`
and their options, and depends on the shared framework and `Girder.Abstractions`
and nothing else.

**The namespaces did not change.** They are still `Girder.Infrastructure.*`,
which reads oddly in a package called `Girder.Http` and is deliberate: moving
types between assemblies keeps every `using` compiling, renaming the namespace
would break the source of every caller. `Girder.Infrastructure` references it, so
`AddGirder` and `UseGirder` are unchanged and nobody has to do anything.

This exists because a gateway looked at the cost of taking Girder's two
middlewares and wrote its own instead. A library whose own use is the expensive
path has failed at the thing it is for.

### Rate limiting: brake a named set of paths and nothing else

Always possible, never documented, so it got rebuilt by hand. A limit of `0`
writes no counter, and a request counted against nothing is allowed:

```json
"DistributedRateLimiting": {
  "RequestsPerMinute": 0, "RequestsPerHour": 0, "RequestsPerDay": 0,
  "EndpointSpecificLimits": {
    "/auth/login":    { "RequestsPerMinute": 20 },
    "/auth/register": { "RequestsPerMinute": 5 }
  }
}
```

Only the two named paths count. That is the shape a gateway needs — the whole
user interface travels through it, and a default over everything would count
each asset fetch. It is now held by tests, which is what makes it an offer
rather than an accident.

`LimitMultiplier` lifts every ceiling for an environment where a test suite
hammers itself. A factor and not a switch: the limiter still counts, still keys
per subject, still answers with its headers. A limiter switched off is invisible
in the one environment that runs continuously. A limit of `0` stays off — zero
times anything is still off, not a small limit.

The 429 now carries `X-Content-Type-Options` and `X-Frame-Options` itself. It
writes the response and returns, so nothing further down the chain reached it.

### One over-match fixed

`$(` no longer counts as command substitution on its own — it has to name a
command. Measured against realistic text on a job board, the old rule refused
`$(document).ready()`, and a portfolio is exactly where people describe the code
they wrote.

## Upgrading to 4.1

Nothing to rewrite. Six behaviours change, each because the old one was wrong in
a way you could not see from outside.

| Was | Is |
|---|---|
| `UseSharedInfrastructure(env, name)` unconditionally called every step, so `UseDefaults()` plus the default chain **died at startup** | the chain skips a step whose module was left out. `Without(module, reason)` decides once, on the service side |
| `Authorization` was one module, so a service with any public surface had to choose between no policy provider and a blanket 401 | two: `Authorization` (the policy provider) and `PermissionEnforcement` (the pipeline step). Both in the default set |
| `RateLimiting` was in the default set and registered no counter | it brings an in-process one; `AddRedisCache`/`AddInMemoryCache` replaces it |
| `EndpointSpecificLimits` arrived holding seven paths from another application, and configuration could only add to them | empty |
| the 429 was `application/json` with a `traceId` | `application/problem+json`, naming the correlation id (and still the trace id) |
| input sanitization matched bare SQL keywords and single punctuation marks, treated `Referer` as input, and never looked in JSON bodies | matches injection syntax; ignores `Referer`; inspects JSON string values and refuses over them (`InspectJsonBodies`), leaving accepted bodies untouched |
| its refusal was `application/json` with an `error` string and no id | `application/problem+json`, naming the correlation id |
| `LdapInjection` had a name in the enum and no expression — it was caught by the rule that matched every parenthesis | a real filter-breakout detector |
| `UserClaims.EmailVerified`/`AccountStatus` defaulted to `false`/`"Active"`, so `EmailVerified` refused everyone and `ActiveAccount` admitted everyone | `bool?`/`string?` with no default. Unset means the claim is not written, and both policies refuse for want of an answer |
| a list-valued custom claim was impossible | `UserClaims.CustomClaimArrays` writes one, as a JSON array |
| `ClientAddress.Of` gave `10.0.0.1` and `::ffff:10.0.0.1` separate buckets | IPv4-mapped addresses are normalised |

**The one to look at before upgrading** is `AccountStatus`. If your composition
root never set it, every token you issued said `"Active"` — so a policy on
`ActiveAccount` was letting suspended and deleted accounts through, and it will
start refusing them. That is the fix, not a regression; set the field where you
know the answer.

## Upgrading to 4.0

| Was | Is |
|---|---|
| `AddSharedInfrastructure(config, env, name)` | `AddGirder(config, env, name, g => g.UseDefaults())` |
| `AddSharedInfrastructure(…, infra => …)` — **replaced** the default | `AddGirder(…, g => g.UseDefaults().Without(module, reason))` |
| `UseSharedInfrastructure(env, name[, pipeline])` | `UseGirder(env, name[, pipeline])` — the old name forwards, with an `[Obsolete]` |
| `X-Forwarded-For` believed by default | believed only from `TrustForwardedHeadersFrom(...)` |
| `X-Real-IP` read | not read; set `ForwardedForHeaderName` if a proxy sends only that |
| three rate limiters, two of which did not brake | one |
| `IRateLimitService`, `ConfigureRateLimitRules` | removed; counting goes through the cache |
| `EnableIpRateLimiting` / `EnableUserRateLimiting` / `ClientIdStrategy` | `RateLimitSubject`, read on every request |
| `GetAsync<T>` returned `T?`, non-2xx became `null` | returns `ServiceResponse<T>` with status and body |
| every non-success retried three times | only `408`, `429`, `5xx` except `501` |
| correlation id only via the manager or MassTransit | on every `HttpClient` the factory builds |

Full detail, with the two entry points side by side, in
[MIGRATION.md](MIGRATION.md).

## Upgrading to 3.0

Five changes, all in [MIGRATION.md](MIGRATION.md).

| | |
|---|---|
| `AddCQRS()` | registers the cache behaviours only where something implements `ICacheableQuery` or `ICacheInvalidatingCommand`, and requires a cache where it does. A query marked cacheable with no cache registered was never cached; it now refuses to start instead |
| `CacheInvalidationBehavior` | no longer takes `IETagGenerator`, so a command pipeline no longer requires `AddHttpResponseCaching()`. ETags are cleared through the cache it already holds |
| `IETagGenerator`, `ProviderRequirement`, `ProviderRequirements` | moved. `IETagGenerator` to `Girder.Infrastructure.Caching.Http`; the other two to `Girder.Abstractions.Hosting`. `InfrastructureBuilder.RequiresProvider<T>()` is unchanged |
| `AddHttpResponseCaching()` | requires an `IDistributedCacheService` and says so at startup. `ETagGenerator` takes one as a mandatory constructor parameter — an ETag store with nowhere to store is not one |
| `Girder.InMemory` pattern invalidation | applies the store's key prefix to the pattern. With a prefix configured — and `AddInMemoryCache` always configures one — it previously matched nothing and removed no keys |

## Upgrading to 2.0

Three removals and one changed default; everything else is unchanged.
[MIGRATION.md](MIGRATION.md) has the before and after for each.

| | |
|---|---|
| `JwtSettings.ExpireMinutes` | 60 → **15** minutes. Set it back in configuration if you want the old window |
| `UserClaims.FirstName` / `.LastName` | removed. Never written into a token, and demanding them refused one to anyone whose name does not split in two |
| `IJwtService.GenerateRefreshTokenAsync()` | removed, with `TokenResult.RefreshToken`. It produced a token stored nowhere and validated by nothing |

## Package licensing

MediatR, MassTransit and FluentAssertions are pinned to their last Apache-2.0
releases. Later versions are commercially licensed, so upgrading them is a
licensing decision rather than a routine version bump. See
`Directory.Packages.props`.

Four OpenTelemetry contrib instrumentation packages have no stable release and
are pinned to prereleases.

## Consuming Girder

Packages are published to GitHub Packages. There is no anonymous read access,
so a consumer needs a personal access token with `read:packages`:

```bash
dotnet nuget add source https://nuget.pkg.github.com/DavidOeztuerk/index.json \
  --name girder \
  --username <your-github-username> \
  --password <token-with-read:packages> \
  --store-password-in-clear-text
```

Then reference only what the service actually runs:

```xml
<PackageReference Include="Girder.Infrastructure" Version="1.0.0" />
<PackageReference Include="Girder.Redis" Version="1.0.0" />
<PackageReference Include="Girder.Data.EntityFrameworkCore" Version="1.0.0" />
```

A service that speaks to no broker leaves out `Girder.Messaging.MassTransit`
and never sees MassTransit. That is the point of the split.

### Releasing

Publishing runs from a GitHub release, or manually via **Actions → Publish**
with a version. Either way the workflow builds and **runs the full test suite
before pushing** — a release tag points at a commit, not at a green run.

`1.0.0` means the ports are settled: a breaking change to any of them raises
the major version. Symbols and Source Link are included, so a debugger steps
into Girder source at the exact commit a package was built from.
