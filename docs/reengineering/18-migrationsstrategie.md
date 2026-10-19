# 18 – Migrationsstrategie

## Entscheidung (19.10.2026)

**EstrichONE ist ein Neuprodukt. Es gibt keine Datenübernahme aus Estrich Web.**

Diese Entscheidung ersetzt den ursprünglichen Vorschlag (einmalige Datenübernahme mit Probeläufen).

## Folgen

| Thema | Folge |
|---|---|
| Altbestand `legacy/estrichweb/` | dient nur noch als **fachliche Referenz**: Abläufe, Felder, UX-Entscheidungen. Kein Code wird übernommen, keine Daten werden gelesen. |
| Estrich Web im Betrieb | läuft unabhängig weiter, bis der Betrieb selbst umstellt. Die Sofortmaßnahmen aus [17, Abschnitt 1](17-sicherheit-und-datenschutz.md) bleiben für diese Zeit wichtig. |
| Datenmodell | wird ohne Rücksicht auf alte Strukturen entworfen: keine 32-Hex-IDs, keine `tenant_id`, keine bcrypt-Kompatibilität, keine Lexware-Referenzen. Bauakte und Baustelle werden zusammengeführt (E-05 entschieden). |
| Startzustand einer Installation | leer. Die Ersteinrichtung legt Firma, ersten Administrator, Standardrollen, Estricharten-Grundliste und Bauakten-Vorlagen an. |
| Stammdaten (Kunden, Mitarbeiter, Fahrzeuge) | werden neu erfasst. **Optional [1.0, klein]:** CSV-Import für Kunden und Mitarbeiter mit Vorschau und Dublettenprüfung. Er ist allgemein gehalten und an keine Quelle gebunden. |
| Roadmap | Das Arbeitspaket „Datenmigration“ entfällt. An seine Stelle tritt „Ersteinrichtung und Stammdaten-Import“ (20). |
| Risiken | R-3 (unbekannter Produktivdatenstand) entfällt. Neu: Akzeptanz im Betrieb, wenn historische Bauakten nur im Altsystem nachschlagbar sind. Gegenmaßnahme: Estrich Web bleibt schreibgeschützt erreichbar, solange der Betrieb es braucht. |

## Umstellung im Betrieb (Vorschlag)

1. EstrichONE wird parallel eingerichtet. Neue Bauakten entstehen ab einem Stichtag nur noch in EstrichONE.
2. Laufende Bauakten werden im Altsystem zu Ende geführt **oder** bei Bedarf manuell in EstrichONE neu angelegt.
   Dafür braucht es mit der Schnellanlage nur wenige Sekunden je Akte.
3. Estrich Web wird später schreibgeschützt geschaltet und schließlich vom Betrieb archiviert.
   Für EstrichONE ist dieser Schritt nicht erforderlich.
