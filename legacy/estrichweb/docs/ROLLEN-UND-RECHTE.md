# Rollen und Rechte

## Rollen aus dem Pflichtenheft

- Super Admin
- Firmenadmin
- Geschäftsführer
- Projektleiter
- Bauleiter
- Disponent
- Vorarbeiter
- Kolonnenführer
- Estrichleger
- Innendienst
- Buchhaltung
- Kunde
- Subunternehmer
- Gutachter

## Umsetzung

Rollen sind je Mandant definierbar. Rechte werden über `role_permissions` zugewiesen. Benutzer erhalten Rollen über `user_roles`.

## Datensatzebene

Das Schema ist vorbereitet: alle Datensätze haben `tenant_id`. Für feinere Datensatzrechte können zusätzliche ACL-Tabellen ergänzt werden:

```sql
record_permissions(id, tenant_id, subject_type, subject_id, entity, entity_id, permission, created_at)
```

Die API kann dann vor Detailzugriffen zusätzlich prüfen, ob ein Benutzer etwa nur bestimmte Baustellen, Räume oder Dokumente sehen darf.
