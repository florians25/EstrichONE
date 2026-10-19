# 0009 – iOS als erste native Plattform

- **Status:** angenommen · **Datum:** 2026-10-19 (Entscheidung des Produktverantwortlichen)

## Entscheidung
Die iPhone-App entsteht als erste native Anwendung, direkt nach dem Server-Fundament. Die Web-Oberfläche wächst
zunächst nur so weit, wie die App sie braucht (Administration, Gerätefreigabe). Android folgt nach der
iOS-Version 1.0.

## Folgen
Für Build und Tests sind macOS-Runner in CI und ein Mac mit Xcode für Gerätetests nötig.
