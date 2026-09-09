# Plan: das Girder-Dashboard (5.0)

Ein Dienst, der Girder einrichtet, bekommt eine Seite, die zeigt, was er
eingerichtet hat. Ohne zusätzlichen Server, ohne npm, ohne Konto.

---

## Warum es das geben soll

Girder weiß schon fast alles über sich selbst — es kann es nur niemandem zeigen.

| Was Girder weiß | Wo es liegt | Wer es heute sieht |
|---|---|---|
| welche Module laufen, und welche warum nicht | `GirderComposition` | niemand, außer man protokolliert es selbst |
| welcher Anbieter fehlt | `ProviderRequirements` | nur als Ausnahme beim Start |
| wohin dieser Dienst rufen darf | `IEgressPolicy` | niemand |
| wohin er laut Konfiguration zeigt | `ISovereigntyReport` | wer `Assess()` selbst aufruft |
| welche Sitzungen offen sind | `ITokenSessionService` | nur je Person |
| ob die Prüfspur unversehrt ist | `IAuditTrailService` | niemand |
| ob die Bremse greift | `IDistributedRateLimitStore` | im Zweifel gar nicht |
| was gesund ist | Health Checks | `/health`, als JSON |

Jede dieser Auskünfte ist heute nur zu haben, indem jemand Code schreibt, der sie
abruft. Das ist die Lücke: **die Zusammensetzung ist der Kern von Girder 4, und
sie ist unsichtbar.** Das Dashboard ist die sichtbare Form davon.

Der zweite Grund ist die Aufnahme. Wer Girder das erste Mal einrichtet, hat heute
zwei Rückmeldungen: es startet, oder es startet nicht und nennt einen fehlenden
Anbieter. Dazwischen liegt alles, was man wissen will.

---

## Was es nicht wird

Diese drei Absagen tragen den Rest des Entwurfs.

**Kein zweiter Server.** Die Seite wird vom Dienst selbst ausgeliefert, wie
`/health`. Girders Zusage — *„adding Girder never adds a server you have to
run"* — gilt hier genauso.

**Kein Fremdpaket.** `Girder.Infrastructure` zieht 44 transitive Pakete; das war
der Grund für `Girder.Http`. Ein Dashboard, das Blazor, ein npm-Bündel oder ein
CDN mitbrächte, verlöre genau das wieder. Server-gerendertes HTML, eingebettete
Ressourcen, etwas Vanilla-JS. Messlatte wie bei `Girder.Http`: **null**.

**Keine Schreibbefehle in 5.0.** Eine Sitzung widerrufen, einen Cache leeren, ein
Geheimnis rotieren — das ist verlockend und ein eigener Bedrohungsraum. 5.0 liest
nur. Was 5.1 darf, entscheidet sich, wenn das Lesen steht.

---

## Der Zuschnitt

### Paket

`Girder.Dashboard` — eigenes Paket, `FrameworkReference` auf
`Microsoft.AspNetCore.App`, Projektverweis auf `Girder.Abstractions`, sonst
nichts. Wer es nicht referenziert, hat es nicht.

Der Wächter dazu steht schon: `HttpPackageStaysThinTests` prüft Projektdatei
*und* gebaute Assembly. Dasselbe für das Dashboard.

### Registrierung

Ein Modul wie jedes andere, also sichtbar in der Zusammensetzung und mit Grund
abwählbar:

```csharp
girder.UseDefaults()
      .UseDashboard(dashboard => dashboard
          .At("/girder")
          .VisibleTo(ctx => ctx.User.IsInRole("Operations")));
```

`GirderModule.Dashboard` — **nicht** in `UseDefaults()`. Es zeigt, wohin ein
Dienst rufen darf, welche Sitzungen offen sind und wie die Zusammensetzung
aussieht; das ist eine Entscheidung, keine Vorgabe.

### Wer es sehen darf — die Frage, an der es hängt

Die Seite ist eine Aufklärungshilfe für einen Angreifer: Modulliste,
Egress-Ziele, Anbieterlücken, offene Sitzungen. Deshalb, in dieser Reihenfolge:

1. **In `Production` ist es aus**, auch wenn das Modul gewählt wurde. Das
   Einschalten verlangt eine Begründung, nach dem Muster von `Without`:
   `.InProduction("Betrieb hat keinen anderen Zugang zum Cluster")` — ohne
   Zeichenkette übersetzt es nicht.
2. **Ohne `VisibleTo(...)` gibt es keine Seite**, auch nicht in Development. Kein
   Vorgabewert, der „alle" bedeutet. Eine 404, nicht eine 403 — eine 403 verrät,
   dass es die Seite gibt.
3. **Werte werden nie gezeigt.** Geheimnisnamen ja, Geheimnisse nein.
   Sitzungskennungen ja, Token nein. Es gelten `SensitiveFieldNames` und
   `[REDACTED]` — dieselbe Liste wie in den Logs, damit es nicht eine vierte
   Stelle wird, an der jemand entscheidet, was heikel ist.
4. **Jeder Aufruf steht in der Prüfspur.** Wer die Souveränitätslage eines
   Dienstes ansieht, hat das getan; `IAuditTrailService` hält es fest.

### Was es aus einem abgewählten Modul macht

Der wertvollste Teil. Ein Modul, das nicht läuft, verschwindet nicht — es steht
da mit dem Grund, den jemand geschrieben hat:

```
RateLimiting        nicht in Betrieb
                    „ein Dienst hinter dem Gateway sieht als Herkunft nur das
                     Gateway, also alle Aufrufer als einen — gebremst wird am
                     Eingang, in Bremse.cs"
```

Damit wird die Seite zu dem, was `Without(modul, grund)` immer werden sollte: ein
Ort, an dem die Entscheidung noch lesbar ist, wenn niemand mehr weiß, wer sie
getroffen hat.

---

## Die Flächen

Sechs, jede aus einer Quelle, die es schon gibt. Eine Fläche, deren Quelle nicht
registriert ist, zeigt das — statt zu fehlen.

| Fläche | Quelle | Zeigt |
|---|---|---|
| **Zusammensetzung** | `GirderComposition`, `ProviderRequirements` | jedes Modul: in Betrieb, abgewählt mit Grund, oder nicht genannt. Fehlende Anbieter mit dem Aufruf, der sie behebt |
| **Souveränität** | `ISovereigntyReport`, `IEgressPolicy` | jede Abhängigkeit mit ihrem Urteil; die erklärten Ziele; was ein nicht erklärter Aufruf tut |
| **Prüfspur** | `IAuditTrailService` | Länge der Kette, letzte Einträge (Akteur, Handlungsform, Handlung, Ressource — **keine** Zustände), und ob die Kette hält |
| **Sitzungen** | `ITokenSessionService` | offene Sitzungen je Person, Gerätemerkmal, Ablauf. Keine Token |
| **Grenzen** | `IDistributedRateLimitStore` | worauf gezählt wird, aktuelle Zähler, wer gerade abgewiesen wird |
| **Gesundheit** | `HealthCheckService` | dasselbe wie `/health`, nur lesbar |

### Erste Fläche zuerst

**Zusammensetzung.** Sie ist die einzige, deren Quelle in jeder Installation da
ist, sie braucht keinen Anbieter, und sie ist die, deren Fehlen am meisten
kostet. Wenn nur sie es in 5.0 schafft, ist das Dashboard trotzdem seinen Namen
wert.

---

## Die Reihenfolge

| Schritt | Was | Fertig, wenn |
|---|---|---|
| 1 | Paket + Wächter | `Girder.Dashboard` steht, Fremdpaketzahl **0**, Test dagegen |
| 2 | Sichtbarkeit | ohne `VisibleTo` gibt es 404; in `Production` ohne Begründung übersetzt es nicht; Gegenprobe |
| 3 | Fläche „Zusammensetzung" | ein Dienst mit zwei `Without(...)` zeigt beide Gründe wörtlich |
| 4 | Fehlende Anbieter | ein Dienst ohne Cache zeigt die Anforderung samt Behebungsaufruf |
| 5 | Souveränität | Urteile und erklärte Ziele; `Undetermined` liest sich nicht wie Bestanden |
| 6 | Prüfspur, Sitzungen, Grenzen, Gesundheit | je Fläche eine Messung gegen einen laufenden Dienst |
| 7 | Doku | README-Abschnitt, MIGRATION 4.4 → 5.0, SOVEREIGNTY um den Zugang |

Jeder Schritt mit Test zuerst und einer Gegenprobe, die **übersetzt**.

---

## Die drei Fragen, die vor dem ersten Commit zu klären sind

1. **Ist das Dashboard ein Modul oder ein eigenes Paket, das Module liest?**
   Der Entwurf oben sagt: beides — Paket `Girder.Dashboard`, Modul
   `GirderModule.Dashboard`. Das kostet ein Paket mehr im Verzeichnis und ist
   dafür abwählbar wie alles andere.

2. **Wie kommt es an `GirderComposition`, ohne `Girder.Infrastructure` zu
   referenzieren?** `GirderComposition` liegt in `Girder.Abstractions` — das
   trägt. `ISovereigntyReport` und `IAuditTrailService` liegen dagegen in
   `Girder.Infrastructure`. Entweder wandern ihre Schnittstellen nach
   `Abstractions` (richtig, und ein Bruch), oder das Dashboard liest sie über
   `IServiceProvider` ohne Typbezug (kein Bruch, und hässlich). **Das ist die
   Entscheidung, die 5.0 zu einer Hauptversion macht** — und sie sollte in
   Richtung `Abstractions` fallen, weil es dieselbe Frage ist wie bei
   `GirderBuilder` in 4.0.

3. **Was passiert bei mehreren Instanzen?** Zusammensetzung und Souveränität sind
   je Instanz gleich. Prüfspur-Kette, Bremszähler und Sitzungen sind es nicht —
   eine Kette je Replikat, wie in `SOVEREIGNTY.md` unter „Known gaps" steht. Die
   Seite muss sagen, welche Instanz sie zeigt, sonst liest jemand einen
   Ausschnitt als das Ganze.
