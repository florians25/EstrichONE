# 0011 – Undurchsichtige Sitzungstokens statt JWT

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Sperren von Benutzern und Geräten sollen sofort wirken (docs/reengineering/11, 13). Dafür muss jede Anfrage
den Sitzungs-, Benutzer- und Gerätestatus in der Datenbank prüfen. Ein JWT würde diese Abfrage nicht ersparen,
bräuchte aber Signaturschlüssel samt sicherer Ablage, Rotation und Wiederherstellung aus dem Backup.

## Entscheidung
- Zugriffstoken (`eo1_…`, 10 Minuten) und Refresh-Token (`eor1_…`, Laufzeit nach Geräterichtlinie) sind
  256-Bit-Zufallswerte. Die Datenbank speichert nur ihren SHA-256-Hash.
- Jede Anfrage: Hash des Tokens → eine indizierte Abfrage auf `sessions` (inkl. Benutzerstatus).
- Refresh-Tokens rotieren bei jeder Verwendung. Wird ein bereits verbrauchter Refresh-Token nach Ablauf
  einer kurzen Gnadenfrist (parallele Anfragen) erneut vorgelegt, wird die gesamte Sitzung widerrufen.
- Tokens erscheinen nie in URLs und nie in Logs.

## Folgen
Keine Schlüsselverwaltung, sofortiger Widerruf, einfache Fehlersuche. Jede API-Anfrage kostet eine
Datenbankabfrage. Das ist bei der Größe eines Betriebs unkritisch und kann bei Bedarf kurz zwischengespeichert werden.
