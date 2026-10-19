# Authorization Header Fix für STRATO/PHP

Dieses Paket behebt den Fall, dass der Browser nach dem Login ein gültiges JWT speichert, PHP auf dem Webspace den HTTP-Header `Authorization: Bearer ...` aber nicht erhält.

Änderungen:

- Root `.htaccess` erhält `HTTP_AUTHORIZATION` per Rewrite-Environment.
- `/api/.htaccess` erhält dieselbe Regel.
- Angular sendet zusätzlich `X-Authorization` und `X-EW-Authorization`.
- PHP `Request::bearer()` liest alle üblichen Apache/FastCGI-Header-Varianten.

Test nach Upload:

1. Browser-Cache, Service Worker und Local Storage löschen.
2. Neu anmelden.
3. `http://deine-domain.de/api/auth/check` im eingeloggten Browser öffnen.
4. Erwartung: `authenticated: true`.
