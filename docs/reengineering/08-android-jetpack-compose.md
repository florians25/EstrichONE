# 08 – EstrichONE für Android (Kotlin, Jetpack Compose, Material 3)

**[Verbindlich]** Kotlin, Jetpack Compose, native APIs, Material 3. Funktional gleichwertig zu iOS,
gestalterisch konsequent Android.
**Status:** nicht vorhanden, Neuentwicklung.

## 1. Plattformgerechte Gestaltung

| Thema | Android-Umsetzung | Unterschied zu iOS |
|---|---|---|
| Navigation | **Navigation Bar** (5 Ziele) auf Telefonen; **Navigation Rail** auf Tablets und im Querformat (adaptives `NavigationSuiteScaffold`) | iOS: Tab Bar mit Glas |
| Hauptaktion | **Extended FAB** „Neue Bauakte“ (Material 3, unten rechts) | iOS: Glas-Schaltfläche |
| Detail | `TopAppBar` mit Zurück; Reiter als `PrimaryScrollableTabRow` | iOS: Segmente |
| Kurze Erfassung | `ModalBottomSheet` | iOS: Sheet mit Detents |
| Dialoge | `AlertDialog` nur für Bestätigungen mit Datenverlust | – |
| Rückmeldungen | `Snackbar` mit Aktion („Rückgängig“) | iOS: Haptik + Inline-Status |
| Zurück | **Predictive Back** (`enableOnBackInvokedCallback`, Compose `PredictiveBackHandler`) ab Android 14 sichtbar | iOS: Wischen vom Rand |
| Edge-to-Edge | `enableEdgeToEdge()`, Insets über `WindowInsets` | Safe Areas |
| Farben | eigenes EstrichONE-`ColorScheme` aus `tokens.json`; **dynamische Farben aus** (die Markenfarbe trägt Wiedererkennung und Statusbedeutung). **[Offen] E-17:** optional nur für neutrale Flächen | – |
| Material 3 Expressive | nur Bausteine, die in der verwendeten stabilen `material3`-Version enthalten sind (z. B. neue Ladeindikatoren, Button-Formen); keine Alpha-Abhängigkeit in Releases | – |
| Typografie | Material-Typ-Rampe, skaliert mit der Systemschriftgröße (bis 200 % getestet) | Dynamic Type |

## 2. Geräteintegration

| Funktion | API | Hinweise |
|---|---|---|
| Kamera | **CameraX** (`ImageCapture`), eigene Serienaufnahme-Ansicht | Auslöser auch über Lautstärketaste (mit Handschuhen bedienbar) |
| Galerie / Dateien | **Photo Picker** (`PickVisualMedia`, ohne Speicherberechtigung), `OpenDocument` (SAF) | keine `READ_EXTERNAL_STORAGE`-Berechtigung nötig |
| Dokumentenscanner | ML Kit Document Scanner **oder** Fallback (CameraX + manuelles Zuschneiden) – **[Offen] E-14** | ML Kit benötigt Google Play Services; die Fallback-Variante wird auf jeden Fall gebaut |
| Biometrie | `BiometricPrompt` (androidx.biometric), Klasse 3 („strong“), mit `CryptoObject` an den Keystore-Schlüssel gebunden | |
| Schlüsselspeicher | **Android Keystore** (StrongBox, wenn vorhanden), nicht exportierbar | |
| Lokale Datenbank | **Room**, verschlüsselt mit **SQLCipher**; Schlüssel per Keystore umhüllt | `EncryptedSharedPreferences` (Jetpack Security) ist abgekündigt → **DataStore + Tink** für kleine Einstellungen |
| Hintergrund | **WorkManager**: `SyncWorker` (periodisch, Netzbedingung), `UploadWorker` (expedited bzw. Foreground-Service mit Fortschrittsbenachrichtigung bei großen Videos, Typ `dataSync`) | |
| Benachrichtigungen | `NotificationCompat`, Kanäle („Einsätze“, „Aufgaben“, „Synchronisation“, „Support“), Laufzeitberechtigung ab Android 13 | |
| Push | FCM (optional, siehe 20, Abschnitt 6) **oder** UnifiedPush **[Vormerken]** für Geräte ohne Google-Dienste | ohne Push: Abfrage beim Start + periodischer Worker |
| Standort | `FusedLocationProvider` nur auf Knopfdruck; Fallback `LocationManager` | Fused benötigt Play Services |
| Adressvorschläge | `android.location.Geocoder` (ohne Konto; liefert auf manchen Geräten nichts) bzw. serverseitiger Anbieter | optional |
| TalkBack | `contentDescription`/`semantics` für alle Symbolaktionen, Fokusreihenfolge geprüft | |
| Bildschirmgrößen | Window Size Classes: kompakt (Telefon), mittel (Klapp-/kleine Tablets: Liste + Detail nebeneinander), erweitert (Tablet) | |

## 3. Architektur

```
android/
  app/                       Application, Navigation, DI (Hilt)
  core/model, core/database  Room-Entitäten, DAOs, Migrationen
  core/sync                  SyncEngine (gleiche Spezifikation wie iOS, 15)
  core/network               generierter Client (OpenAPI Generator, Kotlin) + Auth-Interceptor
  core/designsystem          Theme, Komponenten
  feature/onboarding, feature/heute, feature/bauakten, feature/kamera, feature/aufgaben, feature/support
```

- **Muster:** MVVM mit `ViewModel` + `StateFlow`, Unidirectional Data Flow, Hilt für Abhängigkeiten.
  Die Modulaufteilung ist bewusst grob, damit sie für eine Person überschaubar bleibt.
- **Build:** Gradle Kotlin DSL, Version Catalog, R8.

## 4. Mindest- und Ziel-API

- **minSdk [Vorschlag] E-13:** 29 (Android 10).
- **targetSdk:** die jeweils von Google Play geforderte aktuelle Stufe (Google verlangt jährlich eine Anhebung;
  im Jahr 2026 ist das API 35 bzw. höher). Ohne Play-Verteilung gilt diese Pflicht nicht, ist aber trotzdem
  einzuhalten.

## 5. Distribution

| Weg | Voraussetzung | Kosten / Grenzen |
|---|---|---|
| **Google Play** (App Bundle) | Google-Play-Entwicklerkonto | einmalig 25 USD. Neue private Entwicklerkonten müssen vor der Veröffentlichung einen geschlossenen Test mit mindestens 12 Testern über 14 Tage durchführen. Identitätsprüfung erforderlich. |
| **Direkte APK** (vom eigenen EstrichONE-Server herunterladbar) | Signaturschlüssel des Herausgebers | kostenlos; Nutzer müssen „Unbekannte Apps installieren“ erlauben. Updates prüft die App selbst gegen `server-info` und lädt die neue APK. |
| Managed Google Play (private App) | Android Enterprise des Betriebs | für Betriebe mit MDM |

**Hinweis zu angekündigten Einschränkungen:** Google hat für zertifizierte Android-Geräte eine verpflichtende
**Entwickler-Verifizierung auch für Apps außerhalb von Google Play** angekündigt (stufenweise ab 2026).
Vor dem ersten Release prüfe ich, ob und wie das die direkte APK-Verteilung betrifft.
Bis dahin gilt: Signierschlüssel sicher verwahren (Verlust = keine Updates mehr möglich), Play App Signing nutzen.

## 6. Aufwand

50–80 Personentage. Die Sync-Spezifikation und die API-Verträge aus der iOS-Entwicklung verkürzen die Arbeit
nur begrenzt, weil Datenbank, Hintergrundausführung und UI vollständig plattformeigen sind.
