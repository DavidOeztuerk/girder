# ADR-0001 — Souveränität durch Portschnitt, nicht durch Konfiguration

**Status:** Vorschlag · **Datum:** 2026-08-15 · **Fassung:** 2

Fassung 2 nach externer Gegenlesung und einer Nachmessung, die zwei
Annahmen der ersten Fassung widerlegt hat. Was sich geändert hat, steht
unter [Was Fassung 1 falsch hatte](#was-fassung-1-falsch-hatte).

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

Punkt 2 ist der, an dem die meisten Abstraktionen scheitern. Punkt 1 ist der,
den man am leichtesten *behauptet* statt belegt — siehe Messaging unten.

## Der Befund

Gemessen am 15.08.2026 gegen den tatsächlichen Quellcode und die
`.nuspec`-Metadaten im lokalen NuGet-Zwischenspeicher.

| Abhängigkeit | Lizenz (gepinnt) | Urteil | Folge |
|---|---|---|---|
| MediatR 12.5.0 | Apache-2.0 — letzte freie Fassung, ab v13 kommerziell | Lieferkette | gesondert entscheiden |
| MassTransit 8.3.6 | Apache-2.0 — Korrekturen nur bis Ende 2026 | Lieferkette, **mit Datum** | Port existiert, Umzug |
| Serilog.Sinks.Elasticsearch 10.0.0 | Apache-2.0, aber **eingestellt**, Ziel ist ein SSPL-Server | fällt durch 2 und 3 | entfernen |
| AspNetCore.HealthChecks.Redis 9.0.0 | Apache-2.0 | souverän, aber **pinnt Redis in Girder** | ins Anbieterpaket |
| AspNetCore.HealthChecks.Rabbitmq 9.0.0 | Apache-2.0 | souverän, aber pinnt RabbitMQ | ins Anbieterpaket |
| Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 | PostgreSQL | souverän — aber *ist* der Nagel | in die Anwendung |
| Microsoft.EntityFrameworkCore 10.0.11 | MIT | souverän, **ist selbst die Abstraktion** | behalten |
| StackExchange.Redis 3.1.13 | MIT, spricht Valkey/Garnet/KeyDB | souverän | behalten, im Anbieterpaket |
| Serilog 4.4.0 | Apache-2.0 | souverän | Kern behalten, Senken raus |
| OpenTelemetry 1.17.0 | Apache-2.0, herstellerneutral von Bauart | souverän | behalten |
| OpenTelemetry.Exporter.OpenTelemetryProtocol 1.17.0 | Apache-2.0, OTLP gegen selbst hostbaren Collector | souverän | **behalten** |
| OpenTelemetry.Exporter.Prometheus.AspNetCore | Apache-2.0 | herstellerspezifisch | in die Anwendung |
| FluentValidation 12.1.1 | Apache-2.0 | souverän | behalten |
| Swashbuckle 10.2.3 | MIT | souverän | behalten |

### Was der Quellcode dazu sagt

- **Genau eine Zeile nagelt Postgres fest**: `Extensions/DatabaseExtensions.cs:27`
  ruft `options.UseNpgsql(...)`. Sechs Dateien benutzen EF Core, alle über die
  Abstraktion. Das ist keine Umbauarbeit, das ist ein Umzug.
- **Der Messaging-Port existiert bereits**: `IEventBus` in
  `Extensions/MessagingExtensions.cs:181`, **eine** Methode
  (`PublishAsync<TEvent>`), und `MassTransitEventBus` ist bereits dessen
  Implementierung. Die MassTransit-Oberfläche, die Girder überhaupt berührt,
  ist `IPublishEndpoint` (4×) und `ConsumeContext` (4×, Korrelations-Filter).
- **Die acht Redis-gestützten Dienste implementieren bereits fachliche
  Schnittstellen** — `ISecurityAuditService`, `IResourceAuthorizationService`,
  `IDataProtectionService`, `IDataEncryptionService`, `IKeyManagementService`,
  `IRateLimitService`, `ITokenRevocationService`, `ISecretManager` — und in
  **keiner** dieser acht Schnittstellendateien kommt ein Redis-Typ vor
  (`RedisValue`, `RedisKey`, `IDatabase`, `IConnectionMultiplexer`,
  `StackExchange`: null Treffer).
- **Serilog**: Girder pinnt acht Senken-Pakete, benutzt im Code drei
  (`Console`, `File`, `Elasticsearch`).
- **Redis-Nutzung intern**: die acht Implementierungen benutzen Strings, Sets,
  Sorted Sets, Hashes, Listen, Key-Expiry *und* `ScriptEvaluateAsync` (Lua) —
  rund 22 Operationen. Das ist der Grund, warum treibergeschnitten falsch
  wäre; fachlich geschnitten ist es bereits.

## Entscheidung

### 1. Abstrahiert wird am Port, nicht am Treiber — und das ist hier schon geschehen

Der Fehler wäre `IKeyValueStore` mit 22 Methoden — Redis unter anderem Namen,
gescheitert an Prüfpunkt 2. Richtig ist der fachliche Schnitt, und der
existiert bereits in allen acht Fällen. Die Aufgabe ist deshalb **kein
Entwurf, sondern ein Umzug**: Schnittstellen nach `Girder.Abstractions`,
Implementierungen nach `Girder.Redis`.

Der Compiler wird das anschließend beweisen, was der Textgriff heute nur
nahelegt: Wenn eine Schnittstelle nach dem Umzug noch etwas aus
`StackExchange.Redis` braucht, übersetzt `Girder.Abstractions` nicht.

### 2. Die Ports brauchen einen geprüften Vertrag, sonst ist die zweite Implementierung eine Lüge

**Das ist der wichtigste Punkt dieses ADR.** Die Ports sind sauber
geschnitten, aber ihr *Verhalten* ist nirgends festgehalten.
`RedisTokenRevocationService` benutzt `ScriptEvaluateAsync` — Lua, also
Atomarität. An `ITokenRevocationService` steht nirgends, dass Atomarität
zugesichert ist. Eine In-Memory-Implementierung würde übersetzen, die
Schnittstelle erfüllen und unter Parallelität falsch sein.

Der Vertrag gehört deshalb in eine **Conformance-Suite**, nicht in einen
XML-Kommentar — nach dem Vorbild von EF Core:

```csharp
// Girder.Abstractions.ConformanceTests
public abstract class TokenRevocationStoreConformance
{
    protected abstract ITokenRevocationService CreateStore();

    [Fact]
    public async Task Widerruf_ist_atomar_unter_Parallelitaet()
    {
        var store = CreateStore();
        var jti = Guid.NewGuid().ToString("N");

        var ergebnisse = await Task.WhenAll(
            Enumerable.Range(0, 64).Select(_ => store.TryRevokeAsync(jti, TimeSpan.FromMinutes(5))));

        // Genau ein Aufrufer darf 'true' sehen — sonst ist der Port nicht atomar.
        ergebnisse.Count(r => r).Should().Be(1);
    }
}
```

Jede Implementierung erbt die Suite. Der Prüfstein für den Schnitt selbst:
**Lässt sich der Test formulieren, ohne von Sorted Sets zu reden?** Wenn
nicht, ist der Port treibergeschnitten.

### 3. Der Anbieter wird im Code gewählt, nicht in der Konfiguration

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
    .UseRedis()                                                  // oder UseInMemory()
    .UseMassTransit();                                           // oder später etwas anderes
```

#### `IGirderBuilder` liegt in `Girder.Abstractions`, nicht in `Girder`

Damit `UseRedis()` im Anbieterpaket stehen kann, braucht es den Builder-Typ.
Läge der im Motor, zöge jedes Anbieterpaket den ganzen Motor mit. EF Core
macht genau das (`DbContextOptionsBuilder` liegt in
`Microsoft.EntityFrameworkCore`) — für uns ist das die schlechtere Wahl.

```csharp
// Girder.Abstractions/Configuration/IGirderBuilder.cs
namespace Girder.Abstractions.Configuration;

/// <summary>
/// Konfigurationsoberfläche für Girder. Anbieterpakete hängen ihre
/// Registrierungen hier an, ohne den Girder-Motor zu referenzieren.
/// </summary>
public interface IGirderBuilder
{
    /// <summary>Zielsammlung für Anbieter-Registrierungen.</summary>
    IServiceCollection Services { get; }

    /// <summary>Liefert Verbindungsdaten — nie Anbieteridentität.</summary>
    IConfiguration Configuration { get; }
}
```

Preis: `Girder.Abstractions` bekommt eine zweite Plattformabhängigkeit,
`Microsoft.Extensions.Configuration.Abstractions` (MIT). Bewusst in Kauf
genommen und in [Was das nicht behauptet](#was-das-nicht-behauptet) genannt.

#### Gesundheitsprüfungen gehören ins Anbieterpaket

`AspNetCore.HealthChecks.Redis` in Girder würde die ganze Übung aufheben — es
ist selbst ein Redis-Paket. Die Anbieterpakete schreiben ihre eigenen Prüfungen
gegen den Treiber, den sie ohnehin referenzieren.

#### Eine begründete Ausnahme: Serilog — mit ihrem Preis

Serilog bringt mit `Serilog.Settings.Configuration` und
`ReadFrom.Configuration()` die Maschinerie mit, die Senken zur Laufzeit über
Assembly-Scan lädt. Das ist Serilogs eigenes, unterstütztes Muster: Die
Anwendung installiert das Senken-Paket und nennt es in `appsettings.json`.
Girder darf nur keine Senke pinnen.

**Der Preis, offen genannt:** Das ist dasselbe Assembly-Scan-Verfahren, das
oben für die Anbieterwahl abgelehnt wird — mit denselben Folgen für Trimming
und AOT. Der Unterschied ist die Bilanz, nicht das Prinzip: Für Protokollierung
liefert das Ökosystem die Auflösung fertig und erprobt mit; für Cache und
Persistenz müssten wir sie erfinden. Wo eine Anwendung AOT braucht, konfiguriert
sie ihre Senken in Code statt in `appsettings.json` — Serilog kann beides.

### 4. Die Regel wird mechanisch durchgesetzt, sonst erodiert sie

Eine Absicht im ADR hält keine drei Sprints. In sechs Monaten fügt jemand
unter Zeitdruck ein Paket hinzu, und niemand merkt es. Deshalb: Build-Fehler.

```xml
<!-- Directory.Build.targets -->
<Project>
  <Target Name="GirderDependencyGuard" BeforeTargets="Build"
          Condition="'$(GirderProviderFree)' == 'true'">
    <ItemGroup>
      <ForbiddenPackage Include="@(PackageReference)"
        Condition="!$([System.Text.RegularExpressions.Regex]::IsMatch('%(Identity)',
          '^(Microsoft\.Extensions\.[A-Za-z.]+\.Abstractions|Microsoft\.Bcl\.|System\.)'))" />
    </ItemGroup>
    <Error Condition="'@(ForbiddenPackage)' != ''" Code="GIRDER0001"
           Text="ADR-0001 verletzt: '$(MSBuildProjectName)' referenziert Infrastrukturpakete: @(ForbiddenPackage->'%(Identity)', ', ')" />
  </Target>
</Project>
```

Gesetzt wird `<GirderProviderFree>true</GirderProviderFree>` in
`Girder.Core`, `Girder.Contracts` und `Girder.Abstractions`.

Für `Girder` selbst ist die Regel weicher — Motor, aber kein Anbieter —, dort
prüft ein Test die referenzierten Assemblies:

```csharp
[Fact]
public void Girder_kennt_keine_Anbieter_Assemblies()
{
    string[] verboten = ["StackExchange.Redis", "MassTransit", "Npgsql", "Elasticsearch", "RabbitMQ"];

    typeof(GirderBuilder).Assembly.GetReferencedAssemblies().Select(a => a.Name!)
        .Should().NotContain(n => verboten.Any(v => n.StartsWith(v, StringComparison.Ordinal)));
}
```

Central Package Management (`Directory.Packages.props`) ist bereits vorhanden;
die Versionspins stehen dort schon an einer Stelle.

### 5. Eine Solution, mehrere Pakete

Kein eigenes Repository je Bibliothek. EF Core, Serilog, MassTransit und
OpenTelemetry .NET fahren alle ein Repo, eine Solution, viele Projekte, eine
Version. Getrennte Solutions bedeuten getrennte Versionierung und
repoübergreifende Änderungen für einen einzigen Umbau.

### 6. Zielschnitt

```
Girder.Core                      Domänenprimitive.        0 Pakete   (heute erfüllt)
Girder.Contracts                 Grenz-DTOs.              0 Pakete   (heute erfüllt)
Girder.Abstractions              ALLE Ports + IGirderBuilder.
                                 nur DI.Abstractions + Configuration.Abstractions
Girder.Application               CQRS gegen die Ports.
Girder                           Builder/Motor. Kein Anbieter.
                                 — entspricht Microsoft.EntityFrameworkCore

Girder.AspNetCore                Middleware, Header.      nur FrameworkReference
Girder.Data.EntityFrameworkCore  UseEntityFrameworkCore<TContext>()  — ohne Provider
Girder.Redis                     UseRedis()   Redis/Valkey/Garnet — Cache *und*
                                 die acht Sicherheitsspeicher, eine Verbindung
Girder.InMemory                  UseInMemory()  — muss die Conformance-Suite bestehen
Girder.Messaging.MassTransit     UseMassTransit()
Girder.Observability.OpenTelemetry
Girder.Secrets.OpenBao
```

`Girder.Redis`, nicht `Girder.Caching.Redis`: Das Paket enthält
Token-Widerruf, Schlüsselverwaltung und Audit-Speicher. Ein Paket namens
„Caching" mit diesem Inhalt führt in zwei Jahren jemanden in die Irre — und es
entspricht der Wirklichkeit, dass es *eine* `IConnectionMultiplexer`-Instanz
gibt.

Späteres Backup/Objektspeicher folgt derselben Regel: Port `IObjectStore` in
`Girder.Abstractions`, Implementierung in `Girder.Storage.S3` (S3-kompatibel
deckt MinIO, Garage, Ceph ab — alle selbst betreibbar).

### 7. Messaging: Prüfpunkt 1 ist belegt, das Ziel ist offen

Fassung 1 hat den Port mit der Lizenzfrist begründet. Das ist ein Grund zu
*gehen*, kein Grund zu *abstrahieren*. Prüfpunkt 1 verlangt mindestens zwei
souveräne Implementierungen; hier sind drei:

| Kandidat | Lizenz | Anmerkung |
|---|---|---|
| Rebus 8.9.2 | MIT | nächste 1:1-Entsprechung, Transporte für RabbitMQ/Azure SB/Postgres |
| Wolverine | MIT | Kern ist MIT und frei; `Critter Stack Pro` ist ein *separates* Zusatzprodukt, keine Umlizenzierung des Kerns |
| RabbitMQ.Client 7.2.2 | Apache-2.0 OR MPL-2.0 | realistisch, weil der Port eine Methode groß ist |

**Die Port-Form steht bereits fest und ist minimal:** `IEventBus.PublishAsync<TEvent>`,
Fire-and-forget. **Der Port verspricht ausdrücklich keine Outbox und keine
transaktionale Zustellung.** Wer die braucht, konfiguriert sie in der Anwendung
gegen den eigenen `DbContext` — heute über MassTransits
`AddEntityFrameworkOutbox`. Das ist keine Auslassung, sondern die Grenze des
Ports: Eine Zustellgarantie, die je nach Anbieter verschwindet, wäre schlimmer
als keine.

Damit blockiert die Zielentscheidung (Rebus vs. RabbitMQ.Client) den Umbau
**nicht**. Sie ist eine Migrationsentscheidung und wird getroffen, wenn
`Girder.Messaging.MassTransit` steht.

## Was das nicht behauptet

- Es macht Girder nicht abhängigkeitsfrei. `Girder.Abstractions` braucht drei
  Plattformpakete, alle MIT:
  `Microsoft.Extensions.DependencyInjection.Abstractions` (ohne sie gäbe es
  keine `IServiceCollection`, an die man etwas hängt),
  `Microsoft.Extensions.Configuration.Abstractions` (für `IGirderBuilder`) und
  `Microsoft.Extensions.Logging.Abstractions` (weil eine abgeschwächte
  Sicherheitsentscheidung berichtet werden muss, statt still zu bleiben).
- Es beseitigt MediatR nicht durch Umbenennen. `Girder.Application` hängt an
  MediatR 12.5.0, der letzten freien Fassung. Ein In-Process-Dispatcher ist
  überschaubar, aber das ist eine eigene Entscheidung und gehört in ein
  eigenes ADR.
- Es verspricht keine Souveränitätsbewertung nach SEAL-Stufe. Die hängt am
  Betrieb, nicht an der Bibliothek.
- Der Nachweis „keine Redis-Typen in den acht Schnittstellen" ist heute eine
  Textsuche. Verbindlich wird er erst, wenn die Schnittstellen in
  `Girder.Abstractions` liegen und der Übersetzer ihn führt.

## Reihenfolge der Umsetzung

Der Guard kommt zuerst, weil ein Guard nach dem Umbau den Umbau nicht schützt.
Jeder Schritt wird einzeln übersetzt und getestet.

| # | Schritt | Größe | hängt an |
|---|---|---|---|
| 0 | Guard (`Directory.Build.targets`) einziehen, an `Core`/`Contracts` scharf schalten | klein | — |
| 1 | Elasticsearch-Senke entfernen | klein | — |
| 2 | Senken und herstellerspezifische Exporter lösen, `ReadFrom.Configuration()`, OTLP behalten | klein | — |
| 3 | `UseNpgsql` in die Composition Root heben | klein | — |
| 4 | `Girder.Abstractions` anlegen, Ports + `IGirderBuilder` dorthin | mittel | 0 |
| 5 | Conformance-Suite je Port | mittel | 4 |
| 6 | `Girder.Redis` abspalten (Umzug, kein Entwurf) | mittel | 4, 5 |
| 7 | `Girder.Messaging.MassTransit` abspalten | mittel | 4 |
| 8 | `Girder.Data.EntityFrameworkCore` abspalten | klein | 4 |

Schritte 0 bis 3 sind klein, rückbaubar und unstrittig; sie beweisen den Ansatz,
bevor ab 4 umgebaut wird.

## Was Fassung 1 falsch hatte

- **„Der Portschnitt ist Entwurfsarbeit."** Ist er nicht. Alle acht
  Redis-gestützten Dienste implementieren bereits fachliche, Redis-freie
  Schnittstellen; `IEventBus` existiert ebenfalls. Schritt 6 ist ein Umzug.
- **„Exporter raus."** Zu streng. `OpenTelemetry.Exporter.OpenTelemetryProtocol`
  ist Apache-2.0 und spricht OTLP gegen den selbst hostbaren, Apache-2.0
  lizenzierten OpenTelemetry Collector — es besteht alle drei Prüfpunkte. OTLP
  ist das Valkey-Argument in Grün: Der Anbieterwechsel ist eine Endpunkt-URL.
  Raus gehören die herstellerspezifischen Exporter.
- **Prüfpunkt 1 war bei Messaging behauptet, nicht belegt.** Jetzt belegt, mit
  drei Kandidaten.
- **Der Serilog-Ausnahme fehlte der Preis.** `ReadFrom.Configuration()` ist
  derselbe Assembly-Scan, der eine Seite vorher abgelehnt wird. Steht jetzt da.
- **Das Health-Check-Leck war übersehen.** `AspNetCore.HealthChecks.Redis` in
  Girder hätte den Redis-Ausbau aufgehoben.
- **`Girder.Caching.Redis` war ein Fehlname** für ein Paket, das
  Token-Widerruf und Schlüsselverwaltung enthält.
- **Es fehlte die mechanische Durchsetzung** — der wichtigste Zusatz, weil ohne
  sie alles andere eine Absichtserklärung bleibt.
