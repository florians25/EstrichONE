# EstrichWeb – Hotfix 19.08.2026

## Bilder in der Bauakte ließen sich nicht groß öffnen

### Ursache

Für den kleinen Löschknopf oben rechts auf jeder Fotokachel
(`.bauakte-photo-tile-actions`) existieren mehrere CSS-Regeln aus
verschiedenen Runden. Eine ältere legt fest:

```css
position: absolute;
inset: auto auto 0 0;      /* = top:auto right:auto bottom:0 left:0 */
```

Eine spätere Regel überschreibt davon nur `top` und `right`:

```css
position: absolute; top: 6px; right: 6px; z-index: 2;
```

`bottom: 0` und `left: 0` bleiben dabei **stehen**. Der Kasten spannt sich
dadurch von oben rechts bis unten links – also über die **gesamte Kachel**.
Zusammen mit `pointer-events: auto`, das auf Mobilgeräten dauerhaft gesetzt
ist, hat er jeden Klick abgefangen, bevor er den Kachel-Knopf erreichte.

Das Bild ließ sich deshalb nicht öffnen – die Vorschau war nie das Problem,
der Klick kam schlicht nie an.

### Behebung

- **Alle vier Kanten in einer Kurzschreibweise**: `inset: 6px 6px auto auto`.
  Damit können keine Altwerte aus früheren Regeln übrig bleiben.
- **Der Aktionsbereich nimmt selbst keine Klicks mehr an**
  (`pointer-events: none`) – nur die Knöpfe darin.
- **Die Kachel liegt darüber** (`z-index: 1` gegen `3` nur für den Knopf), der
  Klick landet also verlässlich auf dem Bild.
- Der **Löschknopf kapselt seinen Klick** zusätzlich im Code, damit er die
  Großansicht nicht mit auslöst.
- Gleiches Muster bei den **Videokacheln** vorsorglich abgesichert.

### Nebenbei behoben

Die Fotos im Reiter **Erstbegehung** öffneten bisher nur das Einzelbild ohne
Blättern. Sie nutzen jetzt dieselbe Großansicht wie das Fotoraster – mit
Bildstreifen, Vor/Zurück und den Aktionen (Original, Speichern, Teilen,
Löschen).

---

## Deployment

Reine Frontend-Korrektur.

1. Dateien in den Document-Root hochladen (`api/` unverändert, kann mit).
2. **Strg+F5**, damit der Service Worker die neue Fassung übernimmt.

Datenbank-Migrationen sind nicht erforderlich.

---

## Anmerkung

Solche Fälle entstehen, wenn dieselbe Klasse über mehrere Runden hinweg an
verschiedenen Stellen ergänzt wird. Ein `inset` mit vier Werten überschreibt
alle Kanten auf einmal – Einzelangaben wie `top` und `right` lassen den Rest
stehen. Ich achte bei Positionierungen künftig darauf, immer alle vier Kanten
zusammen zu setzen.
