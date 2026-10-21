# EstrichONE Sync-Protokoll v1

Verbindliche Spezifikation für alle Clients mit Offline-Betrieb (iOS, Android). Hintergrund und Begründung:
[docs/reengineering/15-offline-synchronisierung.md](reengineering/15-offline-synchronisierung.md).
Implementierung: `backend/src/EstrichOne.Api/Modules/Sync/`, Tests: `backend/tests/EstrichOne.Api.Tests/Sync/`.

## 1. Grundregeln

1. **Lokal zuerst.** Jede Änderung wird in **einer** lokalen Transaktion gespeichert: Datensatz + Outbox-Eintrag.
   „Gespeichert“ heißt für den Benutzer: lokal dauerhaft gespeichert.
2. **IDs erzeugt der Client** (UUIDv7, ADR 0006). Bauaktennummern vergibt ausschließlich der Server.
3. **Jede Operation hat eine `opId`** (UUIDv7). Der Server verarbeitet jede `opId` genau einmal. Eine Wiederholung
   liefert das gespeicherte Ergebnis mit `status: "duplicate"`.
4. **Reihenfolge:** Operationen werden in Outbox-Reihenfolge gesendet. Abhängige Operationen (z. B. Notiz zu einer
   neuen Bauakte) folgen ihrer Voraussetzung.
5. **Dateien** laufen über eine eigene Upload-Warteschlange (Abschnitt 6). Ihre Ankündigung ist eine normale Operation.
6. **Ablauf eines Sync-Laufs:** Uploads fortsetzen → `push` (bis Outbox leer) → `pull` (bis `hasMore = false`).

## 2. Push

`POST /api/v1/sync/push` (angemeldet, freigegebenes Gerät)

```json
{
  "operations": [
    { "opId": "0193…", "type": "bauakte.create", "entityId": "0193…", "baseVersion": null,
      "payload": { "title": "EFH Müller, Neubau", "city": "Landshut" } }
  ]
}
```

- Höchstens **100** Operationen je Anfrage, `opId` innerhalb der Anfrage eindeutig.
- Jede Operation läuft in einer **eigenen Transaktion**. Eine abgelehnte Operation blockiert die übrigen nicht.

Antwort (`200`):

```json
{ "results": [
  { "opId": "0193…", "status": "applied", "version": 1, "entity": { …aktueller Serverstand… } }
] }
```

| `status` | Bedeutung | Client |
|---|---|---|
| `applied` | übernommen | Outbox-Eintrag entfernen, `entity` lokal übernehmen |
| `duplicate` | früher schon verarbeitet; Felder wie damals | wie `applied` bzw. wie das damalige Ergebnis |
| `conflict` | teilweise übernommen, einzelne Felder parallel anders geändert | Eintrag entfernen, `entity` übernehmen, `conflicts` dem Benutzer anzeigen |
| `rejected` | abgelehnt (`code`) | siehe Tabelle |

| `code` bei `rejected` | Gespeichert? | Client |
|---|---|---|
| `VALIDATION_FAILED`, `NOT_FOUND`, `BAUAKTE_NOT_FOUND`, `PERMISSION_DENIED`, `ID_CONFLICT`, `UNKNOWN_CUSTOMER`, `UNKNOWN_OPERATION`, `FILE_TOO_LARGE` | ja (inkl. Nutzdaten, für die Administration nachvollziehbar) | Eintrag als „abgelehnt“ markieren, Benutzer informieren, **nicht** erneut senden |
| `OP_ID_CONFLICT` | – | neue `opId` erzeugen (sollte bei UUIDv7 nie vorkommen) |
| `RETRY_LATER` | nein | später unverändert erneut senden |

HTTP-Fehler (`401`, `403`, `5xx`, Netzwerk) betreffen die ganze Anfrage. Dann werden alle Einträge später unverändert
erneut gesendet. Das ist dank `opId` gefahrlos.

## 3. Feldweises Zusammenführen

Änderungen an bestehenden Datensätzen senden **je geändertem Feld** den neuen Wert (`changes`) und den Wert, den der
Client vor der Änderung kannte (`base`):

```json
{ "type": "bauakte.update", "entityId": "…",
  "payload": { "changes": { "city": "Ergolding" }, "base": { "city": null } } }
```

Der Server übernimmt ein Feld, wenn sein aktueller Wert gleich `base` **oder** schon gleich dem neuen Wert ist.
Andernfalls entsteht für dieses Feld ein Konflikt: Der Server behält seinen Wert und meldet
`{ "field", "serverValue", "clientValue" }`. **Alle anderen Felder werden trotzdem übernommen.**

Der Client fasst mehrere ungesendete Änderungen desselben Datensatzes zusammen, behält dabei aber den **ältesten**
`base`-Wert je Feld. Bereits gesendete Einträge verändert er nie.

## 4. Operationen v1

| Typ | Nutzdaten | Recht | Regeln |
|---|---|---|---|
| `bauakte.create` | `title`*, `street`, `postalCode`, `city`, `latitude`, `longitude`, `description`, `customerId`, `templateKey` | `bauakten.create` | Antwort enthält die vergebene `number` |
| `bauakte.update` | `changes`/`base`: `title`, `status`, `street`, `postalCode`, `city`, `latitude`, `longitude`, `description`, `customerId`, `responsibleUserId` | `bauakten.edit` + sichtbar | Abschnitt 3 |
| `bauakte.setStatus` | `status`* (`inquiry`/`planned`/`inProgress`/`drying`/`completed`/`cancelled`), `baseStatus` (Stand des Geräts), `reason` (Pflicht bei `cancelled`) | `bauakten.edit` + sichtbar | Storno gewinnt: auf eine inzwischen stornierte Bauakte → `ALREADY_CANCELLED` (außer `baseStatus` = `cancelled`); anderer Serverstatus als `baseStatus` → `STATUS_CHANGED`; nicht erlaubter Übergang (`BauakteStatusRules`, z. B. Storniert → Abgeschlossen) → `STATUS_NOT_ALLOWED`; gleicher Status → bestätigt |
| `bauakte.delete` | – | `bauakten.delete` + sichtbar | Papierkorb (Tombstone) |
| `note.create` | `bauakteId`*, `text`*, `pinned`, `color` | `notes.write` + Bauakte sichtbar | nur anfügen |
| `note.update` | `text`, `pinned`, `color` | Verfasser | letzte Änderung gilt (nur ein Verfasser) |
| `note.delete` | – | Verfasser | Tombstone |
| `contact.create` | `name`*, `kind` (`person`/`company`, Standard `person`), `street`, `postalCode`, `city`, `phone`, `email`, `notes` | `contacts.edit` oder `bauakten.create` + `contacts.read` | Kundenanlage bei der Schnellanlage; steht in der Outbox vor `bauakte.create` mit dieser `customerId`. Keine Kundennummer (vergibt das Büro). Gleiche ID + gleicher Name: bestätigt |
| `workitem.create` | `kind`* (`task`/`defect`), `title`*, `description`, `dueDate`, `assigneeUserId`, `bauakteId` | `work.edit` | |
| `workitem.update` | `changes`/`base`: `title`, `description`, `dueDate`, `assigneeUserId`, `status` | `work.edit` + sichtbar | Abschnitt 3 |
| `file.announce` | `bauakteId`, `kind`*, `originalName`*, `contentType`*, `sizeBytes`*, `sha256`*, `capturedAt`, `latitude`, `longitude`, `caption`, `category` (nur `document`: `offer`/`order`/`invoice`/`plan`/`delivery`/`acceptance`/`customer`/`office`/`other`) | `files.upload` | danach Upload (Abschnitt 6) |
| `file.update` | `changes`/`base`: `caption`, `category` | `files.upload` + sichtbar | Abschnitt 3; der Inhalt bleibt unveränderlich |
| `file.delete` | – | `files.delete` + sichtbar | Tombstone |

\* Pflichtfeld. Werte im JSON-Format der API: Aufzählungen als camelCase-Text, Zeitpunkte ISO 8601 mit Zeitzone.

## 5. Pull

`GET /api/v1/sync/pull?cursor=…&limit=200&includeScope=true`

```json
{
  "changes": [
    { "entityType": "bauakte", "id": "…", "deleted": false, "data": { … } },
    { "entityType": "note", "id": "…", "deleted": true, "data": null }
  ],
  "cursor": "…", "hasMore": false,
  "visibleBauakteIds": [ "…" ]
}
```

- **Ohne Cursor** liefert der Abruf den vollständigen aktuellen Stand aller sichtbaren Datensätze (Erstabgleich).
- Der Feed enthält **je Datensatz nur den letzten Stand**. Eine zwischenzeitlich mehrfach geänderte Bauakte kommt einmal.
- **Entitätstypen v1:** `bauakte`, `note`, `file`, `workItem`, `contact` (nur mit `contacts.read`), `screedType`.
- **Sichtbarkeit:** wie in der API (`bauakten.read_all` oder Mitglied bzw. zuständig). Notizen, Dateien und Aufgaben
  folgen ihrer Bauakte.
- **`includeScope=true`:** liefert alle aktuell sichtbaren Bauakten-IDs. Lokale Bauakten, die nicht (mehr) darin
  stehen, löscht der Client samt Notizen und Dateien, etwa nach entzogenen Rechten. Empfohlen bei jedem App-Start
  und mindestens einmal täglich.
- **Garantie:** Der Cursor überholt keine noch offene Transaktion. Eine Änderung, die später festgeschrieben wird,
  aber früher begonnen hat, geht nie verloren. Sie wird zurückgehalten, bis alle früheren Transaktionen abgeschlossen sind.
- **`409 RESYNC_REQUIRED`:** Der Server wurde aus einem Backup wiederhergestellt (anderer `instanceEpoch`) oder der
  Cursor ist ungültig. Der Client verwirft den Cursor und gleicht ohne Cursor neu ab. **Die Outbox bleibt erhalten und
  wird gesendet.** Lokal noch vorhandene Fotos, die auf dem Server fehlen, werden erneut angekündigt und hochgeladen.

### Snapshot (`POST /sync/snapshot`)

Liefert den vollständigen aktuellen Stand von bis zu 50 Bauakten samt Inhalt (Notizen, Dateien, Aufgaben,
Bautagebuch, Prüfungen) als `changes` im Format des Pulls, nicht (mehr) sichtbare in `missing`. Clients rufen ihn
auf, wenn `visibleBauakteIds` eine Bauakte enthält, die sie noch nicht kennen (z. B. neu eingeplant oder über
„Mir zuweisen“, `POST /bauakten/{id}/members/me`). Sichtbar sind Bauakten auch für Personen, die (selbst oder über
ihre Kolonne) in den letzten 30 Tagen oder künftig dafür eingeplant sind.

## 6. Dateien

1. Aufnahme: Datei sofort dauerhaft im Benutzer-Container speichern, SHA-256 berechnen.
2. Outbox: `file.announce` (mit Größe und SHA-256).
3. Upload-Warteschlange: `PUT /api/v1/files/{id}/content`. Große Dateien in Teilen mit `Content-Range: bytes start-ende/gesamt`.
   Nach einem Abbruch liefert `GET /api/v1/files/{id}/upload-status` die Fortsetzungsposition. `409 OFFSET_MISMATCH`
   enthält `receivedBytes`.
4. Der Server prüft Größe, SHA-256 und Typ. `422 CHECKSUM_MISMATCH`: Datei erneut vollständig senden.
5. **Fotos als JPEG hochladen** (iOS: ImageIO, Qualität 0,85, EXIF inkl. Aufnahmezeit behalten). Vorschauen erzeugt der Server.
6. Hochgeladene Dateien bleiben **7 Tage** lokal (Absicherung für eine Wiederherstellung des Servers), danach nur noch die Vorschau.

## 7. Zustände im Client

```
Outbox-Eintrag:  pending → sending → (entfernt) | rejected | pending (bei RETRY_LATER / Netzfehler)
Upload:          waiting → uploading(receivedBytes) → verifying → done | failed(code)
```

Nach einem App-Neustart werden Einträge im Zustand `sending` wieder zu `pending`. Wiederholen ist durch `opId` sicher.
