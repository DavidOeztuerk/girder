# Masterplan 5.0 — Neolia

**Stand:** 13.09.2026 · **Gilt bis:** 5.0.0 veröffentlicht ist
**Darunter:** [PLAN-DASHBOARD-5.0.md](PLAN-DASHBOARD-5.0.md) · [befunde/](befunde/)

Dieses Dokument steht über dem Dashboard-Plan. Es sagt, **warum** 5.0 eine
Hauptversion wird, und beantwortet die Fragen, die beim Lesen der neun Befunde
aufkamen.

---

## 1. Die Diagnose — und sie ist nicht „zu viele Abhängigkeiten"

Der Verdacht lautete: zu viele Abhängigkeiten, Pakete nicht einzeln
benutzbar, fehlende Konfiguration. **Der Verdacht ist berechtigt, die Ursache
liegt woanders.** Sieh dir an, was zehn Funde in einer Woche gemeinsam haben:

| Fund | Was es behauptete | Was es tat |
|---|---|---|
| `DataEncryptionService` | `"Algorithm":"AES256GCM"` | legte Klartext ab |
| `AddSecretManagement` | Geheimnisverwaltung samt Rotation | bindet Optionen, die niemand liest |
| `AddSecretStoreMasterKey` | Girders eigene Empfehlung | verlangt einen `ISecretProvider`, den kein Modul registriert |
| `ISecretManager` | eine Schnittstelle | hat in ganz Girder **keinen** Verbraucher |
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

**Und genau deshalb ist das Dashboard keine Spielerei, sondern die Antwort.**
Eine Seite, die zeigt, *was wirklich wirkt*, ist die mechanische Umkehrung
dieser ganzen Fehlerklasse. Sie hätte acht der zehn Funde sichtbar gemacht,
bevor jemand danach gesucht hat.

---

## 2. Die vier Entscheidungen, die 5.0 tragen

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
3. **welches Paket** es liefert (`Neolia.Redis` → `AddRedisEncryption()`)

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
`Neolia.Http` beweist aber, dass es geht — **null Fremdpakete**, und das
Gateway von WorkerTransfer fährt genau darauf.

Die Regel ab 5.0:

- Jedes Paket ist **für sich lauffähig** oder **bricht beim Start mit einer
  Meldung ab**, die das fehlende Geschwisterpaket nennt. Kein drittes.
- Die transitive Last je Paket steht in der README **als Zahl**, und ein Test
  hält sie fest. `Neolia.Infrastructure` zog 44 — wer das nicht weiß,
  entscheidet nicht.
- **Kein Paket darf ein anderes stillschweigend voraussetzen.**

### 2.4 `.Without(...)` bleibt — aber die Absicht wird gegen die Wirklichkeit geprüft

Der Einwand *„das ist doch voll dumm"* trifft etwas Echtes, nur nicht das
`Without` selbst. Das Problem ist, dass **Absicht und Wirklichkeit
auseinanderlaufen können**: CLAUDE.md führt fünf Module als „bewusst nicht",
die in Wahrheit laufen, weil `UseDefaults()` sie mitbringt.

`Without` mit Pflichtbegründung ist gut — es zwingt jemanden, den Grund
aufzuschreiben. Was fehlt, ist die **Gegenprobe**: der Startbericht muss
sagen, was wirklich läuft, und ein Test muss die Liste gegen die Absicht
halten. Dann ist ein `Without`, das nicht wirkt, ein roter Lauf statt einer
Überraschung nach drei Monaten.

Das Dashboard macht dasselbe für einen Menschen sichtbar.

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
3. Eine Konfigurationsänderung zur Laufzeit widerspricht dem, was
   WorkerTransfer trägt: *die Umgebung ist der Mechanismus.* Wer über eine
   Webseite schreibt, hat zwei Wahrheiten.

**5.0 liest. 5.1 entscheidet über das Schreiben**, wenn das Lesen steht und
die Zugriffsfrage beantwortet ist. Das ist kein Nein, es ist eine
Reihenfolge.

**Und eine harte Zusage für 5.0:** die Seite zeigt **niemals einen Wert**,
nur Gestalten — `gesetzt (44 Zeichen)`, `fehlt`, `Vorgabe`. Genau wie die
Protokolle. Ein Dashboard, das `WORKERTRANSFER_SECRETS_KEY` anzeigt, wäre
der elfte Befund dieser Woche.

---

## 4. Was NICHT in 5.0 gehört

**Monolith oder Microservices, und braucht es ein Gateway?**

Das ist eine Frage über **WorkerTransfer**, nicht über Neolia. Eine
Bibliothek darf diese Entscheidung nicht treffen — sie muss in beiden Welten
tragen. Genau das ist heute der Fall: dieselbe Bibliothek bedient dreizehn
Dienste und könnte einen Monolithen bedienen.

Sie gehört in eine eigene Sitzung mit WorkerTransfer als Gegenstand, und sie
braucht Messungen (Aufrufwege, Latenz, Betriebsaufwand), keine Meinung. **Sie
in 5.0 zu mischen wäre der sicherste Weg, beides zu verderben.**

Kurzfassung, damit sie nicht verloren geht: das Gateway trägt heute drei
Dinge, die es sonst nirgends gäbe — die Bremse je Herkunft (ein Dienst
dahinter sieht nur das Gateway), die `Sec-Fetch-Dest`-Regel, und **einen**
Eingang statt dreizehn. Wer es abschafft, muss für alle drei eine Antwort
haben.

---

## 5. Die Reihenfolge

| # | Was | Wo | Größe |
|---|---|---|---|
| **A** | WorkerTransfer auf 4.4.1 heben, Ticket schließen | workertransfer | 20 min |
| **B** | Advisory freigeben, Secret Scanning + Dependabot an | GitHub-UI | 10 min, **nur David** |
| **C** | Die neun Befunde einordnen: Sicherheit jetzt, Gestaltung nach 5.0 | Girder | 1 Sitzung |
| **D** | Umbenennung Girder → **Neolia**, als 5.0.0-Vorbereitung | Girder | 1 Sitzung |
| **E** | Die vier Entscheidungen aus §2 umsetzen | Neolia | 2–3 Sitzungen |
| **F** | Dashboard nach `PLAN-DASHBOARD-5.0.md` | Neolia | 2 Sitzungen |
| **G** | WorkerTransfer auf Neolia 5.0.0 nachziehen | workertransfer | 1 Sitzung |

**A und B zuerst**, sie sind klein und schließen den Sicherheitsvorgang ab.
**C vor D**, weil ein Befund, der bei der Umbenennung mitwandert, doppelt
kostet. **E vor F**, weil das Dashboard genau das anzeigt, was E erst
erzeugt.

---

## 6. Die Umbenennung — was daran nicht kosmetisch ist

`Girder` ist auf nuget.org von einem anderen Projekt besetzt (Datenumfeld);
wer sucht, findet das falsche. `Neolia` ist frei — geprüft am 13.09.2026 für
`neolia`, `.core`, `.abstractions`, `.http`, `.redis`, `.infrastructure`.

**Die alten Pakete werden NICHT ungelistet.** Sie werden **deprecated** mit
Verweis auf den Nachfolger: sie bleiben installierbar, zeigen im Editor eine
Warnung und nennen `Neolia.*` als Alternative. Für `Girder.Redis` zusätzlich
der Grund *Critical Bugs* — dort lag der Verschlüsselungsdefekt.

Das ist der ehrliche Weg: niemandem etwas wegnehmen, aber niemanden
hineinlaufen lassen.

**5.0.0 ist ohnehin eine Hauptversion** — wegen §2.1 (Modulvertrag) und der
Frage aus dem Dashboard-Plan (`ISovereigntyReport` nach `Abstractions`). Die
Umbenennung reist mit, statt einen eigenen Bruch zu erzeugen.
