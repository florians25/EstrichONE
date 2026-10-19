# EstrichWeb Professional UX / Foto-Patch

## Ziel

Dieser Patch korrigiert die UI-/Funktionskritik aus dem Live-Test:

- kein Dark Mode
- eckigeres, professionelleres Layout
- ältere Angular-Material-Linie: Angular 20.3.13 + Material 20.2.14
- keine überladene Sidebar mit jedem Einzelmodul
- auftragszentrierte Navigation
- Icons sichtbar über Material-Icons-Font
- Dashboard verursacht ohne Login keinen 401-Request mehr
- FormArray-Fehler im Auftrag-Wizard behoben
- Auftragsakte/Baustellenakte auf das Wesentliche reduziert
- Erstbegehung als Wizard ergänzt
- Tagesdokumentation mit Foto-Komprimierung ergänzt

## Neue Routen

- `/auftragsakte`
- `/erstbegehung-wizard`
- `/tagesdokumentation`

## Neue API-Endpunkte

- `POST /api/daily-documentation`
- `POST /api/inspections/initial`

## Foto-Speicherstrategie

Fotos werden zuerst im Browser auf maximal 1600px verkleinert und als JPEG mit reduzierter Qualität an die API gesendet. Serverseitig wird zusätzlich ein Thumbnail erzeugt. Originale werden standardmäßig nicht gespeichert. Dadurch bleibt der Webspace auch bei großen Mengen wie 9000 Fotos deutlich stabiler.

Ordnerstruktur:

```text
/storage/photos/{tenant_id}/{YYYY}/{MM}/{site_id}/
```

Pro Foto:

```text
{id}.webp oder {id}.jpg
{id}_thumb.webp oder {id}_thumb.jpg
{id}_original.jpg nur wenn aktiv gewählt
```

## Datenbank-Patch

Bei bestehender Datenbank optional ausführen:

```text
/database/2026_05_29_professional_ux_photos.sql
```

Die API ergänzt fehlende Foto-Spalten beim Upload zusätzlich automatisch, damit bestehende Installationen robuster sind.
