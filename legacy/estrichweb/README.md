# Estrich Web – Altbestand (eingefroren)

Dieser Ordner enthält den Quellcode von **Estrich Web**, dem Vorgänger von EstrichONE,
Stand **23.08.2026** (Runde 66). Er dient als fachliche Referenz (siehe
`docs/reengineering/01-bestandsaufnahme.md`). **Es werden weder Code noch Daten übernommen** – EstrichONE ist ein
Neuprodukt (siehe `docs/reengineering/18-migrationsstrategie.md`).

**Nicht weiterentwickeln.** Dieser Ordner ist eingefroren.

## Bewusst nicht übernommen

| Ausgelassen | Grund |
|---|---|
| `api/config/local.php` | enthielt echte Produktionszugangsdaten (DB-Passwort, JWT-Secret) |
| DB-Host, DB-Name, DB-Benutzer in `docs/GO-LIVE-FINAL-2026-06-28.md` | geschwärzt |
| `public/`, `public-angular-build/` | Build-Artefakte (minifizierte Chunks), aus der Source reproduzierbar |
| `storage/` | nur leere Platzhalter; echte Dateien liegen auf dem Webspace |

Eine Vorlage für die Konfiguration liegt in `api/config/config.sample.php`.
