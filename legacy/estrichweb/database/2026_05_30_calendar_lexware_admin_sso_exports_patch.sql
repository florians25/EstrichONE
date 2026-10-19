-- EstrichWeb Patch 2026-05-30
-- Kalenderkategorien, robuste Kolonnenplanung, Microsoft SSO Administration, Lexware-Verknüpfung, Export-Log.

CREATE TABLE IF NOT EXISTS `calendar_event_categories` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  code VARCHAR(80) NOT NULL,
  name VARCHAR(120) NOT NULL,
  color VARCHAR(20) NOT NULL,
  sort_order INT NOT NULL DEFAULT 100,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_calendar_event_categories_code (tenant_id, code),
  KEY idx_calendar_event_categories_tenant (tenant_id)
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

CREATE TABLE IF NOT EXISTS `export_jobs` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  project_id CHAR(32) NULL,
  site_id CHAR(32) NULL,
  export_type VARCHAR(80) NOT NULL,
  title VARCHAR(255) NOT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'created',
  file_path VARCHAR(500) NULL,
  created_by CHAR(32) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_export_jobs_tenant (tenant_id),
  KEY idx_export_jobs_project (tenant_id, project_id),
  KEY idx_export_jobs_site (tenant_id, site_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `project_id` CHAR(32) NULL AFTER `tenant_id`;
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `site_id` CHAR(32) NULL AFTER `project_id`;
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `crew_id` CHAR(32) NULL;
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `vehicle_id` CHAR(32) NULL;
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `machine_id` CHAR(32) NULL;
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `title` VARCHAR(255) NOT NULL DEFAULT 'Einplanung';
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `status` VARCHAR(40) NOT NULL DEFAULT 'planned';
ALTER TABLE `planning_assignments` ADD COLUMN IF NOT EXISTS `notes` TEXT NULL;

ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `project_id` CHAR(32) NULL AFTER `tenant_id`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `site_id` CHAR(32) NULL AFTER `project_id`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `all_day` TINYINT(1) NOT NULL DEFAULT 0 AFTER `end_at`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `event_type` VARCHAR(80) NULL AFTER `all_day`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `color` VARCHAR(20) NULL AFTER `event_type`;

ALTER TABLE `customers` ADD COLUMN IF NOT EXISTS `lexware_contact_id` VARCHAR(120) NULL AFTER `customer_no`;
ALTER TABLE `projects` ADD COLUMN IF NOT EXISTS `lexware_order_id` VARCHAR(120) NULL AFTER `project_no`;
ALTER TABLE `projects` ADD COLUMN IF NOT EXISTS `lexware_quote_id` VARCHAR(120) NULL AFTER `lexware_order_id`;
