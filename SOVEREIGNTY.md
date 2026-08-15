# Digital sovereignty

Girder is built so that an application on top of it can run without depending on
infrastructure outside the operator's control or jurisdiction.

**What this document does not claim.** A library cannot make a deployment
sovereign. Where servers stand, who owns them and which law reaches them are
decisions made outside the code. What Girder can do — and what it does — is make
every such decision explicit, refuse to make one silently on your behalf, and
report what the running configuration actually points at.

## The frame

The European Commission's Cloud Sovereignty Framework scores eight objectives
(SOV-1 to SOV-8) on five assurance levels, SEAL-0 (no sovereignty) to SEAL-4
(full digital sovereignty). SEAL-4 requires two things in particular: data held
only within the EU, and *customer key sovereignty* — the provider has no
technical access to the encryption keys.

Most of those objectives are properties of a deployment, not of a library. Three
are directly affected by code:

| Objective | What Girder contributes |
|---|---|
| **SOV-2** Legal & jurisdictional | No SDK for a third-country provider is a dependency. Configuring one is refused rather than silently substituted. |
| **SOV-3** Data & AI | Outbound destinations must be declared; undeclared calls fail. Key material can be held outside the application. |
| **SOV-6** Technology | Every dependency is permissively licensed, and every backend can be replaced by one you run yourself. |

SOV-1, SOV-4, SOV-5, SOV-7 and SOV-8 — ownership, operations, supply chain,
certification, sustainability — belong to whoever runs the system.

## Backends that keep you sovereign

Girder talks to each of these through a wire protocol, not a vendor SDK, so the
sovereign option is a configuration change rather than a rewrite.

| Purpose | Use | Rather than | Why |
|---|---|---|---|
| Secrets | **OpenBao** | HashiCorp Vault | Vault moved to the Business Source License in 2023. OpenBao is the MPL-2.0 fork under Linux Foundation governance, and speaks the same HTTP API. |
| Cache | **Valkey** | Redis | Redis moved to RSALv2/SSPL in 2024. Valkey is the BSD-3-Clause fork under Linux Foundation governance, wire-compatible with the same client. |
| Logs & search | **OpenSearch** | Elasticsearch | Elasticsearch is under SSPL, which the Open Source Initiative does not recognise as open source. |
| Database | **PostgreSQL** | — | Already permissive, already self-hostable. |
| Messaging | **RabbitMQ** | — | MPL-2.0, self-hostable. |
| Telemetry | **OpenTelemetry** to a collector you run | a vendor endpoint | The exporter defaults to `localhost:4317`, so nothing leaves the host unless you point it elsewhere. |

None of these switches requires a change in Girder. The client libraries are
unchanged; only the endpoint moves.

## What the code does

### Outbound calls must be declared

```csharp
builder.Services.AddGirderEgressPolicy(p => p
    .AllowLoopback()
    .AllowPrivateNetworks()
    .Allow("openbao.internal"));
```

Every client created through `IHttpClientFactory` is then guarded, and a call to
anything else throws `EgressDeniedException` **before it leaves**. A log entry
about data that already left is a record, not a control.

Declaring nothing keeps the previous behaviour, so adding this changes nothing
until someone states an intent. Loopback and private ranges are opt-in rather
than assumed: a collector on localhost is still a destination.

A client built with `new HttpClient()` bypasses the factory and therefore this
guard. That is a limitation of the mechanism, not a gap the report hides.

### The configuration reports itself

```csharp
builder.Services.AddGirderSovereigntyReport(
    new DeclaredDependency("Secrets", configuration["OpenBao:Address"]),
    new DeclaredDependency("Telemetry", configuration["Otlp:Endpoint"]));
```

Connection strings are picked up automatically. Each destination is classified:

- **Self-hosted** — loopback, a private range, or an internal name.
- **Third-country provider** — a domain belonging to a provider subject to
  third-country access law, recognised by name. A region in Frankfurt does not
  change who operates the service.
- **Undetermined** — a public name whose operator cannot be told from the name.

**Undetermined is not a pass.** Recognition by name produces no false alarms and
cannot be complete; a provider missing from the list comes back undetermined,
and answering that is the operator's job. Credentials never reach the report.

### You hold the master key

Girder never generates and never stores the key that protects stored key
material. It asks for it, uses it, and forgets it when the process ends.

```csharp
// The sovereign arrangement: the key lives in a store you run.
builder.Services.AddSecretStoreMasterKey();

// Or, where the key arrives as an environment variable from outside:
builder.Services.AddConfiguredMasterKey();
```

Without one of these the application does not start. There is no generated
default, because a key the application invents is a key the operator does not
hold — which is exactly what SEAL-4 rules out.

Key material is sealed under that master key with AES-GCM before it reaches the
store, so whoever can read the cache holds ciphertext rather than keys, and a
modified entry fails to open rather than being used.

### Secrets stay where you put them

`ISecretProvider` is the seam that makes the secret store a choice. Only
providers you can run yourself ship in the box: OpenBao, environment variables,
an encrypted file, and ASP.NET Data Protection. Configuring `azure` or `aws`
fails with a message saying so.

Earlier versions shipped `AzureKeyVaultProvider` and `AwsSecretsManagerProvider`
that referenced no cloud SDK and kept secrets in a dictionary. They are gone.

The file provider derives its key with PBKDF2-HMAC-SHA256, 600,000 iterations,
and a salt generated once per installation. There is no fallback password, and
values are sealed with AES-GCM so a tampered file fails to open rather than
decrypting into something the caller trusts.

## What you still have to do

Girder cannot do these for you:

1. **Choose where it runs.** A sovereign stack on a third-country hyperscaler is
   not sovereign.
2. **Keep the master key somewhere you control.** Girder requires one and never
   invents it, but where it lives — a store you run, or an environment variable
   handed in by a platform you may not control — is your decision.
3. **Answer the undetermined entries** in the report — with a contract, not a
   host name.
4. **Keep an exit.** `IDataExportService` and `IDataErasureService` are the
   seams for GDPR Articles 20 and 17; they need implementations in each service
   before portability is real.

## Known gaps

- **No memory-hard password hashing.** Argon2id, Argon2i, Argon2d, BCrypt and
  SCrypt appear in `HashingAlgorithm` but are not implemented, and selecting one
  is refused rather than quietly answered with PBKDF2. Adding a real Argon2id
  would mean adding a dependency, which is a decision, not an omission.
- **`new HttpClient()` escapes the egress guard**, as noted above.

## Sources

- [EU Cloud Sovereignty Framework — SEAL levels and sovereignty objectives](https://www.nlighten.com/en/blog/the-eu-cloud-sovereignty-framework-explained-seal-levels-sovereignty-objectives-and-the-sovereignty-score/)
- [European Commission — A Cloud Sovereignty Framework for strategic procurement](https://data-en-maatschappij.ai/en/publications/europese-commissie-een-kader-voor-cloudsoevereiniteit-bij-strategische-aanbesteding)
- [OpenBao — the MPL-2.0 fork of Vault under Linux Foundation governance](https://openbao.org/)
- [Valkey — the BSD-3-Clause fork of Redis](https://valkey.io/)
- [Bitkom — Kriterien für Cloud-Souveränität in Europa](https://www.bitkom.org/Bitkom/Publikationen/Kriterien-fuer-Cloud-Souveraenitaet-in-Europa)
