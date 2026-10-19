# STRATO Upload

## Upload nach `/htdocs`

Hochladen:

```text
.htaccess
.user.ini
/public
/api
/storage
/install
/database
```

## Installation

1. Domain öffnen: `https://deine-domain.de/install/install.php`
2. Datenbankdaten eintragen oder vorausgefüllte Werte nutzen
3. Firmenadmin anlegen
4. Installation ausführen
5. Danach löschen: `/install` und `/database`

## Danach benötigte Runtime-Dateien

```text
.htaccess
.user.ini
/public
/api
/storage
```

## Wichtig

`/frontend-angular-source` ist nur für lokale Entwicklung. STRATO braucht nur den gebauten Angular-Code in `/public`.

## Wichtig bei Angular-Updates

Vor dem Upload einer neuen Angular-Version den alten Inhalt von `/public` auf dem Webspace löschen und den neuen `/public` Ordner vollständig hochladen. Sonst kann eine alte `index.html` auf nicht mehr vorhandene `chunk-*.js` Dateien zeigen.

Wenn der Browser weiterhin alte Dateien lädt:

1. Entwicklertools öffnen.
2. Application/Anwendung > Service Workers öffnen.
3. Alten Service Worker deregistrieren.
4. Cache Storage leeren.
5. Seite hart neu laden.

Test vor dem Löschen des Installers:

```text
https://deine-domain.de/install/asset-check.php
```
