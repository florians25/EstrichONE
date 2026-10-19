# HTTP-Umgebung ohne SSL

Diese Paketvariante erzwingt kein HTTPS. Sie ist für STRATO-/Testumgebungen gedacht, in denen SSL noch nicht funktioniert.

## Wichtig

Browser warnen bei Passwortfeldern auf `http://`-Seiten. Diese Warnung ist korrekt und kann nicht seriös entfernt werden.

Für Produktion sollte die Domain später auf HTTPS laufen. Bis dahin gilt:

- keine echten produktiven Zugangsdaten in HTTP-Testumgebungen verwenden
- keine Kundendaten in einer öffentlich erreichbaren HTTP-Installation testen
- nach Aktivierung von SSL wieder eine HTTPS-erzwingende `.htaccess` verwenden

## Upload

Für den Betrieb über HTTP müssen mindestens ersetzt werden:

```text
.htaccess
/public
```

Die App bleibt weiterhin loginpflichtig. Der Unterschied ist nur, dass `http://...` nicht mehr auf `https://...` weitergeleitet wird.
