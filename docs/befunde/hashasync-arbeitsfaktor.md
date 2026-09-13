# `IDataEncryptionService.HashAsync` rechnet 30 000 PBKDF2-Runden, wo Girder selbst 600 000 fordert

- **Girder-Fassung:** 4.4.0 (unverändert in 4.4.1)
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Fehler
- **Blockiert:** nein

## Was passiert

`DataEncryptionService.HashAsync` leitet den Arbeitsfaktor aus `TimeCost` ab:

```csharp
private static byte[] HashPBKDF2(byte[] data, byte[] salt, HashingOptions options, …)
{
    var iterations = options.TimeCost * 10000;
    …
    return Rfc2898DeriveBytes.Pbkdf2(data, salt, iterations, HashAlgorithmName.SHA256, options.HashSize);
}
```

`HashingOptions.TimeCost` ist auf `3` voreingestellt. Das ergibt **30 000
Runden**.

Nebenan im selben Haus steht der Wert, den Girder für richtig hält:

```csharp
// src/Girder.Infrastructure/Security/Passwords/PasswordHashingOptions.cs
/// PBKDF2 iterations for new entries. OWASP's floor for HMAC-SHA256 at the
/// time of writing.
public int Iterations { get; set; } = 600_000;
```

Zwanzigmal so viel. Zwei Passwortwege in einer Bibliothek, und der schwächere
ist der, den `IDataEncryptionService` anbietet.

Dazu kommt, dass der Name über die Einheit täuscht. Die Doku sagt:

```csharp
/// <summary>
/// Time cost (iterations)
/// </summary>
public int TimeCost { get; set; } = 3;
```

„Iterations" — aber es ist ein Faktor auf 10 000. Wer die Doku liest und
`TimeCost = 600_000` setzt, weil er den OWASP-Wert kennt, bekommt sechs
Milliarden Runden und einen Dienst, der nicht mehr antwortet.

## Warum es Girders ist

```csharp
// nur Girder, kein Redis noetig: HashAsync fasst die Datenbank nicht an.
var dienst = new DataEncryptionService(
    Substitute.For<IKeyManagementService>(),
    NullLogger<DataEncryptionService>.Instance,
    Options.Create(new DataEncryptionOptions()),
    Substitute.For<IConnectionMultiplexer>());

var uhr = Stopwatch.StartNew();
var h = await dienst.HashAsync("ein-passwort");
uhr.Stop();

Console.WriteLine($"{h.Algorithm} Runden={h.Parameters["Iterations"]} in {uhr.ElapsedMilliseconds} ms");
// -> PBKDF2 Runden=30000 in ~15 ms
// zum Vergleich, derselbe Rechner:
Console.WriteLine(new Pbkdf2PasswordHasher().Hash("ein-passwort"));
// -> $pbkdf2-sha256$i=600000$…      also 600 000
```

## Nebenbefund an derselben Stelle

`HashingAlgorithm.SHA256` und `SHA512` sind über dieselbe `HashAsync`
erreichbar und rechnen **eine** Runde:

```csharp
private static byte[] HashSHA256(byte[] data, byte[] salt) => SHA256.HashData(combined);
```

Für einen Prüfwert über unveränderliche Daten ist das richtig. Für ein
Passwort ist es das nicht, und die API unterscheidet die beiden Zwecke nicht —
`HashAsync(password, new HashingOptions { Algorithm = HashingAlgorithm.SHA256 })`
compiliert und läuft. Dass `HashAsync` die speicherharten Verfahren seit 4.3
ausdrücklich **ablehnt**, statt PBKDF2 unter ihrem Namen zu liefern, zeigt, dass
diese Unterscheidung an dieser Stelle schon einmal bedacht wurde; sie ist nur
nicht zu Ende gegangen.

## Was es kostet

Wer über `IDataEncryptionService` hasht — die naheliegende Wahl, wenn man ohnehin
das Verschlüsselungsmodul fährt — bekommt einen Arbeitsfaktor, den Girder an
anderer Stelle selbst für zu niedrig erklärt.

Behebung, in dieser Reihenfolge:

1. `TimeCost` so auslegen, wie die Doku es sagt — als Rundenzahl, mit einer
   Vorgabe von 600 000 — oder umbenennen, damit der Faktor sichtbar wird.
   Ersteres bricht bestehende Hashes nicht: jeder Eintrag trägt seine
   `Iterations` mit, `VerifyHashAsync` liest sie von dort.
2. Einen Satz an `HashAsync`, der auf `IPasswordHasher` verweist, wo es um
   Passwörter geht.

## Stand

- [ ] behoben, Fassung: <…>
