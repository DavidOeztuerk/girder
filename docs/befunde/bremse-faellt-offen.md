# Die verteilte Bremse lässt durch, wenn Redis ausfällt — und sagt es niemandem

- **Girder-Fassung:** 4.4.0 (unverändert in 4.4.1)
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Lücke
- **Blockiert:** nein

## Was passiert

`RedisDistributedRateLimitStore.SlidingWindowIncrementAsync` fängt jeden Fehler
und antwortet mit `IsAllowed = true`:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to execute sliding window increment for key {Key}", key);

    // Fallback: Allow request but log error
    return new WindowCheckResult { IsAllowed = true, CurrentCount = 0, Limit = limit, … };
}
```

Dieselbe Entscheidung noch einmal eine Ebene höher, in
`DistributedRateLimitingMiddleware`:

```csharp
_logger.LogError(ex, "Rate limit check failed for key {Key}, allowing request", key);
```

Ist Redis weg, ist die Bremse weg. Es steht kein Schalter daneben, und in der
README steht es auch nicht.

## Warum es Girders ist

```csharp
// nur Girder. Redis absichtlich auf einen toten Port zeigen lassen.
var muxer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
{
    EndPoints = { "127.0.0.1:6399" },   // hier lauscht nichts
    AbortOnConnectFail = false,
    ConnectTimeout = 200
});

var speicher = new RedisDistributedRateLimitStore(
    muxer, NullLogger<RedisDistributedRateLimitStore>.Instance);

for (var i = 0; i < 100; i++)
{
    var r = await speicher.SlidingWindowIncrementAsync("probe", limit: 1, TimeSpan.FromMinutes(1));
    Console.WriteLine($"{i}: erlaubt={r.IsAllowed}");
}
// -> 100-mal erlaubt=True, bei einem Limit von 1
```

**Der Zusage widerspricht es nicht wörtlich, aber sie schweigt.** Die README
beschreibt für die Token-Rücknahme ausdrücklich, was bei einem Ausfall gilt
(*„Behaviour during an outage"*). Für die Bremse steht nichts — und genau die
Lücke füllt der Leser mit der Annahme, die bei einer Sicherheitsleistung
naheliegt: dass sie im Zweifel schließt.

## Was es kostet

Ein Redis-Ausfall verwandelt eine gebremste Anmeldung in eine ungebremste,
ohne dass sich am Verhalten sonst etwas ändert — es gibt kein Merkmal am
Ergebnis, an dem ein Aufrufer den Unterschied sähe. `CurrentCount = 0` sieht aus
wie „noch nichts verbraucht", nicht wie „nicht gezählt".

Zwei Wege, und beide sind vertretbar — nur nicht dieser dritte, stille:

1. **Offen fallen, aber es sagen.** Ein Feld am `WindowCheckResult`
   (`Counted`/`Degraded`), damit die Zwischenschicht entscheiden kann, und ein
   Satz in der README.
2. **Geschlossen fallen, wahlweise.** `DistributedRateLimitingOptions.FailOpen`
   mit einer ausgesprochenen Vorgabe.

Dass hier etwas entschieden wurde, ist richtig. Dass die Entscheidung nur im
Kommentar eines `catch`-Blocks steht, ist der Befund.

## Stand

- [ ] entschieden, welcher Weg
- [ ] behoben, Fassung: <…>
