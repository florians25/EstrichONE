# Patch 2026-05-30: Kalender, Kolonnenplan, Lexware, Admin SSO

## Frontend
- Design auf professionelles Mittelmaß umgestellt: nicht hartkantig, nicht verspielt.
- Kalender mit Wochenansicht, deutschen Tagesnamen, parallelen Terminen, Kategorien und Farbzuordnung.
- Termine können ohne Baustellen-/Auftragsbezug angelegt werden; wenn Auftrag/Baustelle vorhanden ist, wird der Bezug angezeigt.
- Kolonnenplan im Wochenraster verbessert und druckbar gemacht.
- PDF-/Druckexporte für Tagesdokumentation und Erstbegehung ergänzt.
- Offline-Queue nutzt kein `crypto.randomUUID()` mehr; dadurch funktioniert sie auch in HTTP-/älteren Browser-Umgebungen.

## Backend
- `/api/planning/assignments` ist robuster gegen ältere DB-Stände und ergänzt fehlende Spalten automatisch.
- Lexware-REST-Integration: Kontaktprüfung, bessere Fehlerbehandlung, Sync-Logs, lokale Verknüpfung Kunde/Auftrag.
- Microsoft SSO kann zusätzlich über die Admin-Oberfläche gespeichert werden.
- Neue Tabellen/Spalten siehe `database/2026_05_30_calendar_lexware_admin_sso_exports_patch.sql`.

## STRATO
- HTTPS wird weiterhin nicht erzwungen, da die Zielumgebung aktuell HTTP nutzt.
- Passwortwarnung im Browser bei HTTP ist nicht unterdrückbar und technisch korrekt.
