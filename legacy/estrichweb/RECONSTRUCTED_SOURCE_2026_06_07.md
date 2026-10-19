# Rekonstruierte EstrichWeb-Source – 2026-06-07

Diese Arbeitsbasis wurde aus der letzten verfügbaren Source-ZIP und den in diesem Chat erzeugten Hotfixes rekonstruiert.

## Enthalten

- Aktuelle API aus dem letzten Overlay-Stand inklusive:
  - Innendienst-Zugriff Bauakte/users-Referenzdaten
  - automatische Bauaktennummern
  - Storno/Löschen mit Passwortprüfung und verknüpften Dateien
  - statisches „Was gibt es Neues“
  - Anrufe/Anfragen-Split inkl. `in_progress`, Abbruch, Umwandlung
  - Anfrage-Detailfelder
  - Materialbestellungen, Lieferanten, Summary-Endpunkte
- Aktuelle Datenbank-Migrationen:
  - `2026_06_05_calls_inquiries_split.sql`
  - `2026_06_06_inquiry_detail_fields.sql`
  - `2026_06_06_material_orders_suppliers.sql`
- Angular-Source rekonstruiert für die weitere Entwicklung:
  - Büro → Anrufe/Anfragen
  - Anfrage-Detailseite mit horizontalem Status-Stepper
  - Material in Bauakte
  - Büro → Materialbestellungen mit offener Badge-Zahl in Sidebar
  - Lieferanten über Stammdaten/Modul-Registry
  - mobile Bauaktenanlage-Optimierungen aus den verfügbaren Patches
  - Erstbegehung: „Keine Erstbegehung notwendig“ und Foto-Deduplizierung
- `public/` enthält einen frisch aus dieser Source erzeugten, lauffähigen Development-Build ohne Source-Maps.

## Build-Prüfung

Ausgeführt und erfolgreich:

```bash
cd frontend-angular-source
npm ci
npx tsc -p tsconfig.app.json --noEmit
npx ng build --configuration development --source-map=false --progress=false
```

Hinweis: Der vollständige Angular-Production-Build (`npm run build`) konnte in dieser Umgebung wegen Zeitlimit/CLI-Hang nicht abgeschlossen werden. Die Source kompiliert und der Development-Build wurde erfolgreich erzeugt. Für Deployment auf dem Server kann lokal weiterhin `npm run build` ausgeführt werden.

## Nicht enthalten

- `node_modules/`
- `.angular/` Cache
- `api/config/local.php` mit echten Zugangsdaten

## Deployment-Hinweis

Vor einem produktiven Deployment:

1. SQL-Migrationen in `database/` ausführen, falls noch nicht geschehen.
2. `api/config/local.php` aus der Serverumgebung verwenden.
3. Lokal im Ordner `frontend-angular-source` `npm ci` und danach `npm run build` ausführen.
4. Danach den erzeugten Inhalt aus `public-angular-build/browser/` als `public/` deployen.
