# EstrichWeb – Hotfix 20.08.2026: Serverfehler beim Speichern von Anrufen

## Die Ursache

Im API-Router stand diese Zeile:

```php
$kind = in_array(($b['kind'] ?? 'call'), ['call','inquiry'], true) ? $b['kind'] : 'call';
```

Der Fehler steckt im Detail: **Geprüft wird der Wert mit Ersatz, zurückgegeben
wird der Wert ohne Ersatz.**

- Enthält der Aufruf ein Feld `kind`, funktioniert alles.
- **Fehlt es**, ergibt `$b['kind'] ?? 'call'` den Wert `'call'`, die Prüfung
  ist erfolgreich – und der Ja-Zweig liefert `$b['kind']`, also den
  **nicht vorhandenen Schlüssel**: `null`.

`null` landet dann in der Spalte `kind`, die als `NOT NULL` definiert ist.
MySQL weist das ab, PHP bricht ab, der Browser bekommt eine leere Antwort mit
Status 500 – genau das, was Sie gesehen haben.

**Warum nur die Schnellerfassung betroffen war:** Der alte Dialog in der
Kopfleiste sendet `kind` immer mit. Meine neue Schnellerfassung nicht. Der
Fehler lag also seit jeher im Code, wurde aber erst durch den neuen Aufruf
ausgelöst.

## Die Behebung

**Serverseitig** – erst den Wert mit Ersatz in eine Variable, dann prüfen:

```php
$kindRaw = (string)($b['kind'] ?? 'call');
$kind = in_array($kindRaw, ['call','inquiry'], true) ? $kindRaw : 'call';
```

**Dasselbe Muster steckte an drei weiteren Stellen** – ich habe die Datei
systematisch danach durchsucht:

| Stelle | Feld | Wirkung bei fehlendem Feld |
|---|---|---|
| POST /calls | `direction` | leerer Wert statt „inbound" |
| POST /calls | `source` | leerer Wert statt „phone" |
| Abnahme speichern | `result` | leerer Wert statt „draft" |

Alle vier sind jetzt nach demselben Schema korrigiert. Eine abschließende
Suche findet kein weiteres Vorkommen.

**Zusätzlich** sendet die Schnellerfassung `kind: 'call'` jetzt ausdrücklich
mit – doppelt abgesichert, damit der Aufruf aus sich heraus vollständig ist.

## Warum die Meldung nichts hergab

Der Abbruch geschah, bevor der vorhandene `try/catch` greifen konnte –
deshalb kam eine **leere** Antwort statt einer Fehlermeldung. Das in dieser
Runde eingebaute Auffangnetz (`register_shutdown_function`) liefert ab sofort
auch bei solchen Abbrüchen eine lesbare JSON-Antwort mit Ursache, Datei und
Zeile.

Falls also künftig noch einmal etwas hakt, steht die Ursache direkt in der
Meldung.

---

## Deployment

**`api/` hochladen genügt** für diesen Hotfix. Das vollständige Paket enthält
zusätzlich alle Änderungen der Runde 50 (Monatsfarben, Kalenderwoche,
Raumdialog, Anrufdialog, Tagesbericht).

Anschließend **Strg+F5**.

---

## Anmerkung

Diese Art Fehler entsteht durch eine sehr kompakte Schreibweise, in der die
geprüfte und die zurückgegebene Größe auseinanderlaufen. Beim Lesen fällt es
kaum auf – das Muster sieht korrekt aus. Ich habe die Datei deshalb nicht nur
an der gemeldeten Stelle geändert, sondern nach allen Vorkommen gesucht.
