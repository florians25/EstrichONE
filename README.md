# EstrichONE

Kostenlose, selbst hostbare Branchensoftware für Estrichbetriebe. Nachfolger von **Estrich Web**.

> **Status: Implementierung läuft** (Roadmap: `docs/reengineering/20-entwicklungsroadmap.md`).
> Fertig: AP-01 Repository und CI, AP-02 Backend-Skelett und Docker-Stack.

## Inhalt

| Pfad | Inhalt |
|---|---|
| [`docs/reengineering/`](docs/reengineering/) | Bestandsaufnahme, Funktionskatalog, Zielarchitektur, UX-Konzept, Roadmap (20 Dokumente + Zusammenfassung) |
| [`docs/reengineering/00-zusammenfassung.md`](docs/reengineering/00-zusammenfassung.md) | **Einstieg:** Ergebnis der Analyse auf wenigen Seiten, offene Entscheidungen |
| [`backend/`](backend/) | ASP.NET-Core-Server ([Entwickeln](backend/README.md)) |
| [`deploy/`](deploy/) | Docker Compose, Caddy, Einrichtung ([Installation](deploy/README.md)) |
| [`contracts/openapi/v1.json`](contracts/openapi/v1.json) | API-Vertrag für alle Clients |
| [`docs/adr/`](docs/adr/) | Architekturentscheidungen |
| [`legacy/estrichweb/`](legacy/estrichweb/) | Quellcode von Estrich Web, Stand 23.08.2026 (ohne Zugangsdaten und Build-Artefakte) |

## Zielstruktur

```
backend/        ASP.NET Core – modularer Monolith (API, Hintergrunddienste)
web/            Angular – Büro, Verwaltung, Administration
desktop/        WinUI 3 – EstrichONE Desktop für Windows
ios/            SwiftUI – EstrichONE für iPhone/iPad
android/        Kotlin + Jetpack Compose – EstrichONE für Android
contracts/      OpenAPI-Spezifikation (Quelle für alle generierten Clients)
deploy/         Docker Compose, Caddy, Backup-Worker, Restore-Werkzeug
docs/           Architektur, Betrieb, Entscheidungen (ADR)
legacy/         Estrich Web (eingefroren, nur Referenz und Migrationsquelle)
```
