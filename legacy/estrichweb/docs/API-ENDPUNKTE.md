# API Endpunkte

Basis: `/api`

## Auth

- `POST /auth/login`
- `POST /auth/refresh`
- `GET /me`

## Meta & Dashboard

- `GET /health`
- `GET /modules`
- `GET /dashboard`

## Generische Module

Für jedes Modul aus `/api/config/modules.php`:

- `GET /module/{key}`
- `GET /module/{key}/{id}`
- `POST /module/{key}`
- `PUT /module/{key}/{id}`
- `DELETE /module/{key}/{id}`

Beispiele:

- `/api/module/construction_sites`
- `/api/module/planning_assignments`
- `/api/module/defects`
- `/api/module/cm_measurements`
- `/api/module/lexware_sync_logs`

## Spezialrouten

- `GET /planning/assignments`
- `POST /planning/assignments/check-conflicts`
- `POST /documents/upload`
- `POST /offline/sync`
