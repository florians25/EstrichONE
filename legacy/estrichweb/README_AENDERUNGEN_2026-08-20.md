# EstrichWeb – Änderungen 20.08.2026 (Runde 50)

---

## 1. Anruf-Schnellerfassung: Serverfehler

**Ich konnte den Fehler nicht nachstellen** – im Container steht kein PHP zur
Verfügung, und die Fehlermeldung „Serverfehler. Bitte später erneut
versuchen." ist die Variante **ohne Details**. Das heißt: Die Antwort war kein
JSON, sondern ein harter Abbruch, an dem der vorhandene `try/catch` vorbeiläuft.

Statt weiter zu raten, habe ich die Diagnose dauerhaft gelöst:

- **Auffangnetz für schwere Fehler** (`register_shutdown_function`): Die API
  liefert jetzt **immer** eine lesbare JSON-Antwort mit der tatsächlichen
  Ursache, Datei und Zeile – auch bei fatalen Fehlern.
- Der allgemeine Fehlerfang gibt die Ursache ebenfalls mit statt „Details
  wurden serverseitig protokolliert".
- Beim Speichern eines Anrufs wird die Datenbankmeldung durchgereicht.

**Bitte einmal erneut versuchen.** Die Meldung nennt jetzt die Ursache; damit
ist die Behebung eine Sache von Minuten.

**Eine Ursache habe ich bereits ausgeschlossen und mitbehoben:** Das Anlegen
verlangte `customers.write`. Wer ans Telefon geht, braucht kein Schreibrecht
auf Kundenstammdaten – jetzt genügt `customers.read`.

## 2. Anrufdialog in der Kopfleiste umgebaut

Aufbau wie die Schnellerfassung:

- **Mitlaufende Gesprächsdauer** oben mit pulsierendem Punkt
- **Abschnitte** „Anrufer" und „Gespräch" mit Überschrift und Trennlinie
- **Dieselben sechs Stichworte** zum Antippen wie in der Schnellerfassung
- **Strg + Enter speichert**
- Deutlich mehr Luft: 14 px Spaltenabstand, 12 px zwischen den Zeilen, der
  leere Hilfstextbereich unter den Feldern entfällt
- Am Handy einspaltig mit 16 px Schrift (kein iOS-Zoom)

## 3. Monatsansicht: echte Terminfarben – Ursache gefunden

In Runde 48 hatte ich geschrieben:

```css
background-color: var(--fc-event-bg-color, #1a73e8) !important;
```

`--fc-event-bg-color` ist im **Monatsraster gar nicht gesetzt** – also griff
immer der Ersatzwert `#1a73e8`. Meine Regel hat damit **jeden Termin blau
gefärbt**, statt die Farbe durchzulassen.

FullCalendar setzt die Terminfarbe als Inline-Stil direkt am Element. Sie darf
schlicht nicht überschrieben werden. Die Regel ist entfernt; gestaltet werden
nur noch Form und linke Kante.

## 4. Kalenderwoche verdeckte das Datum am Montag

Die KW-Marke wird absolut in die Wochenspalte gelegt und lag über der
Tageszahl. Jetzt sitzt sie klein in der linken oberen Ecke mit grauem Grund,
und die Tageszahl rückt am Montag nach rechts.

## 5. Raumerfassung neu gerastert

Im Screenshot standen „Fläche" schmal, „Länge/Breite" schmal und versetzt –
der Dialog wirkte zusammengewürfelt. Jetzt liegt alles auf einem
**12-spaltigen Raster**: Raum 8 / Geschoss 4, Fläche 4 / „oder rechnen" 2 /
Länge × Breite 6. Gleiche Abstände überall, Blocküberschriften mit
Trennlinie, größere Stand-Marken mit Ring um den aktiven Punkt.

## 6. Schnellauswahl im Tagesbericht entfernt

Wie gewünscht ersatzlos entfallen, samt zugehörigem Code. Der
**Wetter-Knopf** im Wetterfeld bleibt – er holt die Lage weiterhin vom
Standort. Die Abschnitte sind auf vier umnummeriert.

## 7. Dateien werden mitgelöscht

Das war bereits Teil der letzten Runde und ist unverändert aktiv: Beim
Löschen werden die Pfadspalten ausgelesen und die Dateien aus dem
Webspace entfernt – Dokumente, alle drei Fotogrößen, Berichts-PDFs,
Unterschriften. Für Altbestände liegt `api/tools/purge_deleted.php` bei.

## 8. Datenbank-Update

Neu: **`database/estrichweb_update_2026_08_20.sql`** – ein Skript für den
gesamten Stand statt vieler Einzelmigrationen.

| Schritt | Inhalt |
|---|---|
| 1–2 | Fehlende Spalten (Termin-Ort, Zuständigkeit, Kundenverknüpfung bei Anrufen, Raum-Bemerkung) |
| 3 | 19 Indizes für Listen, Verknüpfungen und Zeitraumabfragen |
| 4 | Altlasten entfernen – **standardmäßig ausgeschaltet** |

**Wiederholbar:** Jeder Schritt prüft zuerst, ob er nötig ist. Ein zweiter
Lauf ändert nichts und schlägt nicht fehl. Das Skript meldet zu jedem
ausgeführten Schritt eine Zeile.

**Schritt 4 ist bewusst abgeschaltet** (`SET @EW_PURGE_DELETED = 0`). Wer ihn
nutzen will, setzt die Zeile auf `1`. **Vorher sollte allerdings
`php api/tools/purge_deleted.php --force` laufen** – nur das Werkzeug entfernt
auch die zugehörigen Dateien. Das SQL-Skript kennt die Ablage nicht und würde
verwaiste Dateien zurücklassen.

---

## Deployment

1. **Datensicherung anlegen.**
2. **`api/` hochladen** – enthält Auffangnetz, Rechteanpassung, Dateilöschung.
3. Restliche Dateien in den Document-Root.
4. `api/config/local.php` beibehalten (nicht überschreiben).
5. **`database/estrichweb_update_2026_08_20.sql` einspielen.**
6. **Strg+F5**, damit der Service Worker die neue Fassung übernimmt.

---

## Bitte um Rückmeldung

Zum Anruf-Fehler: Bitte einmal erneut auf „Speichern" gehen. Die Meldung
enthält jetzt die Ursache (etwa „Unknown column …" oder „Data too long for
column …"). Mit diesem einen Satz behebe ich es gezielt, statt weiter zu
vermuten.
