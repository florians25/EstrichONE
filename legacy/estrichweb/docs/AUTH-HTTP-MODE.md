# Auth HTTP Mode

Diese Version erzwingt kein HTTPS. Das System bleibt vollständig loginpflichtig, leitet aber `http://` nicht mehr automatisch auf `https://` weiter.

## Browser-Warnung

Die Meldung „Passwort-Felder sind auf einer unsicheren (http://) Seite vorhanden“ kommt vom Browser. Sie ist technisch korrekt und kann nicht sauber deaktiviert werden.

## Empfohlene Nutzung

- HTTP nur für Test/Übergang verwenden.
- Keine echten produktiven Zugangsdaten über HTTP verwenden.
- Für Produktivbetrieb SSL bei STRATO aktivieren und später wieder HTTPS erzwingen.

## Upload

Mindestens ersetzen:

```text
.htaccess
/public
```

Danach im Browser löschen:

```text
Service Worker
Cache Storage
Local Storage estrichweb.*
```
