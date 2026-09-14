# Masterplan 5.0 — Noelia

**Stand:** 14.09.2026 · **Gilt bis:** 5.0.0 veröffentlicht ist
**Darunter:** [PLAN-DASHBOARD-5.0.md](PLAN-DASHBOARD-5.0.md) · [befunde/](befunde/)
· Demo-Abnahme in `~/Projects/Demo`

Dieses Dokument steht über dem Dashboard-Plan. Es sagt, **warum** 5.0 eine
Hauptversion wird, und beantwortet die Fragen, die beim Lesen der Befunde
aufkamen. Spätere Entscheidungen haben Vorrang vor der ersten Fassung: Bis
Noelia 5.0 steht, ist `~/Projects/Demo` die Verbraucher- und Abnahmeumgebung;
WorkerTransfer bleibt außer Betrieb dieser Arbeiten.

---

## 1. Die Diagnose — und sie ist nicht „zu viele Abhängigkeiten"

Der Verdacht lautete: zu viele Abhängigkeiten, Pakete nicht einzeln
benutzbar, fehlende Konfiguration. **Der Verdacht ist berechtigt, die Ursache
liegt woanders.** Sieh dir an, was elf Funde gemeinsam haben:

| Fund | Was es behauptete | Was es tat |
|---|---|---|
| `DataEncryptionService` | `"Algorithm":"AES256GCM"` | legte Klartext ab |
| Encryption-Envelope 2.0 | meldete vollständige Integrität | ließ Steuerdaten außerhalb des GCM-Tags |
| `AddSecretManagement` | Geheimnisverwaltung samt Rotation | bindet Optionen, die niemand liest |
| `AddSecretStoreMasterKey` | Noelias eigene Empfehlung | verlangt einen `ISecretProvider`, den kein Modul registriert |
| `ISecretManager` | eine Schnittstelle | hat in ganz Noelia **keinen** Verbraucher |
| `DataEncryptionOptions` | sieben Stellschrauben | fünf ohne Leser, `ForProduction()` ändert nichts |
| `JwtConfigurationValidator` | prüft die JWT-Einstellungen | prüft Abschnitt `Jwt`, gelesen wird `JwtSettings` |
| Verteilte Bremse (Redis) | 50 Aufrufer, 10 Plätze | gleichzeitige fallen zu **einem** Eintrag zusammen |
| Prüfspur | eine Kette | unterschreibt je Prozess, meldet nach Neustart falschen Alarm |
| `HashAsync` | Ableitung | 30 000 Runden, wo der Passworthasher 600 000 fordert |
| `MaxDataSize` | eine Grenze | ist keine |

**Das Muster:** etwas ist *registriert* und deshalb *anwesend*, aber es ist
nicht *wirksam* — und **nichts im System merkt den Unterschied**.

Das ist keine Abhängigkeitsfrage. Es ist eine fehlende Selbstprüfung. Ein
Container, der sagt „ich habe `IDataEncryptionService`", sagt nichts darüber,
ob der verschlüsselt. Ein Modul, das startet, sagt nichts darüber, ob es
etwas tut.

**Und genau deshalb ist das Dashboard keine Spielerei, sondern ein Teil der
Antwort.** Eine Seite macht sichtbar, *was wirklich eingerichtet ist*;
ausführbare Security-Checks beweisen zusätzlich, ob die zugesagte Eigenschaft
wirkt. Beides zusammen kehrt diese Fehlerklasse mechanisch um.

---

## 2. Die fünf Entscheidungen, die 5.0 tragen

### 2.1 Ein Modul erklärt, was es BRAUCHT und was es LIEFERT

Heute registriert ein Modul Dienste und hofft. Ab 5.0 deklariert es:

```csharp
Requires<IDataEncryptionService>()   // ohne das kann ich nicht
Provides<ISecretStore>()             // das gebe ich anderen
```

Beim Start wird die Zusammensetzung **geprüft**, nicht gehofft. Fehlt ein
`Requires`, endet der Start mit einer Meldung, die drei Dinge nennt:

1. **was** fehlt (`IDataEncryptionService`)
2. **wer** es braucht (`EncryptionModule`)
3. **welches Paket** es liefert (`Noelia.Redis` → `AddRedisEncryption()`)

Punkt 3 ist der, den du wolltest: *„er sollte zumindest eine Alternative
installieren und referenzieren"*. Die Bibliothek installiert nichts von
selbst — das darf sie nicht —, aber sie **nennt den genauen Befehl**.

**Gegenprobe, die das festhält:** ein Modul mit einem `Requires`, dessen
Anbieter fehlt, muss den Start abbrechen. Wird die Prüfung entfernt, fällt
die Probe.

### 2.2 Ein registrierter Dienst, den niemand liest, ist ein Defekt

`AddSecretManagement` bindet `SecretRotationOptions`. Kein Leser. Das ist
kein Schönheitsfehler, es ist eine **Falschaussage**: der Name verspricht
Rotation, es passiert nichts, und niemand erfährt es.

Ab 5.0 gilt: **jede registrierte Optionsklasse hat mindestens einen
Verbraucher**, und ein Test misst das über die Assembly. Was keinen Leser
hat, fliegt raus oder bekommt einen.

Dasselbe für Schnittstellen: `ISecretManager` ohne Verbraucher in der ganzen
Bibliothek ist tote Fläche. Entweder sie trägt etwas, oder sie geht.

### 2.3 Jedes Paket muss allein benutzbar sein — und sagen, wann nicht

Dein Gefühl stimmt: man kann heute nicht sicher ein einzelnes Paket nehmen.
`Noelia.Http` beweist aber, dass es geht — **null Fremdpakete**. Die
Abnahmeumgebung beweist jede weitere Paketgrenze in einem eigenen Projekt:
heute bereits `Noelia.Encryption.Probe` und `Noelia.SecurityHeaders.Probe`.

Die Regel ab 5.0:

- Jedes Paket ist **für sich lauffähig** oder **bricht beim Start mit einer
  Meldung ab**, die das fehlende Geschwisterpaket nennt. Kein drittes.
- Die transitive Last je Paket steht in der README **als Zahl**, und ein Test
  hält sie fest. `Noelia.Infrastructure` zog 44 — wer das nicht weiß,
  entscheidet nicht.
- **Kein Paket darf ein anderes stillschweigend voraussetzen.**

### 2.4 `.Without(...)` bleibt — aber die Absicht wird gegen die Wirklichkeit geprüft

Der Einwand *„das ist doch voll dumm"* trifft etwas Echtes, nur nicht das
`Without` selbst. Das Problem ist, dass **Absicht und Wirklichkeit
auseinanderlaufen können**: `UseDefaults()` kann ein Modul mitbringen, das eine
Anwendungsdokumentation gleichzeitig als „bewusst nicht" führt.

`Without` mit Pflichtbegründung ist gut — es zwingt jemanden, den Grund
aufzuschreiben. Was fehlt, ist die **Gegenprobe**: der Startbericht muss
sagen, was wirklich läuft, und ein Test muss die Liste gegen die Absicht
halten. Dann ist ein `Without`, das nicht wirkt, ein roter Lauf statt einer
Überraschung nach drei Monaten.

Das Dashboard macht dasselbe für einen Menschen sichtbar.

### 2.5 Security-Checks sind keine Healthchecks

Ein Healthcheck beantwortet, ob ein Prozess Verkehr bedienen kann. Ein
Security-Check beantwortet, ob die laufende Zusammensetzung ihre erklärte
Sicherheitsgrenze einhält. Eine erreichbare Datenbank kann gesund sein, während
ihre TLS-Prüfung abgeschaltet ist; ein JWT-Dienst kann leben und trotzdem einen
Entwicklungsschlüssel benutzen.

5.0 bekommt deshalb einen kleinen Vertrag mit stabiler Prüfkennung, Modul,
Kategorie, Zustand (`Pass`, `Warning`, `Fail`, `NotApplicable`), Schwere und
Abhilfe. Ergebnisse enthalten niemals Schlüssel, Token, Verbindungszeichenfolgen
oder rohe Ausnahmen. Prüfungen laufen beim Start und auf ausdrücklichen
Betreiberaufruf mit Zeitgrenze — nicht auf jedem Request und nicht anonym unter
einem `/security`-Endpunkt. Das Dashboard zeigt nur Prüfungen für Module, die in
der tatsächlichen `NoeliaComposition` enthalten sind.

---

## 3. Der Konflikt im Dashboard-Plan — und meine Empfehlung

Du willst: *„kann dann von dort aus auch weitere Anpassungen machen und
seine Konfig anpassen, die dann direkt anpassen."*

`PLAN-DASHBOARD-5.0.md` sagt: **„Keine Schreibbefehle in 5.0."**

**Halte dich an den Plan.** Gründe, in der Reihenfolge ihres Gewichts:

1. Eine Seite, die **Konfiguration schreibt**, ist ein Angriffsziel ersten
   Ranges. Wer sie erreicht, ändert Schlüssel, Grenzen und Freigaben —
   an jeder Prüfspur vorbei.
2. Die Seite zeigt bereits Konfiguration. **Lesen ist schon gefährlich
   genug** und braucht die Zugriffsentscheidung aus dem Plan.
3. Eine Konfigurationsänderung zur Laufzeit schafft neben Deployment und
   Umgebung eine zweite Wahrheit. Wer über eine Webseite schreibt, kann die
   deklarierte Infrastruktur umgehen.

**5.0 liest. 5.1 entscheidet über das Schreiben**, wenn das Lesen steht und
die Zugriffsfrage beantwortet ist. Das ist kein Nein, es ist eine
Reihenfolge.

**Und eine harte Zusage für 5.0:** die Seite zeigt **niemals einen Wert**,
nur Gestalten — `gesetzt (44 Zeichen)`, `fehlt`, `Vorgabe`. Genau wie die
Protokolle. Ein Dashboard, das `JWT_PRIVATE_KEY` anzeigt, wäre der nächste
Sicherheitsbefund.

---

## 4. Zwei Architekturen, eine Bibliothekszusage

Noelia entscheidet nicht, ob eine Anwendung Monolith oder Microservice-System
ist und ob sie ein Gateway braucht. Sie muss alle drei Formen tragen. Das ist
ab jetzt keine Annahme mehr, sondern eine Abnahmebedingung in `~/Projects/Demo`:

- Gateway plus getrennte User- und Todo-Services prüfen Token-Weitergabe,
  Eigentümertrennung und den gemeinsamen Eingang.
- Ein einzelner Monolith betreibt dieselben Features in einem Prozess, ohne
  Ocelot- oder Gateway-Abhängigkeit.
- Isolierte Projekte installieren jeweils nur ein Noelia-Paket und
  aktivieren nur das geprüfte Modul.

Vor jeder Veröffentlichung müssen Paket-Gate, beide Architekturen und die
betroffenen Modul-Probes grün sein. WorkerTransfer wird erst wieder angefasst,
wenn Noelia 5.0 diese Abnahme bestanden hat und ein eigener Auftrag folgt.

---

## 5. Die Reihenfolge

| # | Was | Wo | Größe |
|---|---|---|---|
| **A** | Demo auf öffentliche Pakete, Microservice + Monolith + Probes | Demo | **erledigt** |
| **B** | 4.4.1/4.4.2-Advisories und GitHub-Sicherheitsschutz abschließen | Bibliothek + Demo | **erledigt** |
| **C** | Verbleibende Befunde einordnen: Sicherheit jetzt, Gestaltung nach 5.0 | Bibliothek + Demo | **4.4.3 veröffentlicht** |
| **D** | Vollständige Umbenennung auf **Noelia**, als 5.0.0-Vorbereitung | Noelia + Demo | **erledigt** |
| **E** | Die fünf Entscheidungen aus §2 samt Security-Check-Vertrag umsetzen | Noelia | 2–3 Sitzungen |
| **F** | Dashboard nach `PLAN-DASHBOARD-5.0.md` | Noelia | 2 Sitzungen |
| **G** | Noelia 5.0 in Demo: beide Architekturen und Einzelmodule | Demo | 1 Sitzung |

**A bis D sind abgeschlossen. C kam vor D**, damit kein Befund bei der
Umbenennung doppelt mitwandert. **E kommt vor F**, weil das Dashboard
genau die Verträge und Security-Checks anzeigt, die E erst erzeugt. G ist das
Release-Gate; WorkerTransfer folgt ausdrücklich noch nicht.

---

## 6. Die Umbenennung — was daran nicht kosmetisch ist

Der bisherige Produktname kollidiert auf nuget.org mit einem anderen Projekt
aus dem Datenumfeld; wer sucht, findet das Falsche. `Noelia` ist frei — erneut
geprüft am 14.09.2026 für
`noelia`, `.core`, `.abstractions`, `.http`, `.redis`, `.infrastructure`.

**Die alten Pakete werden NICHT ungelistet.** Sie werden **deprecated** mit
Verweis auf den Nachfolger: sie bleiben installierbar, zeigen im Editor eine
Warnung und nennen `Noelia.*` als Alternative. Für das bisherige Redis-Paket
zusätzlich der Grund *Critical Bugs* — dort lag der Verschlüsselungsdefekt.

Das ist der ehrliche Weg: niemandem etwas wegnehmen, aber niemanden
hineinlaufen lassen.

**5.0.0 ist ohnehin eine Hauptversion** — wegen §2.1 (Modulvertrag) und der
Frage aus dem Dashboard-Plan (`ISovereigntyReport` nach `Abstractions`). Die
Umbenennung reist mit, statt einen eigenen Bruch zu erzeugen.
