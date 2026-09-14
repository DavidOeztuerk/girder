# Die Prompts bis Noelia 5.0.0

**Stand:** 14.09.2026 · **Gehört zu:** [MASTERPLAN-5.0.md](MASTERPLAN-5.0.md)

Einen Kasten kopieren, in eine frische Sitzung, fertig. Jeder ist
selbsttragend — die Sitzung, die ihn bekommt, kennt dieses Repository nicht.

**Die Reihenfolge ist nicht beliebig.** A bis D sind erledigt. C kam vor D,
weil ein Befund, der bei der Umbenennung mitwandert, doppelt gekostet hätte. E kommt
vor F, weil das Dashboard anzeigt, was E erst erzeugt. G ist das Release-Gate.
WorkerTransfer bleibt bis danach außer Betracht.

| | Was | Wo | Größe |
|---|---|---|---|
| A | Demo als Abnahmeumgebung herstellen | Demo | **erledigt** |
| B | Verschlüsselungsphase 4.4.1/4.4.2 veröffentlichen | Bibliothek + Demo | **erledigt** |
| C | Verbleibende Befunde einordnen und Sicherheitsrelevantes beheben | Bibliothek + Demo | **4.4.3 veröffentlicht** |
| D | Vollständige Umbenennung auf Noelia | Noelia + Demo | **erledigt** |
| E | Modulvertrag und Runtime-Security-Checks | Noelia | 2–3 Sitzungen |
| F | Das Dashboard | Noelia | 2 Sitzungen |
| G | Noelia 5.0 gegen Microservice, Monolith und Einzelmodule | Demo | 1 Sitzung |

---

## A — Erledigt: Demo als Abnahmeumgebung

`~/Projects/Demo` steht auf Noelia 5.0.0-preview.1. Es enthält den
Gateway-/Microservice-Weg, einen echten Monolithen ohne Ocelot sowie isolierte
Encryption- und Security-Headers-Probes. Vorabversionen kommen aus GitHub
Packages oder beim Paket-Gate aus einem expliziten lokalen Kandidatenverzeichnis;
alle übrigen Abhängigkeiten kommen von NuGet.org. `eng/test-package-version.sh`
prüft lokale Releasekandidaten mit eigener Paketquelle und frischem Paketcache.

Diese Umgebung ersetzt WorkerTransfer für alle Noelia-Updates bis
5.0.0. Ein Release ist nicht fertig, solange Demo nicht grün ist.

---

## B — Erledigt: Verschlüsselungsphase

- 4.4.1 ersetzte die Klartextattrappe durch echtes AES-GCM und veröffentlichte
  `GHSA-276v-hjxx-vrmw`.
- 4.4.2 band sämtliche semantischen Envelope-Felder in den GCM-Tag ein und
  veröffentlichte `GHSA-jwc7-rw4h-gp9m`.
- Beide Fassungen liefen durch die Bibliotheks-CI, Trusted Publishing und das
  unabhängige Demo-Paket-Gate. 4.4.2 war die letzte öffentliche Demo-Basis vor
  der Noelia-Vorabversion.

Secret Scanning, Push Protection, Dependabot Vulnerability Alerts und
automatische Security Updates sind am 13.09.2026 aktiviert und über die
GitHub-API gegengeprüft. Validity Checks und Non-Provider Patterns bleiben
separate optionale GitHub-Funktionen und sind nicht Voraussetzung dieses Plans.

---

## C — Erledigt: verbleibende Befunde und 4.4.3

4.4.3 ist veröffentlicht, alle zwölf Pakete wurden anonym aus NuGet.org in
Demo wiederhergestellt und alle sechs Security-Advisories sind öffentlich. Der
folgende Kasten bleibt als nachvollziehbarer Arbeitsauftrag erhalten.

````
Du arbeitest im Repository Noelia (~/Projects/Noelia), einer oeffentlichen
.NET-Bibliothek unter MIT.

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c security/remaining-findings
Diese Sitzung arbeitet NIE direkt auf main.

Lies: docs/befunde/ · docs/MASTERPLAN-5.0.md ·
docs/MESSUNG-GEHEIMNISFRAGE.md · SECURITY.md · README.md (Versionspolitik)

AUSGANGSLAGE: Die beiden Verschluesselungsbefunde sind in 4.4.1 und 4.4.2
behoben und veroeffentlicht. Die uebrigen dokumentierten Befunde werden jetzt
einzeln eingeordnet; bereits geschlossene Tickets werden nicht erneut gebaut.

AUFGABE 1 — EINORDNEN, und zwar begruendet

Je Befund entscheidest du EINE von drei Einstufungen und schreibst sie oben
ins Ticket:

  SICHERHEIT — wird in der kleinsten passenden Patchfassung behoben. Kriterium: ein Nutzer, der die
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
gleichzeitiger Stoss faellt zu EINEM Eintrag zusammen. Noelias eigenes
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

AUFGABE 3 — DEMO-GATE UND VEROEFFENTLICHUNG, falls etwas SICHERHEIT war

Packe die exakte Patchfassung lokal und pruefe sie mit
`~/Projects/Demo/eng/test-package-version.sh`. Erst wenn Microservice,
Monolith, betroffene Einzelprobes und Noelia-CI gruen sind, wird ein Release
angelegt. Je Befund entsteht vor der Codeoffenlegung ein Advisory-Entwurf;
veroeffentlicht wird er erst, wenn die korrigierten Pakete auf NuGet.org
wirklich abrufbar sind. Wenn nichts SICHERHEIT war, wird nichts veroeffentlicht.

FALLEN: Warnungen sind Fehler; eine Gegenprobe muss KOMPILIEREN, sonst
liest sich der Build-Fehler wie ein bestandener Test; nach einer Gegenprobe
mit --no-incremental bauen, auch nach dem ZURUECKSETZEN; `dotnet pack` muss
warnungsfrei bleiben, auch NU5*.

NICHT: umbenennen (eigene Sitzung), unlisten oder WorkerTransfer anfassen.

ZUM SCHLUSS: build, test, pack, committen, PR nach main,
gh pr checks --watch, erst dann mergen.

BERICHTE: die Einstufung je Befund mit Grund, was behoben wurde, und ob die
Testluecke geschlossen ist — samt der Probe, die sie schliesst.
````

---

## D — Erledigt: vollständige Umbenennung auf Noelia

Quellbaum und Demo verwenden vollständig Noelia: Paketkennungen, Namespaces,
öffentliche APIs, Konfiguration, Speicherkennungen, Tabellen, Telemetrie und
kryptografische Domänen. Die Vorabversion ist `5.0.0-preview.1`; sie ist nur
für GitHub Packages beziehungsweise das isolierte lokale Demo-Gate vorgesehen.
Die nötigen manuellen Schalter für Repository, Trusted Publishing und die
spätere Deprecation der 4.x-Pakete werden beim Abschlussbericht genannt.

````
Du arbeitest im aktuellen Bibliotheks-Repository.

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c umbenennung/noelia
Diese Sitzung arbeitet NIE direkt auf main.

Lies: docs/MASTERPLAN-5.0.md (§6) · README.md · alle .csproj ·
.github/workflows/publish.yml

WARUM: Der bisherige Produktname kollidiert auf nuget.org mit einem fremden
Projekt aus dem Datenumfeld — wer sucht, findet das Falsche. `Noelia` ist frei,
geprueft am 14.09.2026 fuer noelia, .core, .abstractions, .http, .redis,
.infrastructure.

DAS IST EINE MECHANISCHE AENDERUNG MIT EINER GEFAHR: sie beruehrt jede
Datei, und dabei geht leicht etwas unter. Arbeite deshalb in dieser
Reihenfolge und miss nach jedem Schritt.

1. NAMENSRAEUME UND PAKETKENNUNGEN. Alle bisherigen Namen werden in
   Namensraeumen, using-Zeilen, Projektnamen, .csproj-Dateinamen,
   Verzeichnissen und der .slnx durch `Noelia` ersetzt.

2. VOLLSTAENDIG HEISST VOLLSTAENDIG. Die spaetere Entscheidung lautet:
   auch Prosa, Konfigurationspraefixe, Speicherkennungen, Tabellen,
   Telemetrie und kryptografische Domaenenkennungen werden umbenannt.
   4.x-Daten brauchen deshalb eine ausdrueckliche Migration.

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
   eine Warnung und nennen `Noelia.*` als Alternative. Fuer das bisherige
   Redis-Paket zusaetzlich der Grund "Critical Bugs".
   Das kann nur David in der nuget.org-Oberflaeche. Schreib ihm die genaue
   Liste: welches Paket, welcher Grund, welcher Nachfolger.

MESSEN, nicht glauben:
  - `dotnet build Noelia.slnx -c Release` → 0 Warnungen
  - `dotnet test Noelia.slnx` → alle gruen
  - `dotnet pack -c Release -o /tmp/pakete` → 12 Pakete `Noelia.*`,
    warnungsfrei, auch NU5*
  - Suche nach dem bisherigen Produktnamen im gesamten getrackten Baum → 0

FALLEN: Warnungen sind Fehler; `dotnet pack` warnungsfrei inkl. NU5*; die
Source-Link-Angaben zeigen nach der Umbenennung ins Leere, wenn
RepositoryUrl nicht mitwandert.

ZUM SCHLUSS: build, test, pack, committen, PR nach main, Pruefungen
abwarten, erst dann mergen.

BERICHTE: welche Schalter David umlegen muss (Repository-Name,
Trusted-Publishing-Richtlinie, Deprecation je Paket), und bestaetige, dass
der bisherige Produktname im getrackten Baum nicht mehr vorkommt.
````

---

## E — Der Modulvertrag

````
Du arbeitest im Repository Noelia (~/Projects/Noelia, nach der
Umbenennung).

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c 5.0/modulvertrag
Diese Sitzung arbeitet NIE direkt auf main.

Lies: docs/MASTERPLAN-5.0.md (§1 und §2 — sie tragen alles hier) ·
docs/MESSUNG-GEHEIMNISFRAGE.md · docs/befunde/

DAS PROBLEM, in einem Satz: etwas ist REGISTRIERT und deshalb ANWESEND,
aber nicht WIRKSAM — und nichts im System merkt den Unterschied. Elf
Funde haben genau diese Form; die Tabelle steht im Masterplan.

FUENF AENDERUNGEN, und sie machen zusammen 5.0.0 aus.

1. EIN MODUL ERKLAERT, WAS ES BRAUCHT UND WAS ES LIEFERT.

   Requires<IDataEncryptionService>()  — ohne das kann ich nicht
   Provides<ISecretStore>()            — das gebe ich anderen

   Beim Start wird die Zusammensetzung GEPRUEFT. Fehlt ein Requires, endet
   der Start mit einer Meldung, die DREI Dinge nennt:
     was fehlt · wer es braucht · WELCHES PAKET es liefert, samt Aufruf
     ("Noelia.Redis → AddRedisEncryption()")

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

   `Noelia.Http` beweist, dass es geht: null Fremdpakete. Die Regel:
     - allein lauffaehig ODER Start bricht ab und NENNT das fehlende
       Geschwisterpaket. Kein drittes.
     - die transitive Last je Paket steht in der README ALS ZAHL, und ein
       Test haelt sie fest (Noelia.Infrastructure zog 44).
     - kein Paket setzt ein anderes stillschweigend voraus.

4. ABSICHT WIRD GEGEN DIE WIRKLICHKEIT GEPRUEFT.

   `.Without(modul, grund)` bleibt — die Pflichtbegruendung ist richtig.
   Was fehlt, ist die Gegenprobe: der Startbericht sagt, was WIRKLICH
   laeuft, und ein Test haelt die Liste gegen die Absicht. Heute koennen
   Anwendungsdokumentation und `UseDefaults()` auseinanderlaufen, ohne dass
   es jemand merkt.

5. SECURITY-CHECKS SIND KEINE HEALTHCHECKS.

   Bau einen kleinen Vertrag mit stabiler Pruefkennung, Modul, Kategorie,
   Zustand (`Pass`, `Warning`, `Fail`, `NotApplicable`), Schwere und Abhilfe.
   Kein Ergebnis und kein Log darf Schluessel, Token, Verbindungszeichenfolge
   oder rohe Ausnahme enthalten. Checks laufen beim Start und auf
   ausdruecklichen Betreiberaufruf mit Timeout; niemals auf jedem Request.

   Es gibt keinen anonymen `/security`-Endpunkt. Ein spaeterer HTTP-Zugang
   braucht eine ausdrueckliche Operator-Policy, 404 bei fehlender Freigabe,
   `no-store` und Rate Limiting. Liveness haengt nie von Security-Checks ab.
   Das Dashboard zeigt nur Checks der Module, die in `NoeliaComposition`
   tatsaechlich enthalten sind.

   Beginne mit Composition, JWT, Security Headers, Refresh-Cookie, CORS,
   Secret Provider, Encryption sowie Rate-Limit-/Revocation-Degradation.
   Jeder Check bekommt eine Negativprobe, die gegen eine bewusst unsichere
   Testkonfiguration faellt, ohne den geheimen Wert auszugeben.

AUSSERDEM, und es gehoert hierher: `ISovereigntyReport` und
`IAuditTrailService` liegen in `Noelia.Infrastructure`. Damit kommt das
Dashboard (Prompt F) nicht an sie heran, ohne 44 Fremdpakete zu ziehen.
Ihre Schnittstellen wandern nach `Noelia.Abstractions`. Das ist ein Bruch
und der zweite Grund, warum 5.0 eine Hauptversion ist.

FALLEN: Warnungen sind Fehler; jede Gegenprobe muss KOMPILIEREN; nach einer
Gegenprobe mit --no-incremental bauen, auch nach dem Zuruecksetzen; `dotnet
pack` warnungsfrei inkl. NU5*.

ZUM SCHLUSS: build, test, pack und das lokale Demo-Paket-Gate ausfuehren,
committen, PR nach main, Pruefungen abwarten, erst dann mergen.

BERICHTE: wie viele Optionsklassen und Schnittstellen ohne Leser du
gefunden hast, was du mit ihnen gemacht hast, ob ein `.Without` nicht wirkte
und welche Security-Checks welche Module abdecken.
````

---

## F — Das Dashboard

````
Du arbeitest im Repository Noelia (~/Projects/Noelia).

ZUERST, ohne zu fragen:
  git switch main && git pull && git switch -c 5.0/dashboard
Diese Sitzung arbeitet NIE direkt auf main.

Lies ZUERST und vollstaendig: docs/PLAN-DASHBOARD-5.0.md. Er ist der
Auftrag; dieser Prompt ergaenzt ihn nur um das, was seither entschieden
wurde.

Dazu: docs/MASTERPLAN-5.0.md (§1 und §3) · SOVEREIGNTY.md

DIE DREI FRAGEN aus dem Plan sind entschieden:

1. Eigenes Paket `Noelia.Dashboard`, Modul `NoeliaModule.Dashboard` —
   abwaehlbar wie alles andere.
2. `ISovereigntyReport` und `IAuditTrailService` sind in Prompt E nach
   `Abstractions` gewandert. Das Dashboard liest sie TYPISIERT und zieht
   `Noelia.Infrastructure` NICHT.
3. Die Seite sagt, WELCHE INSTANZ sie zeigt. Zusammensetzung und
   Souveraenitaet sind je Instanz gleich; Pruefspur, Bremszaehler und
   Sitzungen sind es nicht.

ZWEI ZUSAGEN, die nicht verhandelbar sind:

  KEIN WERT, NIEMALS. Die Seite zeigt Gestalten: "gesetzt (44 Zeichen)",
  "fehlt", "Vorgabe". Nie einen Schluessel, nie eine Verbindungszeichen-
  folge, nie ein Geheimnis — auch nicht gekuerzt, auch nicht maskiert.
  Dieselbe Regel wie im Protokoll. Ein Dashboard, das `JWT_PRIVATE_KEY`
  anzeigt, waere der naechste Sicherheitsbefund.

  5.0 LIEST NUR. Keine Schreibbefehle, auch wenn sie gewuenscht wurden:
  eine Seite, die Konfiguration aendert, ist ein Angriffsziel ersten
  Ranges, und die Umgebung soll der Mechanismus bleiben. 5.1 entscheidet
  ueber das Schreiben, wenn das Lesen steht.

WAS SIE ZEIGT — und warum sie das Gegenteil der elf Funde ist:
  - welche Module laufen (aus NoeliaComposition)
  - welche ABGEWAEHLT sind, mit ihrer Begruendung aus `.Without`
  - je Modul: was es BRAUCHT und ob der Anbieter da ist (aus Prompt E)
  - je Modul: was es LIEFERT und ob jemand es liest
  - welche Konfiguration es sieht, als Gestalt
  - nur die Security-Checks der installierten Module, samt Zustand und Abhilfe
  - die Egress-Grenze: welche Hosts erlaubt sind

Ein Modul, das laeuft und nichts bewirkt, MUSS hier als solches zu sehen
sein. Das ist der ganze Zweck. Bau eine Probe, die genau das nachstellt.

MESSLATTE: null Fremdpakete, wie `Noelia.Http`. Server-gerendertes HTML,
eingebettete Ressourcen, etwas Vanilla-JS. Kein Blazor, kein npm, kein CDN.
Ergaenze in `~/Projects/Demo` ein isoliertes Dashboard-Projekt, das nur
`Noelia.Dashboard` installiert. Seine Seite darf genau das Dashboard-Modul
und dessen Checks zeigen — kein Modul, das nicht installiert ist.

FALLEN: Warnungen sind Fehler; jede Gegenprobe muss KOMPILIEREN; `dotnet
pack` warnungsfrei inkl. NU5*; die Seite darf keinen Wert ausgeben — bau
einen Test, der genau danach sucht.

ZUM SCHLUSS: build, test, pack, committen, PR nach main, Pruefungen
abwarten, erst dann mergen.

BERICHTE: wie die Zugriffsentscheidung ausgefallen ist, und was die Seite
bei einem Modul zeigt, das laeuft und nichts tut.
````

---

## G — Noelia 5.0 in Demo abnehmen

````
Du arbeitest im Repository Demo (~/Projects/Demo), NICHT in WorkerTransfer.

ZUERST, ohne zu fragen:
  pruefe, ob das Verzeichnis ein Git-Repository ist. Falls nicht, veraendere
  keine fremde Repository-Historie und dokumentiere den lokalen Stand genau.

Lies: README.md · SECURITY-CHECKS.md · eng/test-package-version.sh ·
~/Projects/Noelia/docs/MASTERPLAN-5.0.md

AUFGABE: den lokal gepackten Noelia-5.0-Releasekandidaten wie ein fremder
Verbraucher abnehmen. Keine Projektverweise nach `~/Projects/Noelia`; nur
Pakete aus dem angegebenen Feed und danach wieder nur NuGet.org.

1. Migriere Microservices und Monolith auf `Noelia.*`. Die Microservices
   behalten Gateway plus getrennte User-/Todo-Prozesse; der Monolith darf
   weiterhin weder Ocelot laden noch einen Gateway-Prozess brauchen.
2. Fuer jedes Noelia-Paket entsteht ein isoliertes Probe-Projekt. Es
   installiert nur dieses Paket, aktiviert nur dessen Modul und prueft die
   extern zugesagte Wirkung. Das gilt besonders fuer Encryption,
   SecurityHeaders, SecurityChecks und Dashboard.
3. Der Modulvertrag muss fehlende Anbieter beim Start mit Modul, fehlendem
   Vertrag und exaktem Installations-/Registrierungsweg melden.
4. Security-Checks duerfen keine Geheimwerte ausgeben. Eine Negativprobe legt
   markierte Canary-Geheimnisse in die Konfiguration und sucht im gesamten
   Ergebnis, HTML und Log nach ihnen.
5. Das Dashboard-Projekt zeigt nur das einzeln installierte Dashboard-Modul
   und dessen Checks. Microservice und Monolith zeigen jeweils ihre wirkliche,
   unterschiedliche Zusammensetzung.

MESSEN: Restore gegen einen leeren Paketcache, exakte Versionen ohne Mischung,
Build mit null Warnungen, alle .NET-/Frontend-Tests, Monolith ohne Ocelot,
Microservice-E2E mit Eigentuertrennung, Dependency-Audit und beide
Docker-Healthchecks. Das bestehende Paket-Gate muss den stabilen
NuGet.org-Zustand auch nach einem Fehler wiederherstellen.

FALLEN: keine privaten GitHub-Package-Quellen oder PATs; keine Geheimnisse aus
`.env` ausgeben; Warnungen sind Fehler; ein gruenes Roundtrip allein beweist
keine Sicherheitswirkung; Health und Security nicht zu einem anonymen
Endpunkt vermischen.

ZUM SCHLUSS: Ergebnis je Architektur und je Probe berichten. Erst wenn alles
gruen ist, darf Noelia 5.0 stabil auf NuGet.org erscheinen. WorkerTransfer
bleibt bis zu einem spaeteren ausdruecklichen Auftrag unangetastet.

BERICHTE: welche `Requires` beim ersten Start gefehlt haben, welche Checks
bewusst `NotApplicable` waren und ob irgendein Canary-Wert sichtbar wurde.
````
