# Die App für Android

Für Android-Smartphones und -Tablets gibt es EstrichONE als eigene App. Sie arbeitet mit demselben Server und denselben
Bauakten wie die App für iPhone und iPad, ist nach Android-Art gestaltet (Material 3) und kann auch ohne Verbindung
arbeiten. Dieser Anhang beschreibt Aufbau und Unterschiede.

## Aufbau

Unten (auf Tablets links) stehen fünf Bereiche: **Heute**, **Baustellen**, **Chat**, **Kunden** und **Mehr**. Der Chat ist
direkt erreichbar, eine Zahl zeigt ungelesene Unterhaltungen. Ohne Chat-Recht oder bei ausgeschaltetem Chat entfällt er.

**Erfassen gibt es nur in der Bauakte.** Unten in jeder Bauakte stehen **Foto** und **Erfassen**. **Erfassen** öffnet
alle Erfassungen für genau diese Baustelle: Video, Aus Galerie, Dokument scannen, Datei hochladen, Bautagebuch,
Erstbegehung, CM-Messung, Abnahme, Mangel melden, Aufgabe anlegen und Notiz schreiben. So ist immer klar, zu welcher
Baustelle ein Foto oder Eintrag gehört. Auf **Heute** öffnen **Foto** und **Tagebuch** am Einsatz die Bauakte und
erfassen dort; **Zurück** führt in die Bauakte.

| Bereich | Funktionen |
|---|---|
| **Einrichtung** | Serveradresse, Anmeldung mit Anmeldename oder E-Mail und Passwort, optional **Einrichtungscode**, Zwei-Faktor-Anmeldung, persönliches Gerät oder Poolgerät mit PIN |
| **Heute** | Einsätze von heute mit „Jetzt“ bzw. „in 2 Std.“, vier Schaltflächen **Route**, **Anrufen**, **Foto**, **Tagebuch**, Überblick (Meine Aufgaben, Wer fährt wohin, Meine Woche), Zuletzt geöffnet |
| **Baustellen** | Suche nach Nummer, Bauvorhaben, Ort und Kunde, Filter, **Neue Bauakte** (auch offline) |
| **Bauakte** | Basisdaten auf einen Blick mit Route und Anruf, **Freitexte** als Textblöcke, Basisdaten, offene Punkte, neueste Fotos, Bereiche (Notizen, Fotos, Dokumente, Aufgaben und Mängel, Bautagebuch, Erstbegehung, Protokolle, Räume, Kontakte), unten **Foto** und **Erfassen** |
| **Chat** | Unterhaltungen, Text, Diktat, **Standort teilen** (eigener Standort oder Baustellenadresse), Antworten, Reaktionen; Hinweis „Dateien und Fotos tauschen Sie über Microsoft Teams aus.“ mit **In Teams öffnen** |
| **Kunden** | Kundenliste mit Suche, Kundenakte mit Anruf, E-Mail und Bauakten |
| **Mehr** | Benachrichtigungen, Microsoft Teams, Urlaub, Tankbelege, IT-Service/Helpdesk, Problem melden, Synchronisation, Einstellungen |

**Freitexte in der Bauakte.** Hinweise und Behinderungen (farbig markiert), Beschreibung und mehrzeilige Felder wie
**Zugang / Besonderheiten** stehen als eigene Karten mit Überschrift. Zeilenumbrüche bleiben erhalten, Links,
E-Mail-Adressen und Telefonnummern lassen sich antippen, lange Texte zeigen zuerst den Anfang (**Mehr anzeigen**). Der
Stift öffnet die Basisdaten mit mehrzeiligen Feldern.

**Dateien und Fotos im Chat.** Im EstrichONE-Chat senden Sie Text und Standort. Dateien und Fotos tauschen Sie über
Microsoft Teams aus; **In Teams öffnen** führt in den Teams-Chat der Baustelle (wenn verknüpft) bzw. nach Teams. Ältere
Fotos und Dokumente im Chat bleiben sichtbar. Fotos zur Baustelle erfassen Sie in der Bauakte.

Die Anmeldung ist wie unter iOS an das Gerät gebunden: Der Geräteschlüssel liegt im Android Keystore (wo vorhanden im
Sicherheitschip StrongBox), die Anmeldung wird verschlüsselt gespeichert und nicht in ein Cloud-Backup übernommen.

## Unterschiede zur App für iPhone und iPad

- Pushnachrichten kommen noch nicht über Google: Die App fragt das Postfach im Hintergrund ab und zeigt Mitteilungen.
- Im Chat fehlen Gruppen anlegen, Weiterleiten, Anheften, Suche und der Ausgang ohne Verbindung.
- Einrichtung per QR-Code, Fotos je Prüfpunkt der Erstbegehung und der Verlauf der Bauakte fehlen noch.
- Videos nimmt die Kamera-App des Geräts auf, gescannte Seiten werden nicht automatisch zugeschnitten.

## Installieren und aktualisieren

Die Android-App kommt von Ihrem eigenen EstrichONE-Server, nicht aus Google Play. Zur Erstinstallation zeigt die
Administration unter **App-Updates** einen QR-Code; die heruntergeladene Datei öffnen und **Installieren** wählen.
Android fragt einmal, ob der Browser Apps installieren darf.

Updates lädt die App selbst: Bei einer neuen Version erscheint oben **Version … verfügbar**. **Aktualisieren** überträgt
zuerst alle offenen Änderungen, lädt das Update vom Server (ein abgebrochener Download wird fortgesetzt), prüft die
Prüfsumme und installiert es. Beim ersten Update fragt Android, ob EstrichONE Apps installieren darf, unter
**Einstellungen** „Aus dieser Quelle zulassen“ einschalten und zurückkehren. Daten und Anmeldung bleiben erhalten.
**Mehr** → **Info** zeigt den Stand und **Nach Updates suchen**.

Meldet die App **Update nicht möglich**, ist das Update mit einem anderen Schlüssel signiert als die installierte App
(z. B. Testversion statt Release). Dann unter **Mehr** → **Synchronisation** prüfen, dass alles übertragen ist, die App
deinstallieren, neu installieren und neu anmelden, und die IT informieren.
