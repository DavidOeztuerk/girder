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
