# Fachmodule EstrichWeb

## Baustellenakte

- Kundendaten
- Ansprechpartner
- Bauvorhaben
- Räume
- Estricharten
- Dokumente
- Bilder
- Termine
- Mängel
- Aufgaben

## Plantafel

Ressourcenplanung für Baustellen, Kolonnen, Fahrzeuge und Maschinen. Die API erkennt Doppelbelegungen von Kolonnen, Fahrzeugen und Maschinen anhand überlappender Zeiträume.

## Kolonnenplaner

Kolonnen werden mit Vorarbeiter, Mitgliedern, Fahrzeugen und Maschinen kombiniert. Urlaub, Krankheit und Ersatzplanung können über Kalenderereignisse oder spätere Abwesenheitstabellen geführt werden.

## Prüfungen

Konfigurierbare Vorlagen über `inspection_templates.schema_json`. Ergebnisse in `inspection_results.result_json`.

Beispiel-Feldschema:

```json
{
  "fields": [
    {"key":"cm_percent","label":"CM %","type":"number","required":true},
    {"key":"temperature","label":"Temperatur","type":"number"},
    {"key":"photos","label":"Fotopflicht","type":"photo","required":true}
  ]
}
```

## Dokumente/Fotos

Dateien werden in `/storage` abgelegt und in der Datenbank referenziert. Spätere Erweiterungen: PDF-Vorschau, Bildkomprimierung, OCR-Texterkennung, Versionierung.
