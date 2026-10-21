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
Kunde anrufen und Route starten aus der Akte. Designsystem: `Design/ActionButtons.swift` (`EstrichButtonStyle`,
`ActionTileStyle`, `IconButtonStyle`), Tokens in `Design/Theme.swift`.

## Demomodus und Hinweise für die App-Prüfung (App Review)

Auf dem Startbildschirm führt **„Demo ausprobieren – ohne Server“** ohne Eingaben in eine voll bedienbare App mit
Beispieldaten (drei Bauakten mit Kunden, Fotos, Lieferschein-PDF, Bautagebuch, Erstbegehung, Aufgaben, Einsätze,
eine Meldung an die IT). Der „Server“ läuft dabei im App-Prozess (`Support/LocalServer.swift`, Daten in
`Support/DemoContent.swift`): **es wird nichts über das Netzwerk gesendet**, Änderungen bleiben auf dem Gerät. Ein grünes
Band oben kennzeichnet den Demomodus; „Beenden“ (dort oder unter Mehr) löscht alle Demodaten und führt zurück zur
Einrichtung. Kamera, Dokumentenscanner und Videoaufnahme funktionieren auch im Demomodus (lokal gespeichert).

Text für die App-Review-Notizen (App Store Connect):

> EstrichONE connects to a self-hosted company server. To review the app without a server, tap
> “Demo ausprobieren – ohne Server” on the first screen. The app then runs with local sample data only; nothing is sent
> over the network. Tap “Beenden” in the green bar to leave the demo. Camera, microphone and location are only used
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

# App inkl. UI-Tests im Simulator
cd ios && xcodebuild test -project EstrichONE.xcodeproj -scheme EstrichONE \
  -destination 'platform=iOS Simulator,name=iPhone 16'
```

Die UI-Tests sprechen keinen echten Server an: Über Umgebungsvariablen (`EstrichONE/Support/UITestSupport.swift`,
nur in Debug-Builds enthalten) liefert die App eine feste `server-info`-Antwort.

CI: `.github/workflows/ios.yml` – EstrichCore unter Linux und macOS, App-Build mit Unit- und UI-Tests im
Simulator sowie ein Release-Build ohne Signatur.

## Akzeptanzkriterien aus 10 – Stand

| AK | Nachweis |
|---|---|
| AK-2 (ungültiges Zertifikat nicht einrichtbar) | Die Zertifikatsprüfung übernimmt das System (ATS, im Release ohne Ausnahmen); die App bietet kein „Trotzdem fortfahren“. Zuordnung der TLS-Fehler: `ServerConnectionCheckerTests`. **Offen:** Gegenprobe mit einem echten selbstsignierten Server auf einem Gerät (manuell, Protokoll). |
| AK-4 (abgelaufener/benutzter Einrichtungscode) | **AP-11:** Der Server stellt Einrichtungscodes erst mit der Geräteregistrierung aus. Die App liest den Code bereits aus dem QR-Code. |
| AK-1 (≤ 60 s bis zur Startseite) | nach AP-11 messbar (braucht die Anmeldung) |

## Signierung und Verteilung

Für den Simulator ist keine Signierung nötig. Für ein echtes Gerät in Xcode unter *Signing & Capabilities* das
eigene Team wählen (kostenloses Apple-Konto genügt für eigene Geräte, 7 Tage gültig). TestFlight und App Store
erfordern das Apple Developer Program (07, Abschnitt 5).
