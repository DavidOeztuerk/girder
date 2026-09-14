# Die Prüfspur unterschreibt mit einem Schlüssel, den jeder Prozess neu würfelt

- **Girder-Fassung:** 4.4.0 bis 4.4.2
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Sicherheitsfehler (CWE-321: hartkodierter beziehungsweise nicht beherrschter kryptografischer Schlüssel)
- **Einstufung:** mittel — erzeugt falsche Manipulationsalarme und entwertet die Signatur als Nachweis
- **Blockiert:** nein

## Was passiert

`Girder.Redis.Security.Audit.SecurityAuditService` schreibt an jedes Ereignis
eine „digitale Unterschrift":

```csharp
private string CreateDigitalSignature(SecurityAuditEvent auditEvent)
{
    var signatureInput = $"{auditEvent.EventHash}|{auditEvent.Timestamp:O}";
    using var hmac = new HMACSHA256(_signingKey);
    …
}
```

`_signingKey` kommt aus dem Konstruktor — und der hat eine Vorgabe:

```csharp
public SecurityAuditService(
    IConnectionMultiplexer connectionMultiplexer,
    ILogger<SecurityAuditService> logger,
    byte[]? signingKey = null)
{
    …
    _signingKey = signingKey ?? GenerateSigningKey();   // 32 gewürfelte Byte
}
```

Registriert wird er ohne diesen Wert:

```csharp
// src/Girder.Redis/Security/RedisSecurityRegistration.cs:23
services.AddSingleton<ISecurityAuditService, SecurityAuditService>();
```

Der Behälter kennt kein `byte[]`, nimmt also die Vorgabe. **Jeder Prozessstart
würfelt einen neuen Schlüssel**, und er wird nirgends abgelegt.

Daraus folgt beides, und das zweite ist das schlimmere:

1. Die Unterschrift beweist niemandem etwas. Niemand außerhalb des laufenden
   Prozesses kennt den Schlüssel — auch der Betreiber nicht.
2. `VerifyAuditIntegrityAsync` meldet nach jedem Neustart **jedes** früher
   geschriebene Ereignis als verfälscht, weil es unter einem anderen Schlüssel
   nachrechnet. Eine Fälschungserkennung, die bei jedem Neustart falschen Alarm
   gibt, wird nach dem zweiten Mal nicht mehr gelesen.

Und die Kette darunter trägt nicht: `CalculateEventHash` ist ein reines SHA-256
über aneinandergehängte Felder, ohne Schlüssel. Wer in Redis schreiben kann,
kann die Kette samt Hashes neu rechnen. Die Unterschrift war die Stelle, die das
auffangen sollte.

## Warum es Girders ist

```csharp
// nur Girder. docker run -d --rm -p 6399:6379 redis:8-alpine
var muxer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6399");

// Erster „Prozess": schreiben.
var eins = new SecurityAuditService(muxer, NullLogger<SecurityAuditService>.Instance);
await eins.LogSecurityEventAsync(new SecurityAuditEvent
{
    EventType = "SignIn", Description = "probe", UserId = "u1", Timestamp = DateTime.UtcNow
});

// Zweiter „Prozess": derselbe Redis, neue Instanz — genau das, was ein
// Neustart tut.
var zwei = new SecurityAuditService(muxer, NullLogger<SecurityAuditService>.Instance);
var ergebnis = await zwei.VerifyAuditIntegrityAsync(
    DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));

Console.WriteLine($"geprüft={ergebnis.EventsVerified} verletzt={ergebnis.IntegrityViolations}");
// -> geprüft=1 verletzt=1   ("Digital signature verification failed")
// erwartet: verletzt=0, es wurde nichts angefasst
```

**Und der Zusage widerspricht es.** Der Typ heißt `SecurityAuditService`, die
Methode `VerifyAuditIntegrityAsync`, der Verstoß `"Digital signature
verification failed - possible tampering"`. Eine Unterschrift, deren Schlüssel
mit dem Prozess stirbt, ist keine.

## Nebenbefund an derselben Stelle: der Vergleich ist nicht zeitkonstant

```csharp
return auditEvent.Signature == expectedSignature;
```

Ein Zeichenkettenvergleich bricht beim ersten Unterschied ab. Bei einem
Prüfwert gehört `CryptographicOperations.FixedTimeEquals` hin — so, wie es
`Pbkdf2PasswordHasher` und `DataEncryptionService.VerifyHashAsync` schon
machen. Ausnutzbar ist es hier schlecht (der Vergleich läuft im Prozess über
gespeicherte Zeilen, nicht an einer Netzgrenze), aber es ist die einzige Stelle
in `src/`, an der ein Prüfwert noch mit `==` verglichen wird, und damit die
Ausnahme von einer Regel, die das Haus sonst einhält.

## Was es kostet

Wer die Prüfspur als Nachweis führt, führt keinen. Die Kette ist ohne Schlüssel
nachrechenbar, die Unterschrift ist außerhalb des Prozesses bedeutungslos, und
die Prüfmethode meldet nach jedem Neustart falschen Alarm.

Behebung: den Schlüssel aus derselben Quelle nehmen wie alles andere — einem
`IMasterKeyProvider` oder der Konfiguration — und die Vorgabe `null` entfernen,
statt sie zu würfeln. Ein Dienst, der ohne Schlüssel nicht unterschreiben kann,
soll das beim Start sagen, nicht im Stillen einen erfinden. Dasselbe Muster
steht in `FileBasedProvider` bereits richtig da: *„There is no fallback
password."*

## Stand

- [x] gemessen, 13.09.2026
- [x] zufällige Prozessvorgabe entfernt; der Dienst verlangt genau 32 Byte stabilen Signierschlüssel
- [x] `AddRedisSecurityAudit()` leitet einen zweckgetrennten Schlüssel vom registrierten `IMasterKeyProvider` ab
- [x] Überladung für einen ausdrücklich getrennten 256-Bit-Auditschlüssel ergänzt
- [x] Signaturen werden zeitkonstant verglichen
- [x] echter Redis-Neustarttest: neue Dienstinstanz, derselbe Schlüssel, keine falsche Verletzung
- [x] Eigenreview: Kettenkopf speichert nun den Ereignis-Hash statt der Ereignis-ID
- [x] Eigenreview: alle Ereignisfelder einschließlich Metadaten werden kanonisch gehasht
- [x] Eigenreview: Redis-Compare-and-set verhindert Kettenzweige zwischen mehreren Prozessen
- [x] Eigenreview: Prüfung rekonstruiert die Kette statt Ereignisse mit gleichem Sekunden-Zeitstempel nach GUID zu sortieren
- [x] reale Redis-Proben für Neustart, Gleichzeitigkeit, gleichen Zeitstempel und Feldmanipulation
- [x] lokaler 4.4.3-Paketkandidat im Demo-Projekt bestätigt
- [ ] 4.4.3 veröffentlicht und danach erneut anonym von NuGet.org geprüft
