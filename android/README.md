# EstrichONE für Android

Kotlin, Jetpack Compose, Material 3 (docs/reengineering/08).

```
core/   reines Kotlin: API-Client (OkHttp, kotlinx.serialization), Anmeldung als „android“, Gerätebindung
        (Registrierung, Nachweis und Erneuern per ECDSA-P-256-Signatur) – ohne Android SDK baubar und testbar
app/    Android-App: Ersteinrichtung (Server, Anmeldung, Einrichtungscode, Warten auf Freigabe), Heute (Mein Einsatz),
        Bauakten (Suche, Schnellanlage), Bauakte (Hinweise, Übersicht, Notizen, Aufgaben/Mängel, Anrufen, Route), Support
```

Geräteschlüssel liegt im Android Keystore (StrongBox, wenn vorhanden), das Refresh-Token AES-GCM-verschlüsselt mit
einem Keystore-Schlüssel. Kein Cloud-Backup der Anmeldung.

**Stand der ersten Version:** online. Die Offline-Datenbank (Room + SQLCipher) mit Synchronisation, Kamera/Fotos und
Poolgeräte mit PIN folgen; iOS hat diese Teile bereits (ios/).

## Tests

```bash
scripts/test-core-against-server.sh   # PostgreSQL (Docker) + API starten, Ersteinrichtung, ./gradlew :core:test
./gradlew :app:assembleDebug           # braucht das Android SDK (ANDROID_HOME)
```
