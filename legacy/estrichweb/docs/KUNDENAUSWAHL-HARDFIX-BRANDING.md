# Kundenauswahl Hardfix und Branding

## Branding
Sichtbarer Produktname: Estrich Leis EstrichWeb.

## Kundenauswahl
Beim Anlegen eines Auftrags gilt ausschließlich der Wert im Dropdown `Kunde aus Kundenkartei`.

Die frühere Zusatzprüfung über `selectedCustomer()` blockiert das Speichern nicht mehr. Der Dropdown-Wert `customer_id` ist die verbindliche Quelle.

## UI
Die Box `Kein Kunde ausgewählt` wurde entfernt.
Wenn ein Kunde gewählt ist, erscheinen darunter automatisch Kundendetails. Wenn kein Kunde gewählt ist, wird keine zusätzliche Box angezeigt.

## Deployment
Für STRATO mindestens `/public` ersetzen.
Für vollständigen Quellstand zusätzlich `/frontend-angular-source` und `/docs` ersetzen.
