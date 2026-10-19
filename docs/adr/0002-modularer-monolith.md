# 0002 – Modularer Monolith mit Minimal APIs

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Microservices würden Betrieb, Tests und Fehlersuche für einen Einzelentwickler unverhältnismäßig erschweren.
Ein unstrukturierter Monolith (wie `api/index.php` in Estrich Web) ist ebenso wenig wartbar.

## Entscheidung
Ein Projekt `EstrichOne.Api`. Fachmodule liegen in `Modules/<Name>/` und registrieren sich über `IModule`
(Dienste + Endpunkte). Endpunkte sind Minimal APIs, gruppiert je Modul unter `/api/v1/...`.
Ein Modul greift auf ein anderes nur über dessen Namespace `Modules.<Name>.Public` zu. Ein Architekturtest
erzwingt das.

## Folgen
- Ein Deployment, eine Datenbank, eine Transaktion über Modulgrenzen, wenn nötig.
- Modulgrenzen sind sichtbar und prüfbar. Eine spätere Abspaltung wäre möglich, ist aber nicht geplant.
