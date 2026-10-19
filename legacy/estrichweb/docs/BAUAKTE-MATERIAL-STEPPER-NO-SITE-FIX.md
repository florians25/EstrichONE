# Bauakte: Material Stepper und No-Site-Fix

## Änderungen
- Statusanzeige in der Bauakte nutzt jetzt Angular Material `mat-horizontal-stepper`.
- Die Meldung `Bitte zuerst eine Baustelle in der Akte anlegen` wurde entfernt.
- Auftrag und Baustelle werden in der Akte als eine Einheit behandelt.
- Datei-/Foto-/Video-Upload funktioniert auch dann, wenn noch kein separater `construction_sites`-Datensatz vorhanden ist.
- Backend erzeugt in diesem Fall automatisch einen Bauakten-/Baustellen-Datensatz zum Auftrag.
- Tagesdokumentation erzeugt ebenfalls automatisch eine Akte/Baustelle, wenn nur der Auftrag vorhanden ist.

## Deployment
Auf STRATO ersetzen:
- /public
- /api
- /docs optional
- /frontend-angular-source optional
