-- EstrichWeb Patch: Lexware Auto-Sync, Kalender aus Auftragsanlage entfernen, Karten-/Export-Erweiterungen

CREATE TABLE IF NOT EXISTS `lexware_synced_records` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  entity_type VARCHAR(80) NOT NULL,
  lexware_id VARCHAR(120) NOT NULL,
  lexware_number VARCHAR(120) NULL,
  local_entity_type VARCHAR(80) NULL,
  local_entity_id CHAR(32) NULL,
  title VARCHAR(255) NULL,
  status VARCHAR(80) NULL,
  document_date DATE NULL,
  amount_total DECIMAL(14,2) NULL,
  currency VARCHAR(8) NULL,
  contact_lexware_id VARCHAR(120) NULL,
  raw_json MEDIUMTEXT NULL,
  last_synced_at DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_lexware_synced (tenant_id,entity_type,lexware_id),
  KEY idx_lexware_synced_tenant (tenant_id),
  KEY idx_lexware_synced_local (tenant_id,local_entity_type,local_entity_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `lexware_auto_sync_runs` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  started_at DATETIME NOT NULL,
  finished_at DATETIME NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'running',
  summary_json TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  KEY idx_lexware_auto_sync_runs_tenant (tenant_id, started_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `lexware_connections` ADD COLUMN IF NOT EXISTS `auto_sync_interval_minutes` INT NOT NULL DEFAULT 4 AFTER `auto_sync_projects`;
ALTER TABLE `lexware_connections` ADD COLUMN IF NOT EXISTS `sync_contacts` TINYINT(1) NOT NULL DEFAULT 1 AFTER `auto_sync_interval_minutes`;
ALTER TABLE `lexware_connections` ADD COLUMN IF NOT EXISTS `sync_vouchers` TINYINT(1) NOT NULL DEFAULT 1 AFTER `sync_contacts`;
ALTER TABLE `lexware_connections` ADD COLUMN IF NOT EXISTS `sync_invoices` TINYINT(1) NOT NULL DEFAULT 1 AFTER `sync_vouchers`;
ALTER TABLE `lexware_connections` ADD COLUMN IF NOT EXISTS `sync_quotations` TINYINT(1) NOT NULL DEFAULT 1 AFTER `sync_invoices`;
ALTER TABLE `lexware_connections` ADD COLUMN IF NOT EXISTS `last_auto_sync_at` DATETIME NULL AFTER `last_test_at`;

ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `project_id` CHAR(32) NULL AFTER `tenant_id`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `site_id` CHAR(32) NULL AFTER `project_id`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `event_type` VARCHAR(80) NULL;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `color` VARCHAR(20) NULL;
