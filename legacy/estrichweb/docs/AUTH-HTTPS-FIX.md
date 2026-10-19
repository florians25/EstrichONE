# Auth-/HTTPS-Hotfix

Dieser Stand behebt zwei konkrete Produktivprobleme:

1. Alte oder ungültige Browser-Tokens werden beim Deployment automatisch verworfen.
   Dadurch werden geschützte API-Aufrufe wie `/api/dashboard` oder `/api/module/projects`
   nicht mehr mit einem ungültigen Token gestartet.

2. Der HTTP-Interceptor bricht API-Aufrufe ohne gültige Sitzung clientseitig ab und leitet
   auf `/login` weiter. 401-Antworten außerhalb des Login-Endpunkts werden nicht mehr als
   ungefangene Angular-Fehler in die Konsole geworfen.

3. Die Root-`.htaccess` erzwingt HTTPS. Passwortfelder und JWT-Login dürfen nicht über
   `http://` verwendet werden. Auf STRATO muss für die Domain ein SSL-Zertifikat aktiv sein.

Nach Upload bitte Service Worker und Cache Storage löschen und die Seite über `https://` öffnen.
