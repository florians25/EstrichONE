# EstrichWeb – Änderungen 18.08.2026 (Runde 30)

## Kalender am Handy: eigene Agenda mit Terminkarten

Am Handy gibt es **kein Stundenraster mehr** – nur noch die Agenda. Sie ist
neu gebaut, nach dem Vorbild Ihres Screenshots.

### Warum eigener Code statt FullCalendar

FullCalendar bringt eine Listenansicht mit, die aber als Tabelle aufgebaut ist:
Zeile mit Punkt, Uhrzeit und Titel. Daraus die Kartenoptik zu erzwingen hätte
bedeutet, gegen die Tabellenstruktur anzuarbeiten – erfahrungsgemäß genau die
Art von Umbau, die später an unerwarteter Stelle bricht.

Am Handy braucht es die Stärken von FullCalendar ohnehin nicht: Ziehen ist
dort abgeschaltet, überlappende Termine gibt es in einer Liste nicht. Deshalb
ist die Agenda eigener Code mit voller Gestaltungsfreiheit. **Am Desktop
bleibt FullCalendar unverändert** – dort zählen Ziehen und Raster.

FullCalendar läuft am Handy weiterhin im Hintergrund und liefert Zeitraum und
Titel für die Kopfleiste, ist aber ausgeblendet.

### Aufbau

Links eine schmale Datumsspalte mit **großer Tageszahl** und Wochentagskürzel –
der heutige Tag als blauer Kreis. Rechts die Termine als eigenständige Karten.

Jede Karte:

- **Ganzflächig in der Farbe ihres Kalenders**, mit 12 px Rundung und feinem
  Schatten
- **Uhrzeit** mit Uhrsymbol, Ziffern mit fester Breite
- **Titel** in kräftiger Schrift, darf umbrechen statt abgeschnitten zu werden
- **Ort** mit Ortssymbol, sofern hinterlegt
- **Kalendername** als Chip am unteren Rand
- Termine aus der Planung schraffiert und mit Schlosssymbol

Die Schriftfarbe wird weiterhin aus der Helligkeit der Kalenderfarbe berechnet –
auf gelben Karten steht sie dunkel, auf blauen weiß. Auch der Chip passt sich
an: heller Grund auf dunklen Karten, dunkler auf hellen.

### Bedienung

- **Tag ohne Termine** bleibt sichtbar, schrumpft aber auf eine Zeile mit dem
  Hinweis „Keine Termine" – so bleibt der Rhythmus der Woche erkennbar.
- Der **Zeitraum** ist über das Menü in der Kopfleiste wählbar: diese Woche,
  drei Wochen (Vorgabe) oder zwei Monate.
- Am Ende ein Knopf **„Weitere drei Wochen anzeigen"**.
- Tippen auf eine Karte öffnet das Detailfenster, das von unten einfährt.
- Der Daumenknopf zum Anlegen bleibt unten rechts.
- Pfeile und „Heute" in der Kopfleiste verschieben den Zeitraum wochenweise.

---

## Deployment

1. **`api/` hochladen.**
2. Restliche Dateien in den Document-Root.
3. `api/config/local.php` beibehalten (nicht überschreiben).
4. **Strg+F5**, damit der Service Worker die neue Fassung übernimmt.

Datenbank-Migrationen sind nicht erforderlich.

---

## Anmerkung

Die Kartenoptik lässt sich leicht anpassen – Höhe, Rundung, welche Angaben
erscheinen. Sagen Sie einfach, was Ihnen fehlt oder zu viel ist; das sind
gezielte Änderungen an einer Stelle, weil die Agenda jetzt eigener,
überschaubarer Code ist.
