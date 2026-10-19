-- ############################################################################
-- ESTRICHWEB – IDEMPOTENTES DB-UPDATE (Stand 2026-06-07)
-- Laeuft FEHLERFREI ueber eine BESTEHENDE Datenbank und ist mehrfach ausfuehrbar.
--   * CREATE TABLE IF NOT EXISTS  (Tabellen nur wenn fehlend)
--   * Spalten via Prozedur + CONTINUE HANDLER (nur wenn fehlend, nie Abbruch)
--   * Rollen/Rechte via INSERT IGNORE
--   * Indizes nur wenn fehlend
-- KEIN DROP TABLE, KEIN TRUNCATE, KEIN DELETE.
-- ############################################################################
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS=0;

-- ===== 1) BASIS-STRUKTUR (schema.sql) =====
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS=0;

CREATE TABLE IF NOT EXISTS tenants (
  id CHAR(32) PRIMARY KEY,
  slug VARCHAR(80) NOT NULL UNIQUE,
  name VARCHAR(180) NOT NULL,
  email VARCHAR(180) NULL,
  phone VARCHAR(80) NULL,
  address VARCHAR(255) NULL,
  zip VARCHAR(20) NULL,
  city VARCHAR(120) NULL,
  plan VARCHAR(40) NOT NULL DEFAULT 'enterprise',
  settings_json JSON NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  name VARCHAR(180) NOT NULL,
  email VARCHAR(180) NOT NULL,
  phone VARCHAR(80) NULL,
  password_hash VARCHAR(255) NOT NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  two_factor_enabled TINYINT(1) NOT NULL DEFAULT 0,
  two_factor_secret VARCHAR(255) NULL,
  role_hint VARCHAR(180) NULL,
  last_login_at DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_user_email (tenant_id,email),
  KEY idx_users_tenant (tenant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS roles (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NULL,
  name VARCHAR(120) NOT NULL,
  code VARCHAR(80) NOT NULL,
  description TEXT NULL,
  is_system TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_roles_tenant (tenant_id),
  KEY idx_roles_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS permissions (
  id CHAR(32) PRIMARY KEY,
  code VARCHAR(120) NOT NULL UNIQUE,
  label VARCHAR(180) NOT NULL,
  scope VARCHAR(40) NOT NULL DEFAULT 'tenant',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_roles (user_id CHAR(32) NOT NULL, role_id CHAR(32) NOT NULL, PRIMARY KEY(user_id,role_id), KEY idx_ur_role(role_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS role_permissions (role_id CHAR(32) NOT NULL, permission_id CHAR(32) NOT NULL, PRIMARY KEY(role_id,permission_id), KEY idx_rp_permission(permission_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS record_permissions (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, entity_type VARCHAR(80) NOT NULL, entity_id CHAR(32) NOT NULL, user_id CHAR(32) NULL, role_id CHAR(32) NULL, can_read TINYINT(1) DEFAULT 0, can_write TINYINT(1) DEFAULT 0, can_delete TINYINT(1) DEFAULT 0, can_export TINYINT(1) DEFAULT 0, can_share TINYINT(1) DEFAULT 0, can_archive TINYINT(1) DEFAULT 0, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_record_permissions (tenant_id,entity_type,entity_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS refresh_tokens (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, user_id CHAR(32) NOT NULL, token_hash CHAR(64) NOT NULL, expires_at DATETIME NOT NULL, revoked_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, KEY idx_refresh_user(user_id), KEY idx_refresh_hash(token_hash)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `customers` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `customer_no` VARCHAR(255) NULL,
  `email` VARCHAR(180) NULL,
  `phone` VARCHAR(80) NULL,
  `address` VARCHAR(255) NULL,
  `zip` VARCHAR(255) NULL,
  `city` VARCHAR(255) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_customers_tenant (tenant_id),
  KEY idx_customers_name (`name`),
  KEY idx_customers_customer_no (`customer_no`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `contacts` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `customer_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `role` VARCHAR(255) NULL,
  `email` VARCHAR(180) NULL,
  `phone` VARCHAR(80) NULL,
  `mobile` VARCHAR(80) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_contacts_tenant (tenant_id),
  KEY idx_contacts_name (`name`),
  KEY idx_contacts_email (`email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `projects` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `customer_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `project_no` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `address` VARCHAR(255) NULL,
  `zip` VARCHAR(255) NULL,
  `city` VARCHAR(255) NULL,
  `start_date` DATE NULL,
  `end_date` DATE NULL,
  `budget_net` DECIMAL(14,2) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_projects_tenant (tenant_id),
  KEY idx_projects_name (`name`),
  KEY idx_projects_project_no (`project_no`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `project_phases` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `project_id` VARCHAR(255) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `status` VARCHAR(80) NULL,
  `start_date` DATE NULL,
  `end_date` DATE NULL,
  `progress_percent` DECIMAL(14,2) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_project_phases_tenant (tenant_id),
  KEY idx_project_phases_name (`name`),
  KEY idx_project_phases_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `construction_sites` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `project_id` VARCHAR(255) NULL,
  `customer_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `site_no` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `address` VARCHAR(255) NULL,
  `zip` VARCHAR(255) NULL,
  `city` VARCHAR(255) NULL,
  `site_manager` VARCHAR(255) NULL,
  `phone` VARCHAR(80) NULL,
  `screed_type` VARCHAR(80) NULL,
  `area_m2` DECIMAL(14,2) NULL,
  `planned_start` DATE NULL,
  `planned_end` DATE NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_construction_sites_tenant (tenant_id),
  KEY idx_construction_sites_name (`name`),
  KEY idx_construction_sites_site_no (`site_no`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `rooms` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `floor` VARCHAR(255) NULL,
  `area_m2` DECIMAL(14,2) NULL,
  `screed_type` VARCHAR(80) NULL,
  `height_mm` DECIMAL(14,2) NULL,
  `heating` TINYINT(1) NOT NULL DEFAULT 0,
  `status` VARCHAR(80) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_rooms_tenant (tenant_id),
  KEY idx_rooms_name (`name`),
  KEY idx_rooms_floor (`floor`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `screed_types` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `norm` VARCHAR(255) NULL,
  `binder` VARCHAR(255) NULL,
  `drying_time_days` DECIMAL(14,2) NULL,
  `min_thickness_mm` DECIMAL(14,2) NULL,
  `is_heated` TINYINT(1) NOT NULL DEFAULT 0,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_screed_types_tenant (tenant_id),
  KEY idx_screed_types_name (`name`),
  KEY idx_screed_types_norm (`norm`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `screed_components` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `component_type` VARCHAR(80) NULL,
  `manufacturer` VARCHAR(255) NULL,
  `product_no` VARCHAR(255) NULL,
  `thickness_mm` DECIMAL(14,2) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_screed_components_tenant (tenant_id),
  KEY idx_screed_components_name (`name`),
  KEY idx_screed_components_component_type (`component_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `screed_buildups` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `insulation` VARCHAR(255) NULL,
  `impact_sound` VARCHAR(255) NULL,
  `foil` VARCHAR(255) NULL,
  `waterproofing` VARCHAR(255) NULL,
  `underfloor_heating` TINYINT(1) NOT NULL DEFAULT 0,
  `edge_strip` TINYINT(1) NOT NULL DEFAULT 0,
  `movement_joints` TEXT NULL,
  `pipe_penetrations` TEXT NULL,
  `total_height_mm` DECIMAL(14,2) NULL,
  `status` VARCHAR(80) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_screed_buildups_tenant (tenant_id),
  KEY idx_screed_buildups_name (`name`),
  KEY idx_screed_buildups_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `inspection_templates` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `type` VARCHAR(80) NULL,
  `schema_json` JSON NULL,
  `requires_photos` TINYINT(1) NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 0,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_inspection_templates_tenant (tenant_id),
  KEY idx_inspection_templates_name (`name`),
  KEY idx_inspection_templates_type (`type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `inspection_results` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `template_id` VARCHAR(255) NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `result_json` JSON NULL,
  `signed_by` VARCHAR(255) NULL,
  `signed_at` DATETIME NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_inspection_results_tenant (tenant_id),
  KEY idx_inspection_results_status (`status`),
  KEY idx_inspection_results_signed_by (`signed_by`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `measurements` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `measurement_type` VARCHAR(80) NULL,
  `value_text` VARCHAR(255) NULL,
  `unit` VARCHAR(255) NULL,
  `measured_at` DATETIME NULL,
  `measured_by` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_measurements_tenant (tenant_id),
  KEY idx_measurements_measurement_type (`measurement_type`),
  KEY idx_measurements_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `cm_measurements` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `sample_location` VARCHAR(255) NOT NULL,
  `sample_weight_g` DECIMAL(14,2) NULL,
  `pressure_bar` DECIMAL(14,2) NULL,
  `moisture_percent` DECIMAL(14,2) NULL,
  `temperature_c` DECIMAL(14,2) NULL,
  `humidity_percent` DECIMAL(14,2) NULL,
  `measured_at` DATETIME NULL,
  `result_status` VARCHAR(80) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_cm_measurements_tenant (tenant_id),
  KEY idx_cm_measurements_sample_location (`sample_location`),
  KEY idx_cm_measurements_result_status (`result_status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `planning_assignments` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `project_id` VARCHAR(255) NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `crew_id` VARCHAR(255) NULL,
  `vehicle_id` VARCHAR(255) NULL,
  `machine_id` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `status` VARCHAR(80) NULL,
  `start_at` DATETIME NULL,
  `end_at` DATETIME NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_planning_assignments_tenant (tenant_id),
  KEY idx_planning_assignments_project (`project_id`),
  KEY idx_planning_assignments_site (`site_id`),
  KEY idx_planning_assignments_title (`title`),
  KEY idx_planning_assignments_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `planning_boards` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `view_type` VARCHAR(80) NULL,
  `date_from` DATE NULL,
  `date_to` DATE NULL,
  `settings_json` JSON NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_planning_boards_tenant (tenant_id),
  KEY idx_planning_boards_name (`name`),
  KEY idx_planning_boards_view_type (`view_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `planning_conflicts` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `assignment_id` VARCHAR(255) NULL,
  `conflict_type` VARCHAR(80) NULL,
  `severity` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `message` TEXT NOT NULL,
  `resolved_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_planning_conflicts_tenant (tenant_id),
  KEY idx_planning_conflicts_conflict_type (`conflict_type`),
  KEY idx_planning_conflicts_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `calendar_events` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `project_id` VARCHAR(255) NOT NULL,
  `site_id` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `description` TEXT NULL,
  `location` VARCHAR(255) NULL,
  `start_at` DATETIME NULL,
  `end_at` DATETIME NULL,
  `all_day` TINYINT(1) NOT NULL DEFAULT 0,
  `event_type` VARCHAR(80) NULL,
  `color` VARCHAR(255) NULL,
  `visibility` VARCHAR(80) NULL,
  `recurrence_rule` VARCHAR(255) NULL,
  `reminder_minutes` DECIMAL(14,2) NULL,
  `external_provider` VARCHAR(80) NULL,
  `external_id` VARCHAR(255) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_calendar_events_tenant (tenant_id),
  KEY idx_calendar_events_project (`project_id`),
  KEY idx_calendar_events_site (`site_id`),
  KEY idx_calendar_events_title (`title`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `crews` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `foreman_user_id` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `color` VARCHAR(255) NULL,
  `capacity_m2_day` DECIMAL(14,2) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_crews_tenant (tenant_id),
  KEY idx_crews_name (`name`),
  KEY idx_crews_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `crew_members` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `crew_id` VARCHAR(255) NOT NULL,
  `user_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `role` VARCHAR(80) NULL,
  `valid_from` DATE NULL,
  `valid_to` DATE NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_crew_members_tenant (tenant_id),
  KEY idx_crew_members_name (`name`),
  KEY idx_crew_members_role (`role`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `vehicles` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `plate` VARCHAR(255) NULL,
  `type` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `inspection_due` DATE NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_vehicles_tenant (tenant_id),
  KEY idx_vehicles_name (`name`),
  KEY idx_vehicles_plate (`plate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `machines` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `serial_no` VARCHAR(255) NULL,
  `type` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `inspection_due` DATE NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_machines_tenant (tenant_id),
  KEY idx_machines_name (`name`),
  KEY idx_machines_serial_no (`serial_no`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `absences` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `user_id` VARCHAR(255) NULL,
  `crew_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `absence_type` VARCHAR(80) NULL,
  `start_date` DATE NULL,
  `end_date` DATE NULL,
  `status` VARCHAR(80) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_absences_tenant (tenant_id),
  KEY idx_absences_absence_type (`absence_type`),
  KEY idx_absences_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `tasks` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NULL,
  `room_id` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `description` TEXT NULL,
  `priority` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `due_date` DATE NULL,
  `assigned_user_id` VARCHAR(255) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_tasks_tenant (tenant_id),
  KEY idx_tasks_title (`title`),
  KEY idx_tasks_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `task_comments` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `task_id` VARCHAR(255) NOT NULL,
  `body` TEXT NOT NULL,
  `created_by_name` VARCHAR(255) NULL,
  `is_internal` TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_task_comments_tenant (tenant_id),
  KEY idx_task_comments_body (`body`(191))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `defects` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `description` TEXT NULL,
  `severity` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `assigned_user_id` VARCHAR(255) NULL,
  `due_date` DATE NULL,
  `closed_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_defects_tenant (tenant_id),
  KEY idx_defects_title (`title`),
  KEY idx_defects_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `defect_comments` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `defect_id` VARCHAR(255) NOT NULL,
  `body` TEXT NOT NULL,
  `created_by_name` VARCHAR(255) NULL,
  `is_customer_visible` TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_defect_comments_tenant (tenant_id),
  KEY idx_defect_comments_body (`body`(191))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `documents` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NULL,
  `room_id` VARCHAR(255) NULL,
  `name` VARCHAR(255) NOT NULL,
  `original_name` VARCHAR(255) NULL,
  `mime_type` VARCHAR(255) NULL,
  `size_bytes` DECIMAL(14,2) NULL,
  `storage_path` VARCHAR(255) NULL,
  `category` VARCHAR(80) NULL,
  `ocr_text` TEXT NULL,
  `is_customer_visible` TINYINT(1) NOT NULL DEFAULT 0,
  created_by CHAR(32) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_documents_tenant (tenant_id),
  KEY idx_documents_name (`name`),
  KEY idx_documents_category (`category`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `document_versions` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `document_id` VARCHAR(255) NOT NULL,
  `version_no` DECIMAL(14,2) NULL,
  `original_name` VARCHAR(255) NULL,
  `mime_type` VARCHAR(255) NULL,
  `size_bytes` DECIMAL(14,2) NULL,
  `storage_path` VARCHAR(255) NULL,
  `change_note` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_document_versions_tenant (tenant_id),
  KEY idx_document_versions_version_no (`version_no`),
  KEY idx_document_versions_original_name (`original_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `photos` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `caption` VARCHAR(255) NULL,
  `category` VARCHAR(80) NULL,
  `original_path` VARCHAR(500) NULL,
  `compressed_path` VARCHAR(500) NULL,
  `thumbnail_path` VARCHAR(500) NULL,
  `original_size_bytes` DECIMAL(14,2) NULL,
  `compressed_size_bytes` DECIMAL(14,2) NULL,
  `thumbnail_size_bytes` DECIMAL(14,2) NULL,
  `width_px` INT NULL,
  `height_px` INT NULL,
  `taken_at` DATETIME NULL,
  `gps_lat` DECIMAL(14,2) NULL,
  `gps_lng` DECIMAL(14,2) NULL,
  `annotations_json` JSON NULL,
  `is_customer_visible` TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_photos_tenant (tenant_id),
  KEY idx_photos_caption (`caption`),
  KEY idx_photos_category (`category`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `reports` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `title` VARCHAR(255) NOT NULL,
  `report_type` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `content` TEXT NULL,
  `generated_pdf_path` VARCHAR(255) NULL,
  `signed_by` VARCHAR(255) NULL,
  `signed_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_reports_tenant (tenant_id),
  KEY idx_reports_title (`title`),
  KEY idx_reports_report_type (`report_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `acceptances` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `room_id` VARCHAR(255) NULL,
  `customer_name` VARCHAR(255) NULL,
  `status` VARCHAR(80) NULL,
  `accepted_at` DATETIME NULL,
  `signature_path` VARCHAR(255) NULL,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_acceptances_tenant (tenant_id),
  KEY idx_acceptances_site_id (`site_id`),
  KEY idx_acceptances_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `quality_protocols` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NOT NULL,
  `title` VARCHAR(255) NOT NULL,
  `protocol_type` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `content_json` JSON NULL,
  `approved_by` VARCHAR(255) NULL,
  `approved_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_quality_protocols_tenant (tenant_id),
  KEY idx_quality_protocols_title (`title`),
  KEY idx_quality_protocols_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `warranty_cases` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NULL,
  `customer_name` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `description` TEXT NULL,
  `status` VARCHAR(80) NULL,
  `due_date` DATE NULL,
  `cost_estimate_net` DECIMAL(14,2) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_warranty_cases_tenant (tenant_id),
  KEY idx_warranty_cases_title (`title`),
  KEY idx_warranty_cases_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `customer_portal_items` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `customer_id` VARCHAR(255) NULL,
  `site_id` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `portal_type` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `payload_json` JSON NULL,
  `expires_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_customer_portal_items_tenant (tenant_id),
  KEY idx_customer_portal_items_title (`title`),
  KEY idx_customer_portal_items_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `subcontractor_orders` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NULL,
  `company_name` VARCHAR(255) NOT NULL,
  `title` VARCHAR(255) NOT NULL,
  `status` VARCHAR(80) NULL,
  `scope` TEXT NULL,
  `due_date` DATE NULL,
  `documents_required` TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_subcontractor_orders_tenant (tenant_id),
  KEY idx_subcontractor_orders_title (`title`),
  KEY idx_subcontractor_orders_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `notifications` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `user_id` VARCHAR(255) NULL,
  `title` VARCHAR(255) NOT NULL,
  `body` TEXT NULL,
  `channel` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `send_at` DATETIME NULL,
  `meta_json` JSON NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_notifications_tenant (tenant_id),
  KEY idx_notifications_title (`title`),
  KEY idx_notifications_channel (`channel`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `notification_templates` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `channel` VARCHAR(80) NULL,
  `subject` VARCHAR(255) NULL,
  `body` TEXT NULL,
  `variables_json` JSON NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_notification_templates_tenant (tenant_id),
  KEY idx_notification_templates_name (`name`),
  KEY idx_notification_templates_channel (`channel`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `lexware_sync_logs` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `entity_type` VARCHAR(80) NULL,
  `entity_id` VARCHAR(255) NULL,
  `external_id` VARCHAR(255) NULL,
  `direction` VARCHAR(80) NULL,
  `status` VARCHAR(80) NULL,
  `message` TEXT NULL,
  `payload_json` JSON NULL,
  `synced_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_lexware_sync_logs_tenant (tenant_id),
  KEY idx_lexware_sync_logs_entity_type (`entity_type`),
  KEY idx_lexware_sync_logs_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `workflows` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `entity_type` VARCHAR(80) NULL,
  `definition_json` JSON NULL,
  `is_active` TINYINT(1) NOT NULL DEFAULT 0,
  `notes` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_workflows_tenant (tenant_id),
  KEY idx_workflows_name (`name`),
  KEY idx_workflows_entity_type (`entity_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `checklist_templates` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `template_type` VARCHAR(80) NULL,
  `items_json` JSON NULL,
  `requires_photos` TINYINT(1) NOT NULL DEFAULT 0,
  `is_active` TINYINT(1) NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_checklist_templates_tenant (tenant_id),
  KEY idx_checklist_templates_name (`name`),
  KEY idx_checklist_templates_template_type (`template_type`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `device_sessions` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `user_id` VARCHAR(255) NULL,
  `device_name` VARCHAR(255) NOT NULL,
  `device_fingerprint` VARCHAR(255) NULL,
  `ip` VARCHAR(255) NULL,
  `user_agent` TEXT NULL,
  `status` VARCHAR(80) NULL,
  `last_seen_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_device_sessions_tenant (tenant_id),
  KEY idx_device_sessions_device_name (`device_name`),
  KEY idx_device_sessions_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `api_tokens` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `token_hash` VARCHAR(255) NULL,
  `scopes_json` JSON NULL,
  `status` VARCHAR(80) NULL,
  `expires_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_api_tokens_tenant (tenant_id),
  KEY idx_api_tokens_name (`name`),
  KEY idx_api_tokens_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `audit_logs` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `user_id` VARCHAR(255) NULL,
  `entity_type` VARCHAR(255) NULL,
  `entity_id` VARCHAR(255) NULL,
  `action` VARCHAR(80) NULL,
  `before_json` JSON NULL,
  `after_json` JSON NULL,
  `ip` VARCHAR(255) NULL,
  `user_agent` TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_audit_logs_tenant (tenant_id),
  KEY idx_audit_logs_entity_type (`entity_type`),
  KEY idx_audit_logs_action (`action`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `offline_sync_jobs` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `client_id` VARCHAR(255) NULL,
  `user_id` VARCHAR(255) NULL,
  `operation` VARCHAR(80) NULL,
  `entity_type` VARCHAR(255) NULL,
  `entity_id` VARCHAR(255) NULL,
  `payload_json` JSON NULL,
  `status` VARCHAR(80) NULL,
  `error_message` TEXT NULL,
  `processed_at` DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_offline_sync_jobs_tenant (tenant_id),
  KEY idx_offline_sync_jobs_client_id (`client_id`),
  KEY idx_offline_sync_jobs_status (`status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `login_logs` (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  `user_id` VARCHAR(255) NULL,
  `email` VARCHAR(180) NULL,
  `ip` VARCHAR(255) NULL,
  `user_agent` TEXT NULL,
  `success` TINYINT(1) NOT NULL DEFAULT 0,
  `failure_reason` VARCHAR(255) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_login_logs_tenant (tenant_id),
  KEY idx_login_logs_email (`email`),
  KEY idx_login_logs_success (`success`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS document_shares (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, document_id CHAR(32) NOT NULL, shared_with_email VARCHAR(180) NULL, share_token_hash CHAR(64) NULL, expires_at DATETIME NULL, can_download TINYINT(1) DEFAULT 0, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_doc_shares(tenant_id,document_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS photo_annotations (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, photo_id CHAR(32) NOT NULL, annotation_type VARCHAR(80) NULL, x DECIMAL(10,4) NULL, y DECIMAL(10,4) NULL, width DECIMAL(10,4) NULL, height DECIMAL(10,4) NULL, text TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_photo_annotations(tenant_id,photo_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS api_webhooks (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, name VARCHAR(180) NOT NULL, target_url VARCHAR(500) NOT NULL, event_types_json JSON NULL, secret_hash CHAR(64) NULL, is_active TINYINT(1) DEFAULT 1, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_webhooks_tenant(tenant_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS ai_jobs (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, job_type VARCHAR(80) NOT NULL, entity_type VARCHAR(80) NULL, entity_id CHAR(32) NULL, status VARCHAR(40) DEFAULT 'queued', input_json JSON NULL, output_json JSON NULL, error_message TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_ai_jobs(tenant_id,status)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
SET FOREIGN_KEY_CHECKS=1;

-- Lexware Office REST-API Integration: Verbindung und lokale Verknüpfungen
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

SET FOREIGN_KEY_CHECKS=1;

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
  UNIQUE KEY uq_activity_user_context (tenant_id,user_id,project_id,site_id),
  KEY idx_activity_project (tenant_id,project_id,last_seen_at),
  KEY idx_activity_site (tenant_id,site_id,last_seen_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- GF login/media/SSO patch
CREATE TABLE IF NOT EXISTS sso_login_tickets (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  user_id CHAR(32) NOT NULL,
  ticket_hash CHAR(64) NOT NULL,
  provider VARCHAR(40) NOT NULL,
  expires_at DATETIME NOT NULL,
  used_at DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  KEY idx_sso_ticket_hash(ticket_hash),
  KEY idx_sso_user(tenant_id,user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS document_shares (
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
  KEY idx_doc_shares(tenant_id,document_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE documents MODIFY COLUMN mime_type VARCHAR(120) NULL;
ALTER TABLE documents MODIFY COLUMN storage_path VARCHAR(500) NOT NULL;


CREATE TABLE IF NOT EXISTS `quality_checks` (
  `id` CHAR(32) PRIMARY KEY,
  `tenant_id` CHAR(32) NOT NULL,
  `site_id` VARCHAR(255) NULL,
  `project_id` VARCHAR(255) NULL,
  `room_id` VARCHAR(255) NULL,
  `check_type` VARCHAR(80) NULL,
  `title` VARCHAR(255) NULL,
  `status` VARCHAR(80) NOT NULL DEFAULT 'open',
  `result` VARCHAR(80) NULL,
  `measured_value` VARCHAR(120) NULL,
  `target_value` VARCHAR(120) NULL,
  `notes` TEXT NULL,
  `checked_by` CHAR(32) NULL,
  `checked_at` DATETIME NULL,
  `created_by` CHAR(32) NULL,
  `created_at` DATETIME NOT NULL,
  `updated_at` DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_quality_checks_site (`tenant_id`,`site_id`),
  KEY idx_quality_checks_project (`tenant_id`,`project_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===== 2) ZUSATZTABELLEN (App ensure_*) =====
CREATE TABLE IF NOT EXISTS project_number_sequences (tenant_id CHAR(32) NOT NULL, `year` CHAR(4) NOT NULL, last_no INT NOT NULL DEFAULT 0, created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL, PRIMARY KEY (tenant_id, `year`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS quality_checks (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, site_id VARCHAR(255) NULL, project_id VARCHAR(255) NULL, room_id VARCHAR(255) NULL, check_type VARCHAR(80) NULL, title VARCHAR(255) NULL, status VARCHAR(80) NOT NULL DEFAULT 'open', result VARCHAR(80) NULL, measured_value VARCHAR(120) NULL, target_value VARCHAR(120) NULL, notes TEXT NULL, checked_by CHAR(32) NULL, checked_at DATETIME NULL, created_by CHAR(32) NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_quality_checks_site (tenant_id, site_id), KEY idx_quality_checks_project (tenant_id, project_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS pumps (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, name VARCHAR(160) NOT NULL, manufacturer VARCHAR(120) NULL, model VARCHAR(120) NULL, serial_no VARCHAR(120) NULL, type VARCHAR(80) NULL, status VARCHAR(60) NULL DEFAULT 'available', max_output_l_min DECIMAL(10,2) NULL, max_pressure_bar DECIMAL(10,2) NULL, max_distance_m DECIMAL(10,2) NULL, hose_length_m DECIMAL(10,2) NULL, operating_hours INT NULL, year_built INT NULL, inspection_due DATE NULL, next_service DATE NULL, notes TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_pumps_tenant (tenant_id), KEY idx_pumps_status (tenant_id,status)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS app_settings (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NULL, setting_key VARCHAR(80) NOT NULL, value_json LONGTEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, UNIQUE KEY uq_app_setting (tenant_id,setting_key)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS project_notes (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, project_id VARCHAR(255) NOT NULL, entry_type VARCHAR(40) NOT NULL DEFAULT 'note', author_id CHAR(32) NULL, author_name VARCHAR(160) NULL, body TEXT NULL, document_id CHAR(32) NULL, meta_json TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_project_notes (tenant_id,project_id,created_at)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS customer_portal_access (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, customer_id VARCHAR(255) NOT NULL, token_hash CHAR(64) NOT NULL, pin_hash CHAR(64) NULL, is_active TINYINT(1) NOT NULL DEFAULT 1, last_access_at DATETIME NULL, failed_attempts INT NOT NULL DEFAULT 0, created_at DATETIME NOT NULL, updated_at DATETIME NULL, revoked_at DATETIME NULL, UNIQUE KEY uq_portal_token (token_hash), KEY idx_portal_customer (tenant_id,customer_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS call_logs (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, kind VARCHAR(20) NOT NULL DEFAULT 'call', caller_name VARCHAR(200) NULL, caller_phone VARCHAR(80) NULL, company VARCHAR(200) NULL, direction VARCHAR(20) NOT NULL DEFAULT 'inbound', subject VARCHAR(255) NULL, notes TEXT NULL, follow_up TINYINT(1) NOT NULL DEFAULT 0, inquiry_status VARCHAR(30) NOT NULL DEFAULT 'open', converted_project_id VARCHAR(255) NULL, project_id VARCHAR(255) NULL, note_id CHAR(32) NULL, handled_by VARCHAR(160) NULL, called_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_calls_tenant (tenant_id,created_at), KEY idx_calls_kind (tenant_id,kind,inquiry_status), KEY idx_calls_project (tenant_id,project_id), KEY idx_calls_followup (tenant_id,follow_up)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS calendar_categories (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, code VARCHAR(80) NOT NULL, label VARCHAR(160) NOT NULL, color VARCHAR(32) NULL, icon VARCHAR(80) NULL, is_active TINYINT(1) NOT NULL DEFAULT 1, sort_order INT NOT NULL DEFAULT 100, notes TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_calendar_category_code (tenant_id,code), KEY idx_calendar_categories_tenant (tenant_id), KEY idx_calendar_categories_active (tenant_id,is_active,sort_order)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS lexware_connections (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, api_base_url VARCHAR(255) NOT NULL DEFAULT 'https://api.lexware.io', api_key_encrypted TEXT NULL, is_active TINYINT(1) NOT NULL DEFAULT 0, auto_sync_customers TINYINT(1) NOT NULL DEFAULT 1, auto_sync_projects TINYINT(1) NOT NULL DEFAULT 1, last_test_at DATETIME NULL, last_error TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_lexware_connections_tenant (tenant_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS lexware_entity_links (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, local_entity_type VARCHAR(80) NOT NULL, local_entity_id CHAR(32) NOT NULL, lexware_entity_type VARCHAR(80) NOT NULL, lexware_id VARCHAR(120) NULL, lexware_number VARCHAR(120) NULL, sync_direction VARCHAR(40) NOT NULL DEFAULT 'manual', status VARCHAR(40) NOT NULL DEFAULT 'linked', last_synced_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_lexware_local (tenant_id,local_entity_type,local_entity_id,lexware_entity_type), KEY idx_lexware_remote (tenant_id,lexware_entity_type,lexware_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS lexware_synced_records (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, entity_type VARCHAR(80) NOT NULL, lexware_id VARCHAR(120) NOT NULL, lexware_number VARCHAR(120) NULL, local_entity_type VARCHAR(80) NULL, local_entity_id CHAR(32) NULL, title VARCHAR(255) NULL, status VARCHAR(80) NULL, document_date DATE NULL, amount_total DECIMAL(14,2) NULL, currency VARCHAR(8) NULL, contact_lexware_id VARCHAR(120) NULL, raw_json MEDIUMTEXT NULL, last_synced_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_lexware_synced (tenant_id,entity_type,lexware_id), KEY idx_lexware_synced_tenant (tenant_id), KEY idx_lexware_synced_local (tenant_id,local_entity_type,local_entity_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS lexware_auto_sync_runs (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, started_at DATETIME NOT NULL, finished_at DATETIME NULL, status VARCHAR(40) NOT NULL DEFAULT 'running', summary_json TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, KEY idx_lexware_auto_sync_runs_tenant (tenant_id, started_at)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS sso_login_tickets (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, user_id CHAR(32) NOT NULL, ticket_hash CHAR(64) NOT NULL, provider VARCHAR(40) NOT NULL, expires_at DATETIME NOT NULL, used_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, KEY idx_sso_ticket_hash(ticket_hash), KEY idx_sso_user(tenant_id,user_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS sso_settings (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, provider VARCHAR(40) NOT NULL DEFAULT 'microsoft', is_enabled TINYINT(1) NOT NULL DEFAULT 0, provider_tenant_id VARCHAR(120) NOT NULL DEFAULT 'common', client_id VARCHAR(160) NULL, client_secret_encrypted TEXT NULL, redirect_uri VARCHAR(500) NULL, allowed_domains TEXT NULL, auto_provision TINYINT(1) NOT NULL DEFAULT 0, default_role_id CHAR(32) NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_sso_provider (tenant_id,provider)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS activity_presence (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    user_id CHAR(32) NOT NULL,
    project_id CHAR(32) NULL,
    site_id CHAR(32) NULL,
    screen VARCHAR(120) NULL,
    last_seen_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    UNIQUE KEY uq_activity_user_context (tenant_id,user_id,project_id,site_id),
    KEY idx_activity_project (tenant_id,project_id,last_seen_at),
    KEY idx_activity_site (tenant_id,site_id,last_seen_at)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS suppliers (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    supplier_no VARCHAR(80) NULL,
    name VARCHAR(255) NOT NULL,
    contact_person VARCHAR(255) NULL,
    email VARCHAR(255) NULL,
    phone VARCHAR(80) NULL,
    address VARCHAR(255) NULL,
    zip VARCHAR(20) NULL,
    city VARCHAR(120) NULL,
    tax_no VARCHAR(120) NULL,
    iban VARCHAR(80) NULL,
    notes TEXT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    deleted_at DATETIME NULL,
    KEY idx_suppliers_tenant (tenant_id,name),
    KEY idx_suppliers_active (tenant_id,is_active)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS material_orders (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    project_id CHAR(32) NOT NULL,
    site_id CHAR(32) NULL,
    supplier_id CHAR(32) NULL,
    order_no VARCHAR(80) NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'to_order',
    requested_delivery_date DATE NULL,
    delivery_address VARCHAR(255) NULL,
    contact_name VARCHAR(255) NULL,
    notes TEXT NULL,
    created_by CHAR(32) NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    deleted_at DATETIME NULL,
    UNIQUE KEY uq_material_order_no (tenant_id,order_no),
    KEY idx_material_orders_project (tenant_id,project_id,status),
    KEY idx_material_orders_supplier (tenant_id,supplier_id)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CREATE TABLE IF NOT EXISTS material_order_items (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    order_id CHAR(32) NOT NULL,
    position_no INT NOT NULL DEFAULT 1,
    description VARCHAR(255) NOT NULL,
    quantity DECIMAL(12,3) NULL,
    unit VARCHAR(40) NULL,
    notes TEXT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    deleted_at DATETIME NULL,
    KEY idx_material_order_items_order (tenant_id,order_id,position_no)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===== 3) SPALTEN-NACHRUESTUNG =====
DROP PROCEDURE IF EXISTS ew_add_column;
DELIMITER //
CREATE PROCEDURE ew_add_column(IN p_table VARCHAR(64), IN p_col VARCHAR(64), IN p_def TEXT)
BEGIN
  DECLARE CONTINUE HANDLER FOR SQLEXCEPTION BEGIN END;
  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=p_table)
     AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=p_table AND COLUMN_NAME=p_col) THEN
    SET @ddl = CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_col, '` ', p_def);
    PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;
  END IF;
END //
DELIMITER ;
CALL ew_add_column('vehicles','license_plate','VARCHAR(255) NULL AFTER `plate`');
CALL ew_add_column('vehicles','registration','VARCHAR(255) NULL AFTER `license_plate`');
CALL ew_add_column('vehicles','make','VARCHAR(120) NULL AFTER `type`');
CALL ew_add_column('vehicles','model','VARCHAR(120) NULL AFTER `make`');
CALL ew_add_column('vehicles','year_built','INT NULL AFTER `model`');
CALL ew_add_column('vehicles','vin','VARCHAR(64) NULL AFTER `year_built`');
CALL ew_add_column('vehicles','mileage_km','INT NULL AFTER `vin`');
CALL ew_add_column('vehicles','fuel_type','VARCHAR(60) NULL AFTER `mileage_km`');
CALL ew_add_column('vehicles','holder','VARCHAR(160) NULL AFTER `fuel_type`');
CALL ew_add_column('vehicles','insurer','VARCHAR(160) NULL AFTER `holder`');
CALL ew_add_column('vehicles','policy_no','VARCHAR(120) NULL AFTER `insurer`');
CALL ew_add_column('vehicles','insurance_due','DATE NULL AFTER `policy_no`');
CALL ew_add_column('vehicles','next_service','DATE NULL AFTER `inspection_due`');
CALL ew_add_column('vehicles','purchase_date','DATE NULL AFTER `next_service`');
CALL ew_add_column('documents','vehicle_id','VARCHAR(255) NULL AFTER `room_id`');
CALL ew_add_column('projects','hinweise','TEXT NULL AFTER `notes`');
CALL ew_add_column('construction_sites','hinweise','TEXT NULL AFTER `notes`');
CALL ew_add_column('customers','contact_person','VARCHAR(255) NULL AFTER `name`');
CALL ew_add_column('users','must_change_password','TINYINT(1) NOT NULL DEFAULT 0');
CALL ew_add_column('calendar_events','all_day','TINYINT(1) NOT NULL DEFAULT 0 AFTER `end_at`');
CALL ew_add_column('calendar_events','event_type','VARCHAR(80) NULL AFTER `all_day`');
CALL ew_add_column('calendar_events','color','VARCHAR(255) NULL AFTER `event_type`');
CALL ew_add_column('calendar_events','visibility','VARCHAR(80) NULL AFTER `color`');
CALL ew_add_column('calendar_events','recurrence_rule','VARCHAR(255) NULL AFTER `visibility`');
CALL ew_add_column('calendar_events','reminder_minutes','DECIMAL(14,2) NULL AFTER `recurrence_rule`');
CALL ew_add_column('calendar_events','external_provider','VARCHAR(80) NULL AFTER `reminder_minutes`');
CALL ew_add_column('calendar_events','external_id','VARCHAR(255) NULL AFTER `external_provider`');
CALL ew_add_column('documents','customer_visible','TINYINT(1) NOT NULL DEFAULT 0');
CALL ew_add_column('customer_portal_access','pin_hash','CHAR(64) NULL');
CALL ew_add_column('customer_portal_access','failed_attempts','INT NOT NULL DEFAULT 0');
CALL ew_add_column('call_logs','kind','VARCHAR(20) NOT NULL DEFAULT ''call'' AFTER `tenant_id`');
CALL ew_add_column('call_logs','caller_email','VARCHAR(200) NULL AFTER `caller_phone`');
CALL ew_add_column('call_logs','customer_id','VARCHAR(255) NULL AFTER `caller_email`');
CALL ew_add_column('call_logs','address','VARCHAR(255) NULL AFTER `company`');
CALL ew_add_column('call_logs','zip','VARCHAR(20) NULL AFTER `address`');
CALL ew_add_column('call_logs','city','VARCHAR(160) NULL AFTER `zip`');
CALL ew_add_column('call_logs','source','VARCHAR(40) NULL AFTER `direction`');
CALL ew_add_column('call_logs','priority','VARCHAR(20) NOT NULL DEFAULT ''normal'' AFTER `source`');
CALL ew_add_column('call_logs','desired_start','DATE NULL AFTER `priority`');
CALL ew_add_column('call_logs','area_m2','DECIMAL(10,2) NULL AFTER `desired_start`');
CALL ew_add_column('call_logs','screed_type','VARCHAR(120) NULL AFTER `area_m2`');
CALL ew_add_column('call_logs','next_step','VARCHAR(255) NULL AFTER `screed_type`');
CALL ew_add_column('call_logs','due_date','DATE NULL AFTER `next_step`');
CALL ew_add_column('call_logs','inquiry_status','VARCHAR(30) NOT NULL DEFAULT ''open'' AFTER `follow_up`');
CALL ew_add_column('call_logs','converted_project_id','VARCHAR(255) NULL AFTER `inquiry_status`');
CALL ew_add_column('tasks','project_id','CHAR(32) NULL AFTER `tenant_id`');
CALL ew_add_column('construction_sites','site_email','VARCHAR(180) NULL AFTER `phone`');
CALL ew_add_column('photos','thumbnail_path','VARCHAR(500) NULL AFTER compressed_path');
CALL ew_add_column('photos','original_size_bytes','DECIMAL(14,2) NULL AFTER thumbnail_path');
CALL ew_add_column('photos','compressed_size_bytes','DECIMAL(14,2) NULL AFTER original_size_bytes');
CALL ew_add_column('photos','thumbnail_size_bytes','DECIMAL(14,2) NULL AFTER compressed_size_bytes');
CALL ew_add_column('photos','width_px','INT NULL AFTER thumbnail_size_bytes');
CALL ew_add_column('photos','height_px','INT NULL AFTER width_px');
CALL ew_add_column('planning_assignments','project_id','CHAR(32) NULL AFTER `tenant_id`');
CALL ew_add_column('planning_assignments','site_id','VARCHAR(255) NULL AFTER `project_id`');
CALL ew_add_column('planning_assignments','crew_id','VARCHAR(255) NULL');
CALL ew_add_column('planning_assignments','vehicle_id','VARCHAR(255) NULL');
CALL ew_add_column('planning_assignments','machine_id','VARCHAR(255) NULL');
CALL ew_add_column('planning_assignments','title','VARCHAR(255) NOT NULL DEFAULT ''Einplanung''');
CALL ew_add_column('planning_assignments','status','VARCHAR(80) NULL DEFAULT ''planned''');
CALL ew_add_column('planning_assignments','start_at','DATETIME NULL');
CALL ew_add_column('planning_assignments','end_at','DATETIME NULL');
CALL ew_add_column('planning_assignments','notes','TEXT NULL');
DROP PROCEDURE IF EXISTS ew_add_column;

-- ===== 4) ROLLEN & RECHTE (INSERT IGNORE) =====
SET NAMES utf8mb4;
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('297b5e5a10d970c830700c409e316af5','*','Alle Rechte','api',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('0369af1819aabc7e5cb928332e9eab92','admin.read','admin Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b8cda5f0ef12cdcbfb38609605d63e2a','admin.write','admin Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('f5f8d78f272a33f52917800e35c4ec32','admin.delete','admin Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('7fa463230ca69dbb69101137c58779fd','admin.export','admin Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('95422e8849802d6c9eddfb1a70dd5f6c','admin.share','admin Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('1c127cbea7a55329d2c0da9488c921c4','admin.archive','admin Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('a5711ba73ed4cadb809122f56c01b7b9','audit.read','audit Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('30f9f9a37920270e03c6c48a01d0fbc6','audit.write','audit Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('cfb63ba33b4df69acc66e425ff4a28a7','audit.delete','audit Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('1728f91626385a4ea8def2f077693de1','audit.export','audit Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('d773939923bc452885f20ffedfbd3c43','audit.share','audit Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('71a128cafc4a5195dee4d1baacbc215b','audit.archive','audit Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('57b14443f1215c9179f50e8b8e8f3ce2','calendar.read','calendar Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('ad31edec7aa7f3bdcb2e1513b64b982c','calendar.write','calendar Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('d469e8d8f06ee6ea591869f1776d9518','calendar.delete','calendar Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('febd39970eb4eb959ccc7ed3360c05f5','calendar.export','calendar Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('0de010edea0477b880a2f42240d1b090','calendar.share','calendar Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6093d40b64f93aae8d488140d4eb3976','calendar.archive','calendar Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('9abf08a5bae58c565bbef0e333a0ef89','crews.read','crews Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('4db3b66a6613974fd2c801ea05b591db','crews.write','crews Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('abce48950b88f0234bd4922f3c61382d','crews.delete','crews Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b0774647c8c9e232c848229f20cd426f','crews.export','crews Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('2ee8de5fc2352bc6aeb889cdedac31be','crews.share','crews Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('82be7db2da04d4244a37e0b1128a879e','crews.archive','crews Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('26f522d0cbaf6e12cf922c480c3c8aef','customers.read','customers Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('5c3d5bf00a2c710aaace9dedb3bcfb48','customers.write','customers Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('5accee95a87dd7973ccfd4cc4583115d','customers.delete','customers Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('3c9fd54bdc7fe514993281155c59be64','customers.export','customers Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b82409c64c25924fd3581b8e95e32d02','customers.share','customers Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('3a2cdca93a955aba33924582554e3820','customers.archive','customers Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('e6044ac1b2e70906eb08a8f617bc4ad4','dashboard.read','dashboard Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('9e939e86f5b82f8c8940493e5e534dc3','dashboard.write','dashboard Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('f3d013a631b0feacb22141d376de71e8','dashboard.delete','dashboard Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('caca15e87a563b8c14dba83c72a9807c','dashboard.export','dashboard Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('210f5550e2d92d03715eb3f556182aa0','dashboard.share','dashboard Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('01c53dc077e63b0817b4e4320c9249d6','dashboard.archive','dashboard Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('71aef754857c3aeb304f1fcd53b6aa54','defects.read','defects Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('26f2a83d578ca8bd6b0e643617799773','defects.write','defects Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('efffd687502be11bf654181930ab7095','defects.delete','defects Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('75f94dce53db152f7804d8cfe21943a7','defects.export','defects Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('3e9a51325efd81a799e01680f790ac44','defects.share','defects Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('86e50607f0903013e25209007846ecbd','defects.archive','defects Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('0821d799417c644c501f4a253aa8d9ef','documents.read','documents Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('9273d6f7d3b8059cb543ca1ba954c9f6','documents.write','documents Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b9304d505de9f7005ca2c0f19696959d','documents.delete','documents Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('0678c153b0baa27e7099792e0e262acc','documents.export','documents Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('02cbefefb3930c27347d5e67bd78419f','documents.share','documents Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('64b387a7936d5a3f90e87213f7435cc5','documents.archive','documents Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('503dfeafa3c55fce3247545f136d2382','inspections.read','inspections Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b92830f66192730535e459abeb0804e1','inspections.write','inspections Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('3c5aee86ea7950082197e34ad2277337','inspections.delete','inspections Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('a9610d7570d400d67097d3f511a4a7a1','inspections.export','inspections Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('48298d6f34b1d0622502b4c7b645b967','inspections.share','inspections Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('2c7b898e6e0edd5bf3bf1512f2f7defc','inspections.archive','inspections Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('c2072c0d0ef071872916581b1b5278df','integrations.read','integrations Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('a011e6c69b47e94cc02c418da0832712','integrations.write','integrations Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('da027c5ffe50aaf8c96b324f5f5fe07b','integrations.delete','integrations Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('cd9966cc923e280f4ca505f1e94e093e','integrations.export','integrations Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6a3f5205a5fa682b36002553824e2b6f','integrations.share','integrations Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('c24548f0150ebd64605768ff97134e90','integrations.archive','integrations Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('993cc5b54c3d755e8c224a6d353feffe','notifications.read','notifications Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('9fadd9f8ce155205806d762243eff534','notifications.write','notifications Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('59666cbf6d3a82e62cdb442b3562f7a2','notifications.delete','notifications Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6271799fb79fb9fae44b26b616f3079d','notifications.export','notifications Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('4221a2e20306fdf2e1b2537f0fcb93a9','notifications.share','notifications Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('491ccf634fc2e9ff4820442cc5beb275','notifications.archive','notifications Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('a845c516a865e611b27317f4f0ccc1de','offline.read','offline Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('736de4346c4b6ad0a06668653a327036','offline.write','offline Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('afb775be72173844844257c7ce5cbaf3','offline.delete','offline Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('1a7276a19059fc5f916805730e1cc938','offline.export','offline Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('fc62ee1b6f314b89425f2af7733d7c0a','offline.share','offline Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('3528127e54f2f63acb59a77706b205fb','offline.archive','offline Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('e61253bcdc4e0c1c856d331a5e222150','photos.read','photos Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6c501903fc0d50071d0cfc0ab05acfac','photos.write','photos Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('d94111244b33b23922babdfe6c133cfa','photos.delete','photos Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('d81f13be78c684c863fe31d215838f9d','photos.export','photos Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('cbb3fe540718ff6fa70f8497f90899c5','photos.share','photos Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('0c6a05c7b567b2fc027d11b9ff3d820a','photos.archive','photos Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b38423865cc1b794a3a8dbab915cee21','planning.read','planning Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('123c8eaf81b7e7902c7a30d1a02a9461','planning.write','planning Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('61feb36d8af45202ada84d90b57335c1','planning.delete','planning Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('72d68530ac9747cab878a9c55ef9a5bc','planning.export','planning Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('eefbedfe9626c79c87ee78daa667d6bd','planning.share','planning Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('ddc096b4481bdb7d46f289fd7fad42f9','planning.archive','planning Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('31be0b0df9f03987ec2c72b7309df97c','portal.read','portal Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('aff75e1dd493611836dc76f547a13f42','portal.write','portal Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('f50d3385b652bb796ea5f82fca70a746','portal.delete','portal Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('a31e4c7ab85ae0c94220afd6c7091275','portal.export','portal Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('65ecd698dd8f3ab59c186c81e2f41ca5','portal.share','portal Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('23d8e41c1712f8124c8858ebdbc06f50','portal.archive','portal Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('c31d37962bacf9217380a7624b4dc450','projects.read','projects Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('68ef81206e0087e910dc941750724ed5','projects.write','projects Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('7c08b113995dbb23c0c7e3c234bcb853','projects.delete','projects Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('f6855bc29b19891cc3027863b88c39c4','projects.export','projects Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('1e3c6bc6935b272d207da4415cd70f2c','projects.share','projects Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('35558b1205584f0890d4cb90057b22c0','projects.archive','projects Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('9bd4b0b7a64dd6669b61ca188a6003a0','quality.read','quality Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('4c1cf8e6593e7443a459a360fc5db719','quality.write','quality Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('b5139cfb8b213443e4e156438c3e7b3d','quality.delete','quality Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('7ac0181223180f77a7fe67668035179c','quality.export','quality Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('d88afac4ce4141113c4a2183379e9f21','quality.share','quality Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('d002c1dcb45f43c4c9173a8af45ce08c','quality.archive','quality Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('8c7c0b37edff5e631d0a00f8aa0add6d','reports.read','reports Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('c0aa553c70a1c6f5d2a07ad518838463','reports.write','reports Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('0d501c7ec2d9cb280a0b93d2d536c972','reports.delete','reports Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('3bad6c3110986a5753d5bfaa92785d60','reports.export','reports Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('afa4e1c753591be155b3866e6c0ab4c4','reports.share','reports Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('74c9a98966e5d0718567de1ce76604e2','reports.archive','reports Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('197e51776cc3491b9dc29edabb00cff4','resources.read','resources Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('bef7dffe25c98a54328cb879823ebab3','resources.write','resources Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('494502e2190aabe5c7055c163cd1959e','resources.delete','resources Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('5e7a3fb4fbf1047f2f678b257f4a34ac','resources.export','resources Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('e85fca2ce3b9384be733c9aa84e24233','resources.share','resources Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('796b72825e2122c822537234c33fecf6','resources.archive','resources Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('70c263d3c1d3163fccfbde6011ce96dd','screed.read','screed Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('571b4bc0e3e488890251ca9e4b31b940','screed.write','screed Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('36994d302467016f4ccf72ed67c51a26','screed.delete','screed Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('2cc2a230574cfb39a19cedf8e1599eed','screed.export','screed Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('398c151efee63ade813802caae001c71','screed.share','screed Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('de7afb4990abc01b82f8362fd21c7c52','screed.archive','screed Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('5b6ce4d32d39d93e76bd27ed9928642c','security.read','security Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('5a24be65e7d9ab201f19d3f7b45e5cb7','security.write','security Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('2a3dab1d420d98ea6539ae73d4f5b38d','security.delete','security Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('36bd32f060b02e1762ec85ae4f3fd3ec','security.export','security Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('412e3934f18a47375c33517750365a1f','security.share','security Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('ed77a501bab808f08f3b15643948db15','security.archive','security Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('9121627ac8e3683c890ed78feb32000b','sites.read','sites Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('bd574e7a4ddd50f2f62f10cc2177e5a6','sites.write','sites Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6583bb7c14c8e4b87880b56ad9043412','sites.delete','sites Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6cca2f856ae3e063b8e63113de84cbd3','sites.export','sites Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('259d8ad0c1c00a3a992a85f62d5a3cd8','sites.share','sites Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('6571b07a7d9751baaba7c31af45e0f12','sites.archive','sites Archivieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('f92ddfbec8f85ef58848c971b0ea46d2','tasks.read','tasks Lesen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('994d8e9a166550d0c7a608e4196d7fa8','tasks.write','tasks Schreiben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('09e663273fa780022eec6398aa151e02','tasks.delete','tasks Löschen','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('4bfeaaf44b96d623f3cec4855fe15233','tasks.export','tasks Exportieren','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('f75324310a18e4a66fa63a090b9bc337','tasks.share','tasks Freigeben','tenant',NOW());
INSERT IGNORE INTO permissions (id,code,label,scope,created_at) VALUES ('bda757461b1e30d4ac4782b92c7fbad3','tasks.archive','tasks Archivieren','tenant',NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('67242094108110acf7715e73add9899e',NULL,'Super Admin','super_admin','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('4ba432bf095b6881446aedee37c48fda',NULL,'Firmenadmin','firmenadmin','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('89e196b5f09b21fa88381812d967f88e',NULL,'Geschäftsführer','geschaeftsfuehrer','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d',NULL,'Projektleiter','projektleiter','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('d89077ac8f167710d77227cad05de881',NULL,'Bauleiter','bauleiter','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('2a1862539ce3050cc667659910115563',NULL,'Disponent','disponent','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('7205d3bbc0c3a25442d017d21abb04ee',NULL,'Vorarbeiter','vorarbeiter','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('0765ead23a56c0ac4d22aae36867d752',NULL,'Kolonnenführer','kolonnenfuehrer','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('7eac7bdf06c1984934c217b0ed2961ff',NULL,'Estrichleger','estrichleger','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('84680160ec4e8f50649feedfcea6e35c',NULL,'Innendienst','innendienst','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33',NULL,'Buchhaltung','buchhaltung','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('73c2821a785bd8dd485dceee77b85ab1',NULL,'Kunde','kunde','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('0fea951a0953ad2e17c870be631b327c',NULL,'Subunternehmer','subunternehmer','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc',NULL,'Gutachter','gutachter','Systemrolle EstrichWeb',1,NOW(),NOW());
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('67242094108110acf7715e73add9899e','297b5e5a10d970c830700c409e316af5');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('4ba432bf095b6881446aedee37c48fda','297b5e5a10d970c830700c409e316af5');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','c31d37962bacf9217380a7624b4dc450');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','68ef81206e0087e910dc941750724ed5');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','b38423865cc1b794a3a8dbab915cee21');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','123c8eaf81b7e7902c7a30d1a02a9461');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','9abf08a5bae58c565bbef0e333a0ef89');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','4db3b66a6613974fd2c801ea05b591db');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','197e51776cc3491b9dc29edabb00cff4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','bef7dffe25c98a54328cb879823ebab3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','71aef754857c3aeb304f1fcd53b6aa54');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','26f2a83d578ca8bd6b0e643617799773');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','8c7c0b37edff5e631d0a00f8aa0add6d');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','c0aa553c70a1c6f5d2a07ad518838463');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','9bd4b0b7a64dd6669b61ca188a6003a0');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','4c1cf8e6593e7443a459a360fc5db719');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','503dfeafa3c55fce3247545f136d2382');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','b92830f66192730535e459abeb0804e1');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','c2072c0d0ef071872916581b1b5278df');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','a011e6c69b47e94cc02c418da0832712');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','a5711ba73ed4cadb809122f56c01b7b9');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('89e196b5f09b21fa88381812d967f88e','30f9f9a37920270e03c6c48a01d0fbc6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','c31d37962bacf9217380a7624b4dc450');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','68ef81206e0087e910dc941750724ed5');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','b38423865cc1b794a3a8dbab915cee21');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','123c8eaf81b7e7902c7a30d1a02a9461');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','71aef754857c3aeb304f1fcd53b6aa54');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','26f2a83d578ca8bd6b0e643617799773');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','8c7c0b37edff5e631d0a00f8aa0add6d');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','c0aa553c70a1c6f5d2a07ad518838463');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','9bd4b0b7a64dd6669b61ca188a6003a0');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','4c1cf8e6593e7443a459a360fc5db719');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','503dfeafa3c55fce3247545f136d2382');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','b92830f66192730535e459abeb0804e1');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('eb9db9c47ec9669aaa240caec2e3ea4d','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','71aef754857c3aeb304f1fcd53b6aa54');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','26f2a83d578ca8bd6b0e643617799773');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','8c7c0b37edff5e631d0a00f8aa0add6d');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','c0aa553c70a1c6f5d2a07ad518838463');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','9bd4b0b7a64dd6669b61ca188a6003a0');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','4c1cf8e6593e7443a459a360fc5db719');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','503dfeafa3c55fce3247545f136d2382');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','b92830f66192730535e459abeb0804e1');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','70c263d3c1d3163fccfbde6011ce96dd');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('d89077ac8f167710d77227cad05de881','571b4bc0e3e488890251ca9e4b31b940');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','b38423865cc1b794a3a8dbab915cee21');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','123c8eaf81b7e7902c7a30d1a02a9461');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','9abf08a5bae58c565bbef0e333a0ef89');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','4db3b66a6613974fd2c801ea05b591db');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','197e51776cc3491b9dc29edabb00cff4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','bef7dffe25c98a54328cb879823ebab3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('2a1862539ce3050cc667659910115563','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','71aef754857c3aeb304f1fcd53b6aa54');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','26f2a83d578ca8bd6b0e643617799773');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','503dfeafa3c55fce3247545f136d2382');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','b92830f66192730535e459abeb0804e1');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','8c7c0b37edff5e631d0a00f8aa0add6d');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','c0aa553c70a1c6f5d2a07ad518838463');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7205d3bbc0c3a25442d017d21abb04ee','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','503dfeafa3c55fce3247545f136d2382');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','b92830f66192730535e459abeb0804e1');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0765ead23a56c0ac4d22aae36867d752','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('7eac7bdf06c1984934c217b0ed2961ff','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','26f522d0cbaf6e12cf922c480c3c8aef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','5c3d5bf00a2c710aaace9dedb3bcfb48');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','c31d37962bacf9217380a7624b4dc450');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','68ef81206e0087e910dc941750724ed5');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','57b14443f1215c9179f50e8b8e8f3ce2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','ad31edec7aa7f3bdcb2e1513b64b982c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','993cc5b54c3d755e8c224a6d353feffe');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('84680160ec4e8f50649feedfcea6e35c','9fadd9f8ce155205806d762243eff534');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','e6044ac1b2e70906eb08a8f617bc4ad4');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','9e939e86f5b82f8c8940493e5e534dc3');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','26f522d0cbaf6e12cf922c480c3c8aef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','5c3d5bf00a2c710aaace9dedb3bcfb48');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','c31d37962bacf9217380a7624b4dc450');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','68ef81206e0087e910dc941750724ed5');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','c2072c0d0ef071872916581b1b5278df');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('b8b0ca030ad53fc6ac8a91af88210f33','a011e6c69b47e94cc02c418da0832712');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('73c2821a785bd8dd485dceee77b85ab1','31be0b0df9f03987ec2c72b7309df97c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('73c2821a785bd8dd485dceee77b85ab1','aff75e1dd493611836dc76f547a13f42');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','31be0b0df9f03987ec2c72b7309df97c');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','aff75e1dd493611836dc76f547a13f42');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','f92ddfbec8f85ef58848c971b0ea46d2');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('0fea951a0953ad2e17c870be631b327c','994d8e9a166550d0c7a608e4196d7fa8');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','9121627ac8e3683c890ed78feb32000b');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','bd574e7a4ddd50f2f62f10cc2177e5a6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','0821d799417c644c501f4a253aa8d9ef');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','9273d6f7d3b8059cb543ca1ba954c9f6');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','e61253bcdc4e0c1c856d331a5e222150');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','6c501903fc0d50071d0cfc0ab05acfac');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','9bd4b0b7a64dd6669b61ca188a6003a0');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','4c1cf8e6593e7443a459a360fc5db719');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','503dfeafa3c55fce3247545f136d2382');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','b92830f66192730535e459abeb0804e1');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','71aef754857c3aeb304f1fcd53b6aa54');
INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES ('a805cbcd979b496d0c3deb2fa4e011bc','26f2a83d578ca8bd6b0e643617799773');

-- ===== 5) PERFORMANCE-INDIZES =====
DROP PROCEDURE IF EXISTS ew_add_index;
DELIMITER //
CREATE PROCEDURE ew_add_index(IN p_table VARCHAR(64), IN p_index VARCHAR(64), IN p_cols VARCHAR(255))
BEGIN
  DECLARE CONTINUE HANDLER FOR SQLEXCEPTION BEGIN END;
  IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=p_table)
     AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=p_table AND INDEX_NAME=p_index) THEN
    SET @ddl = CONCAT('ALTER TABLE `', p_table, '` ADD INDEX `', p_index, '` (', p_cols, ')');
    PREPARE s FROM @ddl; EXECUTE s; DEALLOCATE PREPARE s;
  END IF;
END //
DELIMITER ;
CALL ew_add_index('projects',             'ix_projects_tenant_del',  'tenant_id, deleted_at');
CALL ew_add_index('projects',             'ix_projects_status',      'tenant_id, status, deleted_at');
CALL ew_add_index('projects',             'ix_projects_customer',    'customer_id');
CALL ew_add_index('construction_sites',   'ix_sites_tenant_del',     'tenant_id, deleted_at');
CALL ew_add_index('construction_sites',   'ix_sites_project',        'project_id');
CALL ew_add_index('customers',            'ix_customers_tenant_del', 'tenant_id, deleted_at');
CALL ew_add_index('contacts',             'ix_contacts_customer',    'customer_id');
CALL ew_add_index('documents',            'ix_documents_project',    'project_id');
CALL ew_add_index('documents',            'ix_documents_tenant_del', 'tenant_id, deleted_at');
CALL ew_add_index('documents',            'ix_documents_visible',    'tenant_id, customer_visible');
CALL ew_add_index('photos',               'ix_photos_project',       'project_id');
CALL ew_add_index('photos',               'ix_photos_tenant_del',    'tenant_id, deleted_at');
CALL ew_add_index('tasks',                'ix_tasks_tenant_del',     'tenant_id, deleted_at');
CALL ew_add_index('tasks',                'ix_tasks_assigned',       'assigned_user_id, status');
CALL ew_add_index('defects',              'ix_defects_project',      'project_id, status');
CALL ew_add_index('planning_assignments', 'ix_pa_tenant_window',     'tenant_id, deleted_at, start_at, end_at');
CALL ew_add_index('planning_assignments', 'ix_pa_crew',              'crew_id, start_at');
CALL ew_add_index('calendar_events',      'ix_cal_tenant_window',    'tenant_id, start_at, end_at');
CALL ew_add_index('call_logs',            'ix_calls_tenant_kind',    'tenant_id, kind, deleted_at');
CALL ew_add_index('call_logs',            'ix_calls_customer',       'customer_id');
CALL ew_add_index('call_logs',            'ix_calls_status',         'tenant_id, inquiry_status');
CALL ew_add_index('material_orders',      'ix_mo_tenant_status',     'tenant_id, status, deleted_at');
CALL ew_add_index('material_orders',      'ix_mo_project',           'project_id');
CALL ew_add_index('material_order_items', 'ix_moi_order',            'order_id');
CALL ew_add_index('suppliers',            'ix_suppliers_tenant',     'tenant_id, deleted_at');
CALL ew_add_index('customer_portal_access','ix_cpa_token',           'token_hash');
CALL ew_add_index('customer_portal_access','ix_cpa_customer',        'tenant_id, customer_id');
CALL ew_add_index('audit_logs',           'ix_audit_tenant_time',    'tenant_id, created_at');
CALL ew_add_index('login_logs',           'ix_login_tenant_time',    'tenant_id, created_at');
CALL ew_add_index('user_roles',           'ix_user_roles_user',      'user_id');
CALL ew_add_index('device_sessions',      'ix_sessions_user',        'user_id');
CALL ew_add_index('vehicles',             'ix_vehicles_tenant_del',  'tenant_id, deleted_at');
CALL ew_add_index('machines',             'ix_machines_tenant_del',  'tenant_id, deleted_at');
CALL ew_add_index('crews',                'ix_crews_tenant_del',     'tenant_id, deleted_at');
DROP PROCEDURE IF EXISTS ew_add_index;

-- ===== 6) ADMIN-VOLLZUGRIFF REPARIEREN =====
-- Stellt das '*'-Recht sicher und verknuepft ALLE Admin-Rollen damit – unabhaengig
-- davon, welche '*'-ID historisch existiert (behebt: Berechtigungen wirken nicht).
INSERT IGNORE INTO permissions (id, code, label, scope, created_at)
  VALUES (MD5('*'), '*', 'Vollzugriff (alle Bereiche)', 'tenant', NOW());
INSERT IGNORE INTO role_permissions (role_id, permission_id)
  SELECT r.id, p.id FROM roles r JOIN permissions p ON p.code = '*'
  WHERE r.code IN ('super_admin','firmenadmin','geschaeftsfuehrer','administrator','admin','inhaber')
    AND r.deleted_at IS NULL;

SET FOREIGN_KEY_CHECKS=1;

-- ===========================================================================
-- Rolle "Kolonnenmitglied" (nur Lesen, außer Tagesdokumentation + Fotos/Videos)
-- ===========================================================================
INSERT IGNORE INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at)
  VALUES (MD5('kolonnenmitglied'),NULL,'Kolonnenmitglied','kolonnenmitglied','Nur Lesen – außer Tagesdokumentation und Fotos/Videos',1,NOW(),NOW());
INSERT IGNORE INTO role_permissions (role_id, permission_id)
  SELECT MD5('kolonnenmitglied'), id FROM permissions
  WHERE code IN ('projects.read','sites.read','rooms.read','customers.read','crews.read','vehicles.read','materials.read','suppliers.read','reports.read','reports.write','photos.read','photos.write','documents.read','documents.write','dashboard.read','calendar.read','tasks.read');
