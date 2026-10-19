# EstrichWeb – Änderungen 17.08.2026 (Runde 20)

---

## 1. Termine lagen nicht exakt im Raster – Ursache

```css
.gc-slot { height: 60px; border-bottom: 1px solid #e8eaed; }
```

Ohne `box-sizing: border-box` addiert der Browser den Rahmen **zur** Höhe.
Jede Stunde war damit 61 px hoch, 24 Stunden ergaben **1464 px** – die Termine
rechnen aber mit den 1440 px des Rasters (24 × 60). Der Versatz betrug oben
null und unten fast eine halbe Stunde.

Behoben: `box-sizing: border-box` für Stundenzellen, Stundenachse und
Terminblöcke; Spalten und Achse zusätzlich fest auf 1440 px. Ein Termin um
14:30 sitzt jetzt exakt auf der Linie.

## 2. Verschieben und Dauer ändern

**Ziehen:** Termin anfassen und verschieben – innerhalb des Tages **und über
Tagesspalten hinweg**. Während des Ziehens läuft oben im Block die Zeit mit
(`09:15 – 10:45`). Gerastert wird auf **15 Minuten**, wie bei Google.

**Dauer ändern:** Am unteren Rand jedes Termins liegt ein Anfasser (beim
Überfahren sichtbar). Nach unten ziehen verlängert, nach oben verkürzt –
Mindestdauer 15 Minuten.

Ein Klick ohne Bewegung öffnet weiterhin den Termin; die Unterscheidung läuft
über ein Bewegungsmerkmal, damit sich Ziehen und Klicken nicht in die Quere
kommen.

## 3. Überschneidende Termine

Die frühere Aufteilung hat alle Termine gleichmäßig zu schmalen Streifen
zusammengedrückt – bei drei Terminen blieb je ein Drittel Spaltenbreite.

Jetzt wie bei Google: Es werden Gruppen sich berührender Termine gebildet und
darin Spalten belegt. Spätere Termine greifen um 22 % über ihren Nachbarn –
vom darunterliegenden bleiben Farbrand und Titelanfang sichtbar. Zusätzlich:

- **Mindestbreite 12 %**, damit nie ein unlesbarer Strich entsteht.
- Schmale Blöcke blenden die Zeitzeile aus, sehr flache Blöcke stellen Titel
  und Zeit **nebeneinander** statt untereinander.
- Ein heller Trennstrich links am überlappenden Termin.
- Beim Überfahren rückt der Termin nach vorn.

## 4. Termine ohne Kolonne im Kalender

Kolonneneinsätze bleiben bewusst **draußen** – die gehören in die
Kolonnenplanung, nicht in den persönlichen Kalender. Übernommen werden nur
Einträge aus `planning_assignments` **ohne** `crew_id`: Begehungen,
Kontrollen, Abnahmen, Aufmaße, CM-Messungen, Kundentermine.

- Eigener Eintrag **„Termine ohne Kolonne"** in der Kalenderliste, dort
  ausblendbar wie jeder andere Kalender.
- Orange eingefärbt und **schraffiert** – damit sofort erkennbar ist, dass
  diese Termine aus der Planung stammen.
- **Schreibgeschützt**: kein Ziehen, kein Ändern der Dauer. Ein Klick weist
  darauf hin, dass sie in der Planung bearbeitet werden. So kann im Kalender
  nichts entstehen, was die Disposition nicht kennt.
- Geladen wird der laufende plus die beiden angrenzenden Monate; beim Blättern
  wird nachgeladen.

---

## Deployment

1. **`api/` hochladen.**
2. Restliche Dateien in den Document-Root.
3. `api/config/local.php` beibehalten (nicht überschreiben).
4. **Strg+F5**, damit der Service Worker die neue Fassung übernimmt.

Datenbank-Migrationen sind nicht erforderlich.

---

## Hinweis

Das Ziehen läuft über Zeigereignisse (`pointerdown`/`pointermove`) und
funktioniert damit auch auf Tablets mit Stift oder Finger. Auf dem Handy ist
die Wochenansicht allerdings sehr schmal – dort ist die Tagesansicht oder die
Terminübersicht der sinnvollere Weg. Falls das Ziehen auf Mobilgeräten stören
sollte (weil es mit dem Scrollen kollidiert), lässt es sich dort mit einer
Zeile abschalten – sagen Sie Bescheid, wie es sich im Einsatz anfühlt.
