# Kundenauswahl Dropdown Fix

## Ziel
Beim Anlegen eines Auftrags wird der Kunde ausschließlich aus der bestehenden Kundenkartei gewählt.

## Änderungen
- Keine freie Kundeneingabe.
- Kundenauswahl nur über MatSelect-Dropdown mit Suche.
- Auswahl wird über ein eigenes Signal synchronisiert, damit Angular die Kundendetails sofort neu rendert.
- Unter dem Dropdown werden nach Auswahl automatisch angezeigt:
  - Kundennummer
  - Name
  - Adresse
  - PLZ/Ort
  - E-Mail
  - Telefon
  - Lexware-Status
- Pflichtfeldprüfung unterscheidet sauber zwischen fehlendem Kunden und anderen fehlenden Pflichtfeldern.

## Deployment
Für STRATO mindestens ersetzen:
- /public

Für Quellstand zusätzlich:
- /frontend-angular-source
- /docs
