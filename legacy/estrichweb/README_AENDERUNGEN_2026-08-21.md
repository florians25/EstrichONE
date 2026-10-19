# EstrichWeb – Hotfix 21.08.2026: Dokumentansicht ging ungewollt auf

## Was passiert ist

Am Handy öffnete sich die Dokumentansicht bei Aktionen, die nichts mit
Dokumenten zu tun hatten.

## Die Ursache

Die Vollbildansicht war an `hasSelection()` gekoppelt:

```html
@if (hasSelection()) { <div class="doc-sheet"> … </div> }
```

`hasSelection()` prüft aber nur, **ob irgendetwas ausgewählt ist** – und das
wird an mehreren Stellen gesetzt, ohne dass jemand ein Dokument angetippt hat:

- beim Auswählen eines **Fotos** im Fotoreiter
- beim Öffnen eines **Anhangs aus einer Notiz**
- bei der **Vorauswahl**, wenn eine Bauakte geladen wird

Da die Ansicht seit der letzten Runde **außerhalb der Reiter** liegt – damit
sie überhaupt vollflächig sein kann – erschien sie in all diesen Fällen, egal
in welchem Reiter man gerade war.

## Die Behebung

**Ein eigenes Merkmal für die Absicht:**

```ts
readonly docSheetOpen = signal(false);
```

Es wird **ausschließlich** beim Antippen eines Eintrags in der Dokumenten-
oder Fotoliste gesetzt. Die Ansicht erscheint nur, wenn beides zutrifft:
ausdrücklich geöffnet **und** eine Datei ausgewählt.

Zurückgesetzt wird es beim Schließen und **beim Wechsel des Reiters** – so
bleibt keine Ansicht offen, wenn man zwischendurch woanders hinspringt.

**Zusätzlich aufgeräumt:** Die frühere Ebene über der Liste (aus Runde 51)
war noch aktiv. Bei einer Auswahl wären damit zwei Ebenen übereinander
aufgegangen. Sie ist am Handy jetzt deaktiviert; die Vollbildansicht
außerhalb der Reiter ist der einzige Weg.

---

## Deployment

Reine Frontend-Korrektur.

1. Dateien in den Document-Root hochladen (`api/` unverändert, kann mit).
2. **Strg+F5**, in der installierten App einmal schließen und neu öffnen.

Datenbank-Migrationen sind nicht erforderlich.

---

## Anmerkung

Der Fehler entstand als Folge der Verlagerung: Solange die Ansicht innerhalb
des Dokumentenreiters lag, war `hasSelection()` als Bedingung ausreichend –
außerhalb der Reiter gilt sie überall. Beim Verschieben eines Bausteins muss
ich künftig prüfen, ob seine Anzeigebedingung am neuen Ort noch dasselbe
bedeutet.
