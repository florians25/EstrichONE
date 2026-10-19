-- EstrichWeb Patch 2026-05-30
-- Für bestehende Installationen: Historie, Aktivitätsanzeige und Benutzerrollen-Auswahl.

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

SET @idx := (
  SELECT COUNT(1) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'audit_logs' AND index_name = 'idx_audit_entity_lookup'
);
SET @sql := IF(@idx = 0,
  'ALTER TABLE `audit_logs` ADD INDEX `idx_audit_entity_lookup` (`tenant_id`, `entity_type`, `entity_id`, `created_at`)',
  'SELECT 1'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @idx := (
  SELECT COUNT(1) FROM information_schema.statistics
  WHERE table_schema = DATABASE() AND table_name = 'user_roles' AND index_name = 'idx_user_roles_user'
);
SET @sql := IF(@idx = 0,
  'ALTER TABLE `user_roles` ADD INDEX `idx_user_roles_user` (`user_id`)',
  'SELECT 1'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
