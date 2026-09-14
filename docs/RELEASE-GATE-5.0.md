# Release-Gate Noelia 5.0

**Stand:** 14.09.2026 · **Kandidat:** `5.0.0-preview.1` · **Ergebnis:** bestanden

Das Gate verwendet `~/Projects/Demo` als fremden Paketverbraucher.
WorkerTransfer war weder Teil der Prüfung noch wurde dort etwas verändert.
Demo ist kein Git-Repository; sein lokaler Stand ist deshalb zusätzlich in
`~/Projects/Demo/RELEASE-GATE-5.0.md` beschrieben und nicht als Commit
ausgegeben.

## Ergebnis

| Grenze | Nachweis | Ergebnis |
|---|---|---|
| Noelia-Quellbaum | Release-Build und vollständige Tests | 0 Warnungen; 132 Core- und 3206 Infrastructure-Tests |
| Ausgelieferte Pakete | Audit aller direkten und transitiven Abhängigkeiten | 13 von 13 ohne bekannte Vulnerabilities |
| Paketverbraucher | leerer Paketcache, exakte Kandidatenversion, keine Projektverweise | 13 Pakete, keine Versionsmischung |
| Einzelpakete | genau ein direkter `Noelia.*`-Verweis je Probe | 29 Tests in 13 Probes |
| Microservices | Gateway, User und Todo als getrennte Hosts | 26 Tests, einschließlich Tokenübergabe und Eigentümertrennung |
| Monolith | ein Host ohne Ocelot oder Gateway | 6 Tests |
| Frontend | npm-Clean-Install, DOM-Tests und Audit | 3 Tests; 0 Vulnerabilities |
| Fehlerisolation | Restore mit absichtlich fehlendem `Noelia.Redis` | erwartetes `NU1101`; Demo-Dateien und `bin/obj` bytegleich |
| Container | Microservice-Stack und Monolith aus denselben lokalen Paketen | alle Healthchecks grün; Production-Dashboards überall 404 |
| Geheimnis-Canary | Ergebnisse, HTML, Logs und vollständiges Gate-Protokoll | kein Treffer |

## Paketmatrix

| Direkt installiertes Paket | Probe | Extern geprüfte Wirkung |
|---|---|---|
| `Noelia.Abstractions` | `Noelia.Abstractions.Probe` | unveränderlicher Modulvertrag mit genauer Anbieterhilfe |
| `Noelia.Application` | `Noelia.Application.Probe` | Cache-CQRS verweigert den Start ohne seinen Anbieter |
| `Noelia.Contracts` | `Noelia.Contracts.Probe` | Paging-Wireformat und wirksame DataAnnotations |
| `Noelia.Core` | `Noelia.Core.Probe` | typisierte Identität im JSON und Geheimnisbereinigung |
| `Noelia.Dashboard` | `Noelia.Dashboard.Probe` | einziges Modul/Check, 404-Policy, read-only, wertfreie Shapes |
| `Noelia.Data.EntityFrameworkCore` | `Noelia.Data.EntityFrameworkCore.Probe` | EF-Refresh-Token-Modul und Tabellenabbildung |
| `Noelia.Http` | `Noelia.Http.Probe` | atomare In-Process-Bremse ohne Schlüsseloffenlegung |
| `Noelia.InMemory` | `Noelia.InMemory.Probe` | eigenständiger Cache-Anbieter und Roundtrip |
| `Noelia.Infrastructure` | `Noelia.SecurityHeaders.Probe` | Security-Headers, Runtime-Checks, kein anonymer Security-Endpunkt |
| `Noelia.Messaging.MassTransit` | `Noelia.Messaging.MassTransit.Probe` | Event-Bus-Modul ohne Zugangsdaten im Log |
| `Noelia.Passwords.Argon2` | `Noelia.Passwords.Argon2.Probe` | Hashen/Prüfen über das Argon2-Modul |
| `Noelia.Passwords.BCrypt` | `Noelia.Passwords.BCrypt.Probe` | Hashen/Prüfen über das BCrypt-Modul |
| `Noelia.Redis` | `Noelia.Encryption.Probe` | AEAD, Fremdschlüssel- und Manipulationsablehnung, kein Klartext/Digest |

`Core`, `Contracts`, `Abstractions` und `Application` sind absichtlich keine
Runtime-Infrastrukturmodule. Ihre Modulaktivierung ist `NotApplicable`; die
Probes prüfen stattdessen ihre öffentlichen Verträge. Ein künstliches leeres
Modul würde nur Anwesenheit vortäuschen.

## Fehlende Anbieter und bewusste `NotApplicable`-Ergebnisse

Die Start-Gegenprobe für `TokenSessions` meldete genau:

- Modul `TokenSessions`;
- fehlender Vertrag `IRefreshTokenStore`;
- `Noelia.InMemory → UseInMemoryRefreshTokens()` oder
  `Noelia.Data.EntityFrameworkCore → AddEntityFrameworkRefreshTokens<TContext>()`.

Weitere Paketverträge halten fest:

- `Data.EntityFrameworkCore.RefreshTokens` braucht den gewählten `DbContext`;
- `Redis.Encryption` braucht `IConnectionMultiplexer` über
  `AddRedisConnection(...)` sowie `IMasterKeyProvider` über
  `AddConfiguredMasterKey()` oder `AddSecretStoreMasterKey()`;
- Cache-fähiges CQRS braucht `IDistributedCacheService` über
  `AddRedisCache(prefix)` oder `AddInMemoryCache(prefix)`.

Der einzige bewusst beobachtete Runtime-Status `NotApplicable` ist
`noelia.sessions.refresh-cookie` in User-Service und Monolith: beide geben den
Refresh-Token selbst als Cookie aus und registrieren kein ASP.NET-Cookie-
Authentifizierungsschema. Die Anwendungstests prüfen deshalb die tatsächlich
ausgegebene Cookie-Grenze (`HttpOnly`, `SameSite=Strict`) separat. Noelia
behauptet für eine fremde Cookie-Erzeugung keinen Pass.

## Während des Gates gefundene und behobene Lücken

1. `PagedRequest` trug seine DataAnnotations auf Konstruktorparametern statt
   auf den erzeugten Properties; ungültige Seitengrößen wurden vom üblichen
   Validator akzeptiert. Die Attribute zielen jetzt ausdrücklich auf
   `property`, mit drei Gegenproben.
2. Zwei Logging-Testklassen überschrieben parallel den globalen Serilog-Logger
   und `Console.Out`. Eine nicht parallelisierte xUnit-Collection isoliert den
   Prozesszustand.
3. Das Docker-Restore erbte das öffentliche Package-Source-Mapping und
   ignorierte dadurch den lokalen Kandidatenfeed. `NuGet.Docker.Config` bindet
   `Noelia.*` nun ausdrücklich an die Kandidatenquelle und alle Fremdpakete an
   NuGet.org.

## Veröffentlichungsgrenze

Dieses Dokument erlaubt keine Veröffentlichung aus einem lokalen Arbeitsbaum.
`5.0.0` darf erst von einem gemergten Commit mit grüner GitHub-CI und über den
vorhandenen Trusted-Publishing-Workflow nach NuGet.org gehen. Bis die Pakete
dort wirklich abrufbar sind, bleibt der geprüfte Stand ein lokaler Kandidat.
