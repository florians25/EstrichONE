# Fehlerbehebung: MySQL TEXT-Index und Angular MIME type text/html

## 1. MySQL Fehler #1170 bei task_comments.body

MySQL/MariaDB erlauben keinen normalen Index auf eine `TEXT`-Spalte ohne Prefix-Länge.

Korrigiert in `database/schema.sql`:

```sql
KEY idx_task_comments_body (`body`(191))
KEY idx_defect_comments_body (`body`(191))
```

Wenn der Import bereits bei `task_comments` abgebrochen ist, kann das vollständige korrigierte Schema erneut ausgeführt werden. Die vorherigen Tabellen verwenden `CREATE TABLE IF NOT EXISTS`.

## 2. Angular JS/CSS wird als text/html geladen

Ursache: Apache/STRATO hat fehlende oder falsch geroutete Angular-Asset-Dateien auf `index.html` zurückgeleitet. Browser blockieren dann Module wie `main-*.js` oder `chunk-*.js`, weil statt JavaScript HTML zurückkommt.

Korrigiert in:

- `/.htaccess`
- `/public/.htaccess`
- `/public-angular-build/browser/.htaccess`

Wichtig beim Upload:

- Den Ordner `/public` vollständig hochladen.
- Die Root-Datei `/.htaccess` mit hochladen.
- Alte Angular-Dateien auf dem Webspace vorher löschen, damit keine alten `index.html` auf nicht mehr existierende Chunk-Dateien zeigt.
- Nach Änderungen im Browser Cache leeren und den Service Worker entfernen bzw. neu laden.

## Empfohlene STRATO-Struktur

```text
/htdocs
  .htaccess
  .user.ini
  /public
  /api
  /storage
  /install
  /database
```

Nach erfolgreicher Installation löschen:

```text
/install
/database
```
