# EstrichONE für iOS

Native iPhone- und iPad-App (SwiftUI, ab iOS 17; Liquid Glass ab iOS 26). Konzept:
[`docs/reengineering/07-ios-swiftui-liquid-glass.md`](../docs/reengineering/07-ios-swiftui-liquid-glass.md),
Einrichtungsablauf: [`10-server-onboarding.md`](../docs/reengineering/10-server-onboarding.md).

**Stand (AP-10):** Willkommen → Server per QR-Code oder Adresse → Verbindungsprüfung gegen
`GET /api/v1/server-info` → sichtbare Bestätigung → Server bleibt gespeichert. Anmeldung, Geräteregistrierung
und Poolgeräte folgen mit AP-11.

**Baustellenfunktionen:** Schnellanlage einer Bauakte mit Kunde (vorhandenen suchen oder neu erfassen, offline
über `contact.create` vor `bauakte.create`), Foto-Serien mit Beschriftung und Zuordnung, Dokumentenscanner
(VisionKit, mehrseitig, PDF mit Texterkennung, Dokumentart), Dokumente per QuickLook, Bautagebuch mit Textbausteinen,
Kunde anrufen und Route starten aus der Akte, Räume und Flächen, Beteiligte, Erstbegehung, Abnahme, CM-Messung.

**Designsystem** (`EstrichONE/Design/`): Farben, Abstände (8-pt-Raster), Radien und Größen in `Theme.swift` (eine
Akzentfarbe Markenblau, gedämpfte Statusfarben, hell und dunkel), Schriftstufen mit Dynamic Type in `Typography.swift`,
Schaltflächen in `Buttons.swift` (`EstrichButtonStyle` primär/sekundär/tertiär, `ActionTileStyle`, `IconButtonStyle`,
`ChoiceButton`), Bausteine in `Components.swift` (Karte, Zeilen, Kennzahl, Kapsel, Hinweis, Leerzustand) und
`Feedback.swift` (Platzhalter beim Laden, Fortschritt, Bestätigung). Neue Bildschirme verwenden nur diese Bausteine.

## Demomodus und Hinweise für die App-Prüfung (App Review)

Auf dem Startbildschirm führt **„Demo ausprobieren – ohne Server“** ohne Eingaben in eine voll bedienbare App mit
Beispieldaten (sechs Bauakten in allen Ständen mit Kunden, Fotos, Lieferschein-PDF, Bautagebuch, Erstbegehung,
CM-Messung, Räumen, Beteiligten, Aufgaben, Einsätzen der Woche, „Wer fährt wohin“ mit drei Kolonnen und einer Meldung an
die IT). Der „Server“ läuft dabei im App-Prozess (`Support/LocalServer.swift`, Daten in `Support/DemoContent.swift`):
**es wird nichts über das Netzwerk gesendet**, Änderungen bleiben auf dem Gerät. Die Kennzeichnung „Demo“ oben links
und ein Hinweis auf der Heute-Seite zeigen den Demomodus; „Beenden“ (dort, im Menü „Demo“ oder unter Mehr) löscht alle
Demodaten und führt zurück zur Einrichtung. Kamera, Dokumentenscanner und Videoaufnahme funktionieren auch im Demomodus (lokal gespeichert).

Text für die App-Review-Notizen (App Store Connect):

> EstrichONE connects to a self-hosted company server. To review the app without a server, tap
> “Demo ausprobieren – ohne Server” on the first screen. The app then runs with local sample data only; nothing is sent
> over the network. Tap “Demo” (top left) and “Demo beenden” to leave the demo. Camera, microphone and location are only used
> on explicit user action (photos, document scanner, videos, site address).

UI-Test: `BauakteUITests.testDemoModeWithoutServer`.

## Aufbau

| Pfad | Inhalt |
|---|---|
| `project.yml` | Projektbeschreibung für [XcodeGen](https://github.com/yonaskolb/XcodeGen). Das `.xcodeproj` wird erzeugt, nicht eingecheckt. |
| `EstrichONE/` | App: `App/` (Einstieg, Zustand, Umgebung), `Onboarding/`, `Home/`, `Design/` (Tokens aus 05, Schaltflächen), `Support/` |
| `Packages/EstrichCore/` | Plattformneutrale Logik ohne UIKit: Serveradresse, Einrichtungscode, Versionsabgleich, Verbindungsprüfung, gespeicherter Server. Läuft auch unter Linux. |
| `EstrichONETests/` | Unit-Tests der App (Swift Testing) |
| `EstrichONEUITests/` | XCUITests der Hauptabläufe |

Abweichungen von der Projektskizze in 07, Abschnitt 4 (bewusst, um nichts auf Vorrat zu bauen):
- `EstrichDesign` ist noch kein eigenes Paket. Die Tokens liegen in `EstrichONE/Design/`, solange die App ihr
  einziger Nutzer ist (Auslagerung, sobald eine Erweiterung wie „In Bauakte speichern“ dazukommt).
- `EstrichAPI` (generierter Client) kommt mit AP-11, wenn die ersten angemeldeten Endpunkte gebraucht werden.
  `server-info` ist bis dahin von Hand in `ServerInfo.swift` abgebildet.

## Entwickeln

Voraussetzungen: Mac mit **Xcode 16 oder neuer** (für Liquid Glass Xcode 26) und XcodeGen.

```bash
brew install xcodegen
cd ios
xcodegen generate
open EstrichONE.xcodeproj
```

Nach Änderungen an `project.yml` oder neuen Dateien erneut `xcodegen generate` ausführen.

**Lokaler Testserver:** Debug-Builds erlauben `http://` für `localhost`, `127.0.0.1` und `*.local`
(z. B. `http://localhost:8080` mit `dotnet run` im Simulator). Release-Builds enthalten diese Ausnahme nicht,
weder in der App-Logik noch in der ATS-Konfiguration (`EstrichONE/Config/Info.plist`).

**Einrichtungslink testen** (Simulator):

```bash
xcrun simctl openurl booted "estrichone://enroll?v=1&server=http://localhost:8080"
```

## Tests

```bash
# Kernlogik – auf dem Mac oder unter Linux (auch per Docker: swift:6.1-noble)
cd ios/Packages/EstrichCore && swift test

# App: Unit-Tests im Simulator (so läuft es in der CI)
cd ios && xcodegen generate && xcodebuild test -project EstrichONE.xcodeproj -scheme EstrichONE \
  -only-testing:EstrichONETests -destination 'platform=iOS Simulator,name=iPhone 17 Pro'

# App: UI-Tests (nur lokal, nicht in der CI)
cd ios && xcodebuild test -project EstrichONE.xcodeproj -scheme EstrichONE \
  -only-testing:EstrichONEUITests -destination 'platform=iOS Simulator,name=iPhone 17 Pro'
```

UI-Tests in Xcode: `ios/EstrichONE.xcodeproj` öffnen (vorher `xcodegen generate`), Schema „EstrichONE“, im
Test-Navigator (⌘6) `EstrichONEUITests` bzw. einen einzelnen Test starten. Die meisten UI-Tests sprechen keinen
echten Server an: Über Umgebungsvariablen (`EstrichONE/Support/UITestSupport.swift`, nur in Debug-Builds enthalten)
liefert die App feste Antworten; `testDemoModeWithoutServer` nutzt den Demomodus.

CI: `.github/workflows/ios.yml` – EstrichCore unter Linux gegen einen echten Server (ca. 3 Minuten) und unter macOS,
App-Build (Debug) mit den Unit-Tests der App im Simulator sowie ein Release-Build ohne Signatur. Die UI-Tests laufen
bewusst nicht in der CI.

## Screenshots (visuelle Prüfung)

Debug-Builds haben einen Screenshot-Modus (`EstrichONE/Support/ScreenshotMode.swift`): Mit dem Startargument
`-EstrichShots heute,bauakte,…` startet die App im Demomodus und zeigt die genannten Bildschirme nacheinander; über eine
Datei in ihrem Datencontainer meldet sie, wann ein Bildschirm steht. `scripts/screenshots.sh` nimmt im Simulator
jeden Bildschirm hell (und dunkel) auf, meldet Abstürze mit Absturzbericht und Hänger und macht mit dem nächsten weiter.

Die CI (`.github/workflows/ios.yml`, Job „Screenshots“) läuft bei Änderungen an der App parallel auf drei Runnern:
iPhone in zwei Hälften (Seiten bzw. Formulare, Einrichtung und große Schrift; hell und dunkel) und iPad (hell). Jeder
Lauf lädt die Bilder als Artefakt hoch und schreibt sie verkleinert ins Protokoll (`scripts/shots-to-log.py`). Von Hand:
Workflow „iOS“ starten, optional mit eigener Liste von Bildschirmen (dann nur die erste iPhone-Hälfte).

Bildschirme: `heute`, `heute-unten`, `baustellen`, `bauakte`, `bauakte-unten`, `bereich-fotos`, `bereich-aufgaben`,
`bereich-protokolle`, `bereich-tagebuch`, `bereich-raeume`, `bereich-basisdaten`, `bereich-kontakte`, `bereich-notizen`,
`erfassen`, `neu-1` … `neu-4` (Anlage-Assistent), `erstbegehung`, `abnahme`, `kunden`, `kundenkarte`, `disposition`,
`mehr`, `sync`, `meldung`, `meldung-detail`, `aufgaben`, `woche`, `wartung` (Wartungshinweis), `start` (Willkommen),
`anmelden`, `postfach` (Benachrichtigungen), `dispo-zuweisen`, `dispo-verschieben`, `dispo-tauschen`, `dispo-fahrzeug`,
`dispo-kolonnen`, `dispo-freigabe`.

**App-Store-Screenshots:** Job „App-Store-Screenshots“ (Workflow „iOS“ von Hand mit `appstore` starten oder
`[appstore]` in der Commit-Nachricht) nimmt acht Motive in Originalauflösung auf – iPhone 6,9 Zoll (1320 × 2868) und
iPad 13 Zoll (2064 × 2752), hell, ohne Demo-Kennzeichnung (`-EstrichAppStore YES`). Rahmen, Überschriften und die
6,5-Zoll-Fassung: `AppStore/README.md`.

## Akzeptanzkriterien aus 10 – Stand

| AK | Nachweis |
|---|---|
| AK-2 (ungültiges Zertifikat nicht einrichtbar) | Die Zertifikatsprüfung übernimmt das System (ATS, im Release ohne Ausnahmen); die App bietet kein „Trotzdem fortfahren“. Zuordnung der TLS-Fehler: `ServerConnectionCheckerTests`. **Offen:** Gegenprobe mit einem echten selbstsignierten Server auf einem Gerät (manuell, Protokoll). |
| AK-4 (abgelaufener/benutzter Einrichtungscode) | **AP-11:** Der Server stellt Einrichtungscodes erst mit der Geräteregistrierung aus. Die App liest den Code bereits aus dem QR-Code. |
| AK-1 (≤ 60 s bis zur Startseite) | nach AP-11 messbar (braucht die Anmeldung) |

## Benachrichtigungen (Push, APNs)

Die App ist für Pushnachrichten vorbereitet (`EstrichONE/App/PushNotifications.swift`, Postfach
`EstrichONE/Home/NotificationViews.swift`, API in `Packages/EstrichCore/Sources/EstrichCore/Notifications/`):

- **Berechtigung** erst auf Wunsch (Postfach bzw. Mehr › Benachrichtigungen › „Aktivieren“), nie ungefragt beim Start.
  Abgelehnt: Hinweis mit Weg in die Einstellungen; alles bleibt im Postfach.
- **APNs-Registrierung** nach Zustimmung; das Gerätetoken geht an `POST /api/v1/notifications/devices`
  (`platform: "ios"`, `sandbox` = Debug-Build, Bundle-ID, Gerätename, App-Version). Beim Abmelden
  `DELETE /api/v1/notifications/devices?token=…`. Server-Seite und Einrichtung: `docs/benachrichtigungen.md`.
- **Tippen** auf eine Pushnachricht öffnet das Objekt (Bauakte, Meldung, Planung des Tages, Aufgaben) – Link aus dem
  Payload (`link`, z. B. `/bauakten/{id}`, `/it-center/helpdesk/{id}`, `/planung/wer-faehrt-wohin?tag=…`) oder
  `objectType`/`objectId`; Zuordnung in `NotificationRoute` (getestet). `notificationId` wird dabei als gelesen markiert.
- **Postfach** mit Glocke und Zahl auf der Heute-Seite, Eintrag unter Mehr, Abzeichen am App-Symbol; gelesen/ungelesen,
  „Alle gelesen“. Im Vordergrund erscheinen Pushnachrichten als Banner.
- **Demomodus:** Beispiel-Benachrichtigungen im Postfach; „Beispiel senden“ zeigt nach wenigen Sekunden eine echte lokale
  Mitteilung (ohne Server), deren Tippen „Wer fährt wohin“ für morgen öffnet.

**Was Sie bei Apple bzw. in Xcode einmalig einrichten müssen:**

1. Apple Developer › *Certificates, Identifiers & Profiles* › *Identifiers*: App-ID `de.estrichone.EstrichONE` (bzw. Ihre
   eigene Bundle-ID) mit der Capability **Push Notifications** anlegen/aktivieren.
2. *Keys*: einen **APNs-Schlüssel** (.p8, „Apple Push Notifications service“) erzeugen; Key-ID und Team-ID notieren. Der
   Schlüssel wird nur einmal angezeigt – sicher ablegen. Im EstrichONE-Server unter Einstellungen › Benachrichtigungen
   (bzw. `.env`) .p8-Inhalt, Key-ID, Team-ID, Bundle-ID und Umgebung (Sandbox für Xcode-Builds, Produktion für
   TestFlight/App Store) eintragen.
3. Xcode: `xcodegen generate`, dann im Ziel *EstrichONE* › *Signing & Capabilities* das eigene Team wählen. Die
   Capability „Push Notifications“ ist über `EstrichONE/Config/EstrichONE.entitlements` (`aps-environment`) bereits
   eingetragen; beim Archivieren für TestFlight/App Store setzt Xcode automatisch `production`.
4. Hintergrund-Aktualisierung ist nicht nötig (Pushnachrichten mit sichtbarem Hinweis). Ohne Schritt 1–3 läuft die App
   unverändert – Benachrichtigungen erscheinen dann nur im Postfach.

## Angemeldet bleiben, Face ID und Sperre

- **Persönliches Gerät:** einmal mit Passwort anmelden, danach nur noch entsperren – mit Face ID/Touch ID oder dem
  Gerätecode (Mehr › *Sperre und Face ID*: „Mit Face ID entsperren“ an/aus, „Sperren“ sofort/1/5/15/60 Minuten,
  Standard 15). Nach einem Neustart der App ebenso.
- **Sitzung:** gleitende Gerätesitzung auf dem Server. Jede Nutzung verlängert sie; erst nach *n* Tagen ohne Nutzung
  ist wieder das Passwort nötig (Web › Einstellungen › Reiter „Anmeldung“ › Apps auf Smartphones und Tablets, Standard
  90, 7–365). Die Erneuerung ist an die Gerätesignatur (Secure Enclave) gebunden.
- **Robust gegen Funklöcher und Wettläufe:** Geht die Antwort einer Erneuerung verloren (App im Hintergrund beendet,
  Netzabbruch), stellt der Server bei erneutem Vorlegen des alten Refresh-Tokens mit gültiger Gerätesignatur neu aus,
  solange dessen Nachfolger unbenutzt ist (vorher: „Token wiederverwendet“ → Abmeldung). Ein `401 UNAUTHORIZED` einer
  Anfrage, deren Zugriffstoken eine parallele Erneuerung gerade ersetzt hat, führt zu Erneuern und Wiederholen statt
  zur Abmeldung; je Person gibt es genau einen API-Zugang, auch über Sperren/Entsperren hinweg.
- **Passwort nur bei echtem Sitzungsende**, mit Grund: abgelaufen (Frist ohne Nutzung), Passwort geändert, abgemeldet
  bzw. im IT Center beendet, aus Sicherheitsgründen beendet. Gerät gesperrt/entfernt oder Konto gesperrt zeigt den
  Hinweisbildschirm. Lokale Daten bleiben erhalten.
- **Schlüsselbund:** Der Datenschlüssel des Tresors (darin die Sitzung samt Refresh-Token) liegt zusätzlich als Eintrag
  im Dienst `de.estrichone.unlock` mit `SecAccessControl` `[.biometryCurrentSet, .or, .devicePasscode]` (Face ID aus:
  nur `.devicePasscode`), `WhenPasscodeSetThisDeviceOnly`. Werden Gesichter/Finger geändert, nimmt iOS Face ID für den
  Eintrag nicht mehr an: einmal Gerätecode, dann legt die App ihn mit den neuen Merkmalen neu an (Abgleich über
  `evaluatedPolicyDomainState`) und sagt das. Ohne Gerätecode gibt es keine Systemsperre; die App öffnet dann wie bisher
  über den Geräteschlüssel. Abmelden und „App zurücksetzen“ löschen den Eintrag.
- **Poolgeräte** unverändert: Benutzerauswahl und PIN, Sperre beim Verlassen und nach 5 Minuten Inaktivität.

## Sicherheit und App-Store-Veröffentlichung

Exportkonformität, Krypto-Inventar, Datenschutz-Manifest (`EstrichONE/Resources/PrivacyInfo.xcprivacy`), Nutzungstexte,
Kontolöschung, Review-Hinweise und die vollständige Checkliste mit Store-Texten: **`APPSTORE.md`**.

## Signierung und Verteilung

Für den Simulator ist keine Signierung nötig. Für ein echtes Gerät in Xcode unter *Signing & Capabilities* das
eigene Team wählen (kostenloses Apple-Konto genügt für eigene Geräte, 7 Tage gültig). TestFlight und App Store
erfordern das Apple Developer Program (07, Abschnitt 5).
