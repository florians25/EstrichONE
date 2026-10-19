# EstrichWeb Patch: Professional Cleanup, Historie, Rollen

Diese Version entfernt Prompt-/KI-Hinweise aus der Oberfläche, reduziert Kopfbereiche, nutzt eine sachlichere Angular-Material-Sidebar mit aufklappbaren Gruppen und ergänzt produktive Arbeitsfunktionen.

## Enthalten

- kompakte Seitenköpfe ohne Marketing-/Prompt-Texte
- eckige Dialoge, Karten, Buttons und Tabellen
- Material-Sidebar mit Submenüs
- Auth-Guard plus Token-Ablaufprüfung vor API-Aufrufen
- 401-Handling: abgelaufene Sessions werden auf Login geleitet
- Auftragsakte mit Historie-Tab
- Aktivitätsanzeige pro Auftrag/Baustelle
- Benutzerverwaltung mit Mehrfachauswahl für Rollen
- vollständiges DB-Gesamtskript: `database/estrichweb_complete_database.sql`
- Update-Patch: `database/2026_05_30_professional_cleanup_history_roles.sql`
