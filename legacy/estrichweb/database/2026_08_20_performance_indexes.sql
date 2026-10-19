-- ============================================================================
-- EstrichWeb – Indizes für schnellere Listen und Suchen (20.08.2026)
-- ----------------------------------------------------------------------------
-- Fast jede Abfrage filtert nach `tenant_id` und `deleted_at`. Ohne
-- passenden Index muss MySQL dafür die ganze Tabelle lesen; bei mehreren
-- tausend Datensätzen ist das der Hauptgrund für träge Listen.
--
-- Diese Migration ist wiederholbar: Jeder Index wird nur angelegt, wenn er
-- noch fehlt. Bestehende Daten bleiben unberührt.
-- ============================================================================

DROP PROCEDURE IF EXISTS ew_add_index;
DELIMITER //
CREATE PROCEDURE ew_add_index(IN tbl VARCHAR(64), IN idx VARCHAR(64), IN cols VARCHAR(255))
BEGIN
  DECLARE cnt INT DEFAULT 0;
  SELECT COUNT(*) INTO cnt FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND INDEX_NAME = idx;
  IF cnt = 0 THEN
    SET @s = CONCAT('ALTER TABLE `', tbl, '` ADD INDEX `', idx, '` (', cols, ')');
    PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END //
DELIMITER ;

-- Listen und Suchen
CALL ew_add_index('projects',            'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('construction_sites',  'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('customers',           'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('documents',           'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('photos',              'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('rooms',               'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('calls',               'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('material_orders',     'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');

-- Zuordnungen: die häufigsten Verknüpfungen
CALL ew_add_index('construction_sites',  'idx_ew_project',  '`project_id`');
CALL ew_add_index('documents',           'idx_ew_project',  '`project_id`');
CALL ew_add_index('photos',              'idx_ew_project',  '`project_id`');
CALL ew_add_index('rooms',               'idx_ew_site',     '`site_id`');
CALL ew_add_index('project_notes',       'idx_ew_project',  '`project_id`, `deleted_at`');
CALL ew_add_index('projects',            'idx_ew_customer', '`customer_id`');
CALL ew_add_index('calls',               'idx_ew_customer', '`customer_id`');

-- Zeitraumabfragen in Planung und Kalender
CALL ew_add_index('planning_assignments', 'idx_ew_start', '`tenant_id`, `start_at`');
CALL ew_add_index('calendar_events',      'idx_ew_start', '`tenant_id`, `start_at`');
CALL ew_add_index('daily_reports',        'idx_ew_date',  '`tenant_id`, `report_date`');

DROP PROCEDURE IF EXISTS ew_add_index;
