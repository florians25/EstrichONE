# Häufige Fragen und Fehlerbehebung

## Anmeldung und Verbindung

**„Die Anmeldung wurde angenommen, aber der Browser speichert die Sitzung über die unverschlüsselte Adresse … nicht“**
Sie haben EstrichONE direkt über IP-Adresse und Port aufgerufen, z. B. `http://192.168.1.20:8080`. Browser speichern die
Sitzung dann nicht zuverlässig. Rufen Sie EstrichONE immer über die **HTTPS-Adresse** auf (z. B. die Domain im Nginx
Proxy Manager), nicht direkt über IP und Port.

**Gelber Hinweis „Unverschlüsselte Verbindung: EstrichONE wird über http://… aufgerufen“** (nur für Administratoren)
Passwörter und Daten gehen unverschlüsselt durchs Netz. Richten Sie den Zugang über die HTTPS-Adresse ein und geben Sie
den Port des Servers nur für den Proxy frei.

**Gelber Hinweis „Konfigurierte Adresse weicht ab“** (nur für Administratoren)
Der Server ist auf eine andere Domain eingestellt, als im Browser aufgerufen wird. Die Anmeldung funktioniert trotzdem,
aber Links in E-Mails, QR-Codes und Pushnachrichten zeigen auf die eingestellte Adresse. Führen Sie den Installer erneut
mit `-e ESTRICHONE_DOMAIN=<Ihre Domain>` aus bzw. passen Sie `ESTRICHONE_DOMAIN` und `ESTRICHONE_PUBLIC_URL` in der
`.env` an.

**„Der Browser hat die Sitzung nicht gespeichert. Bitte Cookies für diese Seite erlauben“**
EstrichONE braucht Cookies für die Anmeldung (keine Werbe-Cookies). Erlauben Sie Cookies für die Adresse von EstrichONE –
typische Ursachen sind der private Modus, strenger Tracking-Schutz oder Browser-Erweiterungen. Prüfen Sie außerdem
Datum und Uhrzeit des Geräts; eine falsch gehende Uhr lässt Sitzungen sofort ablaufen.

**„Anmeldename oder Passwort ist nicht korrekt.“**
Prüfen Sie Groß- und Kleinschreibung und die Feststelltaste. Nach mehreren Fehlversuchen kann das Konto vorübergehend
gesperrt sein. Die Administration setzt das Passwort in der Benutzerverwaltung zurück.

**„Das Benutzerkonto ist gesperrt.“**
Ihr Konto wurde in der Benutzerverwaltung gesperrt. Wenden Sie sich an die Administration.

**Der Bestätigungscode der Zwei-Faktor-Anmeldung wird nicht angenommen.**
Die Codes hängen von der Uhrzeit ab: Stellen Sie auf dem Smartphone die automatische Uhrzeit ein. Jeder Code gilt etwa
30 Sekunden. Ohne Handy melden Sie sich mit einem Wiederherstellungscode an.

**Unten steht „Keine Verbindung zum Server“.**
Das Netzwerk oder der Server ist kurz nicht erreichbar. EstrichONE versucht es selbstständig erneut. Bleibt der Hinweis,
prüfen Sie Ihre Internetverbindung und informieren Sie die Administration.

**Die Seite „Wartungsarbeiten“ erscheint.**
Die Administration hat den Wartungsmodus eingeschaltet, z. B. für ein Update. Die Seite prüft alle 30 Sekunden selbst und
arbeitet danach weiter. Administratoren können sich über **Als Administrator anmelden** anmelden, wenn das erlaubt ist.

## Arbeiten mit Bauakten und Dateien

**Eine Bauakte ist nicht in der Liste.**
Prüfen Sie den Reiter (abgeschlossene Akten stehen unter **Altakten**) und die Filter (**Filter zurücksetzen**). Mit
dem Recht „Eigene Bauakten sehen“ sehen Sie nur Bauakten, bei denen Sie zuständig oder Mitglied sind.

**Der Upload einer großen Datei bricht ab.**
EstrichONE lädt in 4-MB-Teilen hoch und setzt bei einem erneuten Versuch fort. Liegt ein Proxy (z. B. Nginx Proxy
Manager) davor, muss er große Anfragen zulassen (`client_max_body_size`, siehe `docs/sicherheit.md`).

**Ich finde eine Schaltfläche aus diesem Handbuch nicht.**
Seiten und Schaltflächen ohne passendes Recht sind ausgeblendet; abgeschaltete Module (Material, Chat) ebenso. Fragen
Sie die Administration nach den Rechten Ihrer Rolle.

**Pushnachrichten kommen im Browser nicht an.**
Web Push braucht HTTPS und Ihre Erlaubnis im Browser. Schalten Sie unter Benutzermenü → **Benachrichtigungen
einrichten** → **Pushnachrichten in diesem Browser** ein und senden Sie eine **Testnachricht**.

## Administration

**Wo finde ich den Einrichtungscode?**
Im Protokoll des Servers beim ersten Start: `docker compose logs api`.

**Ein Mitarbeiter hat sein Handy mit der Authenticator-App verloren.**
**IT Center** → **Benutzer** → **Zwei-Faktor-Anmeldung zurücksetzen (Handy verloren)**. Danach richtet er die App neu ein.

**Ein iPad wurde gestohlen.**
**IT Center** → **Geräte** → **Sperren**. Das Gerät kann sich nicht mehr verbinden. Ändern Sie zusätzlich die Passwörter
der Personen, die das Gerät genutzt haben.

**Die Startseite meldet „Bisher wurde keine Sicherung abgeschlossen“.**
Der Backup-Dienst läuft nicht oder ist nicht eingerichtet. Prüfen Sie **System** → **Sicherung** und starten Sie
**Jetzt sichern**. Bleibt der Fehler, sehen Sie im Protokoll des Backup-Dienstes nach (`docker compose logs backup`).
