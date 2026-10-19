# Bauakte Pro: Aktenansicht, Uploads, Karte und Zuweisungen

## Änderungen
- Auftragsakte deutlich erweitert und übersichtlicher strukturiert.
- Dokumente, Fotos und Videos können direkt in der Bauakte hochgeladen werden.
- Dokumente/Bilder/Videos werden direkt in der Akte angezeigt.
- Standortkarte wird aus der Baustellenadresse in der Bauakte angezeigt.
- Bauarten/Estricharten werden als Tabelle in der Bauakte angezeigt.
- Zuweisungen an Mitarbeiter können direkt aus der Bauakte erstellt werden.
- Aufgaben/Zuweisungen werden mit Auftrag und Baustelle gespeichert, soweit DB-Spalten vorhanden sind.
- Dropdown-Optionen werden im Frontend mit deutschen Fachtexten angezeigt.
- Fahrzeugauswahl im Kolonnenplan zeigt jetzt Name plus Kennzeichen.

## Datenbank-Patch
Bei bestehender Installation ausführen:

`database/2026_05_30_bauakte_pro_assignments_uploads.sql`

## Upload
Auf STRATO ersetzen:
- /public
- /api
- /database
- /frontend-angular-source optional
