# Architekturentscheidungen (ADR)

Jede Entscheidung, die später schwer umkehrbar ist, wird hier festgehalten: kurz, mit Begründung und Folgen.
Neue Entscheidungen: `0000-vorlage.md` kopieren, fortlaufend nummerieren. Entscheidungen werden nicht
gelöscht, sondern durch eine neue ADR **ersetzt** (Status „ersetzt durch 00xx“).

| Nr. | Titel | Status |
|---|---|---|
| [0001](0001-backend-aspnet-core.md) | Backend mit ASP.NET Core (.NET 10 LTS) | angenommen |
| [0002](0002-modularer-monolith.md) | Modularer Monolith mit Minimal APIs | angenommen |
| [0003](0003-postgresql-ef-core.md) | PostgreSQL 17 mit EF Core, Migrationen als eigener Schritt | angenommen |
| [0004](0004-eine-installation-ein-betrieb.md) | Eine Installation = ein Betrieb (keine Mandantenfähigkeit) | angenommen |
| [0005](0005-bauakte-ohne-baustelle.md) | Bauakte und Baustelle sind eine Entität | angenommen |
| [0006](0006-client-generierte-ids.md) | Client-generierte UUIDv7 als Primärschlüssel | angenommen |
| [0007](0007-caddy-reverse-proxy.md) | Caddy als Reverse Proxy und TLS-Terminierung | angenommen |
| [0008](0008-neuprodukt-ohne-datenuebernahme.md) | Neuprodukt ohne Datenübernahme aus Estrich Web | angenommen |
| [0009](0009-ios-zuerst.md) | iOS als erste native Plattform | angenommen |
| [0010](0010-tests-gegen-echte-postgresql.md) | Integrationstests gegen echte PostgreSQL (Testcontainers) | angenommen |
| [0011](0011-undurchsichtige-sitzungstokens.md) | Undurchsichtige Sitzungstokens statt JWT | angenommen |
| [0012](0012-ios-datentresor-und-api-client.md) | iOS: Datentresor je Benutzer und handgeschriebener API-Client | angenommen |
| [0013](0013-ios-lokale-datenbank.md) | iOS: verschlüsselte SQLite-Datenbank ohne Zusatzbibliothek | angenommen |
