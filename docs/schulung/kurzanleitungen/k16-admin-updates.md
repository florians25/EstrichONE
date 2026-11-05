% datei: Kurzanleitung-16-Admin-Updates-einspielen
% art: Kurzanleitung · Administration
% fuer: Administration (Recht „Backups verwalten“)

# Admin: Updates einspielen

## Hinweise in der Weboberfläche

| Hinweis | Bedeutung |
|---|---|
| **Update verfügbar** (blau) | Neue Version derselben Versionslinie; je Version ausblendbar |
| **Sicherheitsupdate verfügbar** (rot) | Bitte **zeitnah** einspielen |
| **Automatisches Update fehlgeschlagen** (gelb) | Ergebnis der Automatik – Protokoll prüfen |

## Update von Hand

1. **Versionshinweise** im Hinweis lesen (Link).
2. Optional eine **Ankündigung** bzw. **Wartung planen …** unter **System & Betrieb** → **Hinweise & Wartung**.
3. Auf dem Server den Befehl aus dem Hinweis ausführen, z. B. `sudo /opt/estrichone/update.sh 0.1.2`. Das Skript
   sichert vorher, wartet auf gesunde Dienste und prüft die gemeldete Version.
4. **Zusatzserver nachziehen:** **Backup-Ziele** bzw. **Archiv** → **Aktualisierung der Zusatzserver** →
   **Alle aktualisieren** (oder **Automatisch mitaktualisieren** einschalten).
5. Kurz prüfen: Anmeldung, Startseite, **Überwachung** „Alle Systeme in Ordnung“.

## Automatik einrichten

| Befehl | Wirkung |
|---|---|
| `sudo /opt/estrichone/update.sh --automatik sicherheit` | nur Sicherheitsupdates, nachts 02:00–05:00 Uhr |
| `sudo /opt/estrichone/update.sh --automatik patch 01:00-04:00` | alle Fehlerbehebungen der Versionslinie im Zeitfenster |
| `sudo /opt/estrichone/update.sh --automatik aus` | Automatik aus |
| `sudo /opt/estrichone/update.sh --pruefen` | nur prüfen |

> **Hinweis:** Neue Versionslinien (z. B. 0.1 → 0.2) kommen **nie** automatisch. Während eines Updates zeigen Web und
> App „Wartungsarbeiten“ und machen danach von selbst weiter. Die Apps aktualisieren Sie über den App Store.
