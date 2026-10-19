# 0001 – Backend mit ASP.NET Core (.NET 10 LTS)

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Der Windows-Client muss mit WinUI 3 in C# entstehen. Die Microsoft-Entra-Anbindung soll mit offiziellen
Bibliotheken erfolgen. Das Backend braucht Hintergrunddienste (Synchronisation, Bildvarianten, Erinnerungen),
läuft in Docker und wird von einer Person gewartet. Abwägung: `docs/reengineering/04-framework-bewertung.md`.

## Entscheidung
ASP.NET Core auf .NET 10 (LTS, Support bis November 2028). Wechsel auf die nächste LTS-Version innerhalb von
sechs Monaten nach deren Erscheinen.

## Folgen
- Gemeinsame Sprache mit dem Desktop; DTOs können geteilt werden.
- Ein Prozess für API und Hintergrunddienste.
- Die Erfahrung mit NestJS aus SATZIO ist nicht direkt übertragbar.
