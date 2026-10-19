-- ============================================================
-- EstrichWeb – Rollen & Rechte vereinfachen (2026-06-05)
-- Sorgt dafür, dass JEDER Funktionsbereich einen vollständigen
-- Satz Rechte-Codes (read/write/delete/export/share/archive) hat,
-- damit der vereinfachte Bereichs-Schalter ("ein Schalter = voller
-- Zugriff") sauber funktioniert.
--
-- Idempotent (INSERT IGNORE / mehrfach ausführbar) für MySQL 8 /
-- MariaDB 10.4+. Bestehende Rollen-Zuweisungen bleiben erhalten.
-- Einspielen z. B. via phpMyAdmin > Importieren.
-- ============================================================

-- 1) Vollständige Rechte-Codes je Bereich anlegen (falls fehlend).
--    permissions.id = MD5(code) – stabil und kollisionsfrei.
INSERT IGNORE INTO permissions (id, code, label, scope, created_at)
SELECT MD5(CONCAT(a.area, '.', x.act)),
       CONCAT(a.area, '.', x.act),
       CONCAT(a.label, ' – ', x.lab),
       'tenant', NOW()
FROM (
  SELECT 'projects'      AS area, 'Bauakten'        AS label UNION ALL
  SELECT 'sites',          'Baustellen'                      UNION ALL
  SELECT 'planning',       'Planung'                         UNION ALL
  SELECT 'calendar',       'Kalender'                        UNION ALL
  SELECT 'crews',          'Kolonnen'                        UNION ALL
  SELECT 'vehicles',       'Fahrzeuge'                       UNION ALL
  SELECT 'machines',       'Maschinen'                       UNION ALL
  SELECT 'pumps',          'Estrichpumpen'                   UNION ALL
  SELECT 'customers',      'Kunden'                          UNION ALL
  SELECT 'documents',      'Dokumente'                       UNION ALL
  SELECT 'photos',         'Fotos'                           UNION ALL
  SELECT 'reports',        'Berichte'                        UNION ALL
  SELECT 'inspections',    'Begehungen'                      UNION ALL
  SELECT 'quality',        'Qualität'                        UNION ALL
  SELECT 'defects',        'Mängel'                          UNION ALL
  SELECT 'tasks',          'Aufgaben'                        UNION ALL
  SELECT 'notifications',  'Benachrichtigungen'              UNION ALL
  SELECT 'screed',         'Stammdaten / Estricharten'       UNION ALL
  SELECT 'portal',         'Kundenportal'                    UNION ALL
  SELECT 'integrations',   'Integrationen'                   UNION ALL
  SELECT 'settings',       'Einstellungen'                   UNION ALL
  SELECT 'users',          'Benutzer & Rollen'               UNION ALL
  SELECT 'reportstemplates','Berichtsvorlagen'
) AS a
CROSS JOIN (
  SELECT 'read'   AS act, 'Lesen'      AS lab UNION ALL
  SELECT 'write',         'Bearbeiten'              UNION ALL
  SELECT 'delete',        'Löschen'                 UNION ALL
  SELECT 'export',        'Export'                  UNION ALL
  SELECT 'share',         'Teilen'                  UNION ALL
  SELECT 'archive',       'Archivieren'
) AS x;

-- 2) Geschäftsführer / Administrator: Vollzugriff über Wildcard '*'.
INSERT IGNORE INTO permissions (id, code, label, scope, created_at)
VALUES (MD5('*'), '*', 'Vollzugriff (alle Bereiche)', 'tenant', NOW());

-- Wildcard den Voll-Rollen zuweisen (nur wenn Rolle existiert).
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, MD5('*') FROM roles r
WHERE r.code IN ('geschaeftsfuehrer','firmenadmin','administrator','admin','inhaber');

-- 3) Sinnvolle Grundausstattung je Systemrolle (idempotent).
--    Bauakten sind ohnehin für alle freigegeben; hier werden zusätzliche
--    Bereiche je Rolle als VOLLER Bereichszugriff (alle 6 Aktionen) gesetzt.
--    Vorhandene Zuweisungen bleiben bestehen (INSERT IGNORE).

-- Innendienst: Büro-orientierte Bereiche inkl. Baustellen
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON 1=1
WHERE r.code = 'innendienst'
  AND p.code REGEXP '^(projects|sites|customers|planning|calendar|documents|reports|portal|tasks|notifications|screed)\\.';

-- Disponent: Planung & Ressourcen
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON 1=1
WHERE r.code = 'disponent'
  AND p.code REGEXP '^(projects|sites|planning|calendar|crews|vehicles|machines|pumps|tasks|notifications)\\.';

-- Bauleiter: Baustellen, Begehungen, Qualität, Mängel, Dokumente, Fotos
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON 1=1
WHERE r.code = 'bauleiter'
  AND p.code REGEXP '^(projects|sites|inspections|quality|defects|documents|photos|reports|tasks|planning|calendar)\\.';

-- Buchhaltung: Kunden, Berichte, Dokumente
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON 1=1
WHERE r.code = 'buchhaltung'
  AND p.code REGEXP '^(projects|customers|reports|documents)\\.';

-- Gutachter: Begehungen, Qualität, Mängel, Dokumente, Fotos
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON 1=1
WHERE r.code = 'gutachter'
  AND p.code REGEXP '^(projects|sites|inspections|quality|defects|documents|photos|reports)\\.';

-- Kolonnenführer / Estrichleger: Baustellen, Tagesdoku, Fotos
INSERT IGNORE INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id FROM roles r JOIN permissions p ON 1=1
WHERE r.code IN ('kolonnenfuehrer','estrichleger')
  AND p.code REGEXP '^(projects|sites|documents|photos|tasks)\\.';

-- Fertig. Die App liest die Rechte beim nächsten Login neu ein.
