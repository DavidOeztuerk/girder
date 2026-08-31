# Girder 4.0.1 → 4.0.2

Zwei Fehler im Katalog, beide aus 4.0.0. Nicht-brechend.

## 1. `IJwtService` stand ohne seinen Schlüsselbund im Behälter

`GirderModule.Jwt` registrierte `IJwtService` bedingungslos. `JwtService` nimmt
einen `KeyRing` als Konstruktorargument, und den legte kein Weg des Baumeisters
ab — weder `FromSharedSecret()` noch `VerifyOnly(...)` noch `Issue(...)`. Das
einzige `AddSingleton(keys)` stand auf dem alten Weg, den `AddGirder` nicht ruft.

Weil `IJwtService` `Scoped` ist, fiel das erst bei der ersten Anfrage auf, die
ein Token anfasst.

Auf dem alten Weg schützte die Kopplung: beide Registrierungen standen im
selben `if (keys is not null)`-Block — beide oder keine. Der Katalog hatte sie
getrennt und eine Hälfte mitgenommen. Sie stehen jetzt in
`AddJwtAuthentication(services, keys, authority, …)`, durch das jeder Weg läuft
— die einzige Stelle, an der sie sich nicht wieder trennen lassen.

**Was sich für dich ändert:** `UseDefaults()` allein registriert keinen
`IJwtService` mehr. Es gab dort auch vorher keinen brauchbaren; jetzt sagt der
Behälter das, statt ihn bei der ersten Anfrage zu verweigern. Wer ihn braucht,
sagt, woher die Schlüssel kommen:

```csharp
girder.UseDefaults().UseJwt(jwt => jwt.VerifyOnly(publicKey, keyId));
```

## 2. `GirderModule.Authorization` tat, was `ResourceAuthorization` heißt

Das Modul rief `AddResourceAuthorization()`. `[RequirePermission]` nennt eine
`Permission:`-Politik, die allein `PermissionPolicyProvider` beantwortet — und
der wird von `AddAuthorization()` registriert. Ohne ihn lehnt das Rahmenwerk jede
Anfrage an genau die Endpunkte ab, die das Attribut schützen soll, als „policy
not found". Fail-closed, aber lautlos: ein Absturz wird behoben, ein falscher
Name überlebt.

Jetzt gibt es zwei Module mit ehrlichen Namen:

| Modul | Was | In `UseDefaults()` |
|---|---|---|
| `Authorization` | Berechtigungspolitiken, `[RequirePermission]` | ✅ |
| `ResourceAuthorization` | `ResourceRead`, `ResourceOwner` und die übrigen | ❌ |

**Was sich für dich ändert:** wer Ressourcenpolitiken benutzt, nennt sie und
registriert einen Anbieter:

```csharp
girder.UseDefaults()
      .Use(GirderModule.ResourceAuthorization);   // + AddInMemoryResourceAuthorization()
```

Sie sind nicht in der Vorgabe, weil ihre beiden Handler
`IResourceAuthorizationService` als Konstruktorargument nehmen. Das kam beim
Einschalten von `ValidateOnBuild` heraus: die Anforderung war nie erklärt, und
ein Behälter mit ihnen ließ sich nicht bauen.

## Und der Wächter dazu

Die Konformitätstests bauen den Behälter jetzt mit `ValidateOnBuild` und
`ValidateScopes`. Ein Modul, das einen Verbraucher ohne seine Abhängigkeit
registriert, fällt damit beim Bauen auf — nicht in Produktion. Beide Fehler
oben wären so nie herausgekommen.

---

# Girder 4.0 → 4.0.1

Eine Zeile, dreimal. Nichts zu ändern auf deiner Seite.

## Fremde Antwortrümpfe stehen nicht mehr im Protokoll

`ServiceCommunicationManager` schrieb den Rumpf einer fehlgeschlagenen Antwort
auf `Warning` — beim GET, beim POST, und die Fehlerliste aus einem
`success: false`-Umschlag noch dazu. Dieser Rumpf ist die Auskunft des anderen
Dienstes über dessen Daten, und er landet hier wörtlich: ein Name, eine Adresse,
was die Gegenseite eben in ihren Fehler geschrieben hat.

Seit 4.0.0 kommt er ohnehin in `ServiceResponse.Body` beim Aufrufer an. Im
Protokoll ist er damit nicht nur riskant, sondern überflüssig — und das
Aufbewahren übernimmt man dabei für Daten, die dieser Dienst nie bekommen hat.

Protokolliert wird jetzt die Form: welcher Dienst, welcher Status, wie viele
Bytes beziehungsweise wie viele Fehler. Die Werte gehören dem Aufrufer, der
weiß, ob er sie behalten darf.

```
vorher   GET to UserService answered NotFound: {"error":"anna@example.com hat kein Konto"}
jetzt    GET to UserService answered NotFound (48 bytes)
```

Wer den Rumpf im Protokoll haben will, schreibt ihn selbst — dort, wo die
Entscheidung darüber hingehört.

---

# Girder 3.x → 4.0

Seven changes. One is the new entry point; six are fixes that could not wait,
because the new shape would have set them in stone.

If you call `AddSharedInfrastructure` and nothing else, only §4 and §5 can reach
you. If you take `GetAsync` from `IServiceCommunicationManager` or you run behind
a proxy, read §3 and §4 first.

---

## 1. `AddGirder` — a default you can see, and depart from

**What changed.** There were two entry points, and both were wrong for someone
who knows what they want. One decided thirteen modules for you. The other
**replaced** the default instead of adjusting it — taking it cost thirteen
modules *and* everything outside the module system (Serilog, Swagger, CORS, the
JSON conventions, the `HttpContext` accessor), with nothing said about it.

| Before | Now |
|---|---|
| `AddSharedInfrastructure(config, env, name)` | `AddGirder(config, env, name, g => g.UseDefaults())` |
| `AddSharedInfrastructure(config, env, name, infra => infra.AddJwtAuthentication().AddHealthChecks())` | `AddGirder(config, env, name, g => g.UseDefaults().Without(GirderModule.Communication, "no broker"))` |
| — no way to say why something was left out | `Without(module, reason)` — the reason is a parameter with no default |
| — a provider could not contribute a module | `Use(module, register)`, and `UseInMemoryCache(...)` from the provider package |

`UseDefaults()` is a call you can see and delete. Delete it and you get nothing,
the way Entity Framework gives you nothing without a provider.

**The two are not identical.** `UseDefaults()` contains only what starts with
nothing else registered. Three modules the old entry point included are not in
it, because each needs a decision Girder must not make for you:

| Module | Needs | Add it with |
|---|---|---|
| `HttpResponseCaching` | a distributed cache | `.Use(GirderModule.HttpResponseCaching)` + `AddRedisCache(prefix)` or `AddInMemoryCache(prefix)` |
| `Communication` | a message bus | `.Use(GirderModule.Communication)` + `AddMessaging(...)` |
| `Encryption` | a master key | `.Use(GirderModule.Encryption)` + `AddConfiguredMasterKey()` or `AddSecretStoreMasterKey()` |

A service that used them keeps working by naming them. A service that did not is
now free of three startup requirements it never wanted.

`AddSharedInfrastructure` still exists and still does what it did.

## 2. Forwarded headers are believed only from proxies you name

**What changed.** Rate limiting read `X-Forwarded-For` and `X-Real-IP`
unconditionally, and so did the audit trail, telemetry and input sanitisation.
A header the caller writes is not information about the caller.

Worst in `IsWhitelisted`: the exemption list was read through the same header and
carries `127.0.0.1` by default, so `X-Forwarded-For: 127.0.0.1` removed the rate
limit entirely, with no configuration at all.

**What to do.** If your service is reached directly, nothing. If it sits behind a
load balancer or ingress, name it — otherwise every request now counts as coming
from the proxy, which is one bucket for everyone:

```csharp
girder.UseRateLimiting(rate => rate.TrustForwardedHeadersFrom("10.0.0.0/8"));

// or, outside the rate limit builder
services.TrustForwardedHeadersFrom(["10.0.0.0/8"]);
```

`X-Real-IP` is no longer read anywhere. It is not part of the platform's
forwarded-headers set, and a second parser would be a second thing to get right.
A proxy that sets only that header says so:

```csharp
services.TrustForwardedHeadersFrom(["10.0.0.0/8"],
    options => options.ForwardedForHeaderName = "X-Real-IP");
```

## 3. One rate limiter, and it reads its own setting

**What changed.** There were three. `RateLimitMiddleware` asked
`IRateLimitService`, whose `CheckRateLimitAsync` runs over a rule collection —
empty unless someone registered rules, and the only way to register them,
`ConfigureRateLimitRules`, registers a singleton factory for `IRateLimitService`
that resolves `IRateLimitService` in its own body. Anyone who resolved it lost
the process with no log. `RateLimitingMiddleware` counted in an `IMemoryCache`,
per process, and was wired nowhere.

`DistributedRateLimitingMiddleware` remains.

**Removed:** `IRateLimitService`, `RateLimitService`, `InMemoryRateLimitService`,
`AddRedisRateLimiting()`, `AddInMemoryRateLimiting()`, `AddRateLimit()`,
`AddRateLimitMiddleware()`, `ConfigureRateLimitRules()`, `RateLimitMiddleware`,
`RateLimitingMiddleware`, `RateLimitOptions`, `RateLimitingOptions`, and the rule
model that served them.

**What to do.** Delete the calls. Rate limiting counts through the cache, so
`AddRedisCache(prefix)` or `AddInMemoryCache(prefix)` is what decides whether the
counters are shared between instances.

**Also changed:** `ClientIdStrategy` and `CustomClientIdExtractor` were written on
an options class and read nowhere — asking for per-origin counted per user.
`EnableIpRateLimiting` and `EnableUserRateLimiting` said the same thing twice and
could disagree. All four are replaced by one setting that is read on every
request:

| Before | Now |
|---|---|
| `EnableUserRateLimiting = true, EnableIpRateLimiting = true` | `Subject = RateLimitSubject.UserThenOrigin` (the default) |
| `EnableIpRateLimiting = true, EnableUserRateLimiting = false` | `Subject = RateLimitSubject.Origin`, or `.PerOrigin()` |
| `EnableUserRateLimiting = true, EnableIpRateLimiting = false` | `Subject = RateLimitSubject.User`, or `.PerUser()` |
| `ClientIdStrategy = Custom` + `CustomClientIdExtractor` | `.PerSubject(context => ...)` |

## 4. A status code reaches the caller

**What changed.** `IServiceCommunicationManager.GetAsync` and `SendRequestAsync`
returned `TResponse?` and mapped every non-2xx onto `null`, so "does not exist",
"exists and the field is empty" and "the far service is broken" were one value.

```csharp
// before
var user = await services.GetAsync<User>("UserService", "/api/users/1");
if (user is null) { /* which of the three? */ }

// now
var answer = await services.GetAsync<User>("UserService", "/api/users/1");
if (answer.IsSuccess) { Use(answer.Value!); }
else if (answer.Status == HttpStatusCode.NotFound) { /* ... */ }
else { logger.LogWarning("{Status}: {Body}", answer.Status, answer.Body); }
```

Only a call that got no answer at all still throws.

**Also changed:** `ResilientHttpPolicyHandler` threw on every non-success status,
and the retry policy turned that into three attempts — a `404` asked three times,
a `401` three times. It now retries only what a second attempt could answer
differently: `408`, `429` and `5xx` except `501`. Everything else comes back on
the first attempt, as the answer it is.

Responses that failed are no longer cached: a remembered `404` keeps answering
after the resource appears.

## 5. The correlation id travels on its own

**What changed.** `LoggingBehavior` reads
`Activity.Current?.GetBaggageItem("CorrelationId")`, and `AddBaggage` appeared
nowhere in Girder — a reader with no writer. What was written was `SetTag`, and a
tag stays on the span it was written to.

The id therefore travelled only through `ServiceCommunicationManager` or
MassTransit. A bare `HttpClient` sent nothing.

**What to do.** Nothing, if you use `AddGirder` — `CorrelationPropagation` is in
`UseDefaults()`. Otherwise:

```csharp
services.AddCorrelationIdPropagation();
```

Every client the factory builds then carries the id. One you set yourself is left
alone, and outside a request none is invented.

## 6. The session store joins a transaction you already have

Unchanged from 3.0.1, and repeated here because it is what makes an audit row and
the change it records commit together. See the 3.0 → 3.0.1 section below.

## 7. Nothing else

Identity, permissions, resources, sessions, passwords, token revocation,
messaging, persistence, health checks, telemetry and the sovereignty report are
unchanged.

---

# Girder 3.0 → 3.0.1

One bug fix. Nothing to change on your side.

## The refresh token store no longer insists on owning the transaction

`EntityFrameworkRefreshTokenStore.TryConsumeAsync` opened a transaction
unconditionally. A caller that already had one — writing an audit row in the same
transaction as the change it records, which is the pattern this library argues
for elsewhere — got:

```
System.InvalidOperationException: The connection is already in a transaction
and cannot participate in another transaction.
```

The line ran through the middle of one interface: `CreateAsync` only saves, and a
save joins whatever is open, so **signing in worked and refreshing did not**.

It now opens a transaction only when none is open, and joins the caller's
otherwise. A rotation inside a caller's bracket commits and rolls back with it.
The store never rolls a caller's transaction back — where a concurrent refresh
has already taken the row, the conditional update matched nothing, there is
nothing to undo, and the caller keeps its work.

No API changed. If you never had a transaction open across a refresh, nothing is
different for you.

---

# Girder 2.x → 3.0

Five things changed. Three are bug fixes — two can stop a service starting, one
makes something start working that silently did nothing — and two are namespaces
nobody outside Girder had reason to write.

If you register a cache, inject `IETagGenerator` nowhere, and never named
`ProviderRequirements`, upgrading is a version number.

---

## 1. `AddCQRS()` takes a cache only where you actually cache

**What changed.** `AddCQRS(assemblies)` scans what you hand it. If nothing in
those assemblies implements `ICacheableQuery` or `ICacheInvalidatingCommand`,
`CachingBehavior` and `CacheInvalidationBehavior` are no longer put in the
pipeline at all. If something does, the cache is required — and missing it now
refuses the start rather than surfacing on whichever request happens to reach
the behaviour:

```
Girder is missing 1 provider registration(s):
  • AddCQRS() (GetJobQuery implements ICacheableQuery) needs IDistributedCacheService — call AddRedisCache(prefix) or AddInMemoryCache(prefix)
Provider packages: Girder.Redis, Girder.InMemory, Girder.Messaging.MassTransit, Girder.Data.EntityFrameworkCore.
```

The count is missing services, not modules that asked for them: where several
registrations need one cache, that is one line and one thing to register, with
every one of them named.

**Why.** Both behaviours took `IDistributedCacheService?` and checked it for
`null`, which reads as "the cache is optional". The container does not honour C#
nullability: without a default value it throws rather than passing `null`. So
the promise in the type never held, and the first `Send` died on a Girder type
the caller never wrote. Meanwhile a service that caches nothing had to register
a cache anyway — and a composition root is the place you look to see which
cross-cutting decisions a service took, so it ended up claiming one it had not.

Giving the parameters `= null` would have worked and been worse: marking a query
`ICacheableQuery` with no cache registered would then produce no error, no log
line, and no caching. A feature you switch on that does nothing is worse than
one that is missing.

**What to do.**

- *You cache and you register a cache.* Nothing. This is the common case.
- *You cache nothing.* You may delete `AddInMemoryCache(...)` from your
  composition root if it was only there to satisfy `AddCQRS()`. Nothing is
  cached either way; the difference is that the file stops saying otherwise.
- *You cache and you register no cache.* The service will not start. It was
  never caching — the behaviour swallowed it — so either register a cache, or
  drop `ICacheableQuery` from the query.

Any `IDistributedCacheService` satisfies the requirement. The check asks for the
interface, not for who registered it, so a provider you wrote yourself counts.

---

## 2. `ProviderRequirement` and `ProviderRequirements` moved

**What changed.** Both moved from `Girder.Infrastructure.Builder` to
`Girder.Abstractions.Hosting`.

**Why.** `AddCQRS()` lives in `Girder.Application`, which
`Girder.Infrastructure` references — so it could not reach the collector that
holds what a registration needs. The types describe a port, not an engine, and
now sit with the other ports where everything can see them.

**What to do.** Change the `using` if you named either type. You almost
certainly did not: `InfrastructureBuilder.RequiresProvider<T>(requiredBy,
remedy)` is unchanged, and it is the only thing that ever wrote to them.

New, and only if you want it: the same declaration is now available on a plain
collection, for registrations that stand outside the `AddSharedInfrastructure`
chain.

```csharp
using Girder.Application.Hosting;

services.RequiresProvider<IDistributedCacheService>(
    "AddMyThing()", "AddRedisCache(prefix) or AddInMemoryCache(prefix)");
```

---

## 3. The CQRS pipeline no longer needs `AddHttpResponseCaching()`

**What changed.** `CacheInvalidationBehavior` no longer takes `IETagGenerator`.
It clears stale ETags through the `IDistributedCacheService` it already holds.
`IETagGenerator` itself moved from `Girder.Application.Abstractions` to
`Girder.Infrastructure.Caching.Http`, beside its implementation and its one
legitimate consumer.

**Why.** `IETagGenerator` is HTTP: its documentation says "for HTTP responses",
its patterns are API paths, and the only thing that ever registered it was
`AddHttpResponseCaching(...)`. A transport-independent layer hung off it, so a
service had to switch on HTTP response caching to make its **command** pipeline
start — and its composition root then said something nobody meant.

Nine methods were reachable through that dependency. The pipeline called one,
`InvalidateETagsByPatternAsync`, and that one is a single
`RemoveByPatternAsync` behind a string concatenation. Splitting the interface
would have been ceremony for a one-liner; the dependency simply goes away.

The shared part is now the key prefix rather than an interface —
`Girder.Abstractions.Caching.CacheKeys.ETagPrefix`. The agreement about that key
already existed; it was a `private const` and a comment instead of a name.

**What to do.**

- *You inject `IETagGenerator` in a controller or service of your own.* Change
  the `using` to `Girder.Infrastructure.Caching.Http`. Nothing else about it
  moved — same methods, same behaviour, still registered by
  `AddHttpResponseCaching(...)`.
- *You called `AddHttpResponseCaching(...)` only to satisfy `AddCQRS()`.* Delete
  it. ETag invalidation from commands no longer goes through it.
- *You want HTTP response caching.* Nothing. `AddCaching()` still brings it, and
  `ETagInvalidationPatterns` on your commands still clear the ETags the
  middleware stored — they meet in the cache, under the same prefix as before.

---

## 4. In-memory pattern invalidation actually removes keys now

**What changed.** `Girder.InMemory`'s `RemoveByPatternAsync` applies the store's
key prefix to the pattern, and matches a wildcard anywhere in it.

**Why.** It matched the raw pattern against keys that had the prefix applied. So
with `AddInMemoryCache("jobs-service")` — and the prefix is not optional there —
a pattern matched nothing and the call removed no keys. Every
`InvalidationPatterns` and `ETagInvalidationPatterns` entry in an in-memory
deployment did nothing, quietly. The Redis store prefixes its `SCAN` pattern and
always did, so the two disagreed about the same contract.

**What to do.** Nothing, unless you were working around it. Check any place you
passed a pattern that already carried the prefix to compensate: it will now be
prefixed twice and match nothing.

---

## 5. `AddHttpResponseCaching()` requires a cache

**What changed.** `AddHttpResponseCaching(...)` declares that it needs an
`IDistributedCacheService`, checked at startup like every other module.
`ETagGenerator` takes one as a mandatory constructor parameter, and the four
branches that did nothing when it was absent are gone.

**Why.** The parameter was `IDistributedCacheService?` with no default value, so
the container threw rather than passing `null` — which made those four branches
unreachable. Read either way it was wrong: calling `AddHttpResponseCaching(...)`
on its own crashed on a Girder type the caller never wrote, and the degradation
the code appeared to offer was never once taken.

Of the nine methods on `IETagGenerator`, four exist only to store and retrieve
ETags. An ETag store with nowhere to store is not one, so the cache is required
rather than made optional.

**What to do.**

- *You reach it through `AddCaching()`.* Nothing. That module already required a
  cache, and the two requirements are reported as the one registration they are.
- *You call `AddHttpResponseCaching(...)` directly.* Register a cache —
  `AddInMemoryCache(prefix)` or `AddRedisCache(prefix)` — or drop the call. It
  was not working without one.
- *You construct `ETagGenerator` yourself.* Pass a cache instead of `null`.

---

## 6. Nothing else

No other signature changed and no other default moved.

---

# Girder 1.x → 2.0

Three things were removed and one default changed. Nothing else moved.

If you are on 1.5.x and use none of the four, upgrading is a version number.

---

## 1. Access tokens now last 15 minutes, not 60

**What changed.** `JwtSettings.ExpireMinutes` defaults to `15`.

**Why.** That number is the window in which a signed-out person is still let in
— every service verifies an access token from its signature alone and asks
nothing. Sixty minutes is a long time for something nobody chose, and shipping
it as the default meant most deployments had it.

**What to do.** Nothing, if fifteen minutes suits you. Clients refresh roughly
four times as often; each refresh is one read and two writes in your own
database.

To keep the old behaviour, say so:

```json
{ "JwtSettings": { "ExpireMinutes": 60 } }
```

**How to tell it matters to you.** If you run no revocation store, this is how
long "sign out" takes to reach the services that only verify. Shortening it is
the cheap way to close that window; a revocation store is the way to close it
to zero.

---

## 2. `UserClaims.FirstName` and `.LastName` are gone

**What changed.** Both properties were removed. They were marked `[Obsolete]` in
1.2.

**Why.** Neither was ever written into a token. They were validated as required
— so Girder refused to issue a token to anyone whose name does not split into
two parts, including mononyms and service accounts — and then discarded.

**Before**

```csharp
var token = await jwt.GenerateTokenAsync(new UserClaims
{
    UserId = subject.ToString(),
    Email = email,
    FirstName = person.FirstName,   // never reached the token
    LastName = person.LastName
});
```

**After**

```csharp
var token = await jwt.GenerateTokenAsync(new UserClaims
{
    UserId = subject.ToString(),
    Email = email
});
```

**If you need a display name in the token**, put it in `CustomClaims`, where it
is actually emitted:

```csharp
CustomClaims = new Dictionary<string, string> { ["name"] = person.DisplayName }
```

Consider whether you want it there at all: a token travels through logs and
proxies, and a name is one of the few things in it that identifies a person to
someone reading over a shoulder.

---

## 3. `IJwtService.GenerateRefreshTokenAsync()` and `TokenResult.RefreshToken` are gone

**What changed.** The method was removed from the interface and the property
from the result.

**Why.** The method returned 64 random bytes that were **stored nowhere and
validated by nothing**, and `TokenResult.RefreshToken` was filled with them. A
developer who saw that field and built a `/refresh` endpoint had nothing to
compare against — so either they wrote the whole store themselves, in which case
Girder generating the value bought nothing, or they treated the presence of a
token as the check, in which case they had a hole because a library looked as
though it had handled this.

**Before**

```csharp
var result = await jwt.GenerateTokenAsync(claims);
return new { access = result.AccessToken, refresh = result.RefreshToken };
```

**After** — refresh tokens come from `ITokenSessionService`, which records them:

```csharp
builder.Services.AddSharedInfrastructure(config, env, "identity", infra => infra
    .AddJwtAuthentication(o => { /* keys */ })
    .AddTokenSessions());

builder.Services.AddInMemoryRefreshTokens();
// or, for the database you already run:
// builder.Services.AddEntityFrameworkRefreshTokens<AppDbContext>();
```

```csharp
var signIn = await sessions.SignInAsync(subject);
var access = await jwt.GenerateTokenAsync(new UserClaims
{
    UserId = subject.ToString(),
    Email = email,
    SessionId = signIn.Session.ToString()   // so a revocation can name this device
});

return new { access = access.AccessToken, /* signIn.RefreshToken → HttpOnly cookie */ };
```

**If you already built your own refresh flow**, keep it. `ITokenSessionService`
is a service, not a requirement — implement `IRefreshTokenStore` over your
existing table if you want the rotation and reuse detection without changing
where anything lives.

### The EF Core store needs a table

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) =>
    modelBuilder.ConfigureGirderRefreshTokens();
```

Then add a migration. The table is yours, in your database; Girder holds no
connection of its own and runs no cleanup — call `PurgeAsync(olderThan,
batchSize)` from whatever already runs your scheduled work.

---

## 4. Nothing else

These stayed exactly as they were, and a 1.x service that uses them compiles
and behaves the same:

- `AddSharedInfrastructure` in both forms, and every module
- `AddJwtAuthentication()` with no arguments — still the shared secret from
  `JwtSettings:Secret` or `JWT_SECRET`
- The whole pipeline builder
- Every provider registration
- Token revocation, in full

---

## Worth doing while you are here

Not required, and not part of 2.0. Each is a change a 1.5 service can make
today.

**Give each service only the key half it needs.** With a shared secret the
verification key *is* the signing key, so every service that checks a token can
mint one — for any subject, with any role. A pair separates them, and the type
enforces it:

```csharp
// the issuing service
o.SigningKey = SigningKey.FromEcdsaPrivateKey(privateKey, kid);
o.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(publicKey, kid));

// everyone else — holds nothing it could sign with
o.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(publicKey, kid));
```

Both algorithms can be live at once, so the change costs nobody their session:

```csharp
o.ValidationKeys.Add(SigningKey.FromEcdsaPublicKey(publicKey, kid));
o.ValidationKeys.Add(SigningKey.FromSharedSecret(legacySecret, kid: null));
```

Drop the secret once every token issued under it has expired.

**Stop writing your own password hashing.** `AddPasswordHashing()` gets the salt,
the work factor, the fixed-time comparison and the upgrade path right, with no
package and no licence. Pass `null` for the stored entry when the account does
not exist — that is what keeps an unknown address as slow to answer as a wrong
password.

**Check where your refresh token lives in the browser.** Not `localStorage` and
not `sessionStorage`: script reads both, and a refresh token is worth days where
an access token is worth minutes.
