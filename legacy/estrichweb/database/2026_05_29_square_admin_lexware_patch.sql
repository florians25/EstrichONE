-- EstrichWeb Patch: eckiges Büro-Design, Admin-Verwaltung, Lexware Office Verknüpfung
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `lexware_connections` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  api_base_url VARCHAR(255) NOT NULL DEFAULT 'https://api.lexware.io',
  api_key_encrypted TEXT NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 0,
  auto_sync_customers TINYINT(1) NOT NULL DEFAULT 1,
  auto_sync_projects TINYINT(1) NOT NULL DEFAULT 1,
  last_test_at DATETIME NULL,
  last_error TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_lexware_connections_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `lexware_entity_links` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  local_entity_type VARCHAR(80) NOT NULL,
  local_entity_id CHAR(32) NOT NULL,
  lexware_entity_type VARCHAR(80) NOT NULL,
  lexware_id VARCHAR(120) NULL,
  lexware_number VARCHAR(120) NULL,
  sync_direction VARCHAR(40) NOT NULL DEFAULT 'manual',
  status VARCHAR(40) NOT NULL DEFAULT 'linked',
  last_synced_at DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_lexware_local (tenant_id,local_entity_type,local_entity_id,lexware_entity_type),
  KEY idx_lexware_remote (tenant_id,lexware_entity_type,lexware_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `customers` ADD COLUMN `lexware_contact_id` VARCHAR(120) NULL AFTER `customer_no`;
ALTER TABLE `projects` ADD COLUMN `lexware_order_id` VARCHAR(120) NULL AFTER `project_no`;
ALTER TABLE `projects` ADD COLUMN `lexware_quote_id` VARCHAR(120) NULL AFTER `lexware_order_id`;
