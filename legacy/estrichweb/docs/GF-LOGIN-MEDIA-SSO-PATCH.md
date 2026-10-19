# GF Login / Medien / SSO Patch

## Enthalten

- Login-Fluss korrigiert: Nach erfolgreicher Anmeldung wird die App hart auf die Zielseite geladen.
- Microsoft Single Sign-On als optionale Zusatzanmeldung vorbereitet.
- Auftragsakte mit Bereichen: Übersicht, Baustellen, Dokumente, Fotos/Videos, QM, Freigaben, Historie.
- PDF-Viewer direkt in der Auftragsakte.
- Foto-Viewer direkt in der Auftragsakte.
- Video-Viewer direkt in der Auftragsakte.
- Tagesdokumentation unterstützt Fotos und Videos.
- Fotos werden clientseitig komprimiert und serverseitig als Webversion plus Thumbnail gespeichert.
- Videos werden als Dokumente der Kategorie `daily_video` abgelegt.
- Neues DB-Update: `database/2026_05_30_gf_login_media_sso_patch.sql`.

## Microsoft SSO aktivieren

In `api/config/local.php` eintragen:

```php
'microsoft_sso' => [
  'enabled' => true,
  'tenant_id' => 'common',
  'client_id' => 'MICROSOFT_APP_CLIENT_ID',
  'client_secret' => 'MICROSOFT_APP_CLIENT_SECRET',
  'redirect_uri' => 'http://deine-domain.de/api/auth/microsoft/callback'
]
```

In Microsoft Entra muss dieselbe Redirect-URI hinterlegt sein.

## Upload auf STRATO

Ersetzen:

- `.htaccess`
- `/public`
- `/api`
- `/database`
- `/docs`

Wenn die Datenbankverbindung bereits korrekt ist, `api/config/local.php` nur übernehmen, wenn die dortige Konfiguration gewünscht ist.
