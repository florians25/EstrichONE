# Die App für Android

Für Android-Smartphones und -Tablets gibt es EstrichONE als eigene App. Sie arbeitet mit demselben Server und denselben
Bauakten wie die App für iPhone und iPad, ist nach Android-Art gestaltet (Material 3) und kann auch ohne Verbindung
arbeiten. Dieser Anhang beschreibt Aufbau und Unterschiede.

## Aufbau

Unten (auf Tablets links) stehen fünf Bereiche: **Heute**, **Baustellen**, **Chat**, **Kunden** und **Mehr**. Der Chat ist
direkt erreichbar, eine Zahl zeigt ungelesene Unterhaltungen. Ohne Chat-Recht oder bei ausgeschaltetem Chat entfällt er.

**Erfassen gibt es nur in der Bauakte.** Unten rechts in jeder Bauakte steht **Erfassen**; beim Scrollen klappt der
Knopf auf das Plus-Symbol ein und verdeckt keinen Inhalt. **Erfassen** öffnet alle Erfassungen für genau diese Baustelle,
**Fotos aufnehmen** steht an erster Stelle, danach Aus Galerie, Video, Dokument scannen, Datei hochladen, Bautagebuch,
Erstbegehung, CM-Messung, Abnahme, Mangel melden, Aufgabe anlegen und Notiz schreiben. So ist immer klar, zu welcher
Baustelle ein Foto oder Eintrag gehört. Auf **Heute** stehen **Foto aufnehmen**, **Bautagebuch** und **Mangel melden**
im Menü **⋮** der Einsatzkarte; sie öffnen die Bauakte und erfassen dort, **Zurück** führt in die Bauakte.

| Bereich | Funktionen |
|---|---|
| **Einrichtung** | Serveradresse, Anmeldung mit Anmeldename oder E-Mail und Passwort, optional **Einrichtungscode**, Zwei-Faktor-Anmeldung, persönliches Gerät oder Poolgerät mit PIN |
| **Heute** | Kopfzeile mit Datum, Abgleich (Wolkensymbol) und Benachrichtigungen; Mitteilungen der Verwaltung als eine Zeile (Störungen rot, antippen klappt die Liste auf); **Jetzt im Einsatz** bzw. **Nächster Einsatz** als Hauptkarte mit **Route**, **Anrufen**, **Chat** und **Bauakte**; weitere Einsätze kompakt; **Offene Aufgaben** bzw. **Aufgaben und Freigaben**; **Morgen** mit **Meine Woche** und **Wer fährt wohin**; Zuletzt geöffnet |
| **Baustellen** | Suche nach Nummer, Bauvorhaben, Ort und Kunde, Filter **Heute**, **Laufend**, **In Ausführung**, **Geplant**, **Anfrage**, **Abgeschlossen** und **Alle** mit Anzahl; heute eingeplante Baustellen zuerst, dann nach Status und nächstem Termin; je Zeile Nummer, Status, Bauvorhaben, Ort, Kunde und nächster Termin; **Neue Bauakte** (auch offline) |
| **Bauakte** | Aufbau nach dem Arbeitsablauf (siehe unten), unten rechts **Erfassen** |
| **Chat** | Unterhaltungen mit Suche, Text, Diktat, **Standort teilen** (eigener Standort oder Baustellenadresse), Antworten, Reaktionen; Hinweis „Dateien und Fotos tauschen Sie über Microsoft Teams aus.“ mit **In Teams öffnen** |
| **Kunden** | Kundenliste mit Suche, Kundenakte mit **Anrufen**, **E-Mail**, **Route** und Bauakten |
| **Mehr** | Benachrichtigungen, Microsoft Teams, Urlaub, Tankbelege, IT-Service/Helpdesk, Problem melden, Synchronisation, Einstellungen |

Listen auf **Heute**, **Baustellen**, **Chat** und **Kunden** aktualisieren Sie, indem Sie die Seite nach unten ziehen.

**Aufbau der Bauakte.** Von oben nach unten, in der Reihenfolge der Arbeit:

1. **Kopf**: Nummer, Bauvorhaben, Status (antippen ändert ihn) und Adresse, darunter die Aktionsleiste **Route**,
   **Anrufen** (bei mehreren Kontakten mit Auswahl: Ansprechpartner vor Ort, Kunde, Bauleiter und weitere Beteiligte),
   **Chat** der Baustelle und **Teams**, wenn die Bauakte mit Microsoft Teams verknüpft ist.
2. **Heute auf der Baustelle** bzw. **Nächster Termin**: Tag und Uhrzeit, Tätigkeit, Kolonne mit Besetzung, Fahrzeuge.
3. **Hinweise und Behinderungen**, **Zugang** und **angeheftete Notizen**, bei Bedarf der Hinweis auf eine fehlende
   Erstbegehung.
4. **Leistung und Kennwerte**: Estrichart, Dicke, Fläche und Termin zuerst, dazu die Räume mit Gesamtfläche; **Alle
   Kennwerte** klappt die übrigen Basisdaten auf, **Bearbeiten** öffnet sie.
5. **Ablauf**: Erstbegehung, Estrich einbringen, Belegreife und Abnahme als Schritte mit Fortschrittsbalken. Erledigte
   Schritte tragen einen Haken, der nächste offene Schritt hat **Erfassen** (z. B. CM-Messung oder Abnahme). Ein Tipp auf
   einen Schritt öffnet den passenden Bereich.
6. **Offene Aufgaben und Mängel**, Mängel zuerst; der Kreis hakt einen Punkt ab.
7. **Fotos und Dokumentation**: neueste Fotos, darunter Fotos und Videos, Dokumente, Bautagebuch, Notizen, Protokolle,
   Erstbegehung und Räume mit Anzahl.
8. **Kontakte**: eingeklappt eine Zeile mit allen Namen, aufgeklappt je Kontakt Rolle, Telefonnummer und Anruf.
9. **Beschreibung** und **Verlauf** (neueste Einträge im Bautagebuch).

Unter **⋮** oben rechts stehen Basisdaten bearbeiten, Bauakte als PDF, Chat mit dem Team in Teams, Austragen und
Stornieren.

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
