-- EstrichWeb Patch 2026-05-31
-- Lexware-Angebote mit Aufträgen verknüpfen, Erstbegehung-Medien/Notizen und reduzierte Sync-Logs.
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `lexware_synced_records` (
  `id` CHAR(32) PRIMARY KEY,
  `tenant_id` CHAR(32) NOT NULL,
  `entity_type` VARCHAR(80) NOT NULL,
  `lexware_id` VARCHAR(120) NOT NULL,
  `lexware_number` VARCHAR(120) NULL,
  `local_entity_type` VARCHAR(80) NULL,
  `local_entity_id` CHAR(32) NULL,
  `title` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `document_date` DATE NULL,
  `amount_total` DECIMAL(14,2) NULL,
  `currency` VARCHAR(12) NULL,
  `contact_lexware_id` VARCHAR(120) NULL,
  `raw_json` JSON NULL,
  `last_synced_at` DATETIME NULL,
  `created_at` DATETIME NOT NULL,
  `updated_at` DATETIME NULL,
  UNIQUE KEY `uq_lexware_synced_record` (`tenant_id`,`entity_type`,`lexware_id`),
  KEY `idx_lexware_synced_local` (`tenant_id`,`local_entity_type`,`local_entity_id`),
  KEY `idx_lexware_synced_type` (`tenant_id`,`entity_type`,`document_date`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `lexware_entity_links` (
  `id` CHAR(32) PRIMARY KEY,
  `tenant_id` CHAR(32) NOT NULL,
  `local_entity_type` VARCHAR(80) NOT NULL,
  `local_entity_id` CHAR(32) NOT NULL,
  `lexware_entity_type` VARCHAR(80) NOT NULL,
  `lexware_id` VARCHAR(120) NULL,
  `lexware_number` VARCHAR(120) NULL,
  `sync_direction` VARCHAR(40) NOT NULL DEFAULT 'manual',
  `status` VARCHAR(40) NOT NULL DEFAULT 'linked',
  `last_synced_at` DATETIME NULL,
  `created_at` DATETIME NOT NULL,
  `updated_at` DATETIME NULL,
  `deleted_at` DATETIME NULL,
  UNIQUE KEY `uq_lexware_local` (`tenant_id`,`local_entity_type`,`local_entity_id`,`lexware_entity_type`),
  KEY `idx_lexware_remote` (`tenant_id`,`lexware_entity_type`,`lexware_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Speicher schonen: alte erfolgreiche Sync-Routine-Logs entfernen, Fehler bleiben erhalten.
DELETE FROM `lexware_sync_logs`
 WHERE `status` = 'success'
   AND `entity_type` IN ('auto_sync','customer','project','documents','connection');
