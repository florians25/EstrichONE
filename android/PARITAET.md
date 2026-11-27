# Parität iOS ↔ Android

Stand: Android 1.0.x gegenüber der iOS-App (ios/EstrichONE, ios/Packages/EstrichCore). Gleiche Funktionen und Abläufe,
Gestaltung nach Material 3 (docs/reengineering/08).

Legende: ✅ umgesetzt · 🟡 teilweise (siehe Bemerkung) · ❌ fehlt noch

## Grundlagen

| Bereich | iOS | Android | Bemerkung |
|---|---|---|---|
| Ersteinrichtung: Server, Anmeldung, Einrichtungscode, Freigabe abwarten | ✅ | ✅ | |
| Zwei-Faktor-Anmeldung, Einmalpasswort wechseln | ✅ | ✅ | |
| Gerätebindung mit Geräteschlüssel (ECDSA P-256), Erneuern mit Signatur | ✅ Secure Enclave | ✅ Android Keystore (StrongBox, wenn vorhanden) | |
| Einrichtung per QR-Code / Einrichtungslink | ✅ | ❌ | Code wird von Hand eingegeben; QR-Scan bräuchte ML Kit (Google-Dienste) oder ZXing |
| Persönliches Gerät oder Poolgerät wählen | ✅ | ✅ | |
| Poolgerät: Benutzerwahl, PIN (6 Ziffern, keine trivialen), 5 Fehlversuche, Sperre nach Inaktivität | ✅ | ✅ | PIN-Tresor PBKDF2-SHA256 + AES-GCM wie ADR 0012 |
| App-Sperre persönliches Gerät (Face ID/Touch ID ↔ BiometricPrompt Klasse 3 / Gerätecode) | ✅ | ✅ | Zeitspanne wählbar, Bildschirminhalt in der App-Übersicht verborgen |
| Sitzungsende durch den Server (Gerät gesperrt/entfernt) | ✅ | ✅ | |
| Abmelden, Gerät zurücksetzen | ✅ | ✅ | |
| Passwort ändern | ✅ | ✅ | |
| Demomodus mit Beispieldaten (Server im App-Prozess) | ✅ | ✅ | gleiche Beispieldaten; Störungen vorführen (Backup, Archiv, Wartung) |
| Offline-Datenbank, verschlüsselt | ✅ SQLite + eigene Verschlüsselung | ✅ SQLCipher, Schlüssel per Keystore umhüllt | |
| Synchronisation (Outbox, feldweises Zusammenführen, Upload in Teilen, Pull, Snapshot, RESYNC) | ✅ | ✅ | gleicher Ablauf (docs/sync-protokoll.md), getestet gegen echten Server |
| Abgleich alle 2 Minuten, nach Änderungen, im Hintergrund | ✅ | ✅ | Hintergrund: WorkManager alle 15 Minuten mit Netz |
| Fotos verschlüsselt auf dem Gerät, 7 Tage nach Upload aufbewahrt | ✅ | ✅ | |
| Synchronisationsseite (Warteschlange, abgelehnte Änderungen) | ✅ | ✅ | |
| „Nur Ansicht“ nach längerem Serverausfall | ✅ | 🟡 | Android zeigt „Offline“ und speichert lokal weiter, ohne eigenen Nur-Ansicht-Modus |
| Wartung (503 MAINTENANCE) | ✅ | ✅ | Hinweis, Abgleich pausiert |
| Push-Benachrichtigungen | ✅ APNs | 🟡 | Server hat noch kein FCM („Android-Push ist noch nicht eingerichtet“): Postfach wird im Hintergrund abgefragt und als Mitteilung gezeigt; Antippen öffnet das Ziel (Bauakte mit Aufgaben bzw. Dokumenten, Chat, Urlaub, Anträge) |

## Heute

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Kompakte Kopfzeile: Datum als Unterzeile, Abgleich als Symbol, Glocke mit Zahl, „Demo“ klein rechts | ✅ | ✅ | ab 1.0.34 ohne großen Titel und Leerraum |
| Mein Tag: drei Kennzahlen (Einsätze ab Uhrzeit, offene Aufgaben mit Mängeln, Freigaben bzw. weitere Einsätze) | ✅ | ✅ | `TodayOverview` (unit-getestet) |
| Mitteilungen (Störung, Wartung, Neuigkeiten) als eine Zeile, aufklappbar; Störungen rot, Ausblenden bleibt | ✅ | ✅ | `TodayOverview.noticeSummary` |
| Ausfall-Hinweise Backup/Archiv nur, wenn der Server sie liefert | ✅ | ✅ | |
| Jetzt im Einsatz bzw. Nächster Einsatz als Hauptkarte (Zeit, Status, Adresse, Leistung mit Kennwerten, Kolonne, Fahrzeuge, Hinweise und Zugang) | ✅ | ✅ | Plan offline aus dem letzten Abruf |
| Schnellaktionen der Hauptkarte: Route, Anrufen, Chat, Bauakte; Foto, Tagebuch, Mangel im Menü „⋮“ | ✅ | ✅ | |
| Weitere Einsätze heute als kompakte Zeilen mit Route | ✅ | ✅ | |
| Offene Aufgaben (drei, mit Bauakte) bzw. Aufgaben und Freigaben | ✅ | ✅ | |
| Morgen bzw. Demnächst mit Meine Woche und Wer fährt wohin, Zuletzt geöffnet | ✅ | ✅ | Android: langes Drücken auf Einsatz- und Baustellenzeilen für Route, Foto, Tagebuch, Mangel; Wischen nur iOS |
| Laden: graue Platzhalterzeilen statt Drehkreisel (Listen, Bauakte) | ✅ | ✅ | |
| „Foto“ und „Tagebuch“ im Einsatz öffnen die Bauakte und erfassen dort (Zurück führt in die Bauakte) | ✅ | ✅ | ab 1.0.32 |

## Baustelle und Kunde vor Ort (ab 1.0.37)

| Funktion | Android | Bemerkung |
|---|---|---|
| Bauakten der Einsätze von heute bis in 6 Tagen vorab laden (Dokumente bis 25 MB, Vorschaubilder der Fotos), Stand als Symbol oben in der Bauakte (verfügbar, Dokumente werden geladen, nur Daten), Antippen zeigt die Einzelheiten | ✅ | `OfflinePack` im Kern; höchstens 400 MB, Überholtes wird entfernt |
| Übertragungsstand in Worten („3 Fotos warten auf Übertragung“) auf Heute und in der Bauakte | ✅ | `TransferStatus` im Kern |
| Fehlgeschlagene Uploads einzeln mit Grund und „Erneut senden“, Mitteilung „Übertragung prüfen“ | ✅ | |
| Upload im Hintergrund auch nach Beenden der App | ✅ | WorkManager stellt die Sitzung wieder her (nicht auf Poolgeräten: dort braucht es die PIN) |
| Entwürfe für Tagebuch, Mangel, Aufgabe, Notiz, CM-Messung, Erstbegehung, Abnahme | ✅ | `Drafts` im Kern, in der verschlüsselten Datenbank; Unterschriften nur bis zum Neustart |
| Bildschirm bleibt während Erfassung, Kamera und Kundenansicht an | ✅ | |
| App-Shortcuts Foto, Tagebuch, Mangel zur Bauakte des laufenden Einsatzes | ✅ | `QuickShortcut` im Kern |
| Angekommen und Fertig am Einsatz | 🟡 | Uhrzeiten auf dem Gerät, Vorschlag für das Bautagebuch; der Server hat kein Feld dafür |
| Kundenansicht: Fotos im Vollbild, Protokoll mit Unterschrift ohne interne Angaben | ✅ | |

## Mehr

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Konto und Synchronisation, Kommunikation (Benachrichtigungen, Chat, Teams), Service (Urlaub, IT-Service/Helpdesk, Problem melden) | ✅ | ✅ | Meine Aufgaben, Wer fährt wohin, Meine Woche nur noch auf „Heute“ |
| Selten Gebrauchtes gebündelt | ✅ „Gerät, Server und App“ (Gerät, Server, App-Update, Konto löschen, App zurücksetzen) | ✅ „Einstellungen“ (Sicherheit, Mitteilungen, Darstellung, Info) | |
| Demo beenden | ✅ | ✅ | nur oben links unter „Demo“ |

## Baustellen (Bauakten)

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Liste mit Suche, Filter (Heute, Laufend, Status, Alle) mit Anzahl, „Heute im Einsatz“ zuerst, dann Status und nächster Termin | ✅ | ✅ | `SiteList` (unit-getestet); kompakte Zeilen mit Status-Kennzeichen und nächstem Termin; Suche auch nach Kunde und Kundennummer, Ziehen zum Abgleichen |
| Schnellanlage mit Art, Adresse, Kunde (vorhanden oder neu) – offline | ✅ | ✅ | |
| Dublettenprüfung beim neuen Kunden | ✅ | 🟡 | Hinweis auf ähnliche Kunden aus dem Bestand auf dem Gerät (offline); Serverprüfung `contacts/duplicates` nicht genutzt |
| Aufbau der Bauakte: Kopf mit Aktionsleiste (Route, Anrufen mit Kontaktauswahl, Chat, Einplanen für Planer, Teams), heute bzw. nächster Termin, Leistung und Kennwerte, Beschreibung und Hinweise (Beschreibung, Hinweise und Zugang, angeheftete Notiz), Aufgaben und Mängel, Fotos und Dokumentation, Kontakte (einklappbar), Verlauf | ✅ | ✅ | ab 1.0.38; der Abschnitt „Ablauf“ ist entfallen (`SiteWorkflow` entfernt), Erfassen über „Erfassen“ und „Fotos und Dokumentation“ |
| Einplanen vom Handy für Planer: in der Bauakte (Aktionsleiste, Menü, „Nächster Termin“) und im Langdruck-Menü der Baustellenliste; kompaktes Blatt mit Tag (Heute, Morgen, Übermorgen, Datum), Kolonne mit Auslastung, Tätigkeit, Uhrzeit, Konfliktprüfung; „Verschieben“ des nächsten Einsatzes | ✅ | ✅ | Android `QuickPlan` in core/DayPlan.kt (unit-getestet), Blatt `QuickPlanSheet` in ui/Dispatch.kt, auch für „Baustelle zuweisen“; gleiche Endpunkte (`POST /planning/batch`), offline vorgemerkt |
| „Erfassen“ unten rechts (klappt beim Scrollen ein, verdeckt nichts) | ✅ | ✅ | Blatt mit Fotos zuerst, dann Video, Scan, Datei, Tagebuch, Erstbegehung, CM-Messung, Abnahme, Mangel, Aufgabe, Notiz für diese Bauakte |
| Menü „…“/„⋮“: Basisdaten, PDF, Teams-Chat, Austragen, Stornieren | ✅ | ✅ | Chat und Teams stehen in der Aktionsleiste |
| Freitexte (Hinweise und Zugang als ein Block, Beschreibung, mehrzeilige Schema-Felder) als Textblöcke mit Überschrift, Links/Telefon antippbar, „Mehr anzeigen“ | ✅ | ✅ | `FreeText` im Kern, unit-getestet |
| Basisdaten: vier Kennwerte direkt (zuerst Estrichart, Dicke, Fläche, Termin), „Alle Basisdaten“ klappt auf | ✅ | ✅ | unit-getestet |
| Angeheftete Notiz, Hinweise nur bei Bedarf; Offene Punkte und neueste Fotos nur, wenn vorhanden | ✅ | ✅ | |
| Basisdaten nach Formular-Schema bearbeiten | ✅ | ✅ | |
| Notizen (anheften, löschen) | ✅ | ✅ | |
| Fotos und Videos (Raster nach Tagen, Großansicht, Beschriftung, Teilen, Löschen) | ✅ | ✅ | |
| Dokumente nach Art, öffnen, scannen, hochladen | ✅ | ✅ | |
| Aufgaben und Mängel (Fälligkeit, abhaken) | ✅ | ✅ | |
| Bautagebuch (Wetter, Temperatur, Personen, Zeiten, Text, Diktat) | ✅ | ✅ | Fotos des Tages werden angehängt |
| Erstbegehung nach Schema (Prüfpunkte, Bemerkungen, Messwerte) | ✅ | ✅ | Zusammenfassung und Pflichtprüfung vor dem Abschluss, Fotos des Tages am Protokoll, PDF ansehen und teilen; Fotos je Prüfpunkt fehlen noch |
| Erstbegehung nicht erforderlich (Hinweiskarte, Protokolle, Rückgängig, offline) | ✅ | ✅ | |
| CM-Messung mit Belegreife | ✅ | ✅ | |
| Abnahme mit Unterschriften Kunde/Auftragnehmer | ✅ | ✅ | geführt in drei Schritten mit Zusammenfassung; Kunde unterschreibt in der Kundenansicht; PDF ansehen, teilen, per E-Mail senden; Freigabelink an den Kunden (`acceptances/{id}/customer-link`) |
| Räume und Flächen (Summen, Stand, bearbeiten) | ✅ | ✅ | |
| Kontakte und Beteiligte | ✅ | ✅ | |
| Bauakte als PDF | ✅ | ✅ | nur mit Verbindung |
| Stornieren mit Begründung | ✅ | ✅ | |
| Verlauf (Historie) | 🟡 | ❌ | |
| Alle Bauakten sehen (online), „Nur ansehen“, „Mich als Beteiligten eintragen“/„Austragen“ | ✅ | ✅ | nicht zugeteilte nur online als Kopf |

## Erfassen

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Erfassen-Blatt der Bauakte (kein eigener Reiter mehr) | ✅ | ✅ | ab 1.0.32; Baustellenwahl nur noch intern |
| Kamera mit Serienaufnahme | ✅ | ✅ CameraX | Lautstärketaste löst aus |
| Galerie | ✅ | ✅ Photo Picker | ohne Speicherberechtigung |
| Video | ✅ eigene Ansicht | 🟡 | Kamera-App des Geräts |
| Dokument scannen zu PDF | ✅ mit Kantenerkennung | 🟡 | Seiten fotografieren → PDF, ohne automatisches Zuschneiden |
| Standort an Fotos | ✅ | ✅ | auf Wunsch (Kapsel „Standort“ in der Kamera), ohne Google-Dienste |
| Serienfotos mit Art (Vorher, Einbau, Nachher, Mangel, Detail) und Raum bzw. Bereich, für die Serie gemerkt | siehe iOS | ✅ | `PhotoSeries` im Kern, steht in der Beschriftung |

## Kunden

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Verzeichnis mit Suche | ✅ | ✅ | |
| Kundenkarte (Nummern, E-Mail, Anschrift, Notiz, Bauakten) | ✅ | ✅ | |
| Kunde bearbeiten | ✅ | 🟡 | Name, Anschrift, Webseite, Notiz; Telefonnummern/E-Mails noch nicht |

## Planung

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Meine Woche | ✅ | ✅ | |
| Wer fährt wohin (lesend, Kolonnen mit Einsätzen in Fahrtreihenfolge, Freigabestand) | ✅ | ✅ | beide `GET /planning/day/{day}`; offline der zuletzt geladene Stand |
| Disposition für Planer: zuweisen mit Suche und Tätigkeit, verschieben (Morgen, Übermorgen, +1 Woche, Datum, Kolonne), Uhrzeit, nächster freier Tag, aus dem Plan nehmen, Reihenfolge ziehen, Mitarbeiter/Fahrzeug/Kolonnen tauschen, Freigabe, Konfliktprüfung, offline vormerken | ✅ | ✅ | iOS CrewPlanView/PlanningSheets, Android ui/Dispatch.kt mit Logik in core/DayPlan.kt; Rechte vom Server (`canPlan`, Recht `planning.manage`) |
| Einstieg Disposition: „Disposition heute“ auf Heute unter den Mitteilungen (Planer), Abschnitt „Planung“ in Mehr | ✅ | ✅ | ab 1.0.36 |
| Disposition auf dem Tablet als Kolonnenraster | ✅ | ❌ | Android zeigt auch auf dem Tablet die Liste (lesbar begrenzt) |
| Urlaub: Konto, Anträge, Vorschau mit Feiertagen, Genehmigen | ✅ | ✅ | |
| Urlaub: Vertretung wählen, Änderungsantrag zu genehmigtem Urlaub | ✅ | ✅ | Demomodus rechnet Feiertage (Bayern) heraus |
| Tankbelege: Beleg fotografieren, Fahrzeug vorbelegt (Dispo), offline über die Outbox, eigene Belege mit Status | ✅ | ✅ | iOS Dokumentkamera, Android Kamera-App bzw. Galerie |
| Tankbelege: Betrag, Liter, Datum aus dem Foto vorschlagen (Texterkennung auf dem Gerät) | ✅ Vision | ❌ | bräuchte Google ML Kit (neue, schwere Abhängigkeit) |

## Kommunikation

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Chat: Unterhaltungen, Verlauf, Senden, Antworten, Reaktionen, Bearbeiten, Löschen | ✅ | ✅ | |
| Chat: eigener Reiter unten mit Ungelesen-Zahl | ✅ | ✅ | ab 1.0.32 |
| Chat: keine Dateien/Fotos mehr senden, Hinweis „Dateien und Fotos tauschen Sie über Microsoft Teams aus.“ über dem Eingabefeld (mit „In Teams öffnen“, wenn Teams im Einsatz) | ✅ | ✅ | `ChatComposerPolicy`; Ziel aus `teamsWebUrl` der Unterhaltung (Server ab 1.0.32) |
| Chat: Standort senden (eigener Standort nach Freigabe, Baustellenadresse) | ✅ | ✅ | ab 1.0.32 |
| Chat: Fotos und Dokumente älterer Nachrichten ansehen/öffnen, Standort ansehen | ✅ | ✅ | |
| Chat: Diktat | ✅ | ✅ Android-Spracheingabe | |
| Microsoft Teams: „In Teams öffnen“ (Bauakte und Heute im Menü, Mehr), Chat-Archiv im Modus „Microsoft Teams“ | ✅ | ✅ | Teams-App per `msteams:`, sonst Browser |
| Chat: Einzelchat starten, Info (Mitglieder, Stummschalten) | ✅ | ✅ | |
| Chat: Gruppen anlegen, Weiterleiten, Anheften, Suche, Medienübersicht | ✅ | ❌ | API im Kern teils vorhanden |
| Chat: Echtzeit | ✅ SSE | 🟡 | Abfrage alle 5 s im Verlauf, 15 s in der Liste |
| Chat: Ausgang offline (Nachrichten später senden) | ✅ | ❌ | ohne Netz schlägt das Senden sichtbar fehl |
| Postfach (Benachrichtigungen, gelesen, alle gelesen, Ziel öffnen) | ✅ | ✅ | |
| Mitteilungseinstellungen je Ereignis | ✅ | ✅ | |
| Meldungen an die IT (melden, Liste, Verlauf, antworten, wieder öffnen) | ✅ | ✅ | Anhänge (Bildschirmfotos) fehlen |

## Gestaltung und Bedienung

| Thema | Android |
|---|---|
| Navigation | NavigationSuiteScaffold mit Heute, Baustellen, Chat, Kunden, Mehr: Leiste auf Telefonen, Rail auf Tablets; Liste + Detail nebeneinander ab 840 dp |
| Hell/dunkel | beide, wählbar (System/Hell/Dunkel); optional Systemfarben (Material You) nur für Flächen |
| Große Schrift | folgt der Systemschriftgröße |
| Zurück | Predictive Back (enableOnBackInvokedCallback), eigener Stapel je Reiter |

## Tests

- `android/core`: JUnit 5 – Synchronisation gegen den Demoserver (Erstabgleich, offline anlegen, Upload, Zusammenführen,
  Ablehnung, Sichtbarkeit), Systemhinweise (wie iOS), PIN-Tresor, Benachrichtigungsziele; gegen den echten Server
  (`scripts/test-core-against-server.sh`, CI „Kern (Kotlin) gegen echten Server“) inklusive Offline-Sync mit Foto-Upload
  und zweitem Gerät.
- `android/app`: Roborazzi-Screenshots (Robolectric, ohne Emulator) Telefon und Tablet, hell und dunkel.

## Offen (Reihenfolge nach Nutzen auf der Baustelle)

1. Chat-Ausgang offline und Echtzeit (SSE), Gruppen anlegen
2. Fotos an Prüfpunkten
3. Ankunft und Abschluss am Einsatz an den Server melden (braucht ein Feld bzw. einen Endpunkt in der Planung)
4. QR-Code bei der Einrichtung
5. FCM, sobald der Server Android-Push anbietet
