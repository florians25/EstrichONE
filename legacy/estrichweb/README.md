# Estrich Web – Altbestand (eingefroren)

Dieser Ordner enthält den Quellcode von **Estrich Web**, dem Vorgänger von EstrichONE,
Stand **23.08.2026** (Runde 66). Er dient als Referenz für die Bestandsaufnahme
(`docs/reengineering/01-bestandsaufnahme.md`) und als Quelle für die Datenmigration.

**Nicht weiterentwickeln.** Fehlerbehebungen für den laufenden Betrieb erfolgen nur noch,
wenn sie für die Migration oder den Parallelbetrieb zwingend sind.

## Bewusst nicht übernommen

| Ausgelassen | Grund |
|---|---|
| `api/config/local.php` | enthielt echte Produktionszugangsdaten (DB-Passwort, JWT-Secret) |
| DB-Host, DB-Name, DB-Benutzer in `docs/GO-LIVE-FINAL-2026-06-28.md` | geschwärzt |
| `public/`, `public-angular-build/` | Build-Artefakte (minifizierte Chunks), aus der Source reproduzierbar |
| `storage/` | nur leere Platzhalter; echte Dateien liegen auf dem Webspace |

Eine Vorlage für die Konfiguration liegt in `api/config/config.sample.php`.
