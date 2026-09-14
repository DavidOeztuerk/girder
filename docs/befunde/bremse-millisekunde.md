# Die verteilte Bremse zählt einen Stoß in derselben Millisekunde als **einen** Aufruf

- **Girder-Fassung:** 4.4.0 bis 4.4.2
- **Gefunden beim:** Absuchen der Bibliothek nach dem Verschlüsselungsdefekt, Phase 2, 13.09.2026
- **Art:** Sicherheitsfehler (CWE-770: fehlende wirksame Begrenzung)
- **Einstufung:** hoch — die verteilte Bremse unterschätzt gerade parallele Angriffsversuche
- **Blockiert:** nein — aber jede Bremse vor einer Anmeldung ist damit umgehbar
- **Advisory:** [GHSA-g396-93pv-84w5](https://github.com/DavidOeztuerk/girder/security/advisories/GHSA-g396-93pv-84w5)

## Was passiert

`Girder.Redis.Caching.RedisDistributedRateLimitStore.SlidingWindowIncrementAsync`
führt ein Lua-Skript aus, das den Zeitpunkt **als Mitglied** in eine sortierte
Menge legt:

```lua
redis.call('ZADD', key, now, now)   -- Bewertung = now, MITGLIED = now
```

`now` ist `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`. Zwei Aufrufe in
derselben Millisekunde tragen dasselbe Mitglied ein — `ZADD` aktualisiert dann
die Bewertung und die Menge bleibt gleich groß. `ZCARD` steigt nicht, also
steigt der Zähler nicht, also wird nichts abgelehnt.

Gemessen gegen Redis 8, mit Girders eigenem Skript und sonst nichts:

```
50 Aufrufe, Limit 10, ALLE mit demselben now
  erlaubt: 50   abgelehnt: 0   ZCARD: 1

50 Aufrufe, Limit 10, jede mit eigenem now
  erlaubt: 10   abgelehnt: 40   ZCARD: 10
```

Erwartet war beide Male `erlaubt: 10`.

Die zweite Zeile ist die Gegenprobe: dieselbe Bremse zählt richtig, sobald die
Aufrufe sich um eine Millisekunde unterscheiden. Der Defekt liegt also genau
und nur im gleichzeitigen Stoß — und das ist die Last, gegen die eine Bremse da
ist.

## Warum es Girders ist

Kein Anwendungscode, nur das Skript aus `RedisDistributedRateLimitStore.cs`
und ein Wegwerf-Redis:

```bash
docker run -d --rm --name probe-redis -p 6398:6379 redis:8-alpine
```

`bremse.lua` — wörtlich aus `src/Girder.Redis/Caching/RedisDistributedRateLimitStore.cs`,
Feld `SlidingWindowScript`:

```lua
local key = KEYS[1]
local window = tonumber(ARGV[1])
local limit = tonumber(ARGV[2])
local now = tonumber(ARGV[3])

redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
local current = redis.call('ZCARD', key)

if current < limit then
    redis.call('ZADD', key, now, now)
    redis.call('EXPIRE', key, math.ceil(window / 1000))
    return {1, current + 1, limit}
else
    return {0, current, limit}
end
```

```bash
# 50 Aufrufe, Limit 10, alle mit demselben Zeitpunkt
erlaubt=0
for i in $(seq 1 50); do
  r=$(redis-cli --eval bremse.lua probe:gleich , 60000 10 1000000 | head -1)
  [ "$r" = "1" ] && erlaubt=$((erlaubt+1))
done
echo "erlaubt: $erlaubt  (erwartet: 10)"   # -> erlaubt: 50
```

Gleichwertig über die öffentliche Schnittstelle, wenn man den Zeitpunkt nicht
von Hand setzen will: genügend gleichzeitige Aufrufer, die in dieselbe
Millisekunde fallen.

**Und der Zusage widerspricht es wörtlich.** README, Abschnitt *Rate limiting*:

> `IDistributedRateLimitStore.SlidingWindowIncrementAsync` counts and decides in
> one indivisible step. Implementations that cannot guarantee that are not valid
> implementations of the port — a conformance suite asserts it with fifty
> concurrent callers racing for ten slots.

Unteilbar **ist** es — Lua läuft atomar. Es zählt nur falsch.

## Warum die eigenen Tests es nicht merken

Das ist der lehrreiche Teil, und es ist dieselbe Bauart wie beim
Verschlüsselungsdefekt.

Die Prüfung, die diesen Fall beschreibt, **gibt es**:
`RateLimitStoreConformance.Counting_and_deciding_happen_as_one_step` — fünfzig
Aufrufer, zehn Plätze, genau die gemessene Lage. Nur leitet sich von dieser
Klasse niemand für Redis ab:

```
tests/…/Caching/RateLimitStoreConformance.cs:170  InMemoryRateLimitStoreConformanceTests
tests/…/Caching/RateLimitStoreConformance.cs:188  InProcessRateLimitStoreConformanceTests
                                                  — und das war es.
```

Die beiden Umsetzungen, die die Prüfung bestehen, halten die Zusage auch:
`InProcessRateLimitStore` führt eine `List<DateTimeOffset>`, in der ein
doppelter Zeitpunkt zweimal steht. Die Umsetzung, die in Betrieb geht — die
**geteilte** —, ist die einzige, die nie gegen die Prüfung gelaufen ist.

Und selbst wenn man die Ableitung nachträgt, fällt sie nicht zuverlässig: fünfzig
Umläufe zu einem Redis-Container dauern zusammen mehr als eine Millisekunde, die
Aufrufe verteilen sich also von selbst. Eine Prüfung, die diesen Defekt sicher
zeigt, muss den Zeitpunkt festhalten, statt auf die Uhr zu hoffen.

## Was es kostet

Wer `AddDistributedRateLimiting` mit einem Redis-Speicher fährt, hat vor jedem
gebremsten Pfad eine Bremse, die einen gleichzeitigen Stoß durchlässt. Bei einer
Anmeldung ist genau das die Last, die gebremst gehört: ein Werkzeug, das
Zugangsdaten durchprobiert, schickt sie gleichzeitig, nicht nacheinander.

Die Obergrenze bleibt für den *langsamen* Fall wirksam — pro Millisekunde kommt
ein Eintrag hinzu, das Fenster füllt sich also weiter. Es ist keine vollständige
Aufhebung der Bremse, sondern eine Aufhebung genau für Gleichzeitigkeit.

Umweg: `UseSlidingWindow = false` schaltet auf das feste Fenster, dessen Skript
mit `INCR` arbeitet und richtig zählt. Das kostet die Eigenschaft, dass ein Stoß
am Fensterende nicht ins nächste Fenster übergreift — also genau das, wofür das
gleitende Fenster da ist.

Behebung: ein eindeutiges Mitglied je Aufruf, etwa `now .. ':' .. ARGV[4]` mit
einer gewürfelten Kennung — die übliche Bauart eines gleitenden
Fensterprotokolls. Die Bewertung bleibt `now`, damit `ZREMRANGEBYSCORE`
unverändert arbeitet.

## Stand

- [x] gemessen, 13.09.2026
- [x] behoben für die nächste Patch-Fassung: Zeit bleibt Bewertung, eine zufällige 128-Bit-Kennung identifiziert jeden Aufruf
- [x] `RedisRateLimitStoreConformanceTests` nachgetragen, einschließlich 50 Aufrufen mit festgehaltenem Zeitpunkt
- [x] lokaler 4.4.3-Paketkandidat im Demo-Projekt bestätigt
- [x] 4.4.3 veröffentlicht und danach erneut anonym von NuGet.org geprüft
