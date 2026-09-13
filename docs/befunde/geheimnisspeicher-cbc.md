# `SecretManager` legt Geheimnisse in AES-CBC ohne Echtheitsprüfung ab

- **Girder-Fassung:** 4.4.0 (unverändert in 4.4.1)
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Fehler
- **Blockiert:** nein

## Was passiert

`Girder.Redis.Security.SecretManager` verschlüsselt — anders als
`DataEncryptionService` vor 4.4.1 — **wirklich**. Aber mit der Vorgabe von
`Aes.Create()`, und die ist CBC mit PKCS7:

```csharp
private (string encryptedValue, string iv) EncryptSecret(string value)
{
    using var aes = Aes.Create();     // Mode = CBC, Padding = PKCS7
    aes.Key = _encryptionKey;
    aes.GenerateIV();
    …
}
```

Kein Prüfwert, kein MAC, keine Echtheitsprüfung. Der Geheimtext ist formbar:
wer in Redis schreiben kann, kann Bits im Vektor kippen und damit den ersten
Klartextblock gezielt verändern, ohne dass beim Lesen etwas auffällt. Und
`DecryptSecret` wirft bei falscher Füllung anders als bei richtiger — die
Vorbedingung eines Füllungsorakels, sobald ein Aufrufer den Unterschied nach
außen trägt.

Zwei Zeilen weiter im selben Haus steht, wie es gemeint ist:
`FileBasedProvider` und `KeyManagementService` benutzen beide AES-GCM, und der
Kommentar dort sagt auch, warum: *„GCM authenticates as well as encrypts, so a
tampered file fails to open instead of decrypting to something the caller then
trusts."*

## Warum es Girders ist

```csharp
// nur Girder. docker run -d --rm -p 6399:6379 redis:8-alpine
var b = WebApplication.CreateBuilder();
b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["SecretManager:EncryptionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
});
b.Services.AddRedisConnection("127.0.0.1:6399", "probe");
b.Services.AddRedisSecretManager(b.Configuration, b.Environment);

var app = b.Build();
var speicher = app.Services.GetRequiredService<ISecretManager>();

await speicher.SetSecretAsync("probe", "der-urspruengliche-wert");

// Geheimtext aus Redis holen, EIN Bit im Vektor kippen, zurückschreiben.
var db = app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
var roh = (string)(await db.StringGetAsync("secrets:probe"))!;
var daten = JsonDocument.Parse(roh).RootElement;

var iv = Convert.FromBase64String(daten.GetProperty("IV").GetString()!);
iv[0] ^= 0x01;
// … IV ersetzen, JSON zurückschreiben …

var danach = await speicher.GetSecretAsync("probe");
Console.WriteLine(danach);
// -> ein VERÄNDERTER Wert, ohne Fehler und ohne Hinweis
// erwartet: eine Absage, wie bei FileBasedProvider
```

## Was es kostet

Ein Geheimnisspeicher, dem man ansieht, dass er verschlüsselt, aber nicht, dass
er unverfälscht ist. Vertraulichkeit ist gegeben — das unterscheidet diesen
Befund grundsätzlich vom Verschlüsselungsdefekt in 4.4.1. Was fehlt, ist die
Zusage, die man von einem Speicher für Betriebsgeheimnisse erwartet: dass das,
was herauskommt, das ist, was hineinging.

Behebung: dieselbe Form wie in `FileBasedProvider` — AES-GCM, Vektor und
Prüfsumme vor dem Geheimtext, `CryptographicException` beim Öffnen. Das ändert
das Ablageformat, betrifft also Daten, die bereits in Redis liegen; es gehört
mit einer Formatfassung und einem Satz in den Anmerkungen zusammen, nicht in
einen stillen Wechsel.

**Kleiner Nebenbefund an derselben Stelle:** `_encryptionKey =
Convert.FromBase64String(encryptionKeyBase64)` prüft die Länge nicht. Ein zu
kurzer Schlüssel fällt erst bei `aes.Key = …` auf, also beim ersten Geheimnis
statt beim Start. `ConfiguredMasterKeyProvider` macht es nebenan richtig
vor: *„The master key in '…' is {n} bytes; 32 are required."*

## Stand

- [ ] behoben, Fassung: <…>
