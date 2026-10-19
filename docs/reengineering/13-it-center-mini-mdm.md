# 13 – EstrichONE IT Center (Mini-MDM)

**Status heute:** nicht vorhanden. `device_sessions` wird befüllt, aber nicht ausgewertet (01, ❌-6).
**[Verbindlich]** Ein einfaches, ehrliches Werkzeug, kein Nachbau von Intune und kein vorgetäuschtes
Betriebssystem-MDM.

## 1. Was EstrichONE selbst kann – und was nicht

| Fähigkeit | Selbst umsetzbar? | Grenze |
|---|---|---|
| Geräteinventar (nur EstrichONE-Installationen) | ✅ | Erfasst werden nur Geräte, auf denen die App eingerichtet ist |
| Freigabe, Sperre, Widerruf des **EstrichONE-Zugangs** | ✅ sofort wirksam | serverseitig: Tokens werden bei der nächsten Anfrage abgelehnt |
| Sitzungen widerrufen | ✅ | wie oben |
| **Lokale EstrichONE-Daten löschen** | ✅ beim nächsten Serverkontakt | **Ein Gerät, das offline bleibt, löscht nichts.** Es gibt keine Garantie und keinen Zeitpunkt. Die Daten sind bis dahin verschlüsselt (11) und nach Ablauf der Offline-Höchstdauer (Richtlinie) für Benutzer nicht mehr zugänglich. |
| App-Version prüfen, Mindestversion erzwingen | ✅ | Update selbst installiert der Benutzer bzw. der Store |
| Richtlinien **innerhalb der App** (Sperrzeit, Offline erlaubt, Cache-Dauer) | ✅ | gelten nur für EstrichONE |
| Geräte-PIN/Bildschirmsperre des **Betriebssystems** erzwingen | ❌ | nur über echtes MDM. EstrichONE kann aber **prüfen**, ob eine Gerätesperre eingerichtet ist (iOS `LAContext.canEvaluatePolicy(.deviceOwnerAuthentication)`, Android `KeyguardManager.isDeviceSecure`), und bei fehlender Sperre den Zugriff verweigern (Richtlinie). |
| Andere Apps verwalten, Gerät orten, Gerät komplett löschen, WLAN-Profile, Zertifikate verteilen | ❌ | nur über Apple MDM, Android Enterprise oder Intune |

In der Oberfläche heißen die Aktionen deshalb **„EstrichONE-Daten auf dem Gerät löschen“** und
**„EstrichONE-Zugang sperren“**, niemals „Gerät löschen“.

## 2. Inventar – erfasste Daten

| Feld | iOS | Android | Windows | Quelle |
|---|---|---|---|---|
| Gerätename | vom Benutzer bzw. Admin vergeben (iOS liefert seit iOS 16 ohne Sonderberechtigung nur „iPhone“) | `Settings.Global.DEVICE_NAME` bzw. Vorgabe | `Environment.MachineName` | Einrichtung |
| Modell | `utsname.machine` (z. B. `iPhone16,2`) → Klartext per Zuordnungstabelle | `Build.MANUFACTURER`, `Build.MODEL` | WMI `Win32_ComputerSystem.Model` | App |
| Plattform, OS-Version | `UIDevice.systemVersion` | `Build.VERSION.RELEASE`, `SDK_INT` | `Environment.OSVersion` | App |
| App-Version, Build | Bundle | `PackageInfo` | Package | App |
| Gerätetyp | persönlich / Pool | | persönlich | Server |
| Zugeordneter Benutzer (persönlich) bzw. letzte Benutzer (Pool) | | | | Server |
| Registriert am, freigegeben von/am | | | | Server |
| Letzte Verbindung, letzte erfolgreiche Synchronisation | | | | Server |
| Ausstehende Vorgänge (Anzahl, ohne Inhalt) | | | | Heartbeat |
| Gerätesperre eingerichtet (ja/nein) | `LAContext` | `KeyguardManager` | – | App |
| Supportstatus | | | | Helpdesk-Verknüpfung |

**Nicht erfasst:** IMEI, Seriennummer, MAC-Adresse, Telefonnummer, installierte Apps, Standort. Diese Daten sind
größtenteils ohnehin gesperrt und für den Zweck nicht nötig (Datenminimierung, 17). Kein Fingerprinting.

## 3. Verwaltungsfunktionen

| Aktion | Wirkung | Recht |
|---|---|---|
| Gerät einrichten (QR-Code erzeugen) | einmaliger Code, 15 min, mit Typ und Name | `devices.enroll` |
| Freigeben / Ablehnen | `pending` → `active` / gelöscht | `devices.manage` |
| Umbenennen, Typ ändern, Benutzer zuordnen | sofort, App übernimmt beim nächsten Kontakt | `devices.manage` |
| Sitzungen widerrufen | alle Benutzersitzungen dieses Geräts ungültig, erneute Anmeldung nötig | `devices.manage` |
| Zugang sperren / entsperren | `locked` | `devices.manage` |
| EstrichONE-Daten löschen | Befehl `wipe` in `device_commands`; Ausführung beim nächsten Kontakt; Bestätigung mit Zeitstempel | `devices.wipe` (gesondert) |
| Ungesendete Daten abholen, dann löschen | Befehl `flush_then_wipe`; das Gerät übermittelt ausstehende Vorgänge zuerst. Server nimmt sie trotz Sperre an, markiert als „nach Sperre eingeliefert“ | `devices.wipe` |
| Aus Verwaltung entfernen | `retired`; Eintrag bleibt 90 Tage für die Nachvollziehbarkeit | `devices.manage` |
| Sync-Probleme anzeigen | Liste der Geräte mit Fehlern, ältester ausstehender Vorgang, letzte Fehlermeldung (technischer Code) | `devices.read` |

**Ausführung des Löschbefehls auf dem Gerät:**
1. Beim nächsten API-Aufruf antwortet der Server `DEVICE_WIPE_REQUESTED`. Bei konfigurierter Push-Zustellung
   kommt eine stille Benachrichtigung hinzu; das System garantiert deren Zustellung **nicht**.
2. Die App löscht die Schlüssel (zuerst, damit die Daten sofort unlesbar sind), dann Container, Dateien und
   Einstellungen.
3. Die App sendet eine Bestätigung, wenn noch eine Verbindung besteht.
4. Das IT Center zeigt „Löschung bestätigt am …“ oder „ausstehend seit …“.

## 4. Sicherheitsrichtlinien

Global mit Ausnahmen je Gerätetyp (persönlich/Pool). Erzwingt die App und prüft zusätzlich der Server.

| Richtlinie | Standard persönlich | Standard Pool | Durchsetzung |
|---|---|---|---|
| App-Sperre nach Inaktivität | 15 min | 5 min | App |
| Biometrisches Entsperren erlaubt | ja | nein | App |
| Erneute Online-Anmeldung spätestens nach | 30 Tagen | 7 Tagen | Server (Refresh-Token-Laufzeit) |
| Offline-Datenspeicherung erlaubt | ja | ja | App + Server (Sync-Umfang) |
| Höchstdauer ohne Serverkontakt | 14 Tage | 3 Tage | App sperrt lokale Daten danach bis zur Online-Anmeldung |
| Aufbewahrung lokaler Kopien (nicht geänderte Bauakten) | 30 Tage | 7 Tage | App |
| Gerätesperre des Betriebssystems erforderlich | ja | ja | App prüft (siehe 1) |
| Zulässige App-Mindestversion | – | – | Server (`426 CLIENT_TOO_OLD`) |
| Erneute Registrierung erforderlich | – | – | Server: Gerät → `pending` |
| Bildschirmfotos verhindern (Android `FLAG_SECURE`; iOS nur Abdeckung im App-Umschalter, kein Verbot möglich) | nein | ja | App |

## 5. Optionale MDM-Erweiterung (später)

- **Managed App Configuration:** Apple MDM (`com.apple.configuration.managed`) und Android Enterprise (Managed
  Configurations) können der App **Serveradresse und Gerätetyp vorgeben**. Damit entfällt die Einrichtung
  per QR-Code. Das funktioniert mit jedem MDM ohne Herstellerbindung. **[Vormerken 1.1]**, geringer Aufwand.
- Intune App Protection (MAM): erfordert das Intune SDK. **[Vormerken 2.0]**, nur bei Bedarf.
- Direkte Anbindung an MDM-APIs zum Auslösen eines Betriebssystem-Löschens: **nicht geplant**. Dafür ist
  das MDM selbst zuständig.

## 6. Oberfläche (Web, Desktop)

```
IT Center › Geräte                                             [Gerät einrichten]
Filter: Alle · Wartet auf Freigabe (2) · Probleme (1) · Pool · Persönlich       🔍
┌─────────────────────┬──────────┬───────────────┬────────────┬────────────┬──────────────┐
│ Gerät               │ Typ      │ Benutzer      │ App        │ Zuletzt    │ Status       │
├─────────────────────┼──────────┼───────────────┼────────────┼────────────┼──────────────┤
│ Fahrzeug 3          │ Pool     │ M. Huber (zul)│ 1.0.4      │ vor 2 Min. │ ● Aktiv      │
│ iPhone von L. Brandl│ Persönl. │ L. Brandl     │ 1.0.2 ⚠    │ vor 9 Tg.  │ ⚠ 14 ausst.  │
│ Pixel 8             │ Pool     │ –             │ 1.0.4      │ –          │ ◐ Wartet     │
└─────────────────────┴──────────┴───────────────┴────────────┴────────────┴──────────────┘
Detailbereich: Kenndaten · Sitzungen · Verlauf (Audit) · Tickets · Aktionen
```

## 7. Personenbezogene Daten im IT Center

Siehe 17, Abschnitt 4: Verzeichnis der Verarbeitung mit Zweck, Datenarten, Speicherdauer und Zugriff.
