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
| Push-Benachrichtigungen | ✅ APNs | 🟡 | Server hat noch kein FCM („Android-Push ist noch nicht eingerichtet“): Postfach wird im Hintergrund abgefragt und als Mitteilung gezeigt |

## Heute

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Begrüßung, Datum, Demo-Hinweis | ✅ | ✅ | |
| Systemhinweise als Karten (Störung, Wartung, Countdown, Details, Ausblenden, „weitere Hinweise“) | ✅ | ✅ | gleiche Logik (NoticePresentation, unit-getestet) |
| Ausfall-Hinweise Backup/Archiv nur, wenn der Server sie liefert | ✅ | ✅ | |
| Mein Einsatz (Zeit, Status, Adresse + Route, Ansprechpartner + Anruf, Tätigkeit, Kolonne, Fahrzeuge, Notiz, Aktionen) | ✅ | ✅ | Plan offline aus dem letzten Abruf |
| Offene Punkte, Diese Woche | ✅ | ✅ | |
| Chat- und Postfach-Symbol mit Zahl | ✅ | ✅ | |

## Baustellen (Bauakten)

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Liste mit Suche, Gruppen nach Status, „Heute im Einsatz“ | ✅ | ✅ | zusätzlich Filter-Chips |
| Schnellanlage mit Art, Adresse, Kunde (vorhanden oder neu) – offline | ✅ | ✅ | |
| Dublettenprüfung beim neuen Kunden | ✅ | ❌ | API `contacts/duplicates` vorhanden, Oberfläche fehlt |
| Kopf: Nummer, Status ändern, Titel, Kennzahlen aus dem Schema | ✅ | ✅ | |
| Angeheftete Notiz, Kontakte (Adresse, Kunde, vor Ort, Bauleitung, nächster Einsatz) | ✅ | ✅ | |
| Aktionsleiste (Foto, Scannen, Tagebuch, Mangel, Mehr) | ✅ | ✅ | |
| Offene Punkte, Fotostreifen, Estrich und Bauvorhaben | ✅ | ✅ | |
| Basisdaten nach Formular-Schema bearbeiten | ✅ | ✅ | |
| Notizen (anheften, löschen) | ✅ | ✅ | |
| Fotos und Videos (Raster nach Tagen, Großansicht, Beschriftung, Teilen, Löschen) | ✅ | ✅ | |
| Dokumente nach Art, öffnen, scannen, hochladen | ✅ | ✅ | |
| Aufgaben und Mängel (Fälligkeit, abhaken) | ✅ | ✅ | |
| Bautagebuch (Wetter, Temperatur, Personen, Zeiten, Text, Diktat) | ✅ | ✅ | Fotos am Tagebucheintrag fehlen noch |
| Erstbegehung nach Schema (Prüfpunkte, Bemerkungen, Messwerte) | ✅ | ✅ | Fotos je Prüfpunkt fehlen noch |
| CM-Messung mit Belegreife | ✅ | ✅ | |
| Abnahme mit Unterschriften Kunde/Auftragnehmer | ✅ | ✅ | Freigabelink per E-Mail an den Kunden fehlt |
| Räume und Flächen (Summen, Stand, bearbeiten) | ✅ | ✅ | |
| Kontakte und Beteiligte | ✅ | ✅ | |
| Bauakte als PDF | ✅ | ✅ | nur mit Verbindung |
| Stornieren mit Begründung | ✅ | ✅ | |
| Verlauf (Historie), „Mir zuweisen“ | 🟡 | ❌ | |

## Erfassen

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Aktionsblatt mit Baustellenwahl (heute zuerst, Suche) | ✅ | ✅ | |
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

## Kommunikation

| Funktion | iOS | Android | Bemerkung |
|---|---|---|---|
| Chat: Unterhaltungen, Verlauf, Senden, Antworten, Reaktionen, Bearbeiten, Löschen | ✅ | ✅ | |
| Chat: Fotos senden und ansehen, Dokumente öffnen, Standort ansehen | ✅ | ✅ | |
| Chat: Diktat | ✅ | ✅ Android-Spracheingabe | |
| Chat: Einzelchat starten, Info (Mitglieder, Stummschalten) | ✅ | ✅ | |
| Chat: Gruppen anlegen, Weiterleiten, Anheften, Suche, Medienübersicht, Standort senden | ✅ | ❌ | API im Kern teils vorhanden |
| Chat: Echtzeit | ✅ SSE | 🟡 | Abfrage alle 5 s im Verlauf, 15 s in der Liste |
| Chat: Ausgang offline (Nachrichten später senden) | ✅ | ❌ | ohne Netz schlägt das Senden sichtbar fehl |
| Postfach (Benachrichtigungen, gelesen, alle gelesen, Ziel öffnen) | ✅ | ✅ | |
| Mitteilungseinstellungen je Ereignis | ✅ | ✅ | |
| Meldungen an die IT (melden, Liste, Verlauf, antworten, wieder öffnen) | ✅ | ✅ | Anhänge (Bildschirmfotos) fehlen |

## Gestaltung und Bedienung

| Thema | Android |
|---|---|
| Navigation | NavigationSuiteScaffold: Leiste auf Telefonen, Rail auf Tablets; Liste + Detail nebeneinander ab 840 dp |
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
2. Fotos an Bautagebuch und Prüfpunkten, Standort an Fotos
3. Disposition/Kolonnenplan für Planer
4. QR-Code bei der Einrichtung, Dublettenprüfung bei neuen Kunden
5. FCM, sobald der Server Android-Push anbietet
