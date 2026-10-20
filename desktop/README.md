# EstrichONE für Windows

Windows-Desktop-App (WinUI 3) für das Büro, siehe [`docs/reengineering/06-windows-winui3.md`](../docs/reengineering/06-windows-winui3.md).
Online gegen den EstrichONE-Server; keine lokale Datenbank.

```
src/EstrichOne.Client/     API-Client (.NET 10, ohne UI): Anmeldung als „desktop“, Token-Erneuerung, Bauakten, Planung, Helpdesk
tests/EstrichOne.Client.Tests/  Tests des Clients gegen die echte API (im Speicher) und PostgreSQL (Testcontainers)
```

## Tests

```bash
dotnet test --solution EstrichOne.Desktop.slnx   # braucht Docker (PostgreSQL) oder ESTRICHONE_TEST_DATABASE
```
