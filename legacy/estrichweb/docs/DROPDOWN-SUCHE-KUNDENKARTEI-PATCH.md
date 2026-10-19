# Patch: Suchbare Dropdowns und Kundenkartei

## Inhalt
- Auftrag anlegen nutzt weiterhin ausschließlich bestehende Kunden aus der Kundenkartei.
- Die Fehlermeldung unterscheidet jetzt zwischen fehlendem Kunden und fehlenden Pflichtfeldern in Auftrag, Baustelle, Räumen oder Planung.
- Kundenauswahl hat eine Suche nach Name, Kundennummer, Ort, PLZ, E-Mail und Telefon.
- Komplexe Dropdowns haben Suchfelder:
  - Kunden
  - Aufträge / Projekte
  - Baustellen
  - Kolonnen
  - Fahrzeuge
  - Maschinen
- Betroffene Masken:
  - Neuer Auftrag
  - Kalender
  - Kolonnenplan
  - Tagesdokumentation
  - Erstbegehung
  - Auftragsakte

## Deployment
Auf STRATO mindestens ersetzen:
- /public
- /frontend-angular-source

Für Betrieb reicht /public. Danach Service Worker, Cache Storage und Local Storage im Browser löschen.
