-- ############################################################################
-- ESTRICHWEB – VOLLSTÄNDIGES MYSQL-8 UPDATE FÜR BESTEHENDE DATENBANK
-- Stand: 2026-05-31
-- Ziel: bestehende Datenbank aktualisieren, KEINE neue Struktur leeren/anlegen.
-- Sicher für phpMyAdmin / STRATO MySQL 8.x:
--   * kein ALTER TABLE ... ADD COLUMN IF NOT EXISTS
--   * keine Datenlöschung
--   * fehlende Spalten/Indexe werden über INFORMATION_SCHEMA geprüft
--   * kann mehrfach ausgeführt werden
-- ############################################################################

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

DROP PROCEDURE IF EXISTS ew_add_column;
DROP PROCEDURE IF EXISTS ew_add_index;
DROP PROCEDURE IF EXISTS ew_add_column_if_table;
DROP PROCEDURE IF EXISTS ew_backfill_runtime;

DELIMITER //

CREATE PROCEDURE ew_add_column(
  IN p_table VARCHAR(64),
  IN p_column VARCHAR(64),
  IN p_definition VARCHAR(1200)
)
BEGIN
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table
  ) AND NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_column
  ) THEN
    SET @ew_sql = CONCAT(
      'ALTER TABLE `', REPLACE(p_table, '`', '``'),
      '` ADD COLUMN `', REPLACE(p_column, '`', '``'), '` ', p_definition
    );
    PREPARE ew_stmt FROM @ew_sql;
    EXECUTE ew_stmt;
    DEALLOCATE PREPARE ew_stmt;
  END IF;
END //

CREATE PROCEDURE ew_add_index(
  IN p_table VARCHAR(64),
  IN p_index VARCHAR(64),
  IN p_definition VARCHAR(1200)
)
BEGIN
  IF EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table
  ) AND NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND INDEX_NAME = p_index
  ) THEN
    SET @ew_sql = CONCAT(
      'ALTER TABLE `', REPLACE(p_table, '`', '``'), '` ', p_definition
    );
    PREPARE ew_stmt FROM @ew_sql;
    EXECUTE ew_stmt;
    DEALLOCATE PREPARE ew_stmt;
  END IF;
END //

CREATE PROCEDURE ew_backfill_runtime()
BEGIN
  -- Fahrzeugkennzeichen: altes Feld plate in neue kompatible Felder spiegeln.
  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='vehicles' AND COLUMN_NAME='plate')
     AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='vehicles' AND COLUMN_NAME='license_plate') THEN
    UPDATE `vehicles`
       SET `license_plate` = `plate`
     WHERE (`license_plate` IS NULL OR `license_plate` = '')
       AND `plate` IS NOT NULL AND `plate` <> '';
  END IF;

  -- Bauaktenbezug für Dokumente/Fotos/Aufgaben aus Baustelle nachziehen.
  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='documents' AND COLUMN_NAME='project_id')
     AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='construction_sites' AND COLUMN_NAME='project_id') THEN
    UPDATE `documents` d
    JOIN `construction_sites` cs ON cs.id = d.site_id AND cs.tenant_id = d.tenant_id
       SET d.project_id = cs.project_id
     WHERE (d.project_id IS NULL OR d.project_id = '')
       AND cs.project_id IS NOT NULL AND cs.project_id <> '';
  END IF;

  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='photos' AND COLUMN_NAME='project_id')
     AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='construction_sites' AND COLUMN_NAME='project_id') THEN
    UPDATE `photos` p
    JOIN `construction_sites` cs ON cs.id = p.site_id AND cs.tenant_id = p.tenant_id
       SET p.project_id = cs.project_id
     WHERE (p.project_id IS NULL OR p.project_id = '')
       AND cs.project_id IS NOT NULL AND cs.project_id <> '';
  END IF;

  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='tasks' AND COLUMN_NAME='project_id')
     AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='tasks' AND COLUMN_NAME='site_id')
     AND EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME='construction_sites' AND COLUMN_NAME='project_id') THEN
    UPDATE `tasks` t
    JOIN `construction_sites` cs ON cs.id = t.site_id AND cs.tenant_id = t.tenant_id
       SET t.project_id = cs.project_id
     WHERE (t.project_id IS NULL OR t.project_id = '')
       AND cs.project_id IS NOT NULL AND cs.project_id <> '';
  END IF;
END //

DELIMITER ;

-- -----------------------------------------------------------------------------
-- 1) Runtime-/Kompatibilitätsfelder, die den aktuellen 500er vermeiden
-- -----------------------------------------------------------------------------
CALL ew_add_column('vehicles', 'license_plate', 'VARCHAR(255) NULL AFTER `plate`');
CALL ew_add_column('vehicles', 'registration', 'VARCHAR(255) NULL AFTER `license_plate`');

CALL ew_add_column('projects', 'hinweise', 'TEXT NULL AFTER `notes`');
CALL ew_add_column('construction_sites', 'hinweise', 'TEXT NULL AFTER `notes`');

CALL ew_add_column('calendar_events', 'all_day', 'TINYINT(1) NOT NULL DEFAULT 0 AFTER `end_at`');
CALL ew_add_column('calendar_events', 'event_type', 'VARCHAR(80) NULL AFTER `all_day`');

-- -----------------------------------------------------------------------------
-- 2) Planung / Kolonnenplaner / Plantafel
-- -----------------------------------------------------------------------------
CALL ew_add_column('planning_assignments', 'project_id', 'CHAR(32) NULL AFTER `tenant_id`');
CALL ew_add_column('planning_assignments', 'site_id', 'VARCHAR(255) NULL AFTER `project_id`');
CALL ew_add_column('planning_assignments', 'crew_id', 'VARCHAR(255) NULL AFTER `site_id`');
CALL ew_add_column('planning_assignments', 'vehicle_id', 'VARCHAR(255) NULL AFTER `crew_id`');
CALL ew_add_column('planning_assignments', 'machine_id', 'VARCHAR(255) NULL AFTER `vehicle_id`');
CALL ew_add_column('planning_assignments', 'title', 'VARCHAR(255) NOT NULL DEFAULT ''Einplanung'' AFTER `machine_id`');
CALL ew_add_column('planning_assignments', 'status', 'VARCHAR(80) NULL DEFAULT ''planned'' AFTER `title`');
CALL ew_add_column('planning_assignments', 'start_at', 'DATETIME NULL AFTER `status`');
CALL ew_add_column('planning_assignments', 'end_at', 'DATETIME NULL AFTER `start_at`');
CALL ew_add_column('planning_assignments', 'notes', 'TEXT NULL AFTER `end_at`');

CALL ew_add_index('planning_assignments', 'idx_planning_assignments_project', 'ADD KEY `idx_planning_assignments_project` (`project_id`)');
CALL ew_add_index('planning_assignments', 'idx_planning_assignments_site', 'ADD KEY `idx_planning_assignments_site` (`site_id`)');
CALL ew_add_index('planning_assignments', 'idx_planning_assignments_time', 'ADD KEY `idx_planning_assignments_time` (`tenant_id`,`start_at`,`end_at`)');

-- -----------------------------------------------------------------------------
-- 3) Aufgaben, Erstbegehung, Dokumente und Bilder mit Bauaktenbezug
-- -----------------------------------------------------------------------------
CALL ew_add_column('tasks', 'project_id', 'CHAR(32) NULL AFTER `tenant_id`');
CALL ew_add_index('tasks', 'idx_tasks_project', 'ADD KEY `idx_tasks_project` (`tenant_id`,`project_id`)');
CALL ew_add_index('tasks', 'idx_tasks_site', 'ADD KEY `idx_tasks_site` (`tenant_id`,`site_id`)');
CALL ew_add_index('tasks', 'idx_tasks_due', 'ADD KEY `idx_tasks_due` (`tenant_id`,`status`,`due_date`)');

CALL ew_add_column('documents', 'project_id', 'VARCHAR(255) NULL AFTER `site_id`');
CALL ew_add_index('documents', 'idx_documents_project', 'ADD KEY `idx_documents_project` (`tenant_id`,`project_id`)');
CALL ew_add_index('documents', 'idx_documents_site', 'ADD KEY `idx_documents_site` (`tenant_id`,`site_id`)');

CALL ew_add_column('photos', 'project_id', 'VARCHAR(255) NULL AFTER `site_id`');
CALL ew_add_column('photos', 'thumbnail_path', 'VARCHAR(500) NULL AFTER `compressed_path`');
CALL ew_add_column('photos', 'original_size_bytes', 'DECIMAL(14,2) NULL AFTER `thumbnail_path`');
CALL ew_add_column('photos', 'compressed_size_bytes', 'DECIMAL(14,2) NULL AFTER `original_size_bytes`');
CALL ew_add_column('photos', 'thumbnail_size_bytes', 'DECIMAL(14,2) NULL AFTER `compressed_size_bytes`');
CALL ew_add_column('photos', 'width_px', 'INT NULL AFTER `thumbnail_size_bytes`');
CALL ew_add_column('photos', 'height_px', 'INT NULL AFTER `width_px`');
CALL ew_add_index('photos', 'idx_photos_project', 'ADD KEY `idx_photos_project` (`tenant_id`,`project_id`)');
CALL ew_add_index('photos', 'idx_photos_site', 'ADD KEY `idx_photos_site` (`tenant_id`,`site_id`)');

-- -----------------------------------------------------------------------------
-- 4) Fehlende Hilfstabellen für Bauakte, QM, Freigaben, SSO, Präsenz
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `quality_checks` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  site_id VARCHAR(255) NULL,
  project_id VARCHAR(255) NULL,
  room_id VARCHAR(255) NULL,
  check_type VARCHAR(80) NULL,
  title VARCHAR(255) NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'open',
  result VARCHAR(80) NULL,
  measured_value VARCHAR(120) NULL,
  target_value VARCHAR(120) NULL,
  notes TEXT NULL,
  checked_by CHAR(32) NULL,
  checked_at DATETIME NULL,
  created_by CHAR(32) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_quality_checks_site (tenant_id, site_id),
  KEY idx_quality_checks_project (tenant_id, project_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `document_shares` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  document_id CHAR(32) NOT NULL,
  shared_with_email VARCHAR(180) NULL,
  share_token_hash CHAR(64) NULL,
  expires_at DATETIME NULL,
  can_download TINYINT(1) DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_doc_shares (tenant_id, document_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `photo_annotations` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  photo_id CHAR(32) NOT NULL,
  annotation_type VARCHAR(80) NULL,
  x DECIMAL(10,4) NULL,
  y DECIMAL(10,4) NULL,
  width DECIMAL(10,4) NULL,
  height DECIMAL(10,4) NULL,
  text TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_photo_annotations (tenant_id, photo_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `activity_presence` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  user_id CHAR(32) NOT NULL,
  project_id CHAR(32) NULL,
  site_id CHAR(32) NULL,
  screen VARCHAR(120) NULL,
  last_seen_at DATETIME NOT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  UNIQUE KEY uq_activity_user_context (tenant_id, user_id, project_id, site_id),
  KEY idx_activity_project (tenant_id, project_id, last_seen_at),
  KEY idx_activity_site (tenant_id, site_id, last_seen_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `sso_settings` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  provider VARCHAR(40) NOT NULL DEFAULT 'microsoft',
  is_enabled TINYINT(1) NOT NULL DEFAULT 0,
  provider_tenant_id VARCHAR(120) NOT NULL DEFAULT 'common',
  client_id VARCHAR(160) NULL,
  client_secret_encrypted TEXT NULL,
  redirect_uri VARCHAR(500) NULL,
  allowed_domains TEXT NULL,
  auto_provision TINYINT(1) NOT NULL DEFAULT 0,
  default_role_id CHAR(32) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_sso_provider (tenant_id, provider),
  KEY idx_sso_settings_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CALL ew_add_index('sso_settings', 'idx_sso_settings_tenant', 'ADD KEY `idx_sso_settings_tenant` (`tenant_id`)');

-- -----------------------------------------------------------------------------
-- 5) Erstbegehungstabellen absichern
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `inspection_templates` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `type` VARCHAR(80) NULL,
  schema_json JSON NULL,
  requires_photos TINYINT(1) NOT NULL DEFAULT 0,
  is_active TINYINT(1) NOT NULL DEFAULT 0,
  notes TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_inspection_templates_tenant (tenant_id),
  KEY idx_inspection_templates_type (`type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `inspection_results` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  template_id VARCHAR(255) NULL,
  site_id VARCHAR(255) NOT NULL,
  room_id VARCHAR(255) NULL,
  status VARCHAR(80) NULL,
  result_json JSON NULL,
  signed_by VARCHAR(255) NULL,
  signed_at DATETIME NULL,
  notes TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_inspection_results_tenant (tenant_id),
  KEY idx_inspection_results_site (tenant_id, site_id),
  KEY idx_inspection_results_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- -----------------------------------------------------------------------------
-- 6) Daten nachziehen
-- -----------------------------------------------------------------------------
CALL ew_backfill_runtime();

-- -----------------------------------------------------------------------------
-- 7) Rechte/Permissions für Module ergänzen, ohne Duplikate zu erzeugen
-- -----------------------------------------------------------------------------
INSERT IGNORE INTO permissions (id, code, label, scope, created_at) VALUES
  (MD5('planning.read'), 'planning.read', 'planning Lesen', 'tenant', NOW()),
  (MD5('planning.write'), 'planning.write', 'planning Schreiben', 'tenant', NOW()),
  (MD5('tasks.read'), 'tasks.read', 'tasks Lesen', 'tenant', NOW()),
  (MD5('tasks.write'), 'tasks.write', 'tasks Schreiben', 'tenant', NOW()),
  (MD5('projects.read'), 'projects.read', 'projects Lesen', 'tenant', NOW()),
  (MD5('projects.write'), 'projects.write', 'projects Schreiben', 'tenant', NOW()),
  (MD5('sites.read'), 'sites.read', 'sites Lesen', 'tenant', NOW()),
  (MD5('sites.write'), 'sites.write', 'sites Schreiben', 'tenant', NOW()),
  (MD5('documents.read'), 'documents.read', 'documents Lesen', 'tenant', NOW()),
  (MD5('documents.write'), 'documents.write', 'documents Schreiben', 'tenant', NOW()),
  (MD5('photos.read'), 'photos.read', 'photos Lesen', 'tenant', NOW()),
  (MD5('photos.write'), 'photos.write', 'photos Schreiben', 'tenant', NOW()),
  (MD5('inspections.read'), 'inspections.read', 'inspections Lesen', 'tenant', NOW()),
  (MD5('inspections.write'), 'inspections.write', 'inspections Schreiben', 'tenant', NOW()),
  (MD5('admin.read'), 'admin.read', 'admin Lesen', 'tenant', NOW()),
  (MD5('admin.write'), 'admin.write', 'admin Schreiben', 'tenant', NOW());

DROP PROCEDURE IF EXISTS ew_backfill_runtime;
DROP PROCEDURE IF EXISTS ew_add_index;
DROP PROCEDURE IF EXISTS ew_add_column;

SET FOREIGN_KEY_CHECKS = 1;

-- Prüfungen nach dem Import:
-- SHOW COLUMNS FROM `vehicles` LIKE 'license_plate';
-- SHOW COLUMNS FROM `projects` LIKE 'hinweise';
-- SHOW COLUMNS FROM `construction_sites` LIKE 'hinweise';
-- SHOW COLUMNS FROM `calendar_events` LIKE 'all_day';
-- SHOW INDEX FROM `planning_assignments`;
-- EstrichWeb Kalender-Überarbeitung: Kategorien/Farben für Google-ähnlichen Kalender
-- MySQL 8 kompatibel, idempotent
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `calendar_categories` (
  `id` CHAR(32) PRIMARY KEY,
  `tenant_id` CHAR(32) NOT NULL,
  `code` VARCHAR(80) NOT NULL,
  `label` VARCHAR(160) NOT NULL,
  `color` VARCHAR(32) NULL,
  `icon` VARCHAR(80) NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 1,
  `sort_order` INT NOT NULL DEFAULT 100,
  `notes` TEXT NULL,
  `created_at` DATETIME NOT NULL,
  `updated_at` DATETIME NULL,
  `deleted_at` DATETIME NULL,
  UNIQUE KEY `uq_calendar_category_code` (`tenant_id`,`code`),
  KEY `idx_calendar_categories_tenant` (`tenant_id`),
  KEY `idx_calendar_categories_active` (`tenant_id`,`is_active`,`sort_order`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DROP PROCEDURE IF EXISTS ew_add_column;
DELIMITER //
CREATE PROCEDURE ew_add_column(IN p_table VARCHAR(64), IN p_column VARCHAR(64), IN p_def VARCHAR(1000))
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_column
  ) THEN
    SET @ddl = CONCAT('ALTER TABLE `', REPLACE(p_table,'`','``'), '` ADD COLUMN `', REPLACE(p_column,'`','``'), '` ', p_def);
    PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
END //
DELIMITER ;

CALL ew_add_column('calendar_events','all_day','TINYINT(1) NOT NULL DEFAULT 0 AFTER `end_at`');
CALL ew_add_column('calendar_events','event_type','VARCHAR(80) NULL AFTER `all_day`');
CALL ew_add_column('calendar_events','color','VARCHAR(255) NULL AFTER `event_type`');
CALL ew_add_column('calendar_events','visibility','VARCHAR(80) NULL AFTER `color`');
CALL ew_add_column('calendar_events','recurrence_rule','VARCHAR(255) NULL AFTER `visibility`');
CALL ew_add_column('calendar_events','reminder_minutes','DECIMAL(14,2) NULL AFTER `recurrence_rule`');
CALL ew_add_column('calendar_events','external_provider','VARCHAR(80) NULL AFTER `reminder_minutes`');
CALL ew_add_column('calendar_events','external_id','VARCHAR(255) NULL AFTER `external_provider`');

DROP PROCEDURE IF EXISTS ew_add_column;

-- Standard-Kategorien für alle bestehenden Mandanten anlegen.
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'site_visit')),
       t.id,'site_visit','Erstbegehung','#1a73e8','location_on',1,10,NOW(),NOW() FROM tenants t;
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'installation')),
       t.id,'installation','Ausführung','#188038','construction',1,20,NOW(),NOW() FROM tenants t;
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'measurement')),
       t.id,'measurement','Messung / QM','#f9ab00','science',1,30,NOW(),NOW() FROM tenants t;
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'acceptance')),
       t.id,'acceptance','Abnahme','#9334e6','fact_check',1,40,NOW(),NOW() FROM tenants t;
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'delivery')),
       t.id,'delivery','Lieferung','#b06000','local_shipping',1,50,NOW(),NOW() FROM tenants t;
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'meeting')),
       t.id,'meeting','Besprechung','#0097a7','groups',1,60,NOW(),NOW() FROM tenants t;
INSERT IGNORE INTO `calendar_categories` (`id`,`tenant_id`,`code`,`label`,`color`,`icon`,`is_active`,`sort_order`,`created_at`,`updated_at`)
SELECT MD5(CONCAT(t.id,'office')),
       t.id,'office','Interner Termin','#5f6368','business_center',1,70,NOW(),NOW() FROM tenants t;

-- Farbe vorhandener Termine anhand der Kategorie nachziehen, falls leer.
UPDATE `calendar_events` ce
JOIN `calendar_categories` cc ON cc.tenant_id = ce.tenant_id AND cc.code = ce.event_type
SET ce.color = cc.color
WHERE ce.deleted_at IS NULL AND (ce.color IS NULL OR ce.color = '');
