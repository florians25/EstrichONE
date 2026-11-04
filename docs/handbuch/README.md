# Benutzerhandbücher

- `EstrichONE-Handbuch-Web.pdf` – Benutzerhandbuch der Weboberfläche für Büro, Bauleitung und Administration.
- `EstrichONE-Handbuch-App.pdf` – Benutzerhandbuch der App für iPhone und iPad (mit Anhang zur Android-App) für
  Bauleitung, Kolonnen und Büro. Quellen unter `app/` (siehe unten).

Bei jedem Release hängt `.github/workflows/release.yml` beide PDFs als Assets an.

| Pfad | Inhalt |
|---|---|
| `kapitel/*.md` | Kapitel (`01-…` bis `09-…`, Anhänge `a1-…`); eine kleine Markdown-Teilmenge, siehe `scripts/pdf.mjs` |
| `bilder/` | Bildschirmfotos (1440 × 900, Horizon hell), erzeugt von `scripts/bilder.sh` |
| `vorlage/handbuch.css` | Druckvorlage A4 (Titelseite, Inhaltsverzeichnis, Kopf-/Fußzeilen, Markenfarben) |
| `scripts/` | Demo-Instanz, Demodaten, Bildschirmfotos, PDF-Bau |

## Neu erzeugen (z. B. für eine neue Version)

Voraussetzungen: Docker, .NET SDK, Node.js ≥ 22.12 (`npm ci` in `web/`), Chromium (`PLAYWRIGHT_CHROMIUM`),
ImageMagick, Poppler (`pdftotext`, `pdfinfo`), Python 3.

```bash
docs/handbuch/scripts/bilder.sh          # frische Demo-Instanz „Estrichbau Muster GmbH“, alle Bilder neu
docs/handbuch/scripts/bilder.sh cockpit  # nur einzelne Szenen (Namen aus scripts/szenen.mjs)
node docs/handbuch/scripts/pdf.mjs       # PDF bauen (zwei Durchläufe für die Seitenzahlen im Inhaltsverzeichnis)
node docs/handbuch/scripts/pdf.mjs app   # App-Handbuch bauen
```

`bilder.sh` startet PostgreSQL, API, Archivserver und Backup-Dienst auf eigenen Ports (55491, 5191, 18491, Web 4291),
richtet die Instanz im Browser ein, legt mit `demodaten.py` fiktive Demodaten über die API an und fotografiert die
Szenen aus `szenen.mjs`. Bei Docker-Hub-Ratenlimit: `HB_MIRROR=mirror.gcr.io`. Version und Stand: `HB_VERSION`,
`HB_STAND` (Standard: neueste Versionsnotiz unter `docs/releases`, heutiges Datum).

## App-Handbuch

| Pfad | Inhalt |
|---|---|
| `app/kapitel/*.md` | Kapitel `01-…` bis `10-…`, Anhänge `a1-…` bis `a3-…` (gleiche Markdown-Teilmenge wie oben) |
| `app/bilder/` | echte Simulator-Aufnahmen der iOS-App im Demomodus (iPhone 17 Pro, iPad Pro 13"), JPEG |
| `app/vorlage/app.css` | Ergänzung zur Druckvorlage: schmale iPhone-Bilder, Bildreihen, iPad-Bilder, Gerät auf der Titelseite |
| `app/bilder.sh` | erzeugt `app/bilder/` aus einem Ordner mit Aufnahmen des CI-Jobs „Screenshots“ und aus `ios/AppStore/roh` |

Bilder neu erzeugen: Workflow „iOS“ (Job „Screenshots“, siehe `ios/README.md`) laufen lassen, die Aufnahmen
`iphone-<bildschirm>-hell.png` / `ipad-<bildschirm>-hell.png` aus Artefakt oder Protokoll in einen Ordner legen und
`docs/handbuch/app/bilder.sh <ordner>` aufrufen. Bildzeilen direkt untereinander (ohne Leerzeile) setzt `pdf.mjs`
nebeneinander; Klassen: `{rechts}` (iPhone neben dem Text), `{ipad}` (quer, volle Breite), `{ipadhoch}`.
