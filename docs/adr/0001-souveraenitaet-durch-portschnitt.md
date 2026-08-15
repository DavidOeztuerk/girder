# ADR-0001 — Souveränität durch Portschnitt, nicht durch Konfiguration

**Status:** Vorschlag · **Datum:** 2026-08-15

## Frage

Girder soll digital souverän sein. Heißt das, dass Girder keine
Infrastrukturpakete referenzieren darf und der Nutzer alles über
Konfiguration wählt?

Teils. Die Frage vermischt zwei Dinge, die auseinandergehalten werden müssen,
sonst baut man viel und gewinnt nichts.

## Was digitale Souveränität hier wirklich bedeutet

Souveränität heißt: niemand kann dir den Betrieb entziehen, die Bedingungen
gegen dich ändern oder auf deine Daten zugreifen, ohne dass du ausweichen
kannst. Daraus folgen zwei getrennte Ebenen:

**Ebene 1 — welchen Server du betreibst.** Redis oder Valkey, Elasticsearch
oder OpenSearch, RabbitMQ oder Kafka, Postgres oder Oracle. Hier liegt die
Souveränität über *Daten*: wer betreibt das Ding, unter welchem Recht, unter
welcher Lizenz, kannst du es selbst hosten.

**Ebene 2 — gegen welche Bibliothek du kompilierst.** `StackExchange.Redis`,
`MassTransit`, `EF Core`. Diese Bibliotheken laufen in *deinem* Prozess auf
*deiner* Maschine. Sie telefonieren nicht nach Hause. Hier liegt keine
Datensouveränität, sondern eine **Lieferkettenfrage**: Lizenz, Wartung,
Forkbarkeit.

Eine Abstraktion über Ebene 2 erhöht die Souveränität **nicht** automatisch.
Sie erhöht die *Portabilität*. Das ist verwandt, aber nicht dasselbe — und
eine Abstraktion, die formgleich mit der versteckten API ist, bringt weder
das eine noch das andere.

### Der Beweis, dass das keine Wortklauberei ist

`StackExchange.Redis` spricht RESP2/RESP3. Valkey (BSD, Linux Foundation)
implementiert dasselbe Protokoll byte-identisch; das Paket nennt Valkey und
Garnet ausdrücklich als unterstützte Server. **Der souveräne Wechsel weg von
Redis ist eine Verbindungszeichenfolge, keine Abstraktion.** Wer hier
abstrahiert, um Souveränität zu gewinnen, baut eine Kapselung für ein Problem,
das das Protokoll schon gelöst hat.

Umgekehrt: `MassTransit 8.3.6` ist Apache-2.0, läuft lokal, greift auf nichts
zu. Trotzdem ist es das **größere** Souveränitätsrisiko — weil v9 kommerziell
ist und v8 nur noch bis Ende 2026 Sicherheitskorrekturen bekommt. Das ist in
vier Monaten.

Die Lizenz und die Wartung entscheiden, nicht das Gefühl von Kopplung.

## Die Prüfliste

### Eine Abhängigkeit darf in ein Girder-Paket, wenn alle drei gelten

1. **Lizenz** — OSI-anerkannt, unwiderruflich, forkbar (MIT, Apache-2.0, BSD,
   MPL-2.0, PostgreSQL). Nicht BSL, SSPL, RSAL, nicht kommerziell.
2. **Kein gehosteter Dienst nötig** — arbeitet gegen etwas, das du selbst
   betreiben kannst.
3. **Gewartet** — bekommt Sicherheitskorrekturen.

### Eine Abstraktion ist gerechtfertigt, wenn alle drei gelten

1. Es gibt **mindestens zwei** souverän betreibbare Implementierungen.
2. Der Port ist **kleiner** als die API, die er verbirgt — fachlich
   geschnitten, nicht treibergeschnitten.
3. Der Wechsel ist eine realistische Betriebsentscheidung.

Punkt 2 ist der, an dem die meisten Abstraktionen scheitern.

## Der Befund

Gemessen am 15.08.2026 gegen den tatsächlichen Quellcode und die
`.nuspec`-Metadaten im lokalen NuGet-Zwischenspeicher.

| Abhängigkeit | Lizenz (gepinnt) | Urteil | Folge |
|---|---|---|---|
| MediatR 12.5.0 | Apache-2.0 — letzte freie Fassung, ab v13 kommerziell | Lieferkette | Port nötig |
| MassTransit 8.3.6 | Apache-2.0 — Korrekturen nur bis Ende 2026 | Lieferkette, **mit Datum** | Port nötig |
| Serilog.Sinks.Elasticsearch 10.0.0 | Apache-2.0, aber **eingestellt**, Ziel ist ein SSPL-Server | fällt durch 2 und 3 | entfernen |
| Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | PostgreSQL | souverän — aber *ist* der Nagel | in die Anwendung |
| Microsoft.EntityFrameworkCore 10.0.11 | MIT | souverän, **ist selbst die Abstraktion** | behalten |
| StackExchange.Redis 3.1.13 | MIT, spricht Valkey/Garnet/KeyDB | souverän | behalten, hinter Ports |
| Serilog 4.4.0 | Apache-2.0 | souverän | Kern behalten, Senken raus |
| OpenTelemetry 1.17.0 | Apache-2.0, herstellerneutral von Bauart | souverän | API/SDK behalten, Exporter raus |
| FluentValidation 12.1.1 | Apache-2.0 | souverän | behalten |
| Swashbuckle 10.2.3 | MIT | souverän | behalten |

### Was der Quellcode dazu sagt

- **Genau eine Zeile nagelt Postgres fest**: `Extensions/DatabaseExtensions.cs:27`
  ruft `options.UseNpgsql(...)`. Sechs Dateien benutzen EF Core, alle über die
  Abstraktion. Das ist keine Umbauarbeit, das ist ein Umzug.
- **Die MassTransit-Oberfläche ist winzig**: Girder benutzt `IPublishEndpoint`
  (4×) und `ConsumeContext` (4×, Korrelations-Filter). Ein Port mit zwei bis
  drei Methoden verbirgt ein Rahmenwerk. Prüfpunkt 2 ist klar erfüllt.
- **Serilog**: Girder pinnt acht Senken-Pakete, benutzt im Code drei
  (`Console`, `File`, `Elasticsearch`).
- **Redis ist der Sonderfall**: elf Dateien unter `Security/` nehmen `IDatabase`
  direkt — Strings, Sets, Sorted Sets, Hashes, Listen, Key-Expiry *und*
  `ScriptEvaluateAsync` (Lua). Rund 22 Operationen. Eine Abstraktion darüber
  wäre formgleich mit `IDatabase` und fällt durch Prüfpunkt 2.

## Entscheidung

### 1. Abstrahiert wird am Port, nicht am Treiber

Der Fehler wäre `IKeyValueStore` mit 22 Methoden — Redis unter anderem Namen.
Richtig ist der fachliche Schnitt: `ITokenRevocationStore` (4 Methoden),
`ISecurityAuditStore`, `IEncryptionKeyStore`. Drei solche Ports gibt es
bereits und sie belegen, dass der Schnitt trägt: `IDistributedRateLimitStore`
(`Caching/`), `IDistributedCacheService` (`Girder.Application.Abstractions`)
und `ISecretProvider` (`Security/Secrets/`) — letzterer ist schon die
Souveränitätsnaht, an der OpenBao statt Vault hängt.
Diese Ports sind *kleiner* als die Redis-API, und
`Girder.Caching.Redis` implementiert sie mit Sorted Sets und Lua, wie es
angemessen ist.

Damit gilt: Die elf Security-Dateien wandern als *Implementierung* in das
Redis-Paket. `Girder` selbst kennt nur die Ports.

### 2. Der Anbieter wird im Code gewählt, nicht in der Konfiguration

Das ist die Stelle, an der die Anforderung „alles aus der Config" korrigiert
werden muss, und der Grund ist zwingend:

> Damit Girder aus `"Cache": { "Provider": "Redis" }` einen Redis-Cache bauen
> kann, müsste Girder `StackExchange.Redis` referenzieren. Genau das soll weg.

Es bliebe nur Reflexion mit Assembly-Laden — fragil, bricht Trimming und AOT,
und scheitert zur Laufzeit statt beim Übersetzen.

EF Core löst das anders, und Girder übernimmt das:
`UseNpgsql()` liegt **im Npgsql-Paket**, nicht in EF Core. Der Kern weiß nie,
dass Npgsql existiert.

**Regel: Die Konfiguration liefert die Verbindungsdaten. Der Code liefert die
Anbieteridentität — in der Composition Root der Anwendung.**

```csharp
// in der Anwendung, nicht in Girder
builder.Services.AddGirder(builder.Configuration)
    .UseEntityFrameworkCore<AppDbContext>(o => o.UseNpgsql(cs))  // App wählt
    .UseRedisCache()                                             // oder UseInMemoryCache()
    .UseMassTransit();                                           // oder später etwas anderes
```

Die Anwendung installiert dafür die NuGet-Pakete, die sie will. Wer kein
Messaging braucht, installiert `Girder.Messaging.*` nicht und bekommt die
Abhängigkeit nie.

#### Eine begründete Ausnahme: Serilog

Serilog bringt mit `Serilog.Settings.Configuration` und
`ReadFrom.Configuration()` genau die Maschinerie mit, die Senken zur Laufzeit
über Assembly-Scan lädt. Das ist Serilogs eigenes, unterstütztes Muster. Für
Protokollierung funktioniert „aus der Config" also nativ: Die Anwendung
installiert das Senken-Paket und nennt es in `appsettings.json`. Girder darf
nur keine Senke pinnen.

Das ist kein Widerspruch zur Regel, sondern ihre Bestätigung: Wo das Ökosystem
die Auflösung mitliefert, nutzen wir sie. Wo nicht, erfinden wir keine.

### 3. Eine Solution, mehrere Pakete

Kein eigenes Repository je Bibliothek. EF Core, Serilog, MassTransit und
OpenTelemetry .NET fahren alle ein Repo, eine Solution, viele Projekte, eine
Version. Getrennte Solutions bedeuten getrennte Versionierung und
repoübergreifende Änderungen für einen einzigen Umbau.

### 4. Zielschnitt

```
Girder.Core                      Domänenprimitive.        0 Pakete  (heute erfüllt)
Girder.Contracts                 Grenz-DTOs.              0 Pakete  (heute erfüllt)
Girder.Abstractions              ALLE Ports.              0 Infrastrukturpakete
Girder.Application               CQRS gegen die Ports.
Girder                           Builder/Motor. Kein Anbieter.
                                 — entspricht Microsoft.EntityFrameworkCore

Girder.AspNetCore                Middleware, Header.      nur FrameworkReference
Girder.Data.EntityFrameworkCore  UseEntityFrameworkCore<TContext>()  — ohne Provider
Girder.Caching.Redis             UseRedisCache()          Redis/Valkey/Garnet
Girder.Caching.InMemory          UseInMemoryCache()
Girder.Messaging.MassTransit     UseMassTransit()
Girder.Observability.OpenTelemetry
Girder.Secrets.OpenBao
```

Späteres Backup/Objektspeicher folgt derselben Regel: Port
`IObjectStore` in `Girder.Abstractions`, Implementierung in
`Girder.Storage.S3` (S3-kompatibel deckt MinIO, Garage, Ceph ab — alle selbst
betreibbar).

## Was das nicht behauptet

- Es macht Girder nicht abhängigkeitsfrei. `Girder.Abstractions` braucht
  `Microsoft.Extensions.DependencyInjection.Abstractions`; das ist Teil der
  Plattform, MIT, und ohne es gäbe es keine `IServiceCollection`, an die man
  etwas hängt.
- Es beseitigt MediatR nicht durch Umbenennen. Ein Port über `IMediator` ist
  Arbeit an `Girder.Application` und wird gesondert entschieden.
- Es verspricht keine Souveränitätsbewertung nach SEAL-Stufe. Die hängt am
  Betrieb, nicht an der Bibliothek.

## Reihenfolge der Umsetzung

Jeder Schritt einzeln übersetzt und getestet, jeder für sich rückbaubar.

1. **Elasticsearch-Senke entfernen.** Ein eingestelltes Paket, das auf einen
   SSPL-Server zielt. Kleinster Eingriff, sofortiger Gewinn.
2. **Senken und Exporter aus Girder lösen**, `ReadFrom.Configuration()`
   aktivieren. Danach entscheidet die Anwendung, wohin Protokolle gehen.
3. **`UseNpgsql` aus Girder in die Composition Root heben.** Eine Zeile.
4. **`Girder.Abstractions` anlegen** und die Ports dorthin ziehen.
5. **`Girder.Messaging.MassTransit` abspalten** — der Port ist zwei Methoden
   groß, die Frist ist Ende 2026.
6. **`Girder.Caching.Redis` abspalten**, die elf Security-Dienste als
   Implementierung fachlicher Ports mitnehmen.
7. **`Girder.Data.EntityFrameworkCore` abspalten.**

Schritte 1 bis 3 sind klein und unstrittig. Ab 4 wird umgebaut.
