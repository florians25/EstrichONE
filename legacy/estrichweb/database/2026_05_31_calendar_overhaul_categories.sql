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
