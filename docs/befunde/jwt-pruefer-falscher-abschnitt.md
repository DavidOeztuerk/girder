# `JwtConfigurationValidator` prüft den Abschnitt `Jwt`; Girder liest `JwtSettings`

- **Girder-Fassung:** 4.4.0 bis 4.4.2
- **Gefunden beim:** Absuchen der Bibliothek, Phase 2, 13.09.2026
- **Art:** Lücke
- **Blockiert:** nein

## Was passiert

Der Prüfer, der das JWT-Geheimnis auf Vorhandensein, Mindestlänge und
Platzhalter abklopft, sieht in einem Abschnitt nach, den Girder sonst nirgends
benutzt:

```csharp
// src/Girder.Infrastructure/Configuration/ConfigurationValidators.cs:19
public string SectionName => "Jwt";
```

Gelesen wird das Geheimnis überall sonst aus `JwtSettings`:

```csharp
// KeyRingFactory.cs:38
var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? configuration["JwtSettings:Secret"] ?? throw …

// ServiceCollectionExtensions.cs:401
opts.Secret = configuration["JwtSettings:Secret"] ?? string.Empty;
```

Ein Dienst, der so eingerichtet ist, wie README und XML-Doku es zeigen, bekommt
vom Prüfer also nicht „in Ordnung", sondern *„JWT configuration section is
missing"* — und die drei Prüfungen, die es wirklich gäbe (nicht leer, ≥ 32
Zeichen, kein Platzhalter), laufen nie über den Wert, der tatsächlich
unterschreibt.

Milderung, und der Grund für „niedrig": `AddConfigurationValidation()` wird von
**keinem** Girder-Modul aufgerufen. Der Prüfer schläft, bis eine Anwendung ihn
selbst anmeldet.

## Warum es Girders ist

```csharp
// nur Girder. Ein korrekt eingerichteter Dienst.
var b = WebApplication.CreateBuilder();
b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["JwtSettings:Secret"]   = new string('x', 64),   // gut, lang, kein Platzhalter
    ["JwtSettings:Issuer"]   = "probe",
    ["JwtSettings:Audience"] = "probe"
});
b.Services.AddConfigurationValidation();

var app = b.Build();
var pruefer = app.Services.GetRequiredService<IConfigurationValidationService>();
foreach (var f in pruefer.ValidateAll().Errors)
    Console.WriteLine($"{f.Key}: {f.Message}");
// -> Jwt: JWT configuration section is missing
// erwartet: nichts zu beanstanden
```

Und die Gegenprobe, die zeigt, dass der Prüfer an sich arbeitet: derselbe Lauf
mit `["Jwt:Secret"] = "kurz"` meldet die Mindestlänge. Er prüft — nur den
falschen Abschnitt.

## Was es kostet

Heute nichts, weil ihn niemand einschaltet. Wer ihn einschaltet, bekommt eine
Falschmeldung über einen Dienst, der richtig eingerichtet ist — und, schlimmer,
keine Warnung über einen, der es nicht ist.

Behebung: `SectionName => "JwtSettings"`. Ein Wort. Und dieselbe Frage an die
drei Nachbarn im selben Datei — `ConnectionStrings`, `Smtp`, `RateLimiting` —,
von denen keiner gegengeprüft wurde.

## Stand

- [x] Prüfer und Laufzeit lesen beide `JwtSettings`
- [x] Fehlerpfade nennen die tatsächlich verwendeten `JwtSettings:*`-Schlüssel
- [x] zusätzlicher Fund behoben: Prüfer liest nun `ExpireMinutes` statt des ebenfalls wirkungslosen `ExpirationInMinutes`
- [x] gültige Laufzeitkonfiguration ergibt keine falsche Beanstandung
- [x] lokaler 4.4.3-Paketkandidat im Demo-Projekt bestätigt
- [x] 4.4.3 veröffentlicht und danach erneut anonym von NuGet.org geprüft
