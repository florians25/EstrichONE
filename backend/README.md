# EstrichONE Backend

ASP.NET Core (.NET 10), modularer Monolith (ADR 0001, 0002), PostgreSQL 17 mit EF Core (ADR 0003).

## Entwickeln

```bash
cd backend
dotnet tool restore                          # dotnet-ef
dotnet build EstrichOne.slnx
dotnet test --solution EstrichOne.slnx       # braucht Docker (Testcontainers) – oder ESTRICHONE_TEST_DATABASE

# Lokale Datenbank für dotnet run (Werte aus appsettings.Development.json)
docker run -d --name estrichone-dev-db -p 5432:5432 \
  -e POSTGRES_USER=estrichone -e POSTGRES_PASSWORD=estrichone -e POSTGRES_DB=estrichone postgres:17-alpine
dotnet run --project src/EstrichOne.Api -- migrate
dotnet run --project src/EstrichOne.Api        # http://localhost:5080/api/v1/server-info
```

## Aufbau

```
src/EstrichOne.Api/
  Program.cs                     Start, Middleware, Befehle (migrate, healthcheck)
  Infrastructure/
    Hosting/                     Optionen, Version, Befehlszeile
    Http/                        Fehlerformat (RFC 9457, Feld code), Sicherheitsheader
    Modules/                     IModule, ModuleCatalog (alle Module ausdrücklich aufgeführt)
    Persistence/                 DbContext, Migrationen
  Modules/<Name>/                ein Ordner je Fachmodul; andere Module nutzen nur <Name>/Public
tests/EstrichOne.Api.Tests/      Integrationstests gegen echte PostgreSQL, Architektur- und Vertragstests
../contracts/openapi/v1.json     API-Vertrag (wird durch Test geprüft)
```

## Regeln

- **Schemaänderung:** `dotnet ef migrations add <Name> --project src/EstrichOne.Api --output-dir Infrastructure/Persistence/Migrations`.
  Ein Test schlägt fehl, wenn das Modell ohne Migration geändert wurde.
- **API-Änderung:** `ESTRICHONE_UPDATE_CONTRACTS=1 dotnet test --solution EstrichOne.slnx` und `contracts/openapi/v1.json`
  mit einchecken. Nicht abwärtskompatible Änderungen nur in einer neuen API-Version.
- **Fehler:** immer `application/problem+json` mit stabilem `code` (`Infrastructure/Http/ErrorCodes.cs`).
  Keine Ausnahmetexte an Clients.
- **Warnungen sind Fehler** (`Directory.Build.props`).
