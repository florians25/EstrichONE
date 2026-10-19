# Professional Navigation / Auth / Orders Patch

Dieser Patch setzt die Oberfläche stärker auf echte Büroprozesse:

- keine App-Nutzung ohne Login: alle App-Routen sind per Angular AuthGuard geschützt
- keine Demo-/Fallbackdaten mehr in ApiService: Listen, Dashboard, Kalender und Planung lesen nur noch API/DB-Daten
- Sidebar fachlich neu sortiert: Aufträge, Planung, Baustelle, Büro
- Stammdaten sind nur noch im Hauptmodul „Stammdaten“ gebündelt sichtbar
- Auftragsakte mit Status-Stepper: Neu → Geplant → In Arbeit → Abgeschlossen
- Auftragssuche mit aktiven Aufträgen und getrenntem Archiv für abgeschlossene Aufträge
- Aufträge und Baustellen auch als Tabellenansicht
- Kolonnen-Wochenplaner mit Druckfunktion und print-optimiertem Layout
- Erfassungsdialoge und Material-Komponenten eckiger und sachlicher
- Workflow-Wizard ohne problematische FormArrayName-Verschachtelung

Nach dem Upload bitte Service Worker und Browser-Cache löschen.
