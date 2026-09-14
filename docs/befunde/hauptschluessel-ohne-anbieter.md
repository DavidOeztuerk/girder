# `AddSecretStoreMasterKey()` — der empfohlene Weg — verlangt einen Anbieter, den kein Modul registriert

- **Noelia-Fassung:** 4.4.0 bis 4.4.2
- **Gefunden beim:** Messung der Geheimnisfrage am 12.09.2026, nachgeprüft beim Absuchen am 13.09.2026
- **Art:** Lücke
- **Blockiert:** nein — es blockiert den Weg, den Noelia selbst empfiehlt

## Was passiert

Noelias eigene XML-Doku empfiehlt diesen Weg gegenüber dem anderen:

> prefer `AddSecretStoreMasterKey()`, which reads the key from a secret store
> you run

Er löst einen `ISecretProvider` auf:

```csharp
// src/Noelia.Infrastructure/Security/Encryption/MasterKeyProviders.cs:130
services.AddSingleton<IMasterKeyProvider>(sp => new SecretStoreMasterKeyProvider(
    sp.GetRequiredService<ISecretProvider>(), …));
```

Und **kein Noelia-Modul registriert je einen**. Nachgeprüft am 13.09.2026:
`GetRequiredService<ISecretProvider>()` in `MasterKeyProviders.cs` ist der
einzige Treffer für `ISecretProvider` in einer Registrierung oder Auflösung in
ganz `src/`. Die vier vorhandenen Anbieter (`OpenBaoSecretProvider`,
`EnvironmentVariableProvider`, `FileBasedProvider`, `InMemoryProvider`) baut
allein `SecureSecretManager` intern — und der ist selbst nirgends verdrahtet.

Der Dienst **startet trotzdem**: die Anbieterprüfung sieht einen
`IMasterKeyProvider`, denn er ist als Fabrik registriert, und ist zufrieden.
Das Loch liegt eine Ebene tiefer.

```
START Encryption + AddSecretStoreMasterKey: LAEUFT AN
   erste Benutzung -> InvalidOperationException: No service for type
      'Noelia.Abstractions.Security.Secrets.ISecretProvider' has been registered.
```

## Warum es Noelias ist

```csharp
// nur Noelia. docker run -d --rm -p 6399:6379 redis:8-alpine
var b = WebApplication.CreateBuilder();
b.Services.AddRedisConnection("127.0.0.1:6399", "probe");
b.Services.AddRedisEncryption();
b.Services.AddSecretStoreMasterKey();            // Noelias eigene Empfehlung
b.Services.AddNoelia(b.Configuration, b.Environment, "probe",
    g => g.Use(NoeliaModule.Encryption));

var app = b.Build();
await app.StartAsync();                          // laeuft an

var chiffre = app.Services.GetRequiredService<IDataEncryptionService>();
await chiffre.EncryptAsync("x", new EncryptionContext());
// -> InvalidOperationException: No service for type 'ISecretProvider'
```

**Und der Zusage widerspricht es wörtlich.** `ProviderRequirementValidator`
existiert, um genau diesen Ausfallmodus abzuschaffen:

> Without this the container resolves lazily, so a missing provider surfaces on
> the first request that happens to need it.

Genau das passiert hier — beim empfohlenen Weg.

## Was es kostet

Der Weg, den die Doku vorzieht, ist der einzige, der nicht funktioniert.
`AddConfiguredMasterKey()` — der Weg, von dem abgeraten wird — läuft.

Zwei Behebungen, und sie schließen einander nicht aus:

1. **Die Startprüfung tiefer ansetzen:** `AddSecretStoreMasterKey()` soll auch
   `ISecretProvider` als Bedarf anmelden, damit die fehlende Registrierung beim
   Start auffällt und nicht bei der ersten Benutzung.
2. **Einen Anbieter anbieten:** eine Registrierung, die einen der vier
   vorhandenen `ISecretProvider` verdrahtet — dann ist die Empfehlung auch eine,
   der man folgen kann.

Bis dahin gehört an die XML-Doku ein Satz, der sagt, dass dieser Weg eine
Registrierung durch die Anwendung voraussetzt, die Noelia nicht mitliefert.

## Stand

- [x] gemessen, 12.09.2026, nachgeprüft 13.09.2026
- [x] `AddSecretStoreMasterKey()` meldet `ISecretProvider` als echte Startanforderung
- [x] Fehlermeldung nennt Registrierung und konkrete Abhilfe statt eines späten DI-Stacktraces
- [x] `AddOpenBaoSecretProvider(configuration)` verdrahtet den vorhandenen selbst betreibbaren KV-v2-Anbieter hinter `ISecretProvider` und `IVersionedSecretProvider`
- [x] Komposition mit OpenBao-Anbieter und Secret-Store-Hauptschlüssel ist vollständig
- [x] lokaler 4.4.3-Paketkandidat im Demo-Projekt bestätigt
- [x] 4.4.3 veröffentlicht und danach erneut anonym von NuGet.org geprüft
