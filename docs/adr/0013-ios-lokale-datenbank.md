# 0013 – iOS: verschlüsselte SQLite-Datenbank ohne Zusatzbibliothek

- **Status:** angenommen · **Datum:** 2026-10-20

## Kontext
04 und 07 sahen GRDB.swift vor, 11 eine verschlüsselte Datenbank je Benutzer (dort: SQLCipher). GRDB lässt sich
über den Swift Package Manager nicht mit SQLCipher kombinieren; dafür bräuchte es einen gepflegten Fork oder
eine selbst gebaute SQLCipher-Variante. Beides wäre für einen einzelnen Entwickler eine dauerhafte Last.

## Entscheidung
- Die Datenbank eines Benutzers ist eine **SQLite-Datenbank im Arbeitsspeicher** (SQLite des Systems).
- Nach jeder Änderung wird ihr Abbild (`sqlite3_serialize`) mit dem Datenschlüssel aus dem Tresor des Benutzers
  (ADR 0012) per AES-GCM verschlüsselt und **atomar** geschrieben. Auf dem Datenträger gibt es nur Chiffrat
  und immer einen vollständigen Stand (alt oder neu).
- Beim Entsperren wird das Abbild entschlüsselt und geladen (`sqlite3_deserialize`).
- Zugriff über eine schlanke Hülle (`SQLiteDatabase`, rund 150 Zeilen) aus einem Actor. Datensätze liegen als
  JSON so vor, wie der Server sie liefert, plus abgeleitete Spalten für Suche und Zuordnung. Neue Felder des
  Servers bleiben damit ohne App-Update erhalten.
- Fotos liegen als einzelne, ebenfalls mit AES-GCM verschlüsselte Dateien neben der Datenbank.

## Folgen
- Keine Abhängigkeit außer dem System. Unter Linux laufen dieselben Tests mit der System-SQLite.
- Jede Änderung schreibt die ganze Datenbank. Bei den erwarteten Mengen (Bauakten, Notizen, Aufgaben eines
  Betriebs, ohne Fotos) sind das wenige Megabyte und Millisekunden. Wächst die Datenbank deutlich über
  ~50 MB, ist auf SQLCipher oder eine seitenweise Verschlüsselung umzustellen. Der Zugriff ist dafür hinter
  `LocalStore` gekapselt.
- Videos (Hunderte MB) werden nicht im Ganzen verschlüsselt im Speicher gehalten. Dafür kommt mit der
  Videoaufnahme eine abschnittsweise Verschlüsselung.
