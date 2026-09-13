# Befunde

Was beim Absuchen der Bibliothek nach dem Verschlüsselungsdefekt (4.4.1,
12./13.09.2026) sonst noch gefunden wurde. Ein Dokument je Befund, nach dem
Muster der Übergabezettel aus dem Verbraucher-Repository: was passiert, eine
Reproduktion **nur mit Girder**, was es kostet, und ein Stand zum Abhaken.

Die gemeinsame Frage war immer dieselbe und ist die, die den ursprünglichen
Defekt gefunden hat: **tut die Stelle, was ihr Name sagt?**

| Befund | Art | Schwere | Paket |
|---|---|---|---|
| [Verschlüsselungs-Envelope: Steuerdaten lagen außerhalb des GCM-Tags](verschluesselungs-envelope-metadaten.md) | Fehler | hoch | `Girder.Redis` |
| [Bremse: ein Stoß in derselben Millisekunde zählt einmal](bremse-millisekunde.md) | Fehler | hoch | `Girder.Redis` |
| [Bremse: fällt bei Redis-Fehler nach OFFEN](bremse-faellt-offen.md) | Lücke | mittel | `Girder.Redis` |
| [Prüfspur: unterschrieben mit einem Schlüssel je Prozess](pruefspur-schluessel-je-prozess.md) | Fehler | mittel | `Girder.Redis` |
| [Geheimnisspeicher: AES-CBC ohne Echtheitsprüfung](geheimnisspeicher-cbc.md) | Fehler | mittel | `Girder.Redis` |
| [Geheimnisspeicher: der Schlüssel steht im Protokoll](geheimnisspeicher-schluessel-im-log.md) | Fehler | mittel | `Girder.Redis` |
| [`AddSecretStoreMasterKey` verlangt einen Anbieter, den niemand registriert](hauptschluessel-ohne-anbieter.md) | Lücke | mittel | `Girder.Infrastructure` |
| [`HashAsync`: 30 000 Runden, wo Girder selbst 600 000 fordert](hashasync-arbeitsfaktor.md) | Fehler | mittel | `Girder.Redis` |
| [`DataEncryptionOptions`: fünf von sieben Einstellungen liest niemand](verschluesselungsoptionen-ohne-leser.md) | Lücke | niedrig | `Girder.Abstractions` |
| [`JwtConfigurationValidator` prüft einen Abschnitt, den Girder nicht liest](jwt-pruefer-falscher-abschnitt.md) | Lücke | niedrig | `Girder.Infrastructure` |

## Geprüft und in Ordnung

Steht hier, weil eine Fehlanzeige auch ein Ergebnis ist — und weil die nächste
Person sonst dieselbe Stelle noch einmal liest.

| Stelle | Geprüft auf | Ergebnis |
|---|---|---|
| `KeyRing.ValidationParameters` | `alg: none`, Algorithmuswechsel, `exp`/`aud`/`iss` | `RequireSignedTokens`, festgenagelte `ValidAlgorithms`, Aussteller und Empfänger geprüft, `RequireExpirationTime`, `ClockSkew = 0`. In Ordnung. |
| `AddJwtAuthentication` ohne Schlüssel | ob ein Dienst ohne Schlüssel anläuft | `keys is null && authority is null` wirft beim Start. In Ordnung. |
| `Pbkdf2PasswordHasher` | Arbeitsfaktor, Salz je Passwort, Vergleich | PBKDF2-HMAC-SHA256, 600 000 Runden, 16 Byte Salz je Eintrag, `FixedTimeEquals`, Nachhashen wird gemeldet. In Ordnung. |
| `BCryptPasswordHasher`, `Argon2PasswordHasher` | Arbeitsfaktor | bcrypt 12; Argon2id 19 MiB / t=2 / p=1. Beide auf der heutigen Empfehlung. In Ordnung. |
| `KeyManagementService` (Schlüsselmaterial) | ob es wirklich umhüllt | AES-256-GCM unter dem Hauptschlüssel, Vektor je Vorgang, Prüfsumme. In Ordnung. |
| `FileBasedProvider` | Verfahren, Salz, eingebautes Passwort | AES-256-GCM, PBKDF2 600 000, Salz je Installation, **kein** eingebautes Passwort. In Ordnung. |
| `SecretGenerator` | Zufallsquelle, Restklassenverzerrung | Durchweg `RandomNumberGenerator`. Die Verzerrung bei `% charset.Length` liegt bei ~2⁻²⁶ und ist ohne Belang. In Ordnung. |
| `System.Random` in `src/` | sicherheitsrelevanter Gebrauch | Eine einzige Stelle: `RetryPolicy._jitterRandom`, Streuung von Wiederholungen. Kein Geheimnis. In Ordnung. |
| `InputSanitizer` | ob es Syntax statt Wörter erkennt, Rückverfolgungs-Explosion | Erkennt Syntax; keines der Muster hat verschachtelte Quantoren, gemessen bis 320 Wiederholungen ohne Anstieg. Anmerkung, kein Befund: **kein** Muster trägt ein `matchTimeout`. |
| Eingebaute Schlüssel, Salze, Geheimnisse | `src/`, `tests/`, `docs/`, README | Keine. Kein Beispielschlüssel, den jemand abschreiben könnte. |
| `SHA1`, `MD5` | ob sie irgendwo tragen | Kommen in `src/` nicht vor. |
| `ISecretManager` | ob er inzwischen einen Verbraucher hat | Nein — drei Umsetzungen, kein einziger Aufruf in ganz Girder. Unverändert gegenüber der Messung vom 12.09.2026. |
