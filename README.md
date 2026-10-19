# EstrichONE

Kostenlose, selbst hostbare Branchensoftware für Estrichbetriebe. Nachfolger von **Estrich Web**.

> **Status: Phase 1–5 (Analyse und Konzept) abgeschlossen. Implementierung noch nicht freigegeben.**
> Es gibt in diesem Repository noch keinen neuen Anwendungscode. Bisher liegen hier nur der eingefrorene
> Altbestand und die Reengineering-Dokumentation.

## Inhalt

| Pfad | Inhalt |
|---|---|
| [`docs/reengineering/`](docs/reengineering/) | Bestandsaufnahme, Funktionskatalog, Zielarchitektur, UX-Konzept, Roadmap (20 Dokumente + Zusammenfassung) |
| [`docs/reengineering/00-zusammenfassung.md`](docs/reengineering/00-zusammenfassung.md) | **Einstieg:** Ergebnis der Analyse auf wenigen Seiten, offene Entscheidungen |
| [`legacy/estrichweb/`](legacy/estrichweb/) | Quellcode von Estrich Web, Stand 23.08.2026 (ohne Zugangsdaten und Build-Artefakte) |

## Geplante Zielstruktur (nach Freigabe)

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
