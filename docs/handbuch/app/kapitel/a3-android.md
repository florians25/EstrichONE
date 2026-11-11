# Die App für Android

Für Android-Smartphones gibt es eine erste Version von EstrichONE. Sie arbeitet mit demselben Server und denselben
Bauakten, kann aber **noch deutlich weniger** als die App für iPhone und iPad. Dieser Anhang beschreibt, was sie heute
tatsächlich kann.

## Was die Android-App kann

| Bereich | Funktionen |
|---|---|
| **Einrichtung** | Serveradresse eingeben (**Verbinden**), Anmeldung mit Anmeldename oder E-Mail und Passwort, optional mit **Einrichtungscode** (dann ist das Gerät sofort freigegeben, sonst wartet es auf die Freigabe durch die IT: „Gerät wartet auf Freigabe“, **Erneut prüfen**) |
| **Anmeldung** | Zwei-Faktor-Anmeldung mit Code aus der Authenticator-App, Wechsel eines Einmalpassworts (**Passwort ändern**, mindestens 12 Zeichen), **Abmelden**, **Anderen Server wählen** |
| **Heute** | Begrüßung und **Mein Einsatz – die nächsten 7 Tage** mit Zeit, Kolonne und Bauakte |
| **Bauakten** | Suche nach Nummer, Bauvorhaben oder Ort; **Neue Bauakte** mit Bauvorhaben und Adresse |
| **Bauakte** | Hinweise, Übersicht, **Notizen** (neue Notiz), **Aufgaben und Mängel** anlegen, Kunde **anrufen**, **Route** starten |
| **Support** | **Problem melden** an die IT (mit Kennzeichnung „Ich kann deshalb nicht weiterarbeiten“); mitgeschickt werden nur App- und Android-Version |

Die Anmeldung ist wie unter iOS an das Gerät gebunden: Der Geräteschlüssel liegt im Android Keystore (wo vorhanden im
Sicherheitschip StrongBox), die Anmeldung wird verschlüsselt gespeichert und nicht in ein Cloud-Backup übernommen.

## Was (noch) fehlt

Die Android-App arbeitet **nur mit Verbindung**. Noch nicht enthalten sind insbesondere:

- Offline-Arbeit mit Synchronisation, „Nur Ansicht“ und Wartungsbildschirm,
- Kamera, Fotos, Videos und Dokumentenscanner,
- Bautagebuch, Erstbegehung, CM-Messung, Abnahme, Räume, Basisdaten und Kontakte der Bauakte,
- Kunden, Disposition („Wer fährt wohin“), Meine Aufgaben und Meine Woche,
- Chat, Benachrichtigungen und Helpdesk-Verlauf,
- Poolgeräte mit PIN, Entsperren per Fingerabdruck sowie ein Demomodus.

Für die Arbeit auf der Baustelle empfehlen wir daher derzeit iPhone oder iPad; im Büro steht der volle Funktionsumfang
in der Weboberfläche zur Verfügung.

## Installieren und aktualisieren

Die Android-App kommt von Ihrem eigenen EstrichONE-Server, nicht aus Google Play. Zur Erstinstallation zeigt die
Administration unter **App-Updates** einen QR-Code; die heruntergeladene Datei öffnen und **Installieren** wählen.
Android fragt einmal, ob der Browser Apps installieren darf.

Updates lädt die App selbst: Bei einer neuen Version erscheint oben **Version … verfügbar**. **Aktualisieren** überträgt
zuerst alle offenen Änderungen, lädt das Update vom Server (ein abgebrochener Download wird fortgesetzt), prüft die
Prüfsumme und installiert es. Beim ersten Update fragt Android, ob EstrichONE Apps installieren darf – unter
**Einstellungen** „Aus dieser Quelle zulassen“ einschalten und zurückkehren. Daten und Anmeldung bleiben erhalten.
**Mehr** → **Info** zeigt den Stand und **Nach Updates suchen**.

Meldet die App **Update nicht möglich**, ist das Update mit einem anderen Schlüssel signiert als die installierte App
(z. B. Testversion statt Release). Dann unter **Mehr** → **Synchronisation** prüfen, dass alles übertragen ist, die App
deinstallieren, neu installieren und neu anmelden – und die IT informieren.
