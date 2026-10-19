# API Contract Kurzfassung

Alle Angular Services sprechen `/api` an. JWT liegt im Authorization Header.

```ts
GET /api/dashboard
GET /api/sites
POST /api/sites
GET /api/planning/assignments?from=YYYY-MM-DD&to=YYYY-MM-DD
POST /api/planning/assignments
POST /api/documents/upload
```
