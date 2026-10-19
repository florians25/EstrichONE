# 07 – EstrichONE für iOS (SwiftUI, Liquid Glass)

**[Verbindlich]** Swift, SwiftUI, Xcode-Projekt, offizielle Apple-APIs, keine WebView als Oberfläche.
**Status:** nicht vorhanden, Neuentwicklung.

## 1. Zielversionen und Liquid Glass

- **Liquid Glass** ist das Designsystem ab **iOS 26**. Standardkomponenten (`TabView`, `NavigationStack`,
  Toolbars, Sheets, Menüs, `Button(.glass)`) übernehmen es automatisch, sobald mit dem iOS-26-SDK gebaut wird.
  Eigene Elemente nutzen `glassEffect(_:in:)` und `GlassEffectContainer`.
- **Mindestversion [Vorschlag] E-12: iOS 17.** Auf iOS 17/18 erscheinen dieselben Standardkomponenten im
  klassischen Stil. Eigene Glaselemente fallen über `if #available(iOS 26, *)` auf `.regularMaterial` bzw.
  eine opake Fläche zurück. Das ist der „hochwertige native Fallback“: kein Nachbau von Glas.

### Wo Glas eingesetzt wird – und wo nicht

| Einsatz | Glas? | Begründung |
|---|---|---|
| Tab Bar, Navigationsleisten, Toolbars, Sheets, Menüs | ja (System) | Apples Vorgabe: Glas für die Steuerungsebene über dem Inhalt |
| Schwebende Hauptaktion „Neue Bauakte“ | ja, `.glassProminent` mit Tönung `brand.navy` | gut erreichbar, hebt sich ab |
| Kameraüberlagerung (Auslöser, Zähler, „Fertig“) | ja | Inhalt (Kamerabild) bleibt sichtbar |
| **Listen, Formulare, Bauakteninhalt, Statusanzeigen** | **nein** | Lesbarkeit in der Sonne; Inhalt nie auf Glas |
| Speicher-/Synchronisationsstatus | **nein**, opake Statuszeile | muss immer lesbar sein |

**Bedienungshilfen:**
- Bei **„Transparenz reduzieren“** und **„Kontrast erhöhen“** liefert das System Glas automatisch opaker bzw.
  kontrastreicher aus.
- Eigene `glassEffect`-Stellen prüfen zusätzlich `accessibilityReduceTransparency` und verwenden dann eine
  deckende Fläche.
- Der **Baustellenmodus** (05, 2.5) schaltet eigene Glaselemente immer auf deckend.

## 2. Navigation

```
TabView (iOS 26: Liquid-Glass-Tab-Bar, minimiert beim Scrollen)
 ├─ Heute       Mein Einsatz heute, zuletzt geöffnete Bauakten, offene Aufgaben
 ├─ Bauakten    Suche (Tab-Rolle .search), Filter: Meine / Heute / Alle (nach Recht)
 ├─ Aufnehmen   → öffnet direkt die Kamera mit Bauaktenwahl (siehe 09, Ablauf 2)
 ├─ Aufgaben    Meine Aufgaben und Mängel
 └─ Mehr        Synchronisation, Problem melden, Einstellungen, Benutzer wechseln (Poolgerät)
Schwebende Aktion „Neue Bauakte“ auf „Heute“ und „Bauakten“ (unten rechts, Daumenbereich)
```

- Bauakte: `NavigationStack` → Detail mit Segmenten (Übersicht · Fotos · Notizen · Aufgaben · Räume ·
  Prüfungen). Die Toolbar unten enthält **Foto · Notiz · Aufgabe**.
- **Sheets** mit Detents (`.medium`, `.large`) für kurze Erfassungen (Notiz, Aufgabe). Das Sheet der
  Schnellanlage öffnet in `.large`.
- **Kontextmenüs** auf Bauaktenzeilen: Öffnen, Foto aufnehmen, Navigation starten (Karten-App), Anrufen
  (Ansprechpartner).

## 3. Geräteintegration

| Funktion | API | Hinweise |
|---|---|---|
| Kamera | `AVCaptureSession` (eigene Serienbild-Ansicht) | Serienaufnahme ohne Rückfrage nach jedem Foto ist auf der Baustelle schneller als `UIImagePickerController` |
| Fotomediathek | `PhotosPicker` | ohne Vollzugriff auf die Mediathek (nur ausgewählte Bilder) |
| Dokumentenscanner | VisionKit `VNDocumentCameraViewController` | Ergebnis als PDF |
| Dateien-App | `fileImporter`, `fileExporter` | – |
| Teilen | `ShareLink`; Share-Extension „In Bauakte speichern“ **[Vormerken 1.1]** | |
| Biometrie | `LocalAuthentication` (Face ID/Touch ID) | nur zum Entsperren des lokal gespeicherten Schlüssels, nie als Ersatz für die Serveranmeldung |
| Push | APNs (optional, siehe 20, Abschnitt 6) | ohne APNs: Benachrichtigungen beim App-Start und über Hintergrundaktualisierung |
| Lokale Datenbank | GRDB.swift | je Benutzer eine Datei (11) |
| Offline-Speicherung | Datenschutzklasse `.complete` für Datenbank und Fotos; Warteschlange `.completeUntilFirstUserAuthentication`, damit Hintergrund-Uploads funktionieren | |
| Hintergrundübertragung | `URLSessionConfiguration.background` für Datei-Uploads; `BGTaskScheduler` (`BGProcessingTask`) für die Synchronisation | iOS bestimmt den Zeitpunkt. **Garantiert ist nur:** Uploads, die bereits an die Hintergrund-Session übergeben wurden, laufen weiter, auch wenn die App beendet wurde. |
| Schlüsselspeicher | Keychain (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`), Geräteschlüssel in der Secure Enclave | |
| Haptik | `sensoryFeedback(.success)` beim Speichern, `.warning` bei Fehlern | |
| Standort | `CLLocationManager` „Beim Verwenden der App“, nur auf Knopfdruck („Standort übernehmen“) | Datenminimierung |
| Adressvorschläge | `MKLocalSearchCompleter` (Apple, kostenlos, kein Konto) bzw. `CLGeocoder` | Daten gehen an Apple; abschaltbar per Server-Richtlinie |

## 4. Projekt

```
ios/
  EstrichONE.xcodeproj
  EstrichONE/            App, Features (Onboarding, Heute, Bauakten, Kamera, Aufgaben, Sync, Support)
  Packages/
    EstrichCore/         Datenmodell, Repository, SyncEngine, lokale DB (Swift Package, plattformneutral testbar)
    EstrichAPI/          generierter Client (swift-openapi-generator)
    EstrichDesign/       Tokens, Komponenten
  EstrichONETests/, EstrichONEUITests/
```

## 5. Distribution und Konten (Transparenz)

| Weg | Voraussetzung | Kosten |
|---|---|---|
| Entwicklung auf eigenem Gerät | Apple-ID, kostenloses Konto | kostenlos; App läuft nur 7 Tage, keine Push-Benachrichtigungen |
| TestFlight / App Store | **Apple Developer Program** | 99 USD/Jahr |
| Firmeninterne Verteilung | Apple Developer Enterprise Program | nur für größere Organisationen, strenge Zulassung → für Estrichbetriebe ungeeignet |
| Custom Apps über Apple Business Manager | Developer Program + Apple Business Manager des Betriebs | App ist nur für ausgewählte Firmen sichtbar |

**Folge für „kostenlos und selbst hostbar“:** Der Server bleibt kostenlos. Die iOS-App muss aber von **einem**
Herausgeber mit Developer-Konto (99 USD/Jahr) über den App Store bereitgestellt werden. Danach kann jeder Betrieb
sie kostenlos laden und mit seinem eigenen Server verbinden (10). Eine generische App mit frei wählbarer
Serveradresse ist im App Store üblich. Für die Prüfung braucht Apple einen Demo-Server mit Zugangsdaten.

**Datenschutzangaben:** Datenschutzbericht (Privacy Manifest `PrivacyInfo.xcprivacy`); keine Tracking-Domains,
kein Analytics-SDK. Berechtigungstexte (Kamera, Fotos, Standort, Face ID) auf Deutsch, mit konkreter Begründung.

**Entitlements:** Keychain Sharing (nur bei Share-Extension), Push Notifications (optional), Background Modes
(Background fetch, Background processing). Associated Domains nur für Entra-Weiterleitungen, falls MSAL das
erfordert.

## 6. Aufwand

50–80 Personentage inkl. Sync-Engine. Die Engine wird einmal sauber spezifiziert (15). Android setzt dieselbe
Spezifikation ein zweites Mal um.
