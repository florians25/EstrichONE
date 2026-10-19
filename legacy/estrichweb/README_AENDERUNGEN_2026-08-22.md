# EstrichWeb – Korrektur 22.08.2026: Schrittleiste im Assistenten

## Die Ursache – ein Namenskonflikt

Die Schrittleiste des Bauakten-Assistenten nutzt seit jeher die Klasse
`wz2-steps` und stand waagerecht:

```css
.wz2-steps { display: flex; flex-wrap: wrap; justify-content: center; }
```

In **Runde 57** habe ich für die neue Vor-Ort-Abfolge („Kunde steht fest →
Baustellenadresse → Anlegen und begehen") **dieselbe Klasse** verwendet – und
darin das Gegenteil gesetzt:

```css
.wz2-steps { display: flex; flex-direction: column; }   /* Runde 57 */
```

Bei gleichem Gewicht gewinnt die spätere Regel. Damit standen die fünf
Schritte oben untereinander und belegten den halben Bildschirm, bevor
überhaupt Inhalt kam.

## Die Behebung

Die Vor-Ort-Abfolge heißt jetzt **`wz2-onsite-list`** – ebenso ihre
Unterelemente (`wz2-onsite-mark`, `-text`, `-act`). Beide Bausteine haben
damit eigene Namen und können sich nicht mehr gegenseitig überschreiben.

**Die Schrittleiste zusätzlich abgesichert:**

- `flex-direction: row` ausdrücklich gesetzt, statt sich auf die Vorgabe zu
  verlassen
- Die Schritte **teilen sich die Breite** (`flex: 1 1 180px`), statt jeder
  eine eigene Zeile zu belegen
- Lange Beschriftungen werden gekürzt statt umzubrechen
- **Am Handy scrollt die Leiste waagerecht** statt umzubrechen – fünf
  Schritte untereinander wären dort besonders störend

---

## Deployment

Reine Frontend-Korrektur.

1. Dateien in den Document-Root hochladen (`api/` unverändert, kann mit).
2. **Strg+F5.**

Datenbank-Migrationen sind nicht erforderlich.

---

## Anmerkung

Das ist innerhalb weniger Runden der zweite Fall dieser Art – vorher hatte
ich `.quick-hits` zu `.cf-hits` umbenannt und die Stile vergessen, jetzt
zweimal denselben Namen für verschiedene Dinge vergeben.

Beide Male entstand der Fehler beim Ergänzen neuer Bausteine. Ich prüfe
künftig vor der Vergabe eines Klassennamens, ob es ihn schon gibt – eine
Suche von Sekunden, die beide Fälle verhindert hätte.
