# Die Prompts bis Neolia 5.0.0

**Stand:** 13.09.2026 · **Gehört zu:** [MASTERPLAN-5.0.md](MASTERPLAN-5.0.md)

Einen Kasten kopieren, in eine frische Sitzung, fertig. Jeder ist
selbsttragend — die Sitzung, die ihn bekommt, kennt dieses Repository nicht.

**Die Reihenfolge ist nicht beliebig.** C vor D, weil ein Befund, der bei der
Umbenennung mitwandert, doppelt kostet. E vor F, weil das Dashboard anzeigt,
was E erst erzeugt.

| | Was | Wo | Größe |
|---|---|---|---|
| A | WorkerTransfer auf Girder 4.4.1 | workertransfer | 20 min |
| B | Advisory, Secret Scanning, Dependabot | **GitHub-UI, nur David** | 10 min |
| C | Die neun Befunde einordnen und die Sicherheitsrelevanten beheben | Girder | 1 Sitzung |
| D | Umbenennung Girder → Neolia | Girder | 1 Sitzung |
| E | Der Modulvertrag: Requires/Provides, geprüft beim Start | Neolia | 2–3 Sitzungen |
| F | Das Dashboard | Neolia | 2 Sitzungen |
| G | WorkerTransfer auf Neolia 5.0.0 | workertransfer | 1 Sitzung |

---

## A — WorkerTransfer auf 4.4.1

````
Du arbeitest im Repository WorkerTransfer (~/Projects/workertransfer),
.NET 10 auf Girder, React.

ZUERST, ohne zu fragen:
  git switch develop && git pull && git switch -c fix/girder-4.4.1
Diese Sitzung arbeitet NIE direkt auf develop.

WARUM: Girder 4.4.1 behebt einen Sicherheitsdefekt. `DataEncryptionService`
in `Girder.Redis` legte den Klartext ab und meldete dabei `AES256GCM`; ein
fremder Schluessel entschluesselte mit `IntegrityVerified = true`.

WorkerTransfer ist NICHT betroffen — es benutzt `AddEncryption` nicht und
referenziert `Girder.Redis` nirgends. Auf einer Fassung zu bleiben, deren
Nachfolger einen Sicherheitsfix traegt, ist trotzdem genau das, was wir bei
anderen kritisieren wuerden.

AUFGABE, klein und abgeschlossen:

1. `Directory.Packages.props`: `<GirderVersion>` von 4.4.0 auf 4.4.1.
   Es ist EINE Zahl — alle Girder-Pakete haengen daran.

2. `bugs/verschluesselung-verschluesselt-nicht.md`: die `## Stand`-Haken
   schliessen. "In Girder behoben, Fassung 4.4.1." Der Haken fuer einen
   Umweg bleibt LEER — einen Umweg gab es hier nie, wir waren nicht
   betroffen. Trag das so ein, statt ihn abzuhaken.

3. Pruefe, ob CLAUDE.md irgendwo "4.4.0" als Girder-Fassung nennt. Wenn ja,
   auf 4.4.1 ziehen.

4. MESSEN, nicht glauben: nach dem Restore muss die .nupkg.metadata jedes
   Girder-Pakets 4.4.1 sagen. Fahr `dotnet restore` gegen einen LEEREN
   Paketordner (`--packages /tmp/probe`), sonst kommt es aus dem Cache und
   beweist nichts.

FALLEN: build und test nie zusammen; nie `dotnet test` ueber die Loesung,
nur ./scripts/test-dotnet.sh; Warnungen sind Fehler; dein .env ist NICHT das
der CI.

ZUM SCHLUSS — selber erledigen, nicht zurueckfragen:
1. dotnet build WorkerTransfer.slnx
2. ./scripts/test-dotnet.sh
3. cd web && pnpm check && pnpm test && pnpm build && cd ..
4. Committen, git push -u origin fix/girder-4.4.1
5. gh pr create --base develop --fill
6. gh pr checks --watch
7. ERST DANN: gh pr merge --merge --delete-branch
8. UND DANN den develop-Lauf ansehen — der PR prueft den Vorschlag, nicht
   das Ergebnis.

Ein roter Lauf wird nicht gemergt. Kein --admin.
````

---

## B — Nur David, keine Sitzung

Drei Griffe in der GitHub-Oberfläche:

1. **Advisory freigeben** — `GHSA-276v-hjxx-vrmw` liegt als Entwurf unter
   `github.com/DavidOeztuerk/girder/security/advisories`. Einmal selbst
   lesen, dann veröffentlichen. Das ist deine Unterschrift.
2. **Secret Scanning + Push Protection** einschalten
   (Settings → Code security). Fängt einen Schlüssel, **bevor** er im
   Verlauf landet.
3. **Dependabot Security Updates** einschalten. Es gibt bewusst kein
   `dependabot.yml` für Versionssprünge — das hier ist etwas anderes und
   meldet nur Sicherheitslücken.

---

## C — Die neun Befunde einordnen

````
Du arbeitest im Repository Girder (~/Projects/Girder), einer oeffentlichen
.NET-Bibliothek unter MIT.

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c sicherheit/neun-befunde
Diese Sitzung arbeitet NIE direkt auf main.

Lies: docs/befunde/ (neun Tickets) · docs/MASTERPLAN-5.0.md ·
docs/MESSUNG-GEHEIMNISFRAGE.md · SECURITY.md · README.md (Versionspolitik)

AUSGANGSLAGE: am 12.09.2026 wurden neun Befunde gemessen und einzeln
dokumentiert, aber keiner behoben — die Sicherheitsveroeffentlichung 4.4.1
sollte klein bleiben. Jetzt werden sie eingeordnet und die dringenden
behoben.

AUFGABE 1 — EINORDNEN, und zwar begruendet

Je Befund entscheidest du EINE von drei Einstufungen und schreibst sie oben
ins Ticket:

  SICHERHEIT — wird in 4.4.2 behoben. Kriterium: ein Nutzer, der die
    Bibliothek bestimmungsgemaess einsetzt, haelt etwas fuer geschuetzt, das
    es nicht ist. Der Bremsenfehler ist das Musterbeispiel.
  GESTALTUNG — wird Teil von 5.0.0. Kriterium: es ist falsch gebaut, aber
    niemand haelt etwas Falsches fuer wahr.
  KEINE AENDERUNG — mit Grund. Auch das ist ein Ergebnis.

Bei SICHERHEIT gehoert eine Schwereeinschaetzung dazu, BEGRUENDET und nicht
uebernommen. Vorbild ist der Text von GHSA-276v-hjxx-vrmw: er sagt, warum
High und nicht Critical, und warum eine CVSS-Zahl die Lage unterschaetzt.

AUFGABE 2 — DIE SICHERHEITSRELEVANTEN BEHEBEN

Mindestens `bremse-millisekunde` ist gemessen und ernst: die verteilte
Bremse legt den Zeitpunkt als Mitglied in eine sortierte Menge, ein
gleichzeitiger Stoss faellt zu EINEM Eintrag zusammen. Girders eigenes
Skript, Limit 10: 50 Aufrufe in derselben Millisekunde → erlaubt 50,
abgelehnt 0, ZCARD 1.

Je Behebung eine GEGENPROBE, die KOMPILIERT und gemessen faellt. Die Probe
fuer die Bremse ist vorgegeben: fuenfzig gleichzeitige Aufrufer, zehn
Plaetze, und es duerfen genau zehn durchkommen.

UND DIE TESTLUECKE MIT BEHEBEN. Zweimal in einer Woche war dasselbe der
Grund, dass niemand es fand:
  - Verschluesselung: ein Hin-und-Rueck-Test ist trivial gruen, wenn NICHTS
    passiert.
  - Bremse: die Pruefung "fuenfzig Aufrufer, zehn Plaetze" GIBT ES — aber
    niemand leitet sie fuer die Redis-Umsetzung ab. Die Umsetzung, die in
    Betrieb geht, ist nie dagegen gelaufen.
Sorg dafuer, dass jede Umsetzung einer Schnittstelle gegen DIESELBE
Pruefreihe laeuft. Wenn das ein Muster braucht, bau es und schreib auf,
warum.

AUFGABE 3 — VEROEFFENTLICHEN, falls etwas SICHERHEIT war

`VersionPrefix` auf 4.4.2, Release anlegen, und je behobenem Befund einen
Advisory-ENTWURF (nicht veroeffentlichen — David gibt frei). Wenn nichts
SICHERHEIT war, wird nichts veroeffentlicht; sag das klar.

FALLEN: Warnungen sind Fehler; eine Gegenprobe muss KOMPILIEREN, sonst
liest sich der Build-Fehler wie ein bestandener Test; nach einer Gegenprobe
mit --no-incremental bauen, auch nach dem ZURUECKSETZEN; `dotnet pack` muss
warnungsfrei bleiben, auch NU5*.

NICHT: umbenennen (eigene Sitzung), unlisten, ein Advisory veroeffentlichen.

ZUM SCHLUSS: build, test, pack, committen, PR nach main,
gh pr checks --watch, erst dann mergen.

BERICHTE: die Einstufung je Befund mit Grund, was behoben wurde, und ob die
Testluecke geschlossen ist — samt der Probe, die sie schliesst.
````

---

## D — Umbenennung auf Neolia

````
Du arbeitest im Repository Girder (~/Projects/Girder).

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c umbenennung/neolia
Diese Sitzung arbeitet NIE direkt auf main.

Lies: docs/MASTERPLAN-5.0.md (§6) · README.md · alle .csproj ·
.github/workflows/publish.yml

WARUM: `Girder` ist auf nuget.org von einem fremden Projekt aus dem
Datenumfeld besetzt — wer sucht, findet das Falsche. `Neolia` ist frei,
geprueft am 13.09.2026 fuer neolia, .core, .abstractions, .http, .redis,
.infrastructure.

DAS IST EINE MECHANISCHE AENDERUNG MIT EINER GEFAHR: sie beruehrt jede
Datei, und dabei geht leicht etwas unter. Arbeite deshalb in dieser
Reihenfolge und miss nach jedem Schritt.

1. NAMENSRAEUME UND PAKETKENNUNGEN. `Girder.` → `Neolia.` in
   Namensraeumen, using-Zeilen, Projektnamen, .csproj-Dateinamen,
   Verzeichnissen, der .slnx. Ein blindes Suchen-und-Ersetzen ueber den
   ganzen Baum ist ZU GROB: es trifft auch Prosa in Kommentaren, in der
   "Girder" als historischer Name richtig bleibt. Unterscheide.

2. WAS NICHT UMBENANNT WIRD: die Geschichte. Commit-Nachrichten, ADRs, die
   Befunde, MESSUNG-GEHEIMNISFRAGE.md und das Advisory reden von Girder,
   und das war damals richtig. Setz oben in README.md einen Absatz
   "Frueher Girder" mit dem Datum und dem Grund.

3. DAS REPOSITORY selbst umbenennen kann nur David (GitHub-Einstellungen).
   Bereite alles vor, was danach zeigt — RepositoryUrl,
   PackageProjectUrl, Links in der README — und SAG IHM, welchen Schalter
   er umlegen muss. GitHub leitet den alten Namen automatisch weiter.

4. VERSION: 5.0.0. Das ist ohnehin eine Hauptversion (Modulvertrag, siehe
   Prompt E), die Umbenennung reist mit, statt einen eigenen Bruch zu
   erzeugen. Setz `VersionPrefix` auf 5.0.0-preview.1 — VORABVERSION, denn
   E steht noch aus.

5. VORABVERSIONEN GEHEN NACH GITHUB PACKAGES, NICHT NACH NUGET.ORG. Das
   steht so in der Versionspolitik der README. Pruef, ob publish.yml das
   einhaelt, und stell es ab, wenn nicht.

6. DIE ALTEN PAKETE WERDEN NICHT UNGELISTET. Sie werden DEPRECATED mit
   Verweis auf den Nachfolger — sie bleiben installierbar, zeigen im Editor
   eine Warnung und nennen `Neolia.*` als Alternative. Fuer `Girder.Redis`
   zusaetzlich der Grund "Critical Bugs".
   Das kann nur David in der nuget.org-Oberflaeche. Schreib ihm die genaue
   Liste: welches Paket, welcher Grund, welcher Nachfolger.

MESSEN, nicht glauben:
  - `dotnet build Neolia.slnx -c Release` → 0 Warnungen
  - `dotnet test Neolia.slnx` → alle gruen
  - `dotnet pack -c Release -o /tmp/pakete` → 12 Pakete `Neolia.*`,
    warnungsfrei, auch NU5*
  - `grep -ril "girder" src/ tests/` → nur noch Prosa, und du zeigst die
    Liste

FALLEN: Warnungen sind Fehler; `dotnet pack` warnungsfrei inkl. NU5*; die
Source-Link-Angaben zeigen nach der Umbenennung ins Leere, wenn
RepositoryUrl nicht mitwandert.

ZUM SCHLUSS: build, test, pack, committen, PR nach main, Pruefungen
abwarten, erst dann mergen.

BERICHTE: welche Schalter David umlegen muss (Repository-Name,
Deprecation je Paket), und ob im Baum noch ein "Girder" steht, das keine
Geschichte ist.
````

---

## E — Der Modulvertrag

````
Du arbeitest im Repository Neolia (~/Projects/Girder, nach der
Umbenennung).

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c 5.0/modulvertrag
Diese Sitzung arbeitet NIE direkt auf main.

Lies: docs/MASTERPLAN-5.0.md (§1 und §2 — sie tragen alles hier) ·
docs/MESSUNG-GEHEIMNISFRAGE.md · docs/befunde/

DAS PROBLEM, in einem Satz: etwas ist REGISTRIERT und deshalb ANWESEND,
aber nicht WIRKSAM — und nichts im System merkt den Unterschied. Zehn
Funde einer Woche haben genau diese Form; die Tabelle steht im Masterplan.

VIER AENDERUNGEN, und sie machen zusammen 5.0.0 aus.

1. EIN MODUL ERKLAERT, WAS ES BRAUCHT UND WAS ES LIEFERT.

   Requires<IDataEncryptionService>()  — ohne das kann ich nicht
   Provides<ISecretStore>()            — das gebe ich anderen

   Beim Start wird die Zusammensetzung GEPRUEFT. Fehlt ein Requires, endet
   der Start mit einer Meldung, die DREI Dinge nennt:
     was fehlt · wer es braucht · WELCHES PAKET es liefert, samt Aufruf
     ("Neolia.Redis → AddRedisEncryption()")

   Der dritte Punkt ist der wichtigste. Die Bibliothek installiert nichts
   von selbst — das darf sie nicht —, aber sie nennt den genauen Befehl.

   GEGENPROBE: ein Modul mit einem Requires, dessen Anbieter fehlt, muss den
   Start abbrechen. Entfernst du die Pruefung, faellt die Probe.

2. EIN REGISTRIERTER DIENST, DEN NIEMAND LIEST, IST EIN DEFEKT.

   `AddSecretManagement` bindet `SecretRotationOptions` — kein Leser. Das
   ist keine Unschoenheit, es ist eine Falschaussage: der Name verspricht
   Rotation, es passiert nichts, niemand erfaehrt es.

   Bau einen Test, der ueber die Assembly misst: jede registrierte
   Optionsklasse hat mindestens einen Verbraucher. Was keinen hat, fliegt
   raus oder bekommt einen. Dasselbe fuer Schnittstellen — `ISecretManager`
   hat in der ganzen Bibliothek keinen Verbraucher.

3. JEDES PAKET IST ALLEIN LAUFFAEHIG — ODER SAGT, WANN NICHT.

   `Neolia.Http` beweist, dass es geht: null Fremdpakete. Die Regel:
     - allein lauffaehig ODER Start bricht ab und NENNT das fehlende
       Geschwisterpaket. Kein drittes.
     - die transitive Last je Paket steht in der README ALS ZAHL, und ein
       Test haelt sie fest (Neolia.Infrastructure zog 44).
     - kein Paket setzt ein anderes stillschweigend voraus.

4. ABSICHT WIRD GEGEN DIE WIRKLICHKEIT GEPRUEFT.

   `.Without(modul, grund)` bleibt — die Pflichtbegruendung ist richtig.
   Was fehlt, ist die Gegenprobe: der Startbericht sagt, was WIRKLICH
   laeuft, und ein Test haelt die Liste gegen die Absicht. Heute koennen
   sie auseinanderlaufen, ohne dass es jemand merkt: WorkerTransfers
   CLAUDE.md fuehrt fuenf Module als "bewusst nicht", die laufen.

AUSSERDEM, und es gehoert hierher: `ISovereigntyReport` und
`IAuditTrailService` liegen in `Neolia.Infrastructure`. Damit kommt das
Dashboard (Prompt F) nicht an sie heran, ohne 44 Fremdpakete zu ziehen.
Ihre Schnittstellen wandern nach `Neolia.Abstractions`. Das ist ein Bruch
und der zweite Grund, warum 5.0 eine Hauptversion ist.

FALLEN: Warnungen sind Fehler; jede Gegenprobe muss KOMPILIEREN; nach einer
Gegenprobe mit --no-incremental bauen, auch nach dem Zuruecksetzen; `dotnet
pack` warnungsfrei inkl. NU5*.

ZUM SCHLUSS: build, test, pack, committen, PR nach main, Pruefungen
abwarten, erst dann mergen.

BERICHTE: wie viele Optionsklassen und Schnittstellen ohne Leser du
gefunden hast, was du mit ihnen gemacht hast, und ob ein `.Without` nicht
wirkte.
````

---

## F — Das Dashboard

````
Du arbeitest im Repository Neolia (~/Projects/Girder).

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c 5.0/dashboard
Diese Sitzung arbeitet NIE direkt auf main.

Lies ZUERST und vollstaendig: docs/PLAN-DASHBOARD-5.0.md. Er ist der
Auftrag; dieser Prompt ergaenzt ihn nur um das, was seither entschieden
wurde.

Dazu: docs/MASTERPLAN-5.0.md (§1 und §3) · SOVEREIGNTY.md

DIE DREI FRAGEN aus dem Plan sind entschieden:

1. Eigenes Paket `Neolia.Dashboard`, Modul `NeoliaModule.Dashboard` —
   abwaehlbar wie alles andere.
2. `ISovereigntyReport` und `IAuditTrailService` sind in Prompt E nach
   `Abstractions` gewandert. Das Dashboard liest sie TYPISIERT und zieht
   `Neolia.Infrastructure` NICHT.
3. Die Seite sagt, WELCHE INSTANZ sie zeigt. Zusammensetzung und
   Souveraenitaet sind je Instanz gleich; Pruefspur, Bremszaehler und
   Sitzungen sind es nicht.

ZWEI ZUSAGEN, die nicht verhandelbar sind:

  KEIN WERT, NIEMALS. Die Seite zeigt Gestalten: "gesetzt (44 Zeichen)",
  "fehlt", "Vorgabe". Nie einen Schluessel, nie eine Verbindungszeichen-
  folge, nie ein Geheimnis — auch nicht gekuerzt, auch nicht maskiert.
  Dieselbe Regel wie im Protokoll. Ein Dashboard, das
  WORKERTRANSFER_SECRETS_KEY anzeigt, waere der elfte Befund.

  5.0 LIEST NUR. Keine Schreibbefehle, auch wenn sie gewuenscht wurden:
  eine Seite, die Konfiguration aendert, ist ein Angriffsziel ersten
  Ranges, und die Umgebung soll der Mechanismus bleiben. 5.1 entscheidet
  ueber das Schreiben, wenn das Lesen steht.

WAS SIE ZEIGT — und warum sie das Gegenteil der zehn Funde ist:
  - welche Module laufen (aus NeoliaComposition)
  - welche ABGEWAEHLT sind, mit ihrer Begruendung aus `.Without`
  - je Modul: was es BRAUCHT und ob der Anbieter da ist (aus Prompt E)
  - je Modul: was es LIEFERT und ob jemand es liest
  - welche Konfiguration es sieht, als Gestalt
  - die Egress-Grenze: welche Hosts erlaubt sind

Ein Modul, das laeuft und nichts bewirkt, MUSS hier als solches zu sehen
sein. Das ist der ganze Zweck. Bau eine Probe, die genau das nachstellt.

MESSLATTE: null Fremdpakete, wie `Neolia.Http`. Server-gerendertes HTML,
eingebettete Ressourcen, etwas Vanilla-JS. Kein Blazor, kein npm, kein CDN.

FALLEN: Warnungen sind Fehler; jede Gegenprobe muss KOMPILIEREN; `dotnet
pack` warnungsfrei inkl. NU5*; die Seite darf keinen Wert ausgeben — bau
einen Test, der genau danach sucht.

ZUM SCHLUSS: build, test, pack, committen, PR nach main, Pruefungen
abwarten, erst dann mergen.

BERICHTE: wie die Zugriffsentscheidung ausgefallen ist, und was die Seite
bei einem Modul zeigt, das laeuft und nichts tut.
````

---

## G — WorkerTransfer auf Neolia 5.0.0

````
Du arbeitest im Repository WorkerTransfer (~/Projects/workertransfer).

ZUERST, ohne zu fragen:
  git switch develop && git pull && git switch -c feature/neolia-5
Diese Sitzung arbeitet NIE direkt auf develop.

Lies: CLAUDE.md (Abschnitt "Die Girder-Module" und die Tabelle darunter) ·
~/Projects/Girder/docs/MASTERPLAN-5.0.md

AUFGABE: von Girder 4.4.x auf Neolia 5.0.0. Das ist eine Umbenennung UND
ein Bruch — der Modulvertrag aus 5.0 verlangt, dass jedes Modul sagt, was
es braucht.

1. `Directory.Packages.props`: alle `Girder.*` auf `Neolia.*`, Version
   5.0.0. `NuGet.Config`: das Source Mapping zeigt auf `Neolia.*`.
2. Jedes `using Girder.` → `using Neolia.`. Wie in Prompt D: ein blindes
   Ersetzen ist zu grob, "Girder" in Prosa bleibt historisch richtig.
3. `Dienstgrundlage.cs`: der Modulvertrag greift. Wo ein `Requires` nicht
   erfuellt ist, BRICHT DER START AB — mit einer Meldung, die das Paket
   nennt. Das ist kein Fehler, das ist der Zweck. Geh die Meldungen durch.
4. DIE TABELLE IN CLAUDE.md WIRD EHRLICH. Sie fuehrt fuenf Module als
   "bewusst nicht", die in Wahrheit laufen — der Startbericht sagt es
   selbst. Nach 5.0 gibt es dafuer eine Pruefung; trag ein, was WIRKLICH
   laeuft, mit dem Datum der Messung.
5. Das Dashboard steht dann zur Verfuegung. Entscheide NICHT allein, ob es
   eingeschaltet wird und wer es sehen darf — leg David die Frage vor. Auf
   einer Plattform, die ueber Einwilligung entscheidet, ist eine Seite, die
   die Zusammensetzung zeigt, keine Kleinigkeit.

MESSEN: `dotnet restore --packages /tmp/probe` gegen einen LEEREN
Paketordner, und die .nupkg.metadata muss `neolia.*` 5.0.0 sagen.

FALLEN: build und test nie zusammen; nie `dotnet test` ueber die Loesung,
nur ./scripts/test-dotnet.sh; nach jeder Modelaenderung sofort `dotnet ef
migrations add`; ein aenderndes SichereAsync braucht .AsTracking();
Warnungen sind Fehler; ein neuer Aufruf nach draussen muss in der
Konfiguration stehen; dein .env ist NICHT das der CI.

ZUM SCHLUSS:
1. dotnet build WorkerTransfer.slnx
2. ./scripts/test-dotnet.sh
3. cd web && pnpm check && pnpm test && pnpm build && cd ..
4. Committen, pushen, gh pr create --base develop --fill
5. gh pr checks --watch
6. ERST DANN: gh pr merge --merge --delete-branch
7. UND DANN den develop-Lauf ansehen.

BERICHTE: welche `Requires` beim ersten Start gefehlt haben — das ist die
interessanteste Zahl der ganzen Umstellung.
````
