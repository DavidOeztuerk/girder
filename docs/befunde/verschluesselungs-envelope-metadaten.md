# Verschlüsselungs-Envelope: Steuerdaten lagen außerhalb des GCM-Tags

- **Betroffen:** `Girder.Redis` 4.4.1
- **Behoben:** 4.4.2, Envelope-Version 2.1
- **Klasse:** Integritätsverletzung bei schreibbarem Speicher

## Befund

4.4.1 verschlüsselte den Payload mit AES-GCM und authentifizierte optionale
Caller-AAD. Die JSON-Felder des Envelopes wurden danach jedoch ungebunden um
Ciphertext und Tag gelegt.

`Metadata["compressed"]` steuert nach erfolgreicher GCM-Prüfung die
Dekomprimierung. Eine Änderung von `true` auf `false` ließ deshalb
`Success = true` und `IntegrityVerified = true` bestehen, lieferte aber andere
Daten. Auch Timestamp, Key-ID und Key-Version waren nicht authentifiziert.

## Behebung

Envelope 2.1 bildet alle semantischen Felder kanonisch und längenpräfixiert ab
und übergibt diese Darstellung als AES-GCM Associated Data. Metadaten werden
ordinal nach Schlüssel sortiert. Ciphertext bleibt direkt durch GCM geschützt;
Tag und Ciphertext selbst werden nicht rekursiv in die Associated Data gelegt.

Envelope 2.0 wird abgelehnt. Sein Payload kann kryptografisch echt sein, seine
Steuerdaten aber nicht. Ein pauschales `IntegrityVerified = true` wäre daher
weiterhin falsch. Migration: kontrolliert mit 4.4.1 lesen und mit 4.4.2 neu
schreiben.

## Gegenbeweise

`DataEncryptionServiceCipherTests` verändert jeweils nur einen Teil:

- Komprimierungsflag,
- Key-Version-Metadatum,
- hinzugefügtes und entferntes Metadatum,
- Algorithmusname,
- Timestamp,
- Key-ID,
- Caller-AAD.

Jede Änderung muss `Success = false` und `IntegrityVerified = false` ergeben.
Ein weiterer Test verlangt für Envelope 2.0 eine konkrete Migrationsmeldung.
