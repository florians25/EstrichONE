# Material, Bestellungen und Lieferanten

Das Modul **Material** lässt sich unter **Systemeinstellungen** → **Module** ein- und ausschalten (Abschnitt 9.6).
Ausgeschaltet verschwinden Bestellungen, Materialstamm und Lieferanten aus Navigation und Bauakte; die Daten bleiben
erhalten.

## Materialstamm

**Material** → **Materialstamm** enthält die Artikel, die Sie verbauen und bestellen: Bezeichnung, Artikelnummer,
**Kategorie** (Estrichmörtel, Bindemittel, Zuschlag, Dämmung, Folie/Abdeckung, Randdämmstreifen, Zusatzmittel,
Sonstiges), **Einheit**, Standardlieferant und Einkaufspreis je Einheit.

![Materialstamm](bilder/material.png "Der Materialstamm mit Kategorie, Einheit, Lieferant und Preis")

**Artikel anlegen** erfasst einen neuen Artikel. Nicht mehr benötigte Artikel setzen Sie auf inaktiv statt sie zu
löschen; **Inaktive anzeigen** blendet sie wieder ein.

## Lieferanten

**Material** → **Lieferanten** verwaltet Ihre Lieferanten mit Kundennummer (Ihre Nummer beim Lieferanten),
Ansprechpartner, Telefon, E-Mail, Anschrift und **Bestellhinweisen** wie Bestellschluss oder Mindestmengen.

![Lieferanten](bilder/lieferanten.png "Lieferanten mit Kundennummer und Kontakt")

## Bestellungen

**Material** → **Bestellungen** listet alle Materialbestellungen nach Liefertermin. Oben stehen die Kennzahlen
**Lieferungen heute**, **Offene Bestellungen**, **Noch nicht abgeschickt** und **Lieferung überfällig**.

![Bestellungen](bilder/bestellungen.png "Bestellungen mit Liefertermin, Lieferant, Lieferadresse, Summe und Status")

### Material bestellen

1. Klicken Sie auf **Material bestellen** (oder in der Bauakte unter **Material** auf **Auswahl bestellen** bzw.
   **Material bestellen** – dann sind Bauakte und Positionen schon vorbelegt).
2. Wählen Sie den **Lieferanten** und optional die **Bauakte**. Ohne Bauakte gilt die Bestellung für das Lager.
3. **Liefertermin** und **Zeitfenster** (oder „nach Absprache“).
4. **Lieferadresse** und **Ansprechpartner vor Ort** – leer bedeutet: Baustelle bzw. Ansprechpartner der Bauakte.
5. **Positionen**: Artikel aus dem Materialstamm oder ein freier Artikel, Menge, Einheit, Einzelpreis. **Position
   hinzufügen** für weitere Zeilen; die Summe wird mitgerechnet.
6. **Hinweise an den Lieferanten**, z. B. „Anfahrt nur über Hofeinfahrt, Kran nötig“.
7. **Speichern**. Die Bestellung ist zunächst ein **Entwurf**.

Den Bestellschein schicken Sie per E-Mail an den Lieferanten oder drucken ihn (**Drucken / PDF**). Danach fragt
EstrichONE „E-Mail gesendet?“ – mit **Nur als bestellt markieren** setzen Sie den Status ohne erneuten Versand. Der
Bestellschein wird zusätzlich in der Bauakte abgelegt.

### Status einer Bestellung

| Status | Bedeutung |
|---|---|
| **Entwurf** | Angelegt, noch nicht an den Lieferanten geschickt |
| **Bestellt** | An den Lieferanten verschickt |
| **Bestätigt** | Der Lieferant hat bestätigt |
| **Geliefert** | Ware ist angekommen |
| **Storniert** | Bestellung entfällt |

Die Reiter **Offen**, **Entwürfe**, **Geliefert**, **Storniert** und **Alle** filtern die Liste; zusätzlich filtern Sie
nach Lieferant und suchen nach Nummer, Material oder Ort. Anstehende Lieferungen erscheinen auch im Kopf der Bauakte
(z. B. „Lieferung Mittwoch, 04.11. (07:00): Baustoff Huber KG“).
