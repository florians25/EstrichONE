# Benachrichtigungen und Pushnachrichten

EstrichONE hat ein Postfach je Benutzer. Jede Benachrichtigung landet dort (gelesen/ungelesen, Link auf das Objekt)
und wird – wenn eingerichtet – zusätzlich als Pushnachricht zugestellt: im Browser per **Web Push** (PWA, VAPID)
und auf dem iPhone/iPad per **APNs** (Token-Anmeldung mit .p8-Schlüssel). Ohne Einrichtung bleibt es beim Postfach;
nichts bricht ab.

## Ereignisse

| Ereignis | Empfänger | Kategorie |
|---|---|---|
| Antwort/Statusänderung an einem Helpdesk-Ticket | Melder bzw. zuständige Person | `ticket` |
| Dokumentfreigabe angefordert | zugewiesene Person bzw. alle Mitglieder der Rolle | `approval` (dringend) |
| Dokumentfreigabe entschieden | anfragende Person | `approval` |
| Plan freigegeben / erneut freigegeben („Plan geändert“) | alle eingeplanten Mitarbeiter der Tage | `planning` |
| Zuweisung zu einer Bauakte (Mitglied oder zuständig) | die neu zugewiesene Person | `bauakte` |
| Frist: zugewiesene Aufgabe/Mangel heute oder morgen fällig, überfällig (höchstens 7 Tage, einmal je Tag) | zugewiesene Person | `deadline` |
| Nachricht aus dem Büro | ausgewählte Personen, Rollen, Kolonnen oder alle | `message` |

Die handelnde Person selbst wird nie benachrichtigt.

## API (alle unter `/api/v1`, Anmeldung erforderlich)

### Postfach
| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/notifications?unread=true&category=ticket&limit=50&before=<Zeitpunkt>` | eigenes Postfach (`items`, `unreadCount`) |
| GET | `/notifications/unread-count` | `{ "count": 3 }` (Glocke, App-Badge) |
| POST | `/notifications/{id}/read` · `/notifications/{id}/unread` | gelesen / ungelesen |
| POST | `/notifications/read-all` | alles gelesen |
| DELETE | `/notifications/{id}` | Eintrag entfernen |
| POST | `/notifications/test` | Testnachricht an sich selbst (Postfach + Push) |

Eintrag: `id, category, urgency (low|normal|high), title, body, link, objectType, objectId, senderName, createdAt, readAt`.
`link` ist immer ein relativer Pfad der Weboberfläche (z. B. `/bauakten/{id}`, `/it-center/helpdesk/{id}`,
`/planung/wer-faehrt-wohin?tag=2026-11-03`); Apps bilden daraus ihren Deep Link. `objectType` ∈ `bauakte`, `ticket`,
`approval`, `plan`, `workItem`.

### Geräte (Push-Token)
| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/notifications/push-config` | `webPushEnabled`, `vapidPublicKey` (für `PushManager.subscribe`), `apnsEnabled`, `showContent` |
| GET | `/notifications/devices` | eigene registrierte Geräte/Browser |
| POST | `/notifications/devices` | registrieren oder auffrischen (bei jedem App-Start aufrufen) |
| DELETE | `/notifications/devices/{id}` | Gerät entfernen |
| DELETE | `/notifications/devices?token=<token>` | abmelden per Token (beim Abmelden in der App) |

Registrierung:

```json
// iOS (Token als Hex-Text aus didRegisterForRemoteNotificationsWithDeviceToken)
{ "platform": "ios", "token": "a1b2…", "sandbox": false, "bundleId": "de.estrichone.EstrichONE", "deviceName": "iPhone Lukas", "appVersion": "0.1.0" }
// Browser (PushSubscription.toJSON())
{ "platform": "web", "token": "<endpoint>", "p256dh": "<keys.p256dh>", "auth": "<keys.auth>", "deviceName": "Chrome unter Windows" }
// Android (FCM-Token; Versand über FCM ist noch nicht angebunden – nur Postfach)
{ "platform": "android", "token": "<fcm-token>" }
```

Ein Token gehört immer dem zuletzt angemeldeten Benutzer (Gerätewechsel). `sandbox: true` für Entwicklungs-Builds aus
Xcode. Web-Push-Endpunkte werden nur für bekannte Push-Dienste angenommen (Google, Mozilla, Apple, Microsoft;
weitere über `EstrichOne:Push:WebPushHosts`) und nur per HTTPS – der Server ruft keine beliebigen Adressen auf.

### Senden (Recht `notifications.send`)
| Methode | Pfad | Zweck |
|---|---|---|
| POST | `/notifications/send` | `{ title, body?, link?, urgency, userIds?, roleIds?, crewIds?, all }` |
| GET | `/notifications/sent` | Verlauf mit Zählern: Empfänger, gelesen, per Push zugestellt, nur Postfach, fehlgeschlagen, wartend |
| GET | `/notifications/sent/{id}` | Empfänger einzeln mit `readAt`, `pushState`, `pushError` |

`pushState`: `pending` (wartet), `sent` (vom Push-Dienst angenommen), `noDevice` (kein Gerät registriert),
`inAppOnly` (Kanal nicht eingerichtet, z. B. APNs ohne Schlüssel), `failed`, `expired` (über 24 h liegen geblieben).

Das Recht `notifications.send` haben Administratoren und – bei Neuinstallationen – die Rolle „Büro und Bauleitung“.
Bei bestehenden Installationen in **Verwaltung → Rollen** ergänzen.

### Einrichtung (Recht `settings.manage`)
`GET/PUT /notifications/settings` – Web Push an/aus, Kontakt (`mailto:`/`https://`), APNs (Key-ID, Team-ID,
Bundle-ID, Produktion/Sandbox, .p8-Schlüssel), „Inhalt in Pushnachrichten zeigen“. Der .p8-Schlüssel wird nur
verschlüsselt gespeichert und nie zurückgegeben (`apnsKeyStored`).

## Web Push (Browser)

1. Nichts zu tun: Beim ersten Bedarf erzeugt der Server ein VAPID-Schlüsselpaar (P-256). Der private Schlüssel liegt
   verschlüsselt (ASP.NET Data Protection, AES-256-GCM) in der Datenbank; Schlüsselring in `data/files/.keys`
   (Teil der Sicherung).
2. Jeder Benutzer schaltet Pushnachrichten unter **Konto → Benachrichtigungen** für seinen Browser ein. Der Service
   Worker (`/sw.js`) zeigt die Nachricht und öffnet beim Klick den Link.
3. Voraussetzung: HTTPS (Web Push funktioniert nicht über `http://`, außer `localhost`). iPhone/iPad: nur als
   Web-App auf dem Home-Bildschirm (ab iOS 16.4).

Die Nachricht ist Ende-zu-Ende verschlüsselt (RFC 8291 „aes128gcm“); der Push-Dienst des Browserherstellers sieht den
Inhalt nicht.

## APNs (iOS-App)

Voreingestellt (deploy/compose.yml): Team-ID `9GVGJKZ9H5`, Key-ID `HT48M8LMF3`, Bundle-ID `de.estrichone.EstrichONE`.
Es fehlt nur noch der `.p8`-Schlüssel: unter **Einstellungen → Benachrichtigungen → Apple Push (APNs)** hochladen,
APNs einschalten, speichern, „Testnachricht“. Xcode-Builds melden sich automatisch als Sandbox-Gerät an.

Benötigt ein Apple Developer Program-Konto.

1. **Apple Developer → Certificates, Identifiers & Profiles → Keys → +**: Name „EstrichONE Push“, Haken bei
   **Apple Push Notifications service (APNs)**, anlegen, `.p8` herunterladen (nur einmal möglich!). Key-ID notieren.
2. Team-ID: oben rechts im Developer-Konto (10 Zeichen).
3. Bundle-ID der App (z. B. `de.estrichone.EstrichONE`), Capability **Push Notifications** muss für diese App-ID aktiv sein
   (siehe `ios/README.md`).
4. In EstrichONE: **Einstellungen → Benachrichtigungen → Apple Push (APNs)**: Key-ID, Team-ID, Bundle-ID eintragen,
   `.p8` hochladen, „Produktion“ für App Store/TestFlight, „Sandbox“ für Xcode-Builds. Speichern, dann „Testnachricht“.
   Alternativ per `.env`: `ESTRICHONE_APNS_KEY_ID`, `ESTRICHONE_APNS_TEAM_ID`, `ESTRICHONE_APNS_BUNDLE_ID`,
   `ESTRICHONE_APNS_KEY_PATH` (Datei z. B. unter `data/files/.push/AuthKey.p8`), `ESTRICHONE_APNS_PRODUCTION`.
   Einstellungen in der Oberfläche haben Vorrang.
5. Ausgehende Verbindung des Servers zu `api.push.apple.com:443` (bzw. `api.sandbox.push.apple.com`) per HTTP/2
   muss erlaubt sein.

Versand: `POST https://api.push.apple.com/3/device/<token>` mit `authorization: bearer <JWT ES256, kid=Key-ID,
iss=Team-ID>` (alle 45 Minuten erneuert), `apns-topic: <Bundle-ID>`, `apns-push-type: alert`, Priorität 10 (5 bei
„niedrig“), Ablauf 24 h. Antwortet APNs mit `BadDeviceToken`/`Unregistered`/410, wird das Gerät entfernt.

Payload:

```json
{
  "aps": { "alert": { "title": "Plan freigegeben: Mo, 03.11.", "body": "Neubau Halle Ost" }, "sound": "default",
           "badge": 3, "thread-id": "planning", "mutable-content": 1 },
  "notificationId": "0193…", "link": "/planung/wer-faehrt-wohin?tag=2026-11-03", "category": "planning"
}
```

Die App öffnet beim Tippen `link` (Deep Link) und markiert `notificationId` per `POST /notifications/{id}/read`.

## Datenschutz und Sicherheit

- Pushnachrichten enthalten nur Titel, einen kurzen Hinweis (max. 180 Zeichen) und den Link – keine Kommentare,
  Ticket-Texte, Preise oder Personaldaten. Mit „Inhalt in Pushnachrichten zeigen“ = aus steht auf dem Sperrbildschirm
  nur „Neue Benachrichtigung“; den Inhalt lädt die App nach der Anmeldung aus dem Postfach.
- Links sind nur relative Pfade der eigenen Oberfläche (kein offener Weiterleiter).
- Private Schlüssel (VAPID, APNs) nur verschlüsselt in der Datenbank, nie in Antworten oder Protokollen.
- Gesendete Nachrichten werden im Audit-Log vermerkt (`notifications.sent`).
