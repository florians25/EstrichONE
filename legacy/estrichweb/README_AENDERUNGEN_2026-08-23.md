# EstrichWeb – Änderungen 23.08.2026 (Runde 66)

---

## 1. Hinweise zur Baustelle ganz oben

Bisher stand über den Reitern nur die angeheftete Anmerkung. Dinge, die den
Ablauf betreffen, musste man sich aus den Reitern zusammensuchen.

Jetzt erscheinen bis zu **drei Hinweise als schmale Zeilen** – bewusst keine
Kästen, sie sollen auffallen, ohne die halbe Seite zu belegen:

| Hinweis | Farbe | Wann |
|---|---|---|
| **Einsatz heute um 07:00 · Kolonne 2** | grün | Termin heute oder morgen |
| **Erstbegehung steht noch aus** | orange | keine Begehung erfasst |
| **2 Materialbestellungen noch offen** | blau | nicht geliefert |
| **Keine Adresse hinterlegt** | orange | Baustelle ohne Adresse |

Jede Zeile führt mit einem Klick in den zuständigen Reiter. Der Einsatz steht
bewusst **grün** – er ist kein Problem, sondern das Wichtigste des Tages.

**Ist alles in Ordnung, erscheint der Bereich gar nicht.** Eine Akte ohne
Auffälligkeiten sieht aus wie zuvor – das war mir nach dem Cockpit-Einwand
wichtig.

## 2. Farbpunkt beim Anheften saß außermittig

`.postit-swatch` trägt `margin-right: 10px`. In den Menüeinträgen ist das
richtig, dort folgt Text. Im Farbknopf steht der Punkt allein und wurde
dadurch nach links gedrückt.

Behoben: Im Knopf ohne Abstand, rund statt eckig und auf 16 px vergrößert.

## 3. Räume: leerer Zustand mittig

Der Text stand linksbündig neben einer leeren Fläche, der Knopf rechts
daneben – ohne erkennbaren Zusammenhang. Jetzt eine Spalte in der Mitte:
Symbol, Text, Knopf. Text auf 470 px Breite begrenzt, damit die Zeilen
lesbar bleiben.

## 4. Raumdarstellung überarbeitet

**Angaben als abgesetzte Gruppen** statt als Textkette: Fläche, Estrichart
und Dicke stehen jeweils in einem eigenen hellen Feld mit Symbol. Ziffern mit
fester Breite, damit Flächen untereinander vergleichbar sind.

**Statusmarke mit Punkt:** Vor der Beschriftung steht ein Punkt in der
Statusfarbe. Damit ist der Stand auch erkennbar, wenn jemand Farben schlecht
unterscheidet.

**Weiter:** Geschossmarke in Versalien mit mehr Gewicht, Zeilen etwas höher
und mit weicheren Ecken, Aktionen blassgrau bis zum Überfahren,
Gruppenüberschrift je Geschoss mit Trennlinie.

**Hinweis zur Flächenabweichung** („Für die Baustelle sind 200 m² hinterlegt,
die Räume ergeben 50 m²") jetzt mit farbigem linken Rand statt flächigem
Kasten – er informiert, er warnt nicht.

---

## Deployment

Reine Frontend-Änderung.

1. Dateien in den Document-Root hochladen (`api/` unverändert, kann mit).
2. **Strg+F5.**

Datenbank-Migrationen sind nicht erforderlich.

---

## Anmerkung zu den Hinweisen

Ich habe die Liste bewusst auf drei begrenzt und nur Dinge aufgenommen, die
blockieren oder erfahrungsgemäß vergessen werden. Wenn Ihnen im Alltag ein
Punkt fehlt – etwa eine überfällige CM-Messung oder eine nicht bestätigte
Abnahme – ergänze ich ihn gern. Umgekehrt gilt: Wenn eine der vier Zeilen
stört, nehme ich sie wieder heraus.
