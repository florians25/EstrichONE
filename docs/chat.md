# Chat (Modul)

Betriebs-Messenger als Ersatz für WhatsApp-Gruppen: Einzel- und Gruppenchats (frei, je Kolonne, je Bauakte, Ankündigungsgruppen),
Fotos und PDFs (verdichtet, verschlüsselt), Standort, Antworten mit Zitat, Reaktionen, @Erwähnungen, Bearbeiten und
Löschen für alle, Weiterleiten, Anheften, Suche, Medienübersicht, Lesebestätigungen, Echtzeit (Server-Sent Events) und
Pushnachrichten je Nachricht. **Keine Sprachnachrichten** – stattdessen Diktat (Spracherkennung auf dem Gerät bzw. im
Browser, Text vor dem Senden bearbeitbar; Audio wird nie gespeichert oder gesendet).

## Ein- und ausschalten

**Einstellungen → Module → Chat** (Recht `chat.admin`). Standard: **aus**.

Begründung: Ein Chat unter Beschäftigten ist eine technische Einrichtung, die Verhalten und Leistung erfassen kann –
mit Betriebsrat mitbestimmungspflichtig (§ 87 Abs. 1 Nr. 6 BetrVG) und ein Eintrag im Verzeichnis der
Verarbeitungstätigkeiten (Art. 30 DSGVO). Der Betrieb soll ihn deshalb bewusst einschalten, nicht nebenbei bekommen.

Abgeschaltet antworten alle Chat-Endpunkte (außer `/chat/status` und `/chat/admin/*`) mit **409** und Problem-Code
`MODULE_DISABLED`. Daten bleiben erhalten (Löschfrist läuft weiter); Export und Löschung für Betroffenenrechte
funktionieren auch bei abgeschaltetem Modul.

## Microsoft Teams (Chat-Modus)

Unter **Einstellungen › Integrationen › Microsoft Teams** wählt der Betrieb „EstrichONE-Chat“ (Standard), „Microsoft
Teams“ oder „beides“ ([Microsoft Teams](microsoft-teams.md)). Im Modus „Microsoft Teams“ ist der Chat ein **Archiv**:
Lesen, gelesen markieren, stumm schalten, Anhang in Bauakte übernehmen und Export gehen weiter; alle anderen schreibenden
Endpunkte antworten mit **409** `CHAT_READ_ONLY`. Kolonnen- und Baustellengruppen werden nicht mehr angepasst. Es wird
nichts gelöscht. `GET /chat/status` enthält `mode` und `readOnly`; Web und Apps zeigen „Chat-Archiv“ und „In Teams öffnen“.

## Rechte

| Recht | Bedeutung | Standardrollen |
|---|---|---|
| `chat.use` | Chat nutzen, Einzelchats beginnen, in eigenen Unterhaltungen schreiben | Büro und Bauleitung, Baustelle, Administrator |
| `chat.admin` | Gruppen anlegen/ändern/löschen, fremde Nachrichten löschen (Moderation), Personen sperren, Einstellungen, Export/Löschung je Person | Büro und Bauleitung, Administrator |

Zusätzlich je Gruppe: **Gruppenadmins** (die anlegende Person und von ihr bestimmte Mitglieder) dürfen Name, Bild,
Beschreibung, Mitglieder, Admins und „nur Admins schreiben“ ändern und die Gruppe löschen – auch ohne `chat.admin`.
Baustellen-Gruppen legen Team-Mitglieder der Bauakte oder `chat.admin` an. „In Bauakte übernehmen“ braucht `files.upload`.

Bestehende Installationen erhalten diese Rechte für die Standardrollen einmalig beim nächsten `migrate`.
Nur Betriebsangehörige (angemeldete, aktive Benutzer mit Recht) können chatten; es gibt keinen Zugang für Kunden.

## Datenschutz

- **Verschlüsselt gespeichert:** Nachrichtentexte liegen nur verschlüsselt in der Datenbank (ASP.NET Data Protection,
  AES-256-GCM, Zweck `EstrichOne.Chat.Messages.v1`). Der Schlüsselring liegt in `data/files/.keys` und ist Teil der
  Sicherung – ohne ihn sind wiederhergestellte Nachrichten nicht lesbar (Antwort dann `text: null`).
- **Löschfrist:** Nachrichten älter als *n* Tage (Standard 90, einstellbar 7–730) werden stündlich endgültig gelöscht.
- **Pushnachrichten ohne Inhalt:** Standard ist „Neue Nachricht im Chat“ mit Absender bzw. Gruppe. Nur wenn
  „Vorschau in Pushnachrichten“ eingeschaltet ist, steht der Anfang des Textes (max. 120 Zeichen) in Push und Postfach.
- **Audit ohne Inhalte:** protokolliert werden nur Ereignisse (`chat.conversation_created`, `chat.group_changed`,
  `chat.conversation_deleted`, `chat.message_deleted` durch Verwaltung, `chat.settings_saved`, `chat.data_exported`,
  `chat.data_erased`) mit Anzahlen – nie Texte.
- **Betroffenenrechte:** Auskunft/Datenübertragbarkeit (`GET /chat/export`, eigene Daten als JSON;
  `GET /chat/admin/users/{id}/export` durch die Verwaltung) und Löschung (`DELETE /chat/admin/users/{id}/messages`:
  alle Nachrichten der Person, ihre Einzelchats, ihre Mitgliedschaften).
- Hinweise im Postfach werden beim Lesen der Unterhaltung automatisch als gelesen markiert.
- **Anhänge verschlüsselt:** Fotos/PDFs, Vorschaubilder, Gruppenbilder und Dateinamen liegen nur verschlüsselt im
  Dateispeicher (`data/files/chat/…`, Data Protection, Zweck `EstrichOne.Chat.Attachments.v1`). Vor dem Speichern prüft
  der Dateiservice den Typ am Inhalt (JPEG, PNG, WebP, HEIC → JPEG, PDF) und verdichtet wie bei Bauakte-Fotos
  (Einstellungen → Speicher). Höchstens 25 MB je Datei, 10 je Nachricht. Löschfrist, „Löschen für alle“, Löschen der
  Unterhaltung und Löschung je Person entfernen auch die Dateien; nicht gesendete Uploads verfallen nach 24 Stunden.
- **Standort** (Breite/Länge/Bezeichnung) wird ebenfalls nur verschlüsselt gespeichert.
- **Suche** entschlüsselt serverseitig die neuesten 5000 Nachrichten der eigenen Unterhaltungen – es gibt keinen
  Klartext-Suchindex.
- **Lesebestätigungen** lassen sich je Person abschalten (andere sehen dann nicht, wann sie gelesen hat).
- **Datenschutzhinweis** beim ersten Öffnen (Zweck, Löschfrist, Verschlüsselung, Rechte); Bestätigung je Person.
- **Moderation:** `chat.admin` entfernt Nachrichten (Audit `chat.message_deleted`) und sperrt Personen
  (`chat.user_blocked`; gesperrt = lesen ja, schreiben/reagieren nein).
- **Echtzeit ohne Inhalte:** Der Ereignisstrom enthält nur IDs; Inhalte holt der Client über die normalen Endpunkte.
- **Betriebsvereinbarung:** Vor dem Einschalten mit dem Betriebsrat regeln (Zweck: betriebliche Abstimmung; keine
  Leistungs-/Verhaltenskontrolle; keine Auswertung von Lese- oder Online-Zeiten; Löschfrist; wer `chat.admin` hat;
  private Nutzung; Diktat ohne Audiospeicherung). Lesebestätigungen und „… schreibt“ sind Komfort, keine Kontrolle –
  sie werden nicht protokolliert und nicht ausgewertet.

## API (alle unter `/api/v1/chat`, Anmeldung erforderlich)

### Status (immer erreichbar)
`GET /chat/status` → `{ enabled, canUse, canAdmin, unreadCount, unreadConversations, retentionDays, showPreview,
privacyAccepted, blocked, editWindowMinutes, attachmentsEnabled, maxAttachmentBytes, readReceipts, personalShowPreview,
realtime: "sse" }`. Clients zeigen den Chat nur bei `canUse = true`; App-Badge = `unreadConversations` (ungelesene Chats).

### Persönliche Einstellungen
`GET/PUT /chat/me/settings` – `{ showPreview, readReceipts, privacyAccepted }` (PUT, alles optional) →
`{ showPreview, readReceipts, privacyAcceptedAt, blocked, previewAllowedByAdmin }`. Text in Pushnachrichten nur, wenn
`previewAllowedByAdmin` **und** `showPreview`.

### Unterhaltungen (`chat.use`)
| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/chat/contacts` | Personen für einen neuen Einzelchat (`[{id, name}]`) |
| GET | `/chat/conversations` | eigene Unterhaltungen, zuletzt aktive zuerst; markiert alles bis jetzt als **zugestellt** |
| POST | `/chat/conversations/direct` | `{ userId }` → vorhandenen Einzelchat oder neuen (200) |
| POST | `/chat/conversations` | Gruppe `{ title, memberIds?, crewId?, description?, announcementOnly?, adminIds? }` (`chat.admin`, 201) |
| GET | `/chat/conversations/{id}` | Kopf + Mitglieder (mit `isAdmin`, Zustell-/Lesestand), `canManage`, `pinned` (angeheftete Nachrichten), `bauakteTitle` |
| PUT | `/chat/conversations/{id}` | `{ title?, description?, memberIds?, adminIds?, announcementOnly? }` (Gruppenadmin oder `chat.admin`) |
| DELETE | `/chat/conversations/{id}` | Unterhaltung samt Nachrichten und Anhängen löschen (Gruppenadmin oder `chat.admin`) |
| POST | `/chat/conversations/{id}/leave` | Gruppe verlassen (letzter Admin → ältestes Mitglied wird Admin; automatische Mitglieder: 422) |
| PUT/DELETE/GET | `/chat/conversations/{id}/image` | Gruppenbild setzen (Bild roh im Body → 256 px WebP), entfernen, abrufen |
| POST | `/chat/conversations/{id}/mute` | `{ muted, minutes? }` – stumm für 60/480/10080 Minuten oder ohne `minutes` immer; @Erwähnungen kommen trotzdem |
| POST | `/chat/conversations/{id}/typing` | „… schreibt“ an die anderen (höchstens alle 3 s senden; Anzeige 5 s) |
| GET | `/chat/crews` | Kolonnen für Gruppen (`chat.admin`) |
| GET | `/chat/bauakten/{bauakteId}/group` | Baustellen-Gruppe der Bauakte `{ conversationId?, title, member, members, canCreate }` (Link in der Bauakte) |
| POST | `/chat/bauakten/{bauakteId}/group` | Baustellen-Gruppe anlegen (201) bzw. vorhandene (200); Mitglieder = Zuständige(r) + Team mit Chat-Recht, automatisch nachgeführt |
| GET | `/chat/search?q=…&conversationId=` | Suche (mind. 2 Zeichen) → `{ items: [{ messageId, conversationId, conversationTitle, senderName, snippet, createdAt }], searched, truncated }` |

Unterhaltung (`ChatConversationSummary`):

```json
{ "id": "0193…", "kind": "direct", "title": "Lukas Berger", "crewId": null, "partnerUserId": "0192…",
  "members": [{ "id": "…", "name": "Lukas Berger" }, { "id": "…", "name": "Max Huber" }],
  "lastMessage": { "id": "…", "senderUserId": "…", "senderName": "Lukas Berger", "text": "Halle 2 ist fertig.",
                   "createdAt": "2026-10-28T07:12:03Z", "mine": false, "deleted": false },
  "lastActivityAt": "2026-10-28T07:12:03Z", "unreadCount": 1, "muted": false,
  "description": null, "bauakteId": null, "announcementOnly": false, "imageUpdatedAt": null, "mutedUntil": null,
  "isAdmin": false, "canPost": true }
```

`lastMessage.attachment` (`photo`/`document`) und `lastMessage.location` erlauben Vorschauen wie „📷 Foto“.
`canPost = false` in Ankündigungsgruppen (für Nicht-Admins) und für gesperrte Personen – Eingabefeld dann ausblenden.
Gruppen mit `bauakteId` sind Baustellen-Gruppen.

Gruppen mit `crewId` folgen der Kolonne: Mitglieder der Kolonne (mit Chat-Recht) kommen automatisch dazu bzw. fallen
heraus; von Hand hinzugefügte Personen bleiben (`fromCrew: false`).

### Nachrichten
| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/chat/conversations/{id}/messages?limit=50&before=<Zeit>` | Verlauf (aufsteigend sortiert), `hasMore` für ältere |
| GET | `/chat/conversations/{id}/messages?after=<Zeit>` | neue Nachrichten seit dem letzten Abruf (Polling) |
| POST | `/chat/conversations/{id}/messages` | `{ text?, clientMessageId?, replyToId?, attachmentIds?, location?, mentionIds? }` → 201 (bzw. 200, wenn `clientMessageId` schon gesendet) |
| POST | `/chat/conversations/{id}/read` | alles bis zur neuesten Nachricht gelesen (auch die Postfach-Hinweise) |
| GET | `/chat/messages/{id}` | eine Nachricht (nach Ereignis `message.updated`/`reaction`) |
| PUT | `/chat/messages/{id}` | eigene Nachricht bearbeiten `{ text, mentionIds? }` – nur innerhalb `editWindowMinutes` (Standard 15), danach 409 `EDIT_WINDOW_EXPIRED` |
| DELETE | `/chat/messages/{id}` | **Löschen für alle**: eigene Nachricht (`chat.admin`: jede) – Text, Anhänge, Reaktionen weg, Platzhalter „gelöscht“ bleibt |
| PUT | `/chat/messages/{id}/reaction` | `{ emoji }` setzt die eigene Reaktion (eine je Person), `{ emoji: null }` entfernt sie |
| POST | `/chat/messages/{id}/pin` | `{ pinned }` anheften/lösen |
| POST | `/chat/messages/{id}/forward` | `{ conversationIds }` (höchstens 20) → `{ messages }`; Anhänge werden mitkopiert, `forwarded: true` |
| POST | `/chat/conversations/{id}/attachments?fileName=IMG_1.jpg` | Foto/PDF roh im Body → 201 `ChatAttachmentResponse` (danach mit `attachmentIds` senden) |
| GET | `/chat/conversations/{id}/attachments?kind=photo\|document` | Medien-/Dokumente-Übersicht (neueste zuerst) |
| GET | `/chat/attachments/{id}/content?thumbnail=true&download=true` | Anhang entschlüsselt; Vorschaubild WebP 480 px |
| POST | `/chat/attachments/{id}/bauakte` | `{ bauakteId?, caption? }` – **in Bauakte übernehmen** (Foto mit Vorschauvarianten bzw. Dokument, Kategorie `chat`); ohne `bauakteId` die Bauakte der Baustellen-Gruppe |

Nachricht (`ChatMessageResponse`): `id, conversationId, senderUserId, senderName, text (null wenn gelöscht oder nur
Anhang), createdAt, mine, deleted, status, readBy, recipients, clientMessageId, replyTo { id, senderName, text, deleted,
attachment }, attachments [{ id, kind, fileName, contentType, sizeBytes, width, height, hasThumbnail }],
reactions [{ emoji, count, mine, names }], mentionedUserIds, editedAt, forwarded, pinnedAt,
location { latitude, longitude, label }, canEdit`.

@Erwähnungen: Der Client setzt `@Vorname Nachname` in den Text und schickt die IDs in `mentionIds` (nur Mitglieder).
Links im Text erkennt der Client (nur `http(s)://`).

`status` (nur eigene Nachrichten): `sent` (beim Server) → `delivered` (alle Empfänger haben abgeholt) → `read`
(alle haben gelesen). In Gruppen zusätzlich `readBy`/`recipients` („gelesen von 3 von 5“). Die Seite enthält
außerdem `members` mit `deliveredUpTo`/`readUpTo` je Person.

Text: 1–4000 Zeichen, nur Text (Zeilenumbrüche erlaubt). `clientMessageId` (UUID) vergibt die App für die
Offline-Warteschlange: wiederholtes Senden derselben ID liefert die vorhandene Nachricht.

### Echtzeit (Server-Sent Events)

`GET /chat/events` (Anmeldung wie üblich: Cookie im Web, `Authorization: Bearer` in Apps) liefert
`text/event-stream`:

```
retry: 3000
event: ready
data: {"type":"ready","at":"2026-11-03T07:00:00Z"}

event: message
data: {"type":"message","conversationId":"0193…","messageId":"0194…","userId":"0192…","userName":null}

: ping
```

Ereignisse: `message` (neu), `message.updated` (bearbeitet, angeheftet), `message.deleted`, `reaction`, `read`
(`userId` fehlt bei abgeschalteten Lesebestätigungen), `typing` (`userId`, `userName`), `conversation` (geändert/neu),
`conversation.removed`. **Nur IDs, nie Inhalte.** Herzschlag alle 20 s; der Server beendet die Verbindung nach 15 min
(Client verbindet neu, z. B. mit frischem Token). Fällt der Strom aus, auf Polling zurückfallen.

Proxy: `deploy/Caddyfile` reicht `/api/v1/chat/events` ungepuffert und unkomprimiert durch (`flush_interval -1`). Davor
geschaltete **Nginx Proxy Manager** puffern dank `X-Accel-Buffering: no` nicht; der Herzschlag hält sie unter dem
Standard-Timeout von 60 s. Weitere Einstellungen sind nicht nötig.

**Empfohlener Ablauf in Apps:** Liste mit `GET /chat/conversations` (bei Start, Rückkehr in den Vordergrund, Push).
Im Vordergrund `GET /chat/events` offen halten und bei `message` mit `after=<createdAt der letzten Nachricht>` nachladen,
bei `message.updated`/`reaction` `GET /chat/messages/{id}`; ohne Strom alle 5–10 s abfragen. Beim Anzeigen
`POST …/read` senden. Push-Link ist `/chat/{conversationId}` (Kategorie `chat`, `objectType` `chat`).

### Pushnachrichten

Je Nachricht ein Hinweis an jedes Mitglied (außer stumm geschaltete; @Erwähnte immer, Dringlichkeit hoch). Im Postfach
bleibt je Unterhaltung nur der neueste Hinweis. Titel: „Max Huber in Kolonne Nord“ bzw. „Nachricht von Max Huber“.
Text: Vorschau (max. 120 Zeichen, mit „📷 Foto: …“) nur, wenn Verwaltung und Person sie erlauben; sonst „Neue Nachricht
im Chat“ bzw. „Hat Sie erwähnt“.

APNs-Inhalt für Chat:

```json
{ "aps": { "alert": { "title": "Max Huber in Kolonne Nord", "body": "Morgen 6:30 am Lager." }, "sound": "default",
           "badge": 2, "thread-id": "chat-0193…", "category": "chat", "mutable-content": 1 },
  "notificationId": "…", "link": "/chat/0193…", "category": "chat", "objectType": "chat", "objectId": "0193…",
  "conversationId": "0193…" }
```

- `thread-id` = `chat-<conversationId ohne Bindestriche>` gruppiert je Unterhaltung; `apns-collapse-id` je Nachricht.
- `aps.category: chat` → App registriert eine `UNNotificationCategory` mit `UNTextInputNotificationAction`
  „Antworten“ (sendet `POST /chat/conversations/{conversationId}/messages` mit `clientMessageId`) und „Als gelesen
  markieren“ (`POST …/read`). Tippen öffnet `/chat/{conversationId}`.
- `badge` = ungelesene Hinweise, Chat je Unterhaltung einmal gezählt.
- Web Push: `tag` = Thread-ID (Browser ersetzt die Mitteilung je Unterhaltung).

### Verwaltung (`chat.admin`, auch bei abgeschaltetem Modul)
| Methode | Pfad | Zweck |
|---|---|---|
| GET/PUT | `/chat/admin/settings` | `{ enabled, retentionDays, showPreview, workersCanChatEachOther, editWindowMinutes, attachmentsEnabled, version? }` (+ `lastCleanupAt`, Zähler inkl. Anhänge und Speicher) |
| GET | `/chat/admin/users/{userId}/export` | Chatdaten einer Person (Auskunftsersuchen) |
| GET | `/chat/admin/users/{userId}/export/archive` | dieselben Daten als ZIP mit allen Anhängen |
| PUT | `/chat/admin/users/{userId}/block` | `{ blocked }` – Moderation: sperren/entsperren |
| DELETE | `/chat/admin/users/{userId}/messages` | Nachrichten (mit Anhängen), Reaktionen, Einzelchats, Mitgliedschaften und Einstellungen der Person löschen |

Eigene Daten mit Anhängen: `GET /chat/export/archive` (ZIP: `chat-export.json` + `anhaenge/`).

`workersCanChatEachOther = false`: Mitarbeiter ohne `chat.admin` können Einzelchats nur mit dem Büro (Personen mit
`chat.admin`) beginnen.

Fehlercodes: `MODULE_DISABLED` (409), `NOT_FOUND` (404, auch für fremde Unterhaltungen), `VALIDATION_FAILED` (422),
`PERMISSION_DENIED` (403), `ANNOUNCEMENT_ONLY` (403, nur Admins schreiben), `CHAT_BLOCKED` (403, gesperrt),
`FILE_TOO_LARGE` (413), `EDIT_WINDOW_EXPIRED` (409), `VERSION_CONFLICT` (412).

## Weboberfläche

`/chat` und `/chat/{id}`: Unterhaltungsliste mit Suche, Verlauf mit Tagestrennern, Anhänge per Büroklammer, Ziehen
oder Einfügen (Vorschau vor dem Senden), Antworten/Zitat, Reaktionen, @Erwähnungen (Auswahlliste), Bearbeiten,
Löschen für alle, Weiterleiten, Anheften, Suche über alle Chats, Medien-/Dokumente-Übersicht, Gruppeninfo mit Bild,
Beschreibung, Admins und „Nur Admins schreiben“, Diktat per Web Speech API (nur wenn der Browser es kann; Audio
verarbeitet der Browser bzw. dessen Anbieter – Hinweis im Tooltip). In der Bauakte führt „Baustellen-Chat“ in die
Gruppe bzw. legt sie an. Echtzeit über `GET /chat/events`, sonst Polling (4 s / 15 s).

## Hinweise für die Apps (Abgleich mit dem iOS-Entwurf)

Verbindlich ist die API oben. Gegenüber dem ersten App-Entwurf gilt:

| App-Entwurf | Server |
|---|---|
| `kind` direct\|group\|crew\|site\|announcement | `kind` direct\|group; Art ableiten: `crewId` → Kolonne, `bauakteId` → Baustelle, `announcementOnly` → Ankündigung |
| `canWrite`, `onlyAdminsWrite` | `canPost`, `announcementOnly` |
| `mentioned`/`unreadMentions` | `unreadMentions` (ungelesene Nachrichten mit @Erwähnung) |
| `avatarFileId` | `imageUpdatedAt`; Bild über `GET/PUT/DELETE /chat/conversations/{id}/image` |
| `pinned` (Liste/Bool) | Nachricht: `pinnedAt`; Unterhaltung: `pinned` in `GET /chat/conversations/{id}` |
| Mitglied `id`, `automatic`, `label` | `userId`, `fromCrew`, `isAdmin` (Bezeichnung selbst bilden) |
| `privacyNoticeVersion`, `POST /chat/privacy-notice {version}` | genau so (zusätzlich `privacyAccepted` im Status) |
| `mute {muted, until}` | `{ muted, minutes? }` oder `{ muted, until? }` |
| Senden `attachmentFileIds`, `mentionUserIds` | beides angenommen (ebenso `attachmentIds`, `mentionIds`) |
| Anhang `fileId`, `originalName` | `id`, `fileName` |
| `mentionUserIds` in der Nachricht | `mentionedUserIds` |
| `replyTo.attachmentKind` | `replyTo.attachment` |
| `kind: system` | gibt es nicht (keine Systemnachrichten) |
| `PUT /chat/messages/{id}` 409 `EDIT_WINDOW_EXPIRED` | genau so |
| Upload `POST /chat/attachments` + `PUT …/content` | ein Schritt: `POST /chat/conversations/{id}/attachments?fileName=` (roh) → `id`; Wiederholung ist unkritisch, nicht gesendete Uploads verfallen nach 24 h |
| `GET …/content?variant=thumb` | genau so (oder `thumbnail=true`) |
| `after` liefert auch geänderte Nachrichten | nein: Änderungen kommen als SSE `message.updated`/`reaction`/`message.deleted` → `GET /chat/messages/{id}`; beim Öffnen die letzte Seite neu laden |
| Suche `[{message, conversationId, conversationTitle}]` | `{ items: [{ messageId, conversationId, conversationTitle, senderName, snippet, createdAt }] }` |
| Medien `[{attachment, messageId, senderName, createdAt}]` | `[ChatAttachmentResponse]` mit `messageId`, `createdAt`, `uploadedByUserId` |
| SSE `typing {userName}`, Keep-alive ≤ 25 s | genau so (20 s); Ereignis `status` gibt es nicht – bei `message`/`read` `GET /chat/status` für das Badge; `Last-Event-ID` wird nicht ausgewertet (nach Neuverbindung abgleichen) |
| APNs `aps.category "chat"`, thread-id je Unterhaltung | genau so (`thread-id` = `chat-<id ohne Bindestriche>`, zusätzlich `objectType`/`objectId`/`conversationId`/`link`) |
