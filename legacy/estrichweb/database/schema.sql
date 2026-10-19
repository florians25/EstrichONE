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
