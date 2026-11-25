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
| **Heute** | Kopfzeile mit Datum, Abgleich (Wolkensymbol) und Benachrichtigungen; Mitteilungen der Verwaltung als eine Zeile (Störungen rot, antippen klappt die Liste auf); für Planer **Disposition heute**; **Jetzt im Einsatz** bzw. **Nächster Einsatz** als Hauptkarte mit **Route**, **Anrufen**, **Chat** und **Bauakte**; weitere Einsätze kompakt; **Offene Aufgaben** bzw. **Aufgaben und Freigaben**; **Morgen** mit **Meine Woche** und (ohne Recht zur Planung) **Wer fährt wohin**; Zuletzt geöffnet |
| **Baustellen** | Suche nach Nummer, Bauvorhaben, Ort und Kunde, Filter **Heute**, **Laufend**, **In Ausführung**, **Geplant**, **Anfrage**, **Abgeschlossen** und **Alle** mit Anzahl; heute eingeplante Baustellen zuerst, dann nach Status und nächstem Termin; je Zeile Nummer, Status, Bauvorhaben, Ort, Kunde und nächster Termin; **Neue Bauakte** (auch offline) |
| **Bauakte** | Aufbau nach dem Arbeitsablauf (siehe unten), unten rechts **Erfassen** |
| **Chat** | Unterhaltungen mit Suche, Text, Diktat, **Standort teilen** (eigener Standort oder Baustellenadresse), Antworten, Reaktionen; Hinweis „Dateien und Fotos tauschen Sie über Microsoft Teams aus.“ mit **In Teams öffnen** |
| **Kunden** | Kundenliste mit Suche, Kundenakte mit **Anrufen**, **E-Mail**, **Route** und Bauakten |
| **Mehr** | Benachrichtigungen, Microsoft Teams, **Planung** (**Disposition** bzw. **Wer fährt wohin**, **Meine Woche**), Urlaub, Tankbelege, IT-Service/Helpdesk, Problem melden, Synchronisation, Einstellungen |
| **Disposition** | Für Planer wie auf dem iPhone: Tagesleiste mit Kalender, Freigabestand, Kolonnen mit Einsätzen in Fahrtreihenfolge, Menü **⋮** je Einsatz, **Anpassen** je Kolonne (Baustelle zuweisen, Reihenfolge ändern, Mitarbeiter, Fahrzeug oder Kolonnen tauschen), **Freigabe**; ohne Verbindung vorgemerkt (Kapitel 6) |

Listen auf **Heute**, **Baustellen**, **Chat** und **Kunden** aktualisieren Sie, indem Sie die Seite nach unten ziehen.

**Aufbau der Bauakte.** Von oben nach unten:

1. **Kopf**: Nummer, Bauvorhaben, Status (antippen ändert ihn) und Adresse, darunter die Aktionsleiste **Route**,
   **Anrufen** (bei mehreren Kontakten mit Auswahl: Ansprechpartner vor Ort, Kunde, Bauleiter und weitere Beteiligte),
   **Chat** der Baustelle, für Planer **Einplanen** und **Teams**, wenn die Bauakte mit Microsoft Teams verknüpft ist.
2. **Heute auf der Baustelle** bzw. **Nächster Termin**: Tag und Uhrzeit, Tätigkeit, Kolonne mit Besetzung, Fahrzeuge.
   Planer sehen den nächsten Einsatz jeder Kolonne und darunter **Einplanen** und **Verschieben**.
3. **Leistung und Kennwerte**: Estrichart, Dicke, Fläche und Termin zuerst, dazu die Räume mit Gesamtfläche; **Alle
   Kennwerte** klappt die übrigen Basisdaten auf, **Bearbeiten** öffnet sie.
4. **Beschreibung und Hinweise** als zusammenhängender Block: Beschreibung, **Hinweise und Behinderungen**, **Zugang /
   Besonderheiten** und **angeheftete Notizen**, bei Bedarf der Hinweis auf eine fehlende Erstbegehung.
5. **Offene Aufgaben und Mängel**, Mängel zuerst; der Kreis hakt einen Punkt ab.
6. **Fotos und Dokumentation**: neueste Fotos, darunter Fotos und Videos, Dokumente, Bautagebuch, Notizen, Protokolle,
   Erstbegehung und Räume mit Anzahl.
7. **Kontakte**: eingeklappt eine Zeile mit allen Namen, aufgeklappt je Kontakt Rolle, Telefonnummer und Anruf.
8. **Verlauf** (neueste Einträge im Bautagebuch).

Einen eigenen Abschnitt zum Ablauf gibt es nicht mehr. Erstbegehung, CM-Messung und Abnahme erfassen Sie über
**Erfassen** unten rechts oder unter **Fotos und Dokumentation**.

Ganz oben rechts steht neben **⋮** ein kleines Symbol zur Verfügbarkeit ohne Netz: grün bedeutet, die Bauakte liegt
vollständig auf dem Gerät; sonst werden Dokumente noch geladen oder es liegen nur die Daten vor. Antippen zeigt die
Einzelheiten. Unter **⋮** stehen für Planer Einplanen und Einsatz verschieben, außerdem Basisdaten bearbeiten, Bauakte
als PDF, Chat mit dem Team in Teams, Austragen und Stornieren.

**Einplanen vom Handy.** Planer (Recht zur Planung) planen eine Baustelle direkt ein: in der Bauakte über **Einplanen**
oder in der Liste **Baustellen** durch langes Drücken auf die Zeile. Das Blatt fragt nur das Nötige ab: **Tag** (Heute,
Morgen, Übermorgen oder Datum), **Kolonne** mit der Auslastung des Tages (zum Beispiel „2 Einsätze“), **Tätigkeit** (nach
dem Stand der Bauakte vorgeschlagen) und auf Wunsch die **Uhrzeit**. Konflikte prüft die App vor dem Speichern, sie
verhindern das Einplanen nicht. Nach **Einplanen** bestätigt die App den Tag und **Nächster Termin** zeigt den Einsatz.
**Verschieben** setzt den nächsten Einsatz auf einen anderen Tag, eine andere Kolonne oder eine andere Uhrzeit. Ohne
Verbindung gilt der zuletzt geladene Tagesplan und die Änderung wird vorgemerkt; liegt für den Tag kein Plan auf dem
Gerät, sagt das Blatt das. **Baustelle zuweisen** in der Disposition nutzt dasselbe Blatt, Kolonne und Tag sind dort
vorbelegt.

**Freitexte in der Bauakte.** Hinweise und Behinderungen (farbig markiert), Beschreibung und mehrzeilige Felder wie
**Zugang / Besonderheiten** stehen als eigene Karten mit Überschrift. Zeilenumbrüche bleiben erhalten, Links,
E-Mail-Adressen und Telefonnummern lassen sich antippen, lange Texte zeigen zuerst den Anfang (**Mehr anzeigen**). Der
Stift öffnet die Basisdaten mit mehrzeiligen Feldern.

**Dateien und Fotos im Chat.** Im EstrichONE-Chat senden Sie Text und Standort. Dateien und Fotos tauschen Sie über
Microsoft Teams aus; **In Teams öffnen** führt in den Teams-Chat der Baustelle (wenn verknüpft) bzw. nach Teams. Ältere
Fotos und Dokumente im Chat bleiben sichtbar. Fotos zur Baustelle erfassen Sie in der Bauakte.

Die Anmeldung ist wie unter iOS an das Gerät gebunden: Der Geräteschlüssel liegt im Android Keystore (wo vorhanden im
Sicherheitschip StrongBox), die Anmeldung wird verschlüsselt gespeichert und nicht in ein Cloud-Backup übernommen.

## Auf der Baustelle und beim Kunden

**Ohne Netz arbeiten.** Die Bauakten Ihrer Einsätze von heute und der nächsten sechs Tage lädt die App vorab vollständig:
Daten, Kontakte, Hinweise und Protokolle kommen mit dem Abgleich, dazu die Dokumente (Pläne, Aufträge, bis 25 MB je Datei)
und die Vorschaubilder der neuesten Fotos. Das Symbol oben rechts in der Bauakte zeigt den Stand, Antippen nennt
die Zahl der geladenen Dokumente. Was zu keinem kommenden Einsatz mehr gehört, entfernt die App wieder. Eigene Aufnahmen sind davon nie betroffen.

**Übertragung.** Alles Erfasste liegt zuerst verschlüsselt auf dem Gerät. Wartet etwas, zeigen **Heute** und die Bauakte
eine ruhige Zeile, zum Beispiel „3 Fotos warten auf Übertragung“. Die App sendet automatisch: nach jeder Änderung, alle
zwei Minuten und im Hintergrund, auch wenn sie geschlossen wurde. Lehnt der Server eine Datei ab, wird die Zeile rot und
Android zeigt die Mitteilung **Übertragung prüfen**. Unter **Synchronisation** stehen die Dateien einzeln mit dem Grund,
**Erneut senden** wiederholt den Versuch.

**Serienfotos.** Die Kamera bleibt für mehrere Aufnahmen offen. Oben wählen Sie die Art (Vorher, Einbau, Nachher, Mangel,
Detail) und den Raum oder Bereich, vorgeschlagen werden die Räume der Bauakte. Die Wahl gilt für alle folgenden Fotos und
steht in ihrer Beschriftung. **Standort an** speichert die Position mit, die Uhrzeit wird immer gespeichert. Fotos werden
für das Mobilfunknetz verkleinert (längste Kante 2560 Pixel). Ausgelöst wird auch mit der Lautstärketaste.

**Schnellzugriffe.** Langes Drücken auf das App-Symbol bietet **Foto**, **Tagebuch** und **Mangel**. Die App öffnet
dafür die Bauakte des laufenden bzw. nächsten Einsatzes von heute. Ohne Einsatz wählen Sie die Baustelle. Langes Drücken
auf einen Einsatz oder eine Baustelle in den Listen bietet Route, Foto, Bautagebuch und Mangel.

**Angekommen und Fertig.** Am Einsatz von heute merkt **Angekommen** die Uhrzeit, **Fertig** ebenso und öffnet das
Bautagebuch mit Beginn, Ende und einem Textvorschlag. Die Uhrzeiten bleiben auf dem Gerät, bis Sie den Eintrag speichern.

**Entwürfe.** Bautagebuch, Mangel, Aufgabe, Notiz, CM-Messung, Erstbegehung und Abnahme sichern Ihre Eingaben laufend.
Nach einem Abbruch, einem Anruf oder einem Neustart der App steht beim Öffnen **Entwurf wiederhergestellt**,
**Verwerfen** beginnt neu. Während der Erfassung bleibt der Bildschirm an. Diktieren können Sie in allen Textfeldern
mit dem Mikrofon.

**Abnahme mit dem Kunden.** Die Abnahme führt in drei Schritten: Leistung, Ergebnis, Vorbehalte und Fotos, dann die
Unterschriften, zuletzt die Zusammenfassung mit dem, was noch fehlt. **Kunde unterschreibt** öffnet die Kundenansicht:
Der Kunde sieht nur das Protokoll, trägt seinen Namen ein und unterschreibt mit dem Finger. Nach dem Abschluss erstellt
der Server das Protokoll als PDF. In der Abnahme stehen dann **Protokoll ansehen**, **Teilen** und **An Kunden** (E-Mail
mit dem PDF im Anhang). Fehlt die Unterschrift des Kunden, schickt **Freigabelink an den Kunden senden** ihm einen
befristeten Link. Die Erstbegehung zeigt vor dem Abschluss ebenfalls eine Zusammenfassung und verlangt, dass jeder
Prüfpunkt bewertet ist, das Protokoll gibt es danach als PDF zum Ansehen und Teilen.

**Kundenansicht für Fotos.** In den Fotos der Bauakte öffnet das Symbol **Kundenansicht** die Bilder im Vollbild zum
Durchblättern, ohne Navigation, Beschriftungen und Aktionen. So zeigen Sie dem Kunden das Gerät, ohne dass Internes zu
sehen ist.

## Unterschiede zur App für iPhone und iPad

- Pushnachrichten kommen noch nicht über Google: Die App fragt das Postfach im Hintergrund ab und zeigt Mitteilungen.
- Im Chat fehlen Gruppen anlegen, Weiterleiten, Anheften, Suche und der Ausgang ohne Verbindung.
- Einrichtung per QR-Code, Fotos je Prüfpunkt der Erstbegehung und der Verlauf der Bauakte fehlen noch.
- Ankunft und Abschluss eines Einsatzes werden nicht an den Server gemeldet, sondern für das Bautagebuch vorgeschlagen.
- Listenzeilen bieten ihre Aktionen über langes Drücken, nicht über Wischen.
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
