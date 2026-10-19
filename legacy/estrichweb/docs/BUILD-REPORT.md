# Build Report

- Angular Build: erfolgreich (`npm run build` / `ng build --configuration production`)
- Angular Output: `/public-angular-build/browser`
- STRATO Runtime Copy: `/public`
- Enterprise Module: 402
- Datenbanktabellen: 57
- JavaScript Bundles in `/public`: 24
- CSS Bundles in `/public`: 1
- PHP Syntaxcheck: erfolgreich für API, Core, Services, Config und Installer

## Angular-Nachweis

Der Build enthält echte Angular-CLI-Artefakte mit `main-*.js`, Lazy Chunks, `polyfills-*.js`, `ngsw-worker.js`, `ngsw.json` und `manifest.webmanifest`.

## Security-Hinweis

`/api/config/local.php` enthält die vom Auftraggeber bereitgestellte STRATO-Datenbankkonfiguration. ZIP-Datei vertraulich behandeln.
