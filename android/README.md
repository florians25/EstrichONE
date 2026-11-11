# EstrichONE für Android

Kotlin, Jetpack Compose, Material 3 (docs/reengineering/08). Funktional auf dem Stand der iOS-App, gestaltet nach
Android-Art. Was wo steht und was noch fehlt: [PARITAET.md](PARITAET.md).

```
core/   reines Kotlin, ohne Android SDK baubar und testbar:
        API-Client (OkHttp, kotlinx.serialization), Anmeldung als „android“, Gerätebindung (ECDSA P-256),
        Offline-Stand mit Outbox und Synchronisation (docs/sync-protokoll.md), Domänenmodelle (Bauakte, Kunde, Datei,
        Notiz, Aufgabe/Mangel, Bautagebuch, Prüfung, Abnahme, Raum, Beteiligte, Formular-Schema), Systemhinweise,
        Postfach, Chat, Meldungen an die IT, Planung, PIN-Tresor, Demoserver mit Beispieldaten
app/    Android-App:
        data/     Android Keystore (Geräteschlüssel, Datenschlüssel), SQLCipher-Datenbank, verschlüsselte Dateiablage
        ui/       Heute, Baustellen, Bauakte mit allen Bereichen, Erfassen (CameraX, Scannen zu PDF), Kunden, Chat,
                  Postfach, Helpdesk, Synchronisation, Sicherheit, Einrichtung, Poolgeräte, Sperre
        work/     Abgleich im Hintergrund (WorkManager) und Mitteilungen aus dem Postfach
```

**Sicherheit:** Geräteschlüssel im Android Keystore (StrongBox, wenn vorhanden), Refresh-Token AES-GCM-verschlüsselt,
Datenbank mit SQLCipher und Fotos AES-GCM-verschlüsselt (Datenschlüssel per Keystore umhüllt). Kein Cloud-Backup.
Poolgeräte: Anmeldung je Person im PIN-Tresor (PBKDF2-SHA256, 5 Fehlversuche), Sperre beim Verlassen und nach 5 Minuten.

**Demomodus:** „Demo ausprobieren“ in der Einrichtung. Ein Server im App-Prozess (`core/DemoServer`) beantwortet die
API mit den Beispieldaten der iOS-App; nichts wird gesendet.

## Tests

```bash
scripts/test-core-against-server.sh   # PostgreSQL (Docker) + API starten, Ersteinrichtung, ./gradlew :core:test
./gradlew :core:test                   # Kern ohne Server (Demoserver, Hinweise, PIN, Sync-Logik)
./gradlew :app:assembleDebug :app:lintDebug   # braucht das Android SDK (ANDROID_HOME)
./gradlew :app:testDebugUnitTest       # Screenshots (Roborazzi) → app/build/outputs/roborazzi
```

Die CI (`.github/workflows/android.yml`) baut APK und Lint, testet den Kern gegen einen echten Server und nimmt
Screenshots auf (Telefon und Tablet, hell und dunkel; Artefakt `android-screenshots` und base64 im Protokoll).

## Version und Signatur

`versionName`/`versionCode` kommen im Release aus dem Tag (`-PappVersionName=1.0.5` → Code 10005). Ein signiertes
Release-APK entsteht nur, wenn die Secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`
(und optional `ANDROID_KEY_PASSWORD`) gesetzt sind; sonst bleibt es beim Debug-APK. Schlüssel nie ins Repository.

> **Wichtig für Updates:** Android installiert ein Update nur über eine App mit **demselben Signaturschlüssel**. Ohne
> Keystore-Secrets signiert jeder Release-Lauf mit einem neuen, zufälligen Debug-Schlüssel – Geräte können dann nicht
> aktualisieren, sondern müssen deinstallieren und neu installieren. Den Keystore einmal erzeugen
> (`keytool -genkeypair -v -keystore estrichone.jks -keyalg RSA -keysize 4096 -validity 10000 -alias estrichone`),
> base64-kodiert als Secret hinterlegen und an einem sicheren Ort aufbewahren. Geht er verloren, müssen alle Geräte die
> App einmal neu installieren.

## Updates

Die App aktualisiert sich vom eigenen Server (docs/app-updates.md): Das Release legt das APK mit `android.json`
(SHA-256, Fingerabdruck des Signaturzertifikats) ins API-Image; die App prüft beim Start, beim Zurückkehren und von
Hand (`core/AppUpdates.kt`: Versionsvergleich, Drosselung, Zustandsautomat, fortsetzbarer Download mit Prüfsumme),
überträgt vorher offene Änderungen, fragt bei Bedarf nach der Erlaubnis „Apps aus dieser Quelle installieren“ und
installiert per PackageInstaller (`app/update/`). Ein Update mit anderem Schlüssel wird vor dem Download erkannt und
erklärt. Jede Anfrage trägt `X-EstrichONE-Client: android/<version>`; 426 `CLIENT_UPDATE_REQUIRED` zeigt den
Update-Bildschirm. Das Datenbankschema ist versioniert (`core/StorageSchema.kt`, nur ergänzende Migrationen).
