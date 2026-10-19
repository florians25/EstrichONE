# STRATO Deployment in der Tiefe

## Webspace-Kompatibilität

Diese Variante nutzt PHP + MySQL/MariaDB, weil klassische STRATO-Webhosting-Pakete PHP und Datenbanken bereitstellen, aber keinen permanenten Node/NestJS-Prozess wie auf einem VPS. Das Frontend wird als statische PWA ausgeliefert.

## Empfohlene Einstellungen

- PHP: 8.2 oder 8.3
- Datenbank: MySQL 8 oder MariaDB 10.11
- HTTPS aktivieren
- Verzeichnislisting deaktiviert durch `.htaccess`
- `/storage` öffentlich gesperrt
- Upload-Limit in PHP und `api/config/local.php` abstimmen

## Upload per SFTP

1. Paket entpacken.
2. Kompletten Inhalt in den Webspace laden.
3. Domain-Root auf denselben Ordner zeigen lassen.
4. Installer starten.
5. Nach Installation `/install` löschen.

## Wenn der Document Root nur auf `/htdocs` zeigt

Alle Dateien nach `/htdocs` laden. Die Datei `.htaccess` im Root sorgt dafür, dass API, Installer und SPA-Fallback funktionieren.

## Sicherheit

- `api/config/local.php` enthält Datenbankpasswort und JWT-Secret.
- Niemals per Browser anzeigen lassen.
- STRATO-Backups zusätzlich mit eigenen Exporten ergänzen.
- Für echte DSGVO-Abnahme: AV-Vertrag, Berechtigungskonzept, Löschkonzept, TOMs und Protokollierung juristisch prüfen lassen.
