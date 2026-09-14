# `DataEncryptionOptions`: fünf von sieben Einstellungen liest niemand — `ForProduction()` ändert nichts

- **Girder-Fassung:** 4.4.0 bis 4.4.2
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Lücke
- **Blockiert:** nein

## Was passiert

`DataEncryptionOptions` hat sieben Eigenschaften. Gesucht wurde nach jedem
Leser in `src/`:

| Eigenschaft | Vorgabe | gelesen von |
|---|---|---|
| `LogOperations` | `true` | `DataEncryptionService`, zweimal |
| `DefaultPepper` | `null` | `DataEncryptionService.VerifyHashAsync` |
| `DefaultAlgorithm` | `AES256GCM` | **niemandem** |
| `DefaultHashingAlgorithm` | `Argon2id` | **niemandem** |
| `MaxDataSize` | 100 MB | **niemandem** |
| `CacheKeyMetadata` | `true` | **niemandem** |
| `KeyMetadataCacheDuration` | 15 min | **niemandem** |

Geschrieben werden sie durchaus — vom Erbauer:

```csharp
// src/Girder.Infrastructure/Security/Encryption/EncryptionExtensions.cs
public IEncryptionBuilder ForProduction()
{
    …
    _encryptionOptions.DefaultAlgorithm = EncryptionAlgorithm.AES256GCM;
    _encryptionOptions.DefaultHashingAlgorithm = HashingAlgorithm.Argon2id;
    …
}
```

`ForProduction()`, `ForDevelopment()`, `WithAlgorithm(...)` und
`WithHashingAlgorithm(...)` setzen also Werte, die anschließend niemand
ausliest. Der Algorithmus, mit dem tatsächlich verschlüsselt wird, kommt aus
`EncryptionOptions.Algorithm` je Vorgang (Vorgabe `AES256GCM`) oder aus
`CreateEncryptionOptions(context)`, und beide fragen die Vorgabe nie.

Zwei Folgen, unterschiedlich schwer:

- **`.ForDevelopment()` schwächt nichts ab, `.ForProduction()` härtet nichts.**
  Das ist der harmlose Teil — die Vorgaben sind ohnehin die stärkeren.
- **`MaxDataSize` ist keine Grenze.** Die Doku sagt *„Maximum data size for
  encryption (bytes)"* und meint 100 MB. `EncryptWithKeyAsync` prüft nichts;
  eine 2-GB-Zeichenkette wird versucht und scheitert erst an der Zuteilung der
  Puffer. Für einen Dienst, der Feldinhalte von außen verschlüsselt, ist eine
  angekündigte und nicht gezogene Grenze eine Einladung.

`DefaultHashingAlgorithm = Argon2id` ist zusätzlich eine Vorgabe, die
`HashAsync` seit 4.3 **ablehnt** — sie wäre also, würde sie gelesen, sofort ein
`NotSupportedException`.

## Warum es Girders ist

```csharp
// nur Girder. Der Erbauer setzt AES128GCM — heraus kommt AES256GCM.
b.Services.AddEncryption(o => o.WithAlgorithm(EncryptionAlgorithm.AES128GCM));
…
var e = await chiffre.EncryptWithKeyAsync("probe", keyId);
Console.WriteLine(e.Algorithm);
// -> AES256GCM      erwartet: AES128GCM

// Und die angekuendigte Grenze:
var gross = new string('x', 200 * 1024 * 1024);   // 200 MB, Grenze ist 100
var r = await chiffre.EncryptWithKeyAsync(gross, keyId);
Console.WriteLine(r.Success);
// -> es wird versucht; keine Absage mit "MaxDataSize"
```

## Was es kostet

Wenig unmittelbar — die nicht gelesenen Werte sind die schwächeren. Es kostet
Verlässlichkeit: vier öffentliche Erbauer-Methoden, die aussehen, als
entschieden sie etwas, und eine Größenbegrenzung, auf die sich ein Betreiber
berufen könnte.

Behebung, und die Wahl gehört ausgesprochen: entweder die Werte lesen — dann
muss `DefaultHashingAlgorithm` von `Argon2id` weg — oder die Eigenschaften und
die Erbauer-Methoden entfernen. Entfernen bricht die API und gehört damit nach
5.0, angekündigt über `[Obsolete]` in einer Nebenversion, so wie die
Versionspolitik im README es seit 4.4.0 verlangt.

## Stand

- [x] sicherheitswirksame Werte werden gelesen: `DefaultAlgorithm`, `DefaultHashingAlgorithm`, `MaxDataSize` und `CompressionThreshold`
- [x] `MaxDataSize` prüft die tatsächliche UTF-8-Größe vor Schlüsselzugriff und Pufferzuteilung
- [x] Vorgabe für `DefaultHashingAlgorithm` auf das tatsächlich vorhandene PBKDF2 korrigiert
- [x] Tests beweisen, dass konfigurierte Standardalgorithmen das Ergebnis ändern
- [ ] `CacheKeyMetadata` und `KeyMetadataCacheDuration` bleiben wirkungslos; in 5.0 entfernen oder gemeinsam mit einem messbaren Cache-Vertrag implementieren
- [x] lokaler 4.4.3-Paketkandidat im Demo-Projekt bestätigt
- [x] 4.4.3 veröffentlicht und danach erneut anonym von NuGet.org geprüft
