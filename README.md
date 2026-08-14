# Girder

Das lasttragende Fundament für .NET-Microservices: CQRS-Pipeline, Sicherheit,
Caching, Messaging, Observability, Health-Probes und Resilienz — einmal gebaut,
in jedem Dienst wiederverwendet.

Herkunft: herausgelöst aus der `src/shared`-Schicht von **Skillswap**, dort über
Jahre in neun Diensten gelaufen und mit 3.220 Tests belegt. Skillswap selbst
bleibt unberührt.

## Stand

| | |
|---|---|
| Zielframework | `net10.0` (LTS, EOL 2028-11-14) |
| Build | grün, 0 Fehler |
| Tests | **3.220 gelaufen, 3.220 bestanden, 0 übersprungen** |
| Herkunftsumfang | 42.905 Zeilen Infrastruktur + 49.302 Zeilen Tests |

`.NET 11` wurde geprüft und verworfen: zum Zeitpunkt der Herauslösung nur
`preview.7`, und als STS-Release ohnehin die schlechtere Wahl für ein Fundament.

## Projekte

```
src/Girder.Core             Entity, Exceptions, LogSanitizer, Compliance-Ports
src/Girder.Contracts        Grenz-DTOs (PagedRequest, ApiError, Versionierung)
src/Girder.Cqrs             Mediator, Pipeline-Behaviors, Basis-Handler
src/Girder.Infrastructure   Sicherheit, Caching, Messaging, Health, Telemetrie
tests/Girder.Core.Tests             100 Tests
tests/Girder.Infrastructure.Tests  3.120 Tests
```

`Girder.Core` hat keine ASP.NET-Abhängigkeit, `Girder.Contracts` hat gar keine
PackageReference — beides nachgeprüft, nicht behauptet.

## Bauen

```bash
dotnet restore Girder.slnx
dotnet build   Girder.slnx
dotnet test    Girder.slnx
```

Paketversionen liegen zentral in `Directory.Packages.props`.

## Was bewusst noch offen ist

Diese Punkte sind bekannt, benannt und **nicht** versehentlich:

1. **Der Berechtigungskatalog stammt noch aus der Herkunftsdomäne.**
   `Security/Permissions.cs`, `RolePermissions.cs`, `Roles.cs` und
   `Authorization/IPermissionResolver.cs` führen rund 530 Zeilen fester Rechte
   (Skills, Appointments, VideoCalls). Ziel: `IPermissionCatalog`, den die
   Anwendung liefert; Girder liefert eine leere Vorgabe.

2. **Zwei `switch`-Blöcke bilden Pfade auf Ressourcen ab.**
   `Middleware/PermissionMiddleware.cs` (419 Z.) und
   `Security/Authorization/AuthorizationExtensions.cs` (667 Z.) kennen Pfade wie
   `/skills` und `/appointments`. Ziel: `IResourceResolver`.

3. **`Girder.Cqrs` verweist auf `Girder.Infrastructure`** — der innere Ring hängt
   am äußeren. Der Bedarf sind exakt drei `using`-Zeilen, alle für
   `Girder.Infrastructure.Caching`. Behebbar durch Verschieben weniger
   Schnittstellen nach `Girder.Core` oder ein eigenes `Girder.Abstractions`.

4. **`Girder.Infrastructure` ist eine einzige Assembly mit ~45 Paketen.**
   Wer sie referenziert, zieht RabbitMQ, Redis, Postgres, Elasticsearch und
   OpenTelemetry mit — auch wer nur Logging will. Geplanter Schnitt:
   `Girder.Caching.Redis`, `Girder.Messaging.MassTransit`,
   `Girder.Data.EntityFrameworkCore`, `Girder.Observability`.

5. **Sicherheit: festes Salt.** `Security/Secrets/Providers/FileBasedProvider.cs`
   leitet mit einem konstanten Salt und 10.000 PBKDF2-Runden ab. Das ist eine
   echte Schwäche, kein Schönheitsfehler — Salt pro Installation, zeitgemäße
   Rundenzahl. Der Compiler weist über `SYSLIB0060` ohnehin darauf hin.

6. **Attrappen sind mitgekommen.** `AzureKeyVaultProvider` (82 Z.) und
   `AwsSecretsManagerProvider` (55 Z.) referenzieren kein SDK, sondern legen
   Geheimnisse in ein `Dictionary<string, string>`. Wer „Azure" konfiguriert,
   glaubt an Key Vault und hat eine Hashtable. Entweder echt implementieren oder
   entfernen.

7. **Mehrmandantenfähigkeit fehlt vollständig.** In der Herkunft gab es keinen
   einzigen Treffer für „tenant". Geplant: `TenantId?` als *optionales* Attribut
   des Prinzipals (`null` heißt „handelte als Person", nicht „fehlt") und ein
   Marker-Interface `ITenantOwned`, das den EF-Query-Filter **pro Entität**
   einschaltet statt pauschal pro Anwendung.

8. **Drei Pakete sind bewusst nicht auf der neuesten Version.** MediatR (12.5.0)
   und MassTransit (8.3.6) stehen auf ihrem letzten `Apache-2.0`-Stand; die
   neueren sind kommerziell umlizenziert. FluentAssertions steht auf 8.8.0 und
   ist ab 8.x ebenfalls kommerziell. Siehe Kommentar in
   `Directory.Packages.props`.

## Warnungen

76 insgesamt, keine blockierend: 44× `CS0618` (veralteter
`RedisConnectionException`-Konstruktor in Tests), 20× `NU1510` (überflüssige
PackageReferences, die schon im Framework stecken), 8× `SYSLIB0060` (siehe
Punkt 5), 8× `ASPDEPR004/008` (`WebHostBuilder` in Tests).
