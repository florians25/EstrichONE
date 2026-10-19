-- ============================================================================
-- EstrichWeb – Performance-Indizes (Go-Live 2026-06-07 Stand)
-- Idempotent. Mehrfaches Ausfuehren ist gefahrlos. MySQL 5.7/8.0 + MariaDB 10.4+.
-- ============================================================================
SET NAMES utf8mb4;

DROP PROCEDURE IF EXISTS ew_add_index;
DELIMITER //
CREATE PROCEDURE ew_add_index(IN p_table VARCHAR(64), IN p_index VARCHAR(64), IN p_cols VARCHAR(255))
BEGIN
  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=p_table)
     AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=p_table AND INDEX_NAME=p_index) THEN
    SET @ddl = CONCAT('ALTER TABLE `', p_table, '` ADD INDEX `', p_index, '` (', p_cols, ')');
    PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
END //
DELIMITER ;

-- Kern
CALL ew_add_index('projects',             'ix_projects_tenant_del',  'tenant_id, deleted_at');
CALL ew_add_index('projects',             'ix_projects_status',      'tenant_id, status, deleted_at');
CALL ew_add_index('projects',             'ix_projects_customer',    'customer_id');
CALL ew_add_index('construction_sites',   'ix_sites_tenant_del',     'tenant_id, deleted_at');
CALL ew_add_index('construction_sites',   'ix_sites_project',        'project_id');
CALL ew_add_index('customers',            'ix_customers_tenant_del', 'tenant_id, deleted_at');
CALL ew_add_index('contacts',             'ix_contacts_customer',    'customer_id');
-- Dokumente / Fotos
CALL ew_add_index('documents',            'ix_documents_project',    'project_id');
CALL ew_add_index('documents',            'ix_documents_tenant_del', 'tenant_id, deleted_at');
CALL ew_add_index('documents',            'ix_documents_visible',    'tenant_id, customer_visible');
CALL ew_add_index('photos',               'ix_photos_project',       'project_id');
CALL ew_add_index('photos',               'ix_photos_tenant_del',    'tenant_id, deleted_at');
-- Aufgaben / Maengel
CALL ew_add_index('tasks',                'ix_tasks_tenant_del',     'tenant_id, deleted_at');
CALL ew_add_index('tasks',                'ix_tasks_assigned',       'assigned_user_id, status');
CALL ew_add_index('defects',              'ix_defects_project',      'project_id, status');
-- Planung / Kalender
CALL ew_add_index('planning_assignments', 'ix_pa_tenant_window',     'tenant_id, deleted_at, start_at, end_at');
CALL ew_add_index('planning_assignments', 'ix_pa_crew',              'crew_id, start_at');
CALL ew_add_index('calendar_events',      'ix_cal_tenant_window',    'tenant_id, start_at, end_at');
-- Anrufe / Anfragen
CALL ew_add_index('call_logs',            'ix_calls_tenant_kind',    'tenant_id, kind, deleted_at');
CALL ew_add_index('call_logs',            'ix_calls_customer',       'customer_id');
CALL ew_add_index('call_logs',            'ix_calls_status',         'tenant_id, inquiry_status');
-- Material
CALL ew_add_index('material_orders',      'ix_mo_tenant_status',     'tenant_id, status, deleted_at');
CALL ew_add_index('material_orders',      'ix_mo_project',           'project_id');
CALL ew_add_index('material_order_items', 'ix_moi_order',            'order_id');
CALL ew_add_index('suppliers',            'ix_suppliers_tenant',     'tenant_id, deleted_at');
-- Kundenportal
CALL ew_add_index('customer_portal_access','ix_cpa_token',           'token_hash');
CALL ew_add_index('customer_portal_access','ix_cpa_customer',        'tenant_id, customer_id');
-- Audit / Logs
CALL ew_add_index('audit_logs',           'ix_audit_tenant_time',    'tenant_id, created_at');
CALL ew_add_index('login_logs',           'ix_login_tenant_time',    'tenant_id, created_at');
-- Auth
CALL ew_add_index('user_roles',           'ix_user_roles_user',      'user_id');
CALL ew_add_index('device_sessions',      'ix_sessions_user',        'user_id');
-- Ressourcen
CALL ew_add_index('vehicles',             'ix_vehicles_tenant_del',  'tenant_id, deleted_at');
CALL ew_add_index('machines',             'ix_machines_tenant_del',  'tenant_id, deleted_at');
CALL ew_add_index('crews',                'ix_crews_tenant_del',     'tenant_id, deleted_at');

DROP PROCEDURE IF EXISTS ew_add_index;
-- ============================================================================
