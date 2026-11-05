# Schulungsmaterial und Kurzanleitungen

Anleitungen und Unterlagen für die Einführung von EstrichONE im Betrieb. Jede Datei ist eine eigene PDF unter `pdf/`
im Erscheinungsbild der Benutzerhandbücher (`docs/handbuch`). Inhalte stammen aus den Handbüchern und dem Code der
Apps; Bilder sind die Bildschirmfotos der Handbücher (`docs/handbuch/bilder`, `docs/handbuch/app/bilder`).

Bei jedem Release hängt `.github/workflows/release.yml` alle PDFs als ZIP `EstrichONE-Schulung-<tag>.zip` an.

## Kurzanleitungen (je 1–2 Seiten A4, zum Ausdrucken und Aushängen)

| Nr. | PDF | Zielgruppe |
|---|---|---|
| 01 | `EstrichONE-Kurzanleitung-01-App-einrichten-und-anmelden.pdf` | alle mit iPhone/iPad |
| 02 | `EstrichONE-Kurzanleitung-02-Fotos-und-Dokumente-zur-Bauakte.pdf` | Kolonne, Bauleitung |
| 03 | `EstrichONE-Kurzanleitung-03-Bautagebuch-und-Maengel.pdf` | Kolonne, Bauleitung |
| 04 | `EstrichONE-Kurzanleitung-04-Abnahme-mit-Unterschrift.pdf` | Bauleitung |
| 05 | `EstrichONE-Kurzanleitung-05-Wer-faehrt-wohin-und-Meine-Woche.pdf` | Kolonne, Bauleitung, Disposition |
| 06 | `EstrichONE-Kurzanleitung-06-Chat-Kolonne-und-Baustelle.pdf` | alle (wenn der Chat aktiv ist) |
| 07 | `EstrichONE-Kurzanleitung-07-Offline-und-Nur-Ansicht.pdf` | alle mit iPhone/iPad |
| 08 | `EstrichONE-Kurzanleitung-08-Web-Bauakte-anlegen.pdf` | Büro, Bauleitung |
| 09 | `EstrichONE-Kurzanleitung-09-Web-Cockpit-Disposition-und-Freigabe.pdf` | Disposition |
| 10 | `EstrichONE-Kurzanleitung-10-Web-Kunden-Anrufe-Anfragen.pdf` | Büro |
| 11 | `EstrichONE-Kurzanleitung-11-Urlaub-beantragen-und-genehmigen.pdf` | alle, Genehmigende |
| 12 | `EstrichONE-Kurzanleitung-12-Material-und-Bestellungen.pdf` | Büro, Disposition |
| 13 | `EstrichONE-Kurzanleitung-13-Admin-Benutzer-und-Geraete-anlegen.pdf` | Administration |
| 14 | `EstrichONE-Kurzanleitung-14-Admin-Backup-Center-und-Notfallblatt.pdf` | Administration |
| 15 | `EstrichONE-Kurzanleitung-15-Admin-Kombiserver-Backup-Ziel-und-Archiv-koppeln.pdf` | Administration |
| 16 | `EstrichONE-Kurzanleitung-16-Admin-Updates-einspielen.pdf` | Administration |
| 17 | `EstrichONE-Kurzanleitung-17-Erstbegehung-und-CM-Messung.pdf` | Bauleitung |
| – | `EstrichONE-Kurzanleitungen-alle.pdf` | Sammel-PDF aller Kurzanleitungen mit Inhaltsverzeichnis |

## Schulungsunterlagen

| PDF | Inhalt |
|---|---|
| `EstrichONE-Schulungsleitfaden-Trainer.pdf` | Ablaufpläne (Kolonne 60 min, Bauleitung 90 min, Büro/Disposition 2 h, Administration 2 h), Lernziele, Vorbereitung mit Demomodus und Übungsinstanz, Übungen mit Lösung, typische Fragen |
| `EstrichONE-Arbeitsblatt-Kolonne.pdf`, `…-Bauleitung.pdf`, `…-Buero-und-Disposition.pdf`, `…-Administration.pdf` | Übungen für Teilnehmende zum Ausfüllen |
| `EstrichONE-Checkliste-Einfuehrung-im-Betrieb.pdf` | Vor, am und nach dem Go-live |
| `EstrichONE-Wissenstest-Kolonne.pdf`, `…-Bauleitung.pdf`, `…-Buero-und-Disposition.pdf`, `…-Administration.pdf` | je 10 Fragen |
| `EstrichONE-Wissenstests-Loesungsblatt.pdf` | Lösungen aller Wissenstests mit Verweisen (nur für Trainer) |
| `EstrichONE-Teilnahmebestaetigung-Vorlage.pdf` | Vorlage zum Ausfüllen |
| `EstrichONE-Spickzettel-Tastaturkuerzel-Web.pdf` | alle Tastaturkürzel der Weboberfläche auf einer Seite |

## Quellen und Bau

| Pfad | Inhalt |
|---|---|
| `kurzanleitungen/k*.md` | Kurzanleitungen (große Schrift, nummerierte Schritte) |
| `unterlagen/s*.md` | Leitfaden, Arbeitsblätter, Checkliste, Tests, Vorlagen |
| `vorlage/schulung.css` | Ergänzung zur Druckvorlage `docs/handbuch/vorlage/handbuch.css` |
| `pdf/` | erzeugte PDFs |

Die Markdown-Teilmenge ist die der Handbücher (`docs/handbuch/scripts/pdf.mjs`), ergänzt um Kopfzeilen am Dateianfang
(`% datei:`, `% art:`, `% fuer:`, `% stil: kurz|blatt|dokument`, `% inhalt: ja`, `% felder: Name, Datum`,
`% klasse:`), Ankreuzkästchen (`- [ ] …`), Lösungskästen (`> **Lösung:** …`) und Bildoptionen
`{ausschnitt=x,y,b,h breite=px}` (ImageMagick schneidet aus, verkleinert und speichert JPEG – kleine PDFs).

```bash
node docs/handbuch/scripts/pdf.mjs schulung              # alle PDFs und die Sammel-PDF
node docs/handbuch/scripts/pdf.mjs schulung k04 sammel   # nur ausgewählte (Dateinamen-Teil) und die Sammel-PDF
```

Voraussetzungen wie beim Handbuch: Node.js ≥ 22.12 mit `npm ci` in `web/` (playwright-core), Chromium
(`PLAYWRIGHT_CHROMIUM`), ImageMagick (`convert`), Poppler (`pdftotext`, `pdfinfo`). Version und Stand: `HB_VERSION`,
`HB_STAND`. Kurzanleitungen mit mehr als zwei Seiten meldet der Bau als Warnung. Werden die Handbuch-Bilder neu erzeugt,
die PDFs danach neu bauen.
