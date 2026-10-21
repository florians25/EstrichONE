using System.Globalization;

namespace EstrichOne.Archive;

internal static class ExportTexts
{
    public static string Readme(string serverName, int count, VerificationReport report, DateOnly? from, DateOnly? to)
    {
        var range = from is null && to is null ? "alle Dokumente" : $"archiviert {from?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "…"} bis {to?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "…"}";
        return $"""
            EstrichONE – Archivexport für die Betriebsprüfung
            =================================================

            Archivserver:     {serverName}
            Erstellt am:      {report.At:dd.MM.yyyy HH:mm:ss} UTC
            Umfang:           {count} Dokument(e), {range}
            Prüfergebnis:     {(report.Ok ? "in Ordnung – Journal-Kette und alle Prüfsummen bestätigt" : $"{report.Errors.Count} Abweichung(en), siehe pruefprotokoll.json")}
            Journal-Stand:    Nr. {report.LedgerEntries}, Hash {report.LedgerHead}

            Inhalt
            ------
            dokumente/          die archivierten Dateien im Originalformat (unverändert, Name = Objekt-ID_Dateiname)
            metadaten/          je Dokument die beim Archivieren festgeschriebenen Angaben (JSON)
            verzeichnis.csv     Übersicht: Dokumentart, SHA-256, Archivierungszeitpunkt, Aufbewahrungsfrist, Journal-Nr.
            sha256sums.txt      Prüfsummen aller Dateien
            journal.jsonl       vollständiges, verkettetes Journal des Archivservers (jede Zeile enthält den Hash der vorigen)
            pruefprotokoll.json Ergebnis der Prüfung beim Erstellen dieses Exports
            journal_pruefen.py  Prüfskript für das Journal (Python 3, ohne Zusatzpakete)

            Selbst prüfen
            -------------
            1. Dateien:  sha256sum -c sha256sums.txt          (alle Zeilen müssen „OK“ melden)
            2. Journal:  python3 journal_pruefen.py journal.jsonl
            3. Die Spalte journal_hash in verzeichnis.csv muss im Journal in der Zeile mit der Nr. journal_nr stehen.

            Dokumente werden beim Archivieren mit ihrer SHA-256-Prüfsumme festgeschrieben, danach sind sie schreibgeschützt.
            Es gibt keine Funktion zum Überschreiben; Löschen ist erst nach Ablauf der Aufbewahrungsfrist möglich und wird im
            Journal vermerkt (die Metadaten bleiben erhalten).
            """;
    }

    public const string VerifyScript = """
        #!/usr/bin/env python3
        # Prüft das Journal eines EstrichONE-Archivservers: Laufnummern lückenlos, jeder Eintrag mit dem Hash des vorigen
        # verkettet, jeder Hash = SHA-256 der Zeile mit leerem "hash".
        import hashlib, json, sys

        previous = "0" * 64
        expected = 1
        errors = 0
        with open(sys.argv[1] if len(sys.argv) > 1 else "journal.jsonl", encoding="utf-8") as journal:
            for number, line in enumerate(journal, 1):
                line = line.rstrip("\n")
                if not line:
                    continue
                entry = json.loads(line)
                digest = entry["hash"]
                canonical = line.replace('"hash":"%s"' % digest, '"hash":""')
                if hashlib.sha256(canonical.encode("utf-8")).hexdigest() != digest:
                    print("Zeile %d: Hash stimmt nicht" % number); errors += 1
                if entry["previousHash"] != previous:
                    print("Zeile %d: Verkettung unterbrochen" % number); errors += 1
                if entry["seq"] != expected:
                    print("Zeile %d: Laufnummer %s statt %d" % (number, entry["seq"], expected)); errors += 1
                previous, expected = digest, entry["seq"] + 1
        print("Journal in Ordnung: %d Einträge, letzter Hash %s" % (expected - 1, previous) if errors == 0 else "%d Fehler" % errors)
        sys.exit(1 if errors else 0)

        """;
}
