# Planungs-API: Disponenten-Cockpit und Schnelldisposition

Gemeinsame Endpunkte für das Web-Cockpit (`/planung/cockpit`) und die Schnelldisposition in den Apps.
Basis `/api/v1/planning`. Lesen braucht `planning.read`, alles Ändernde `planning.manage` (sonst 403).
`{day}` ist `JJJJ-MM-TT`, `heute` oder `morgen`. Fehler kommen als Problem-JSON mit `code`
(`VALIDATION_FAILED` 422, `NOT_FOUND`/`BAUAKTE_NOT_FOUND` 404, `INVALID_DAY`/`INVALID_RANGE` 400).
Vertrag: `contracts/openapi/` (Operation-IDs in Klammern).

## Lesen

| Methode | Pfad | Zweck |
|---|---|---|
| GET | `/day/{day}` (`GetDayPlan`) | **Wer fährt wohin je Kolonne**: `crews[]` mit `members[]` (inkl. `absenceNote`), `vehicles[]`, `rosterChanged`, `isMine`, `stops[]` in Fahrtreihenfolge (`order`, `startTime`, `endTime`, `activity`, `title`, `address`, `latitude/longitude`, `siteContactName/Phone`, `navigationUrl`, `notes`, `people`, `warnings`, `change` = `new`/`changed`/leer, `version`); dazu `withoutCrew[]`, `absent[]`, `free[]`, `myCrewIds`, `canPlan`, `holiday`, `release` |
| GET | `/cockpit?from&to` (`GetPlanningCockpit`) | Kolonnen × Tage (≤ 62 Tage): je Zelle Besetzung, Fahrzeuge, `assignmentIds` (sortiert), `plannedMinutes`/`capacityMinutes` (8 h), `availableMembers`, `warnings` (Doppelbelegung, Abwesenheit, Gerät bei anderer Kolonne, Wochenende/Feiertag); `days[]` mit `isWorkday`, `holiday`, `release`; `assignments[]` wie `/range` (+ `sequence`, `change`) |
| GET | `/backlog` | Vorrat ungeplanter Bauvorhaben (bestehend) |

`release` (je Tag): `releasedAt`, `releasedByName`, `note`, `changedCount` (neu + geändert + entfernt seit Freigabe), `removed[]` (`assignmentId`, `title`, `crewName`, `activity`). `null` = Tag noch nicht freigegeben (Entwurf).

## Ändern

| Methode | Pfad | Body | Antwort |
|---|---|---|---|
| POST | `/batch` (`ApplyPlanBatch`) | `{ operations: [...], dryRun }` | `{ assignments[], deletedIds[], warnings[], dryRun }` |
| POST | `/check?id=` (`CheckAssignment`) | wie `PUT /assignments/{id}` | `{ warnings[] }` – nichts gespeichert |
| POST | `/assignments/{id}/next-free-day` | `{}` | `AssignmentView` am nächsten freien Arbeitstag der Kolonne |
| PUT | `/crews/{crewId}/days/{day}/order` (`OrderCrewStops`) | `{ assignmentIds: [...], startTimes?: ["06:30:00", null, ...] }` | `DayPlan` |
| PUT | `/crews/{crewId}/days/{day}/roster` (`SaveCrewRoster`) | `{ memberUserIds, resourceIds, note }` | Zelle (`CockpitCell`) |
| DELETE | `/crews/{crewId}/days/{day}/roster` | – | 204, wieder Stammbesetzung |
| POST | `/crews/{crewId}/days/{day}/swap` (`SwapCrewRoster`) | `{ fromUserId?, toUserId?, fromResourceId?, toResourceId?, exchange = true }` | `DayPlan` |
| POST | `/days/{day}/swap-crews` (`SwapCrews`) | `{ crewA, crewB }` | `DayPlan` |
| POST | `/releases` (`ReleasePlan`) | `{ from, to, note? }` | `PlanReleaseInfo[]` |
| DELETE | `/releases/{day}` | – | 204 |

Bestehend und weiter gültig: `PUT /assignments/{id}` (anlegen/ändern), `POST /assignments/{id}/move`,
`DELETE /assignments/{id}`, `GET/POST /dispatch/{day}` („Wer fährt wohin“ nach Baustellen, Ziehen von Personen/Fahrzeugen).

### Batch-Operationen (alles oder nichts)

```json
{ "operations": [
  { "op": "upsert", "id": "<neue uuid>", "assignment": { "bauakteId": "…", "day": "2026-11-04", "crewId": "…",
    "startTime": "07:00:00", "endTime": null, "activity": "Estrich einbringen", "kind": "screed" }, "sequence": 1 },
  { "op": "move", "id": "…", "day": "2026-11-05", "changeCrew": true, "crewId": "…", "setTime": true,
    "startTime": "10:00:00", "endTime": "14:00:00", "sequence": 2 },
  { "op": "delete", "id": "…" }
], "dryRun": false }
```

- `move` ändert nur, was angegeben ist (`day`, Kolonne nur mit `changeCrew`, Uhrzeit nur mit `setTime`, `sequence`).
- `dryRun: true` liefert die Einsätze mit Konflikthinweisen, ohne zu speichern (Vorschau beim Ziehen).
- Ein Fehler in einem Schritt (404/422) nimmt alle Schritte zurück.
- Rückgängig: Client schickt die Gegenoperationen (z. B. `upsert` mit dem alten Stand, `delete` des neu angelegten).

### Tagesbesetzung

Ohne Eintrag gilt die Stammbesetzung der Kolonne (Mitglieder, fest zugeordnete Fahrzeuge). Eine Tagesbesetzung ersetzt sie
für diesen Tag – auch für Konfliktprüfung, „Mein Einsatz“ (`/mine`) und Sichtbarkeit der Bauakte. `swap` legt sie bei Bedarf an;
ist der Neue bei einer anderen Kolonne, wechselt er von dort, und mit `exchange` geht der Bisherige im Gegenzug dorthin.

### Freigabe

`POST /releases` hält den Stand der Einsätze je Tag fest. Danach geänderte oder neue Einsätze tragen `change`, entfernte stehen in
`release.removed`. Erneutes Freigeben setzt den Stand neu. Der Plan bleibt für Mitarbeiter auch vor der Freigabe sichtbar
(als Entwurf zu kennzeichnen: `release == null`); Push-Benachrichtigungen gibt es noch nicht – Apps zeigen `changedCount` an.
