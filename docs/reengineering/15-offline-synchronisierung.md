# 15 – Offline-First und Synchronisation

**Status heute:** Die bestehende Synchronisation ist nur scheinbar vorhanden. Vorgänge werden auf dem Server
in `offline_sync_jobs` abgelegt, aber nie verarbeitet, und der Client verwirft sie danach (01, ❌-1).
Es wird nichts übernommen.

**Muster:** Outbox mit idempotenten Operationen und einem Änderungsfeed per Cursor. Das ist dasselbe Prinzip,
das in SATZIO bereits funktioniert (`SyncOperation` je Benutzer und `opId`, Versionsnummern, Konfliktanzeige),
erweitert um Dateien, Rechte, Geräte und Mehrbenutzerbetrieb.

## 1. Bausteine

| Baustein | Client | Server |
|---|---|---|
| Lokale DB | GRDB/Room je Benutzer (11) | PostgreSQL |
| Stabile IDs | UUIDv7 vom Client | übernimmt die Client-ID |
| Outbox | Tabelle `outbox(opId, seq, type, entityId, baseVersion, payload, state, attempts, lastError)` | – |
| Upload-Warteschlange | Tabelle `pending_files(fileId, path, sha256, size, uploadedBytes, state)` | `files` + Teil-Uploads |
| Annahme | `POST /sync/push` (≤ 100 Operationen, geordnet) | `sync_operations(user_id, device_id, op_id UNIQUE, result_json)` → Idempotenz |
| Änderungen holen | `GET /sync/pull?cursor=…` | `change_log(seq bigserial, entity, id, version, op)`; gefiltert nach Sichtbarkeit (03, Abschnitt 6) |
| Integrität | SHA-256 je Datei, vor und nach Upload | Server prüft den Hash beim Abschluss |

**Reihenfolge eines Sync-Laufs:** (1) Uploads starten bzw. fortsetzen (parallel, max. 2) → (2) Push in
Abhängigkeitsreihenfolge → (3) Pull bis `hasMore=false` → (4) Status aktualisieren.
**Auslöser:** App im Vordergrund, Netzwechsel zu „verbunden“, nach jeder lokalen Änderung (entprellt 2 s),
periodisch im Hintergrund (iOS `BGTaskScheduler`, Android WorkManager 15 min). Bei Fehlern
exponentieller Backoff mit Zufallsanteil (1 s … 15 min).

## 2. Konfliktstrategien je Datentyp

**[Verbindlich]** Kein pauschales „Last Write Wins“.

| Datentyp | Strategie | Begründung |
|---|---|---|
| **Fotos, Videos, Dokumente** | **nur anlegen** (unveränderlich); Löschen ist eine eigene Operation | eine Datei kann nicht „gleichzeitig bearbeitet“ werden |
| **Notizen, Kommentare, Bautagebuch-Einträge** | **Anfügen** – konfliktfrei; Bearbeiten nur durch den Verfasser, LWW innerhalb seiner Geräte | Verlaufscharakter wie im Altbestand („Ticket-Stil“) |
| **Bauakte – Stammfelder** (Bezeichnung, Adresse, Beschreibung, Status, Kunde) | **Feldweises Zusammenführen** mit `baseVersion` je Patch: Geänderte Felder, die auf dem Server seit `baseVersion` **nicht** geändert wurden, werden übernommen. Bei gleichem Feld und unterschiedlichem Wert entsteht ein **Konflikt** → Server behält seinen Wert, Client erhält beide, Anzeige „Max hat inzwischen die Adresse geändert: … – [Meine übernehmen] [Andere behalten]“. | Büro und Baustelle bearbeiten oft dieselbe Akte, aber verschiedene Felder |
| **Status der Bauakte** | Feldweise, **plus** Übergangsregeln (z. B. „Storniert“ nur online mit Recht und Bestätigung) | fachliche Regeln stehen über der Synchronisation |
| **Aufgaben/Mängel** | Feldweise; „erledigt“ gewinnt gegen gleichzeitige Textänderung (beides wird übernommen) | |
| **Räume/Flächen** | Feldweise je Raum | |
| **Messwerte (CM, Erstbegehung)** | **nur anlegen**; Korrektur = neuer Datensatz mit Verweis | Nachweispflicht |
| **Planung/Einsätze** | **nur online** bearbeitbar (Büro); mobil schreibgeschützt | Konfliktprüfung bei Ressourcen braucht den Gesamtstand |
| **Benutzerbezogene Einstellungen** | LWW | unkritisch |

## 3. Sonderfälle

| Fall | Verhalten |
|---|---|
| **Gleichzeitige Bearbeitung** | siehe Tabelle; Konflikte erscheinen in *Mehr › Synchronisation › Konflikte* und als Hinweis in der betroffenen Akte |
| **Datensatz serverseitig gelöscht** | Pull liefert Tombstone → lokale Kopie wird entfernt. Ungesendete Änderungen daran → Operation `rejected: DELETED`. Ungesendete **neue Fotos/Notizen** zu einer gelöschten Bauakte werden **nicht verworfen**: Sie landen in einer serverseitigen „Fundsachen“-Ablage für Administratoren, mit Hinweis an den Benutzer. |
| **Rechte entzogen** | Server lehnt mit `rejected: FORBIDDEN` ab; Pull meldet `removed_from_scope` → lokale Kopie gelöscht; abgelehnte Fotos wie oben in die Fundsachen-Ablage (das Recht zum Hochladen auf einer Baustelle soll keine Arbeit vernichten) |
| **Benutzer gesperrt** | 401 `USER_DISABLED`; keine Annahme mehr; Daten bleiben verschlüsselt auf dem Gerät; Abholung nur per Administrator-Befehl `flush_then_wipe` (13) |
| **Gerät widerrufen** | 401 `DEVICE_REVOKED`; wie oben; Löschbefehl, falls angeordnet |
| **Sitzung abgelaufen** | Outbox bleibt erhalten; nach erneuter Anmeldung desselben Benutzers wird fortgesetzt. Meldet sich ein **anderer** Benutzer an, wird **nie** fremde Outbox gesendet (getrennte Container) |
| **Mehrfachübertragung** (Timeout nach Annahme) | gleiche `opId` → gespeichertes Ergebnis mit `status: duplicate` |
| **Abbruch mitten im Upload** | Fortsetzung ab `uploadedBytes` (Teil-Upload) bzw. erneuter `PUT` mit gleichem Hash (idempotent) |
| **App-Neustart während Sync** | Operationen im Zustand `sending` werden beim Start auf `pending` zurückgesetzt; Wiederholung ist durch die Idempotenz sicher |
| **Serverwechsel** | nur nach vollständiger Übertragung oder ausdrücklichem Export (10, Abschnitt 2) |
| **Server aus Backup wiederhergestellt** | Server hat eine `instanceEpoch` (neu bei jedem Restore). Weicht sie vom gespeicherten Wert des Clients ab: (1) Pull-Cursor verwerfen, vollständiger Neuabgleich; (2) **ungesendete Outbox bleibt und wird gesendet**; (3) bereits gesendete, aber im Backup fehlende Daten: Die Apps senden den `sync_operations`-Abgleich nach. Für Dateien und Anlagen, die das Gerät noch lokal hat, wird ein **„Nachliefern“** angeboten. Dateien, die das Gerät bereits gelöscht hat, sind verloren. Deshalb halten Geräte hochgeladene Fotos noch 7 Tage lokal vor. |
| **Uhrzeit des Geräts falsch** | Server ordnet nach Eingang; Client-Zeit wird nur als „aufgenommen am“ gespeichert |

## 4. Sichtbarkeit für den Benutzer

- Jede Bauakte, Notiz und jedes Foto hat einen Zustand: ● **gespeichert** (lokal) · ◐ **wird übertragen**
  (mit Fortschritt) · ✓ **übertragen** · ⚠ **Problem** (antippen → Erklärung + Aktion).
- In der Kopfzeile: unauffällig bei „alles übertragen“, deutlich bei Offline („Offline – Änderungen werden
  gespeichert“) und bei Fehlern.
- *Mehr › Synchronisation*: ausstehende Vorgänge, Dateien mit Größe und Fortschritt, Konflikte, abgelehnte
  Vorgänge, „Jetzt synchronisieren“, „Problem melden“ (mit Diagnose).
- Große Uploads: Hinweis bei Mobilfunk > 50 MB („Nur im WLAN übertragen“ – Einstellung).

## 5. Spezifikation und Tests

- `docs/sync-protokoll.md` (wird in Arbeitspaket AP-08 erstellt): Nachrichtenformate, Fehlercodes,
  Zustandsautomaten. iOS und Android implementieren gegen dieselbe Spezifikation.
- **Protokoll-Testsuite auf dem Server:** jeder Sonderfall aus Abschnitt 3 als Integrationstest.
- **Client-Tests:** Zustandsautomat der Outbox als reine Logik (ohne UI) mit simuliertem Netzwerk
  (Abbruch nach Senden, nach Annahme, doppelte Antwort, 401 mitten im Lauf).
- Siehe 19 für die Gerätetests.
