# Benutzerhandbuch Web

`EstrichONE-Handbuch-Web.pdf` – Benutzerhandbuch der Weboberfläche für Büro, Bauleitung und Administration.
Bei jedem Release hängt `.github/workflows/release.yml` das PDF als Asset an.

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
```

`bilder.sh` startet PostgreSQL, API, Archivserver und Backup-Dienst auf eigenen Ports (55491, 5191, 18491, Web 4291),
richtet die Instanz im Browser ein, legt mit `demodaten.py` fiktive Demodaten über die API an und fotografiert die
Szenen aus `szenen.mjs`. Bei Docker-Hub-Ratenlimit: `HB_MIRROR=mirror.gcr.io`. Version und Stand: `HB_VERSION`,
`HB_STAND` (Standard: neueste Versionsnotiz unter `docs/releases`, heutiges Datum).
