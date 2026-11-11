# App-Updates (iOS und Android)

Der eigene EstrichONE-Server ist die Quelle der Wahrheit für die Apps: Er kennt die neueste verfügbare App-Version je
Plattform, die Mindestversion, den Hinweis der IT, die Bezugsquelle und die Neuerungen. Die Apps prüfen dagegen und
zeigen je nach Stand nichts, einen Hinweis oder einen Sperrbildschirm.

## Ablauf im Überblick

| Stand | Bedeutung | App |
|---|---|---|
| `current` | App aktuell (oder Hinweise abgeschaltet) | nichts |
| `recommended` | neuere Version verfügbar | Hinweis oben mit **Aktualisieren** und **Später** |
| `required` | unter der Mindestversion | Sperrbildschirm „Update erforderlich“; der Server lehnt Anfragen mit 426 ab |
| `newerThanServer` | App neuer als der Server | App arbeitet weiter, Hinweis unter Mehr › Info; Verwaltung sieht „Server aktualisieren“ |

**Prüfung:** beim Start (höchstens alle 30 Minuten), beim Zurückkehren in die App (höchstens alle 6 Stunden), von Hand
unter Mehr › Info und sofort nach einer Ablehnung durch den Server.

**Mindestversion:** Sie ergibt sich aus der technischen Mindestversion des Servers (`EstrichOne:MinClientVersion`) und –
wenn in der Verwaltung eingeschaltet – der erzwungenen Mindestversion (leer = Serverversion). Die erzwungene
Mindestversion liegt nie über der Version, die tatsächlich bereitsteht (Android-Paket bzw. hinterlegte iOS-Version);
ohne Bezugsquelle wird nichts erzwungen.

**Kennung:** Jede Anfrage der Apps trägt `X-EstrichONE-Client: android/1.0.13` bzw. `ios/1.0.13`. Damit
- lehnt der Server zu alte Apps mit **426 `CLIENT_UPDATE_REQUIRED`** ab (Problem-JSON mit `minimumVersion`,
  `latestVersion`, `installedVersion`); erreichbar bleiben `server-info`, `app-updates/*` und `auth/logout`,
- führt er Version und „zuletzt gesehen“ des Geräts in der Geräteübersicht nach,
- nennt `server-info` die für die Plattform geltende Mindestversion (Einrichtung neuer Geräte).

Die Weboberfläche und ältere Apps ohne Kennung werden nicht gesperrt. Eine 426-Antwort beendet keine Sitzung: Die
Anmeldung und alle nicht übertragenen Änderungen bleiben auf dem Gerät und werden nach dem Update gesendet.

## Schnittstellen

| Endpunkt | Zweck |
|---|---|
| `GET /api/v1/app-updates/check?platform=android&version=1.0.10` | Stand, neueste und Mindestversion, Hinweis, Bezugsquelle, Neuerungen (ohne Anmeldung) |
| `GET /api/v1/app-updates/release-notes?platform=ios&from=1.0.10&to=1.0.13` | „Neu in dieser Version“ nach einem Update (ohne Anmeldung) |
| `GET /api/v1/app-updates/android/package` | Android-App (APK) dieses Servers, `ETag` = SHA-256, `Range` für fortsetzbare Downloads (ohne Anmeldung) |
| `GET /api/v1/app-updates/overview` | Verwaltung: Regeln je Plattform, Paket, alle Geräte mit App-Version und Stand (`devices.read`) |
| `POST /api/v1/app-updates/devices/{id}/request-update` | Person des Geräts per Benachrichtigung zum Update auffordern (`devices.manage`) |

Die Regeln stehen in den Systemeinstellungen (`appUpdates`): `recommendUpdates`, `enforceMinimum`, `minimumVersion`,
`message`, `iosUpdateUrl`, `iosLatestVersion`, `androidUpdateUrl`. Links müssen `https://` sein.

## Android

Das Release baut die App aus dem Tag (`versionName` 1.0.13, `versionCode` 10013), signiert sie mit dem Schlüssel des
Herausgebers und legt sie mit `android.json` (Version, Versionscode, Größe, SHA-256, Fingerabdruck des
Signaturzertifikats) in das API-Image (`/app/app-packages`). Damit liefert jeder Server genau die App seiner eigenen
Version aus – ohne Internetzugang der Geräte und ohne Abhängigkeit von GitHub. Beim Start prüft die API Größe und
Prüfsumme; ein beschädigtes Paket wird nie ausgeliefert, die Verwaltung zeigt den Grund. Ohne Paket (eigener Build)
verweist der Server auf das APK im GitHub-Release derselben Version (`EstrichOne:AppUpdates:AndroidFallbackUrl`).

In der App:

1. **Aktualisieren** überträgt zuerst alle offenen Änderungen. Gelingt das nicht vollständig (kein Netz), fragt die App
   nach; die Änderungen bleiben auch beim Update erhalten.
2. Download vom eigenen Server mit Fortschritt; ein abgebrochener Download wird an derselben Stelle fortgesetzt.
3. Prüfung der SHA-256-Prüfsumme; bei Abweichung wird die Datei verworfen.
4. Beim ersten Mal fragt Android, ob EstrichONE Apps installieren darf („Aus dieser Quelle zulassen“). Nach der
   Rückkehr aus den Einstellungen geht es von selbst weiter.
5. Installation über den PackageInstaller; ab Android 12 ohne weitere Rückfrage, wenn EstrichONE die App selbst
   installiert hat. Die App startet danach neu und zeigt einmal **Neu in dieser Version**.

**Signaturschlüssel:** Android installiert ein Update nur, wenn es mit demselben Schlüssel signiert ist wie die
installierte App. Ohne die Secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` (und
optional `ANDROID_KEY_PASSWORD`) entsteht nur ein Debug-APK mit einem bei jedem Lauf **neuen** Schlüssel – Updates
sind dann nur nach Deinstallation möglich. Die App erkennt einen anderen Schlüssel vor dem Download und erklärt den
Weg (alles übertragen, deinstallieren, neu installieren, neu anmelden). Den Keystore sicher aufbewahren: Geht er
verloren, müssen alle Geräte die App einmal neu installieren.

Erstinstallation ohne App Store: In der Verwaltung unter **App-Updates** › **Android-App laden** steht ein QR-Code mit
dem Download-Link des Servers.

## iOS

iOS-Apps kommen aus dem App Store, über TestFlight oder die Geräteverwaltung (MDM). Die App installiert nicht selbst;
sie zeigt Hinweis bzw. Sperre und öffnet den in der Verwaltung hinterlegten Link (ohne Link den App Store). Vorher
überträgt sie offene Änderungen; was nicht übertragen werden kann, bleibt gespeichert und wird nach dem Update gesendet.

App Store und TestFlight kennen keine Vorabkennung: Das Release setzt `MARKETING_VERSION` auf den Kern des Tags
(1.1.0-preview.2 → 1.1.0) und `CURRENT_PROJECT_VERSION` auf die Laufnummer. Weil eine iOS-Version erst nach der
Prüfung durch Apple bereitsteht, wird unter **Dort verfügbare Version** eingetragen, was im App Store bzw. in TestFlight
tatsächlich verfügbar ist (leer = Serverversion); daran orientieren sich Hinweis und Sperre.

## Lokale Daten über Updates hinweg

- **iOS:** Die verschlüsselte Datenbank hat eine fortlaufende Schemaversion (`PRAGMA user_version`). Migrationen
  werden nur angehängt, laufen samt neuer Versionsnummer in einer Transaktion und löschen nie Daten. Schlägt eine fehl,
  bleibt der alte Stand vollständig erhalten. Eine neuere Datenbank (App zurückgestuft) bleibt unangetastet. Tests:
  `EstrichCoreTests/AppUpdateTests.swift` („Datenbank-Upgrade“).
- **Android:** gleiches Verfahren (`StorageSchema`, SQLCipher). Version 1 entspricht dem Stand vor der Versionierung
  und ist idempotent; destruktive Anweisungen sind in Migrationen verboten (Test `StorageSchemaTests`). Ein nicht
  lesbarer Outbox-Eintrag blockiert die übrigen nicht und bleibt in der Datenbank.

## Versionshinweise für die Apps

„Neu in dieser Version“ entsteht aus `docs/releases/v<version>.md` (im API-Image unter `/app/releases`). Abschnitte zu
Server-Update, Installation und Befehle werden weggelassen. Gibt es Abschnitte wie `## Apps`, `## iPhone und iPad` oder
`## Android`, zeigt die App nur diese (passend zur Plattform).
