# `SecretManager` schreibt den erzeugten Verschlüsselungsschlüssel ins Protokoll

- **Girder-Fassung:** 4.4.0 bis 4.4.2
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Sicherheitsfehler (CWE-532: vertrauliche Information im Protokoll)
- **Einstufung:** mittel — setzt den Entwicklungsweg voraus, legt dort aber den vollständigen Schlüssel offen
- **Blockiert:** nein
- **Advisory:** [GHSA-x82v-xvpr-43hf](https://github.com/DavidOeztuerk/girder/security/advisories/GHSA-x82v-xvpr-43hf)

## Was passiert

Fehlt außerhalb der Produktion ein Schlüssel, würfelt
`Girder.Redis.Security.SecretManager` einen und **protokolliert ihn**, zweimal
im selben Satz:

```csharp
_encryptionKey = GenerateEncryptionKey();
var base64Key = Convert.ToBase64String(_encryptionKey);
_logger.LogWarning(
    "No encryption key found in configuration. Generated transient key (DEV ONLY): {Key}. " +
    "IMPORTANT: Set SECRET_MANAGER_ENCRYPTION_KEY_BASE64={KeyValue} for all services to share the same key.",
    base64Key, base64Key);
```

Und die Meldung fordert den Leser ausdrücklich auf, ihn von dort zu nehmen und
überall einzutragen — der Schlüssel soll also aus dem Protokoll in die
Umgebung wandern und dann bleiben.

Girders Maskierung greift hier nicht, und zwar aus zwei unabhängigen Gründen:

1. `LogSanitizer` hängt an zwei Wegen — der CQRS-Ablaufkette
   (`LoggingBehavior`) und der HTTP-Zwischenschicht für Rümpfe. Ein direkter
   `ILogger`-Aufruf aus Girders eigenem Code läuft an beiden vorbei.
2. Selbst wenn er dort hinge: `SensitiveFieldNames` wird **exakt** verglichen
   und enthält `apikey`, `privatekey`, `publickey` — aber nicht `key`. Die
   Eigenschaften heißen `Key` und `KeyValue`.

## Warum es Girders ist

```csharp
// nur Girder. Kein Schlüssel in der Konfiguration, Umgebung = Development.
var b = WebApplication.CreateBuilder(new WebApplicationOptions
{
    EnvironmentName = Environments.Development
});
b.Logging.AddConsole();
b.Services.AddRedisConnection("127.0.0.1:6399", "probe");
b.Services.AddRedisSecretManager(b.Configuration, b.Environment);

var app = b.Build();
_ = app.Services.GetRequiredService<ISecretManager>();   // Konstruktor läuft

// Auf der Konsole steht jetzt:
// warn: … Generated transient key (DEV ONLY): 7bQ…=   IMPORTANT: Set
//       SECRET_MANAGER_ENCRYPTION_KEY_BASE64=7bQ…= for all services …
```

**Und der Zusage widerspricht es.** Die README führt einen eigenen Abschnitt
*„What never reaches a log"*. Ein 256-Bit-AES-Schlüssel erreicht ihn.

## Was es kostet

Entwicklungsprotokolle landen in Sammlern, in Bildschirmfotos und in Tickets.
Der Schlüssel, den die Meldung ins Protokoll schreibt, ist genau der, den sie
zum dauerhaften Schlüssel machen will. Dass der Produktionspfad wirft statt zu
würfeln, ist richtig und bleibt richtig — der Befund betrifft den Weg dorthin.

Behebung: die Aufforderung behalten, den Wert weglassen. Wer einen Schlüssel
braucht, kann ihn erzeugen; ihn im Protokoll mitzuliefern spart einen
Handgriff und kostet die Vertraulichkeit aller damit abgelegten Geheimnisse.

## Stand

- [x] Schlüssel und strukturierte Schlüsselwerte vollständig aus der Warnung entfernt
- [x] Negativtest sammelt Meldung samt strukturierten Werten und sucht nach 256-Bit-Base64-Schlüsselmaterial
- [x] lokaler 4.4.3-Paketkandidat im Demo-Projekt bestätigt
- [x] 4.4.3 veröffentlicht und danach erneut anonym von NuGet.org geprüft
