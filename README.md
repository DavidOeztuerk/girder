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
    .AddRedisTokenRevocation(maxTokenLifetime: TimeSpan.FromHours(24))
    .AddRedisEncryption();

// or, for a single instance and for tests
builder.Services
    .AddInMemoryCache("identity")
    .AddInMemorySecretManager()
    .AddInMemorySecurityAudit()
    .AddInMemoryResourceAuthorization()
    .AddInMemoryRateLimiting()
    .AddInMemoryTokenRevocation()
    .AddInMemoryRefreshTokens();
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
`AddCommunication`, `AddResilience`, `AddHealthChecks`, `AddObservability`,
`AddPrincipal`, `AddPasswordHashing`, `AddTokenSessions`.

Modules stand alone: each registers what it owns and nothing else. Where a
module genuinely needs something it cannot provide — a cache needs a cache
server — it says so **at startup**, naming the call that fixes it:

```
Girder is missing 1 provider registration(s):
  • AddCaching() needs IDistributedCacheService — call AddRedisCache(prefix) or AddInMemoryCache(prefix)
Provider packages: Girder.Redis, Girder.InMemory, Girder.Messaging.MassTransit, Girder.Data.EntityFrameworkCore.
```

The pipeline side does the same while it is being composed. `UseHttpCaching()`
without `AddCaching()`, or `UseRateLimiting()` without a store, throws there
rather than on the first request that happens to reach the middleware — in
production, naming a Girder-internal type the reader never wrote.

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

app.UseSharedInfrastructure(builder.Environment, "jobs-service", pipeline => pipeline
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

app.UseSharedInfrastructure(builder.Environment, "identity", pipeline => pipeline
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

Two paths write data into a log — the CQRS behaviour writing a whole command,
and the HTTP middleware writing a whole body. Both read **one** list of field
names, in `Girder.Core.Logging.SensitiveFieldNames`. Two lists agreed on the
day they were written and never again: the HTTP one knew about tokens and
addresses and not about names, so a profile update went into the log in full.

Redacted by field name: passwords and secrets, tokens, addresses, phone
numbers, dates of birth, bank and tax identifiers, **and a person's name** —
`displayName`, `firstName`, `lastName`, `username`, `street`, `city`,
`postcode`. **And by shape, wherever it appears**: an email address, a card number, an IBAN
or a national identifier inside any free text. Field names cannot catch what a
person types — a todo titled "reach me at ada@example.com" carries an address
in a field called `title`, and no list of names will ever cover that.

The match is **exact, never a substring**. `RequestName` and `ServiceName` name
software, not people, and a log with those redacted is one nobody can follow.

```csharp
sanitizer.RegisterSensitiveProperty("policyNumber", "caseReference");
```

The diagnostic handle that survives is the pseudonymous id — the logging scope
carries `UserId`, which identifies a person to your database and to nobody
reading the log.

**Request and response bodies are not logged at all by default**
(`Observability:EnableDetailedHttpLogging`). That is where a name, an address
and a password arrive in one place; when an operator turns it on to chase a
problem, the redaction above is what stands between that and wherever logs are
shipped.

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
