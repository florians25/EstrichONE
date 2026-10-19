# Pflichtenheft-Abdeckung

Diese Version bildet die Pflichten aus dem Master-Pflichtenheft als echte Module, Tabellen, API-Endpunkte und Angular-Routen ab.

## Abgedeckte Bereiche

- Mandantenfähigkeit: `tenant_id` in allen Geschäftstabellen, API-Filterung pro Mandant
- Rollen/Rechte: 14 Rollen, Rechte für Lesen/Schreiben/Löschen/Export/Freigabe/Archiv
- Sicherheit: JWT, Refresh Tokens, Device Sessions, Audit Log, Prepared Statements, Upload-Prüfung
- Dashboard: Geschäftsführungs-, Bauleiter- und operative KPIs
- Baustellenakte: Kunden, Ansprechpartner, Projekte, Baustellen, Räume, Estrichaufbau
- Plantafel: Einplanungen, Konfliktprüfung, Ansichten, Ressourcenbezug
- Kolonnenplaner: Kolonnen, Kolonnenmitglieder, Fahrzeuge, Maschinen, Abwesenheiten
- Kalender: Terminmodell mit Wiederholung, Erinnerung, Sichtbarkeit, Google/Outlook Feldern
- Estrich-Fachmodule: Estricharten, Komponenten, Aufbau je Raum
- Erstbegehung/Prüfung: Vorlagen, Ergebnisse, CM-Messung, Prüfungen
- Dokumentation: Dokumente, Versionen, Fotos, Annotationen, Berichte, Abnahmen
- Aufgaben/Mängel: Workflows, Kommentare, Status
- Qualitätsmanagement: Protokolle, Gewährleistung, Reklamation
- Kundenportal/Subunternehmerportal: Portal-Datensätze und Sichtbarkeitsfelder
- Lexware Office: Sync-Log und Payload-Felder für Kunden, Angebote, Rechnungen, Zahlungen
- Offline: Angular-PWA, Service Worker, Offline Queue, Sync API
- KI: `ai_jobs` für OCR, Dokumentenerkennung, Berichtsgenerierung und Projektanalyse als Integrationspunkt

## Grenzen klassischer Webspace-Umgebung

Dauerhafte Worker, WebSockets, lokale OCR/KI-Verarbeitung und lange Hintergrundjobs sind auf klassischem Webspace nur eingeschränkt möglich. Diese Edition speichert Jobs und Status, die Verarbeitung sollte später durch externen Dienst, Cron oder VPS ergänzt werden.
