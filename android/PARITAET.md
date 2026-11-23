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
| Jetzt im Einsatz bzw. Nächster Einsatz als Hauptkarte (Zeit, Status, Adresse, Leistung mit Kennwerten, Kolonne, Fahrzeuge, Zugang) | ✅ | ✅ | Plan offline aus dem letzten Abruf |
| Schnellaktionen der Hauptkarte: Route, Anrufen, Chat, Bauakte; Foto, Tagebuch, Mangel im Menü „⋮“ | ✅ | ✅ | |
| Weitere Einsätze heute als kompakte Zeilen mit Route | ✅ | ✅ | |
| Offene Aufgaben (drei, mit Bauakte) bzw. Aufgaben und Freigaben | ✅ | ✅ | |
| Morgen bzw. Demnächst mit Meine Woche und Wer fährt wohin, Zuletzt geöffnet | ✅ | ✅ | iOS zusätzlich Wischen/langes Drücken für Foto, Tagebuch, Route |
| Laden: graue Platzhalterzeilen statt Drehkreisel (Listen, Bauakte) | ✅ | ✅ | |
| „Foto“ und „Tagebuch“ im Einsatz öffnen die Bauakte und erfassen dort (Zurück führt in die Bauakte) | ✅ | ✅ | ab 1.0.32 |

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
| Aufbau nach dem Arbeitsablauf: Kopf mit Aktionsleiste (Route, Anrufen mit Kontaktauswahl, Chat, Teams), heute bzw. nächster Termin, Hinweise und Behinderungen, Leistung und Kennwerte, Ablauf, Aufgaben und Mängel, Fotos und Dokumentation, Kontakte (einklappbar), Beschreibung und Verlauf | ✅ | ✅ | ab 1.0.34 |
| Ablauf als Fortschritt (Erstbegehung, Einbringen, Belegreife, Abnahme) mit „Erfassen“ beim nächsten Schritt | ✅ | ✅ | `SiteWorkflow` im Kern, unit-getestet |
| „Erfassen“ unten rechts (klappt beim Scrollen ein, verdeckt nichts) | ✅ | ✅ | Blatt mit Fotos zuerst, dann Video, Scan, Datei, Tagebuch, Erstbegehung, CM-Messung, Abnahme, Mangel, Aufgabe, Notiz für diese Bauakte |
| Menü „…“/„⋮“: Basisdaten, PDF, Teams-Chat, Austragen, Stornieren | ✅ | ✅ | Chat und Teams stehen in der Aktionsleiste |
| Freitexte (Hinweise und Behinderungen, Beschreibung, mehrzeilige Schema-Felder) als Textblöcke mit Überschrift, Links/Telefon antippbar, „Mehr anzeigen“ | ✅ | ✅ | `FreeText` im Kern, unit-getestet |
| Basisdaten: vier Kennwerte direkt (zuerst Estrichart, Dicke, Fläche, Termin), „Alle Basisdaten“ klappt auf | ✅ | ✅ | unit-getestet |
| Angeheftete Notiz, Hinweise nur bei Bedarf; Offene Punkte und neueste Fotos nur, wenn vorhanden | ✅ | ✅ | |
| Basisdaten nach Formular-Schema bearbeiten | ✅ | ✅ | |
| Notizen (anheften, löschen) | ✅ | ✅ | |
| Fotos und Videos (Raster nach Tagen, Großansicht, Beschriftung, Teilen, Löschen) | ✅ | ✅ | |
| Dokumente nach Art, öffnen, scannen, hochladen | ✅ | ✅ | |
| Aufgaben und Mängel (Fälligkeit, abhaken) | ✅ | ✅ | |
| Bautagebuch (Wetter, Temperatur, Personen, Zeiten, Text, Diktat) | ✅ | ✅ | Fotos des Tages werden angehängt |
| Erstbegehung nach Schema (Prüfpunkte, Bemerkungen, Messwerte) | ✅ | ✅ | Fotos je Prüfpunkt fehlen noch |
| Erstbegehung nicht erforderlich (Hinweiskarte, Protokolle, Rückgängig, offline) | ✅ | ✅ | |
| CM-Messung mit Belegreife | ✅ | ✅ | |
| Abnahme mit Unterschriften Kunde/Auftragnehmer | ✅ | ✅ | Freigabelink per E-Mail an den Kunden fehlt |
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
| Standort an Fotos | ✅ | ❌ | |

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
| Wer fährt wohin (lesend) | ✅ | ✅ | |
| Kolonnenplan und Disposition für Planer (zuweisen, tauschen, freigeben) | ✅ | ❌ | iOS CrewPlanView/PlanningSheets; auf Android nur lesend |
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
2. Fotos an Prüfpunkten, Standort an Fotos
3. Disposition/Kolonnenplan für Planer
4. QR-Code bei der Einrichtung
5. FCM, sobald der Server Android-Push anbietet
