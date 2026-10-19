# EstrichWeb – Änderungen 16.08.2026 (Runde 16)

Basis: `estrichweb-source-PATCHED-2026-07-01-3.zip` zuzüglich der Korrekturen
der Runden 1–15.

---

## Kontrolle Ihrer acht Punkte

| # | Punkt | Stand |
|---|---|---|
| 1 | „Neue Bauakte" trotz fehlendem Recht in der Sidebar | ✅ Runde 15 |
| 2 | Materialbestellungen über die Rechtematrix | ✅ Runde 15 |
| 3 | Rollenauswahl entzerrt | ✅ Runde 15 |
| 4 | Lexware-Seite neu gestaltet | ✅ Runde 15 |
| 5 | Termine ohne Kolonne im Dispositionsplan | ✅ Runde 15 |
| 6 | **Dispositionsplan-PDF verbessern** | **jetzt umgesetzt** |
| 7 | Drag & Drop in der Wochenplanung | ✅ Runde 15 |
| 8 | „Kolonnenplanung"-Verweis rechtebezogen | ✅ Runde 15 |
| 9 | Live-Zeiten beim Ziehen | ✅ Runde 15 |
| 10 | Termine ohne Kolonne beim Mitarbeiter | ✅ Runde 15 |

Punkt 6 war offen – ich hatte in Runde 14 das PDF des **Arbeitsplans**
verbessert, nicht das des **Dispositionsplans**. Das ist jetzt nachgeholt.

---

## Dispositionsplan-PDF neu aufgebaut

### Was vorher störte

```ts
const rowH = 78;                 // feste Zeilenhöhe
items.slice(0, 4).forEach(...)   // mehr als 4 Einsätze wurden abgeschnitten
if (items.length > 4) doc.text(`+ ${items.length - 4} weitere`, ...)
```

Bei mehr als vier Einsätzen an einem Tag stand im PDF nur noch „+ 2 weitere" –
gerade an vollen Tagen fehlte also genau das, was man wissen wollte. Dazu war
die Darstellung rein schwarz-weiß mit Buchstabenkürzeln (G/A/V/M/F), die man
in der Fußzeile nachschlagen musste.

### Was jetzt anders ist

- **Zeilenhöhe richtet sich nach dem vollsten Tag.** Es wird nichts mehr
  abgeschnitten, egal wie viele Einsätze eine Kolonne an einem Tag hat.
- **Farbige Streifen statt Buchstabenkürzel**: blau Estricharbeiten, orange
  Termine und Begehungen, grau Sonstiges – dieselbe Codierung wie in der
  Anwendung, mit Legende in der Fußzeile.
- **Farbiger Kopf** mit Firmenname, Kalenderwoche und Zeitraum.
- **Summenspalte rechts** mit der Zahl der Einsätze je Kolonne.
- **Abschlusszeile** mit Kolonnen- und Einsatzzahl der Woche.
- Je Einsatz drei Zeilen: Uhrzeit und Bezeichnung, Baustelle, Adresse
  (ersatzweise der Status, wenn keine Adresse hinterlegt ist).
- Heutiger Tag im Spaltenkopf hervorgehoben, freie Tage als „frei".
- Seitenumbruch mit wiederholtem Kopf, Seitenzahl und Zeitstempel.
- Dateiname: `Dispositionsplan_KW34_2026-08-17.pdf`

### Neu: Monats-PDF

Die in Runde 15 ergänzte Monatsplanung hatte noch keine PDF-Ausgabe. Der Knopf
heißt dort jetzt **Monats-PDF** und erzeugt:

- das **Kalenderraster** des Monats mit Auslastungsbalken je Tag, Anzahl der
  Einsätze, belegten Kolonnen und Terminen
- darunter die **Ressourcenübersicht** für Kolonnen, Fahrzeuge und Maschinen
  mit Balken und Tagen
- Hinweis in der Fußzeile: „Rot = Doppelbelegung an mindestens einem Tag"
- Dateiname: `Monatsplanung_2026-08.pdf`

---

## Deployment

1. **`api/` hochladen.**
2. Restliche Dateien in den Document-Root.
3. `api/config/local.php` beibehalten (nicht überschreiben).
4. `https://IHRE-DOMAIN/api/health` prüfen – muss JSON liefern.
5. **Strg+F5**, damit der Service Worker die neue Fassung übernimmt.

Datenbank-Migrationen sind nicht erforderlich.

---

## Kurzübersicht Runden 1–15

| Thema | Kern der Lösung |
|---|---|
| Anmeldemaske | Schlicht, wieder mit Angular-Material-Bausteinen |
| Kolonnenplanung | Tag · Woche · **Monat**, Vorrat als schwebende Box unten rechts |
| Monatsplanung | Auslastung je Tag, Ressourcen mit Doppelbelegungswarnung |
| Arbeitsplan | Eigene Seite für alle, Tagesansicht Standard, PDF |
| Live-Zeiten | Mitlaufende Anzeige beim Ziehen im 15-Minuten-Raster |
| Berechtigungen | Matrix Bereich × Recht; Menü prüft konkrete Rechte exakt |
| Kundenportal | Menü, Routen und alle sieben API-Endpunkte abgeschaltet |
| Dokumentenvorschau | Service Worker fing `/api/**` ab; `navigationUrls` korrigiert |
| Seitenleiste | Desktop- und Handy-Zustand getrennt, Einstellung gespeichert |
| 503-Fehler | Vier gleichzeitige Anfragen, Wiederholung, Zwischenspeicher |
| `public/` | Vollständiger Document-Root mit `api`, `storage`, `install` |
