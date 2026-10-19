# STRATO MIME Hotfix

Problem: Browser meldet `disallowed MIME type (text/html)` für `main-*.js`, `polyfills-*.js`, `chunk-*.js` oder `styles-*.css`.

Ursache: Die Dateien werden nicht als echte statische Dateien ausgeliefert, sondern durch den SPA-Fallback als `index.html` zurückgegeben.

Hotfix in diesem Paket:

- `public/index.html` lädt Bundles explizit über `/public/...`.
- Root-`.htaccess` enthält harte Rewrite-Regeln für Angular-Bundles.
- Missing assets werden als 404 ausgeliefert statt als HTML.

Upload-Pflicht:

1. Alte Dateien auf STRATO löschen oder überschreiben.
2. Root-`.htaccess` wirklich in das Domain-Zielverzeichnis hochladen.
3. Den kompletten Ordner `/public` hochladen, nicht nur `index.html`.
4. Service Worker und Cache im Browser löschen.

Schnelltest im Browser:

- `https://deine-domain.de/public/main-TJKVOYDP.js` muss JavaScript zeigen.
- `https://deine-domain.de/main-TJKVOYDP.js` muss ebenfalls JavaScript zeigen oder mindestens nicht `index.html`.
- `https://deine-domain.de/public/styles-K52MQJLL.css` muss CSS zeigen.
