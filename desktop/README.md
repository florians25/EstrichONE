# EstrichONE für Windows

Windows-Desktop-App (WinUI 3) für das Büro, siehe [`docs/reengineering/06-windows-winui3.md`](../docs/reengineering/06-windows-winui3.md).
Online gegen den EstrichONE-Server; keine lokale Datenbank.

```
src/EstrichOne.Client/          API-Client (.NET 10, ohne UI): Anmeldung als „desktop“, Token-Erneuerung, alle Fachaufrufe
src/EstrichOne.Desktop.Core/    ViewModels und Sitzung (CommunityToolkit.Mvvm, ohne WinUI) – auf Linux testbar
src/EstrichOne.Desktop/         WinUI-3-App: Titelleiste mit Suche (Strg+K), Ribbon, Navigation, Seiten (nur Windows)
tests/EstrichOne.Client.Tests/  Client- und ViewModel-Tests gegen die echte API (im Speicher) und PostgreSQL (Testcontainers)
```

## Windows-App

Funktionsumfang der ersten Version: Anmeldung mit Server-Assistent (Refresh-Token in der Windows-Anmeldeinformations-
verwaltung), Tagesübersicht, Bauaktensuche, Bauakte mit Hinweisen, Notizen, Räumen, Aufgaben/Mängeln, Dateien und
Material, Kolonnenplanung (Woche), Anrufe mit Telefon-Schnellerfassung, Bestellungen mit Status, Helpdesk.
Verwaltung (Benutzer, Sicherung, Protokoll) und Pflege (Planung per Ziehen, Materialstamm) öffnen bewusst das Web
(docs/reengineering/06, Abschnitt 1).

Bauen auf Windows (Visual Studio 2026 oder .NET 10 SDK):

```powershell
dotnet publish src/EstrichOne.Desktop/EstrichOne.Desktop.csproj -c Release -r win-x64 -p:Platform=x64 -o out
out\EstrichONE.exe
```

Die Ausgabe ist eigenständig (inkl. Windows App SDK) und läuft ohne Installation. Die CI baut sie bei jedem Push
(Artefakt „EstrichONE-Windows-x64“) und startet sie einmal zur Kontrolle.

## Tests

```bash
dotnet test --solution EstrichOne.Desktop.slnx   # braucht Docker (PostgreSQL) oder ESTRICHONE_TEST_DATABASE
```
