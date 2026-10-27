# Chat (Modul)

Mini-Chat zwischen Büro und Mitarbeitern – **nur Text**, aufgebaut wie ein Messenger: Unterhaltungsliste, Einzel- und
Gruppenchats (frei oder je Kolonne), Verlauf, Zustell- und Lesestatus. Kein Tippindikator, keine Anhänge.

## Ein- und ausschalten

**Einstellungen → Module → Chat** (Recht `chat.admin`). Standard: **aus**.

Begründung: Ein Chat unter Beschäftigten ist eine technische Einrichtung, die Verhalten und Leistung erfassen kann –
mit Betriebsrat mitbestimmungspflichtig (§ 87 Abs. 1 Nr. 6 BetrVG) und ein Eintrag im Verzeichnis der
Verarbeitungstätigkeiten (Art. 30 DSGVO). Der Betrieb soll ihn deshalb bewusst einschalten, nicht nebenbei bekommen.

Abgeschaltet antworten alle Chat-Endpunkte (außer `/chat/status` und `/chat/admin/*`) mit **409** und Problem-Code
`MODULE_DISABLED`. Daten bleiben erhalten (Löschfrist läuft weiter); Export und Löschung für Betroffenenrechte
funktionieren auch bei abgeschaltetem Modul.

## Rechte

| Recht | Bedeutung | Standardrollen |
|---|---|---|
| `chat.use` | Chat nutzen, Einzelchats beginnen, in eigenen Unterhaltungen schreiben | Büro und Bauleitung, Baustelle, Administrator |
| `chat.admin` | Gruppen anlegen/ändern/löschen, fremde Nachrichten löschen, Einstellungen, Export/Löschung je Person | Büro und Bauleitung, Administrator |

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

## API (alle unter `/api/v1/chat`, Anmeldung erforderlich)

### Status (immer erreichbar)
`GET /chat/status` → `{ enabled, canUse, canAdmin, unreadCount, unreadConversations, retentionDays, showPreview }`.
Clients zeigen den Chat nur bei `canUse = true`; `unreadCount` ist das Badge.

### Unterhaltungen (`chat.use`)
| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/chat/contacts` | Personen für einen neuen Einzelchat (`[{id, name}]`) |
| GET | `/chat/conversations` | eigene Unterhaltungen, zuletzt aktive zuerst; markiert alles bis jetzt als **zugestellt** |
| POST | `/chat/conversations/direct` | `{ userId }` → vorhandenen Einzelchat oder neuen (200) |
| POST | `/chat/conversations` | Gruppe `{ title, memberIds?, crewId? }` (`chat.admin`, 201) |
| GET | `/chat/conversations/{id}` | Kopf + Mitglieder mit Zustell-/Lesestand, `canManage` |
| PUT | `/chat/conversations/{id}` | Gruppe umbenennen / Mitglieder setzen `{ title?, memberIds? }` (`chat.admin`) |
| DELETE | `/chat/conversations/{id}` | Unterhaltung samt Nachrichten löschen (`chat.admin`) |
| POST | `/chat/conversations/{id}/mute` | `{ muted }` – keine Hinweise für diese Unterhaltung |
| GET | `/chat/crews` | Kolonnen für Gruppen (`chat.admin`) |

Unterhaltung (`ChatConversationSummary`):

```json
{ "id": "0193…", "kind": "direct", "title": "Lukas Berger", "crewId": null, "partnerUserId": "0192…",
  "members": [{ "id": "…", "name": "Lukas Berger" }, { "id": "…", "name": "Max Huber" }],
  "lastMessage": { "id": "…", "senderUserId": "…", "senderName": "Lukas Berger", "text": "Halle 2 ist fertig.",
                   "createdAt": "2026-10-28T07:12:03Z", "mine": false, "deleted": false },
  "lastActivityAt": "2026-10-28T07:12:03Z", "unreadCount": 1, "muted": false }
```

Gruppen mit `crewId` folgen der Kolonne: Mitglieder der Kolonne (mit Chat-Recht) kommen automatisch dazu bzw. fallen
heraus; von Hand hinzugefügte Personen bleiben (`fromCrew: false`).

### Nachrichten
| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/chat/conversations/{id}/messages?limit=50&before=<Zeit>` | Verlauf (aufsteigend sortiert), `hasMore` für ältere |
| GET | `/chat/conversations/{id}/messages?after=<Zeit>` | neue Nachrichten seit dem letzten Abruf (Polling) |
| POST | `/chat/conversations/{id}/messages` | `{ text, clientMessageId? }` → 201 (bzw. 200, wenn `clientMessageId` schon gesendet) |
| POST | `/chat/conversations/{id}/read` | alles bis zur neuesten Nachricht gelesen (auch die Postfach-Hinweise) |
| DELETE | `/chat/messages/{id}` | eigene Nachricht löschen (`chat.admin`: jede) – Text weg, Platzhalter „gelöscht“ bleibt |

Nachricht (`ChatMessageResponse`): `id, conversationId, senderUserId, senderName, text (null wenn gelöscht),
createdAt, mine, deleted, status, readBy, recipients, clientMessageId`.

`status` (nur eigene Nachrichten): `sent` (beim Server) → `delivered` (alle Empfänger haben abgeholt) → `read`
(alle haben gelesen). In Gruppen zusätzlich `readBy`/`recipients` („gelesen von 3 von 5“). Die Seite enthält
außerdem `members` mit `deliveredUpTo`/`readUpTo` je Person.

Text: 1–4000 Zeichen, nur Text (Zeilenumbrüche erlaubt). `clientMessageId` (UUID) vergibt die App für die
Offline-Warteschlange: wiederholtes Senden derselben ID liefert die vorhandene Nachricht.

**Empfohlener Ablauf in Apps:** Liste mit `GET /chat/conversations` (bei Start, Rückkehr in den Vordergrund, Push);
geöffnete Unterhaltung alle 5–10 s mit `after=<createdAt der letzten Nachricht>` abfragen und beim Anzeigen
`POST …/read` senden. Push-Link ist `/chat/{conversationId}` (Kategorie `chat`, `objectType` `chat`).

### Verwaltung (`chat.admin`, auch bei abgeschaltetem Modul)
| Methode | Pfad | Zweck |
|---|---|---|
| GET/PUT | `/chat/admin/settings` | `{ enabled, retentionDays, showPreview, workersCanChatEachOther, version? }` (+ `lastCleanupAt`, Zähler) |
| GET | `/chat/admin/users/{userId}/export` | Chatdaten einer Person (Auskunftsersuchen) |
| DELETE | `/chat/admin/users/{userId}/messages` | Nachrichten, Einzelchats und Mitgliedschaften der Person löschen |

`workersCanChatEachOther = false`: Mitarbeiter ohne `chat.admin` können Einzelchats nur mit dem Büro (Personen mit
`chat.admin`) beginnen.

Fehlercodes: `MODULE_DISABLED` (409), `NOT_FOUND` (404, auch für fremde Unterhaltungen), `VALIDATION_FAILED` (422),
`PERMISSION_DENIED` (403), `VERSION_CONFLICT` (412).
