# Auth STRATO 401 Hotfix

Fehlerbild nach Login:

- Loginformular erscheint wieder
- `/api/dashboard` oder `/api/module/projects` meldet `401 Unauthorized`
- Im Browser ist ein Token vorhanden, PHP erkennt den Benutzer trotzdem nicht

Ursache:

Auf manchen PHP/FastCGI-Webspaces wird der HTTP-Header `Authorization` nicht an PHP weitergegeben. Das Frontend sendet das JWT korrekt, aber `Request::bearer()` sieht kein Token.

Fix in diesem Paket:

- `.htaccess` und `api/.htaccess` übergeben `HTTP_AUTHORIZATION` explizit.
- Angular sendet zusätzlich `X-Authorization` und `X-EW-Authorization`.
- PHP liest `Authorization`, `X-Authorization`, `X-EW-Authorization`, `HTTP_AUTHORIZATION` und `REDIRECT_HTTP_AUTHORIZATION`.
- Neuer Test-Endpunkt: `/api/auth/check`.

Test:

1. `/public` und `.htaccess` und `/api` aus diesem Paket hochladen.
2. Browserdaten für die Domain löschen: Service Worker, Cache Storage, Local Storage.
3. Neu anmelden.
4. Danach im eingeloggten Browser `/api/auth/check` öffnen.

Erwartung:

```json
{"authenticated":true}
```
