-- ============================================================
-- EstrichWeb – Sammel-Update 2026-06-03 (Stand: aktuell)
-- Pflegt ALLE jüngsten Änderungen nach:
--   * Kundenportal inkl. PIN (customer_portal_access)
--   * Anrufe / Anfragenverwaltung (call_logs)
--   * Erstanmeldung-Passwortwechsel (users.must_change_password)
--   * Kundensichtbare Dokumente (documents.customer_visible)
--   * Audit-Entfernung (DROP audit_logs)
--   * Performance-Indizes
--
-- Idempotent für MySQL 8 / MariaDB 10.4+. Mehrfach ausführbar.
-- Die Anwendung legt das meiste auch automatisch beim ersten
-- Aufruf an; dieses Skript dient der kontrollierten Migration.
-- ============================================================

-- Hilfsprozedur: Spalte nur anlegen, wenn sie fehlt
DROP PROCEDURE IF EXISTS ew_add_col;
DELIMITER //
CREATE PROCEDURE ew_add_col(IN tbl VARCHAR(64), IN col VARCHAR(64), IN def VARCHAR(255))
BEGIN
  DECLARE cnt INT;
  SELECT COUNT(*) INTO cnt FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col;
  IF cnt = 0 THEN
    SET @ddl := CONCAT('ALTER TABLE `', tbl, '` ADD COLUMN `', col, '` ', def);
    PREPARE st FROM @ddl; EXECUTE st; DEALLOCATE PREPARE st;
  END IF;
END //
DELIMITER ;

-- Hilfsprozedur: Index nur anlegen, wenn er fehlt
DROP PROCEDURE IF EXISTS ew_add_idx;
DELIMITER //
CREATE PROCEDURE ew_add_idx(IN tbl VARCHAR(64), IN idx VARCHAR(64), IN cols VARCHAR(255))
BEGIN
  DECLARE cnt INT;
  DECLARE tblcnt INT;
  SELECT COUNT(*) INTO tblcnt FROM information_schema.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl;
  IF tblcnt > 0 THEN
    SELECT COUNT(*) INTO cnt FROM information_schema.STATISTICS
      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND INDEX_NAME = idx;
    IF cnt = 0 THEN
      SET @ddl := CONCAT('ALTER TABLE `', tbl, '` ADD INDEX `', idx, '` (', cols, ')');
      PREPARE st FROM @ddl; EXECUTE st; DEALLOCATE PREPARE st;
    END IF;
  END IF;
END //
DELIMITER ;

-- ---- 1. Kundenportal: Zugangstabelle + PIN ----
CREATE TABLE IF NOT EXISTS customer_portal_access (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  customer_id VARCHAR(255) NOT NULL,
  token_hash CHAR(64) NOT NULL,
  pin_hash CHAR(64) NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  last_access_at DATETIME NULL,
  failed_attempts INT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  revoked_at DATETIME NULL,
  UNIQUE KEY uq_portal_token (token_hash),
  KEY idx_portal_customer (tenant_id, customer_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
CALL ew_add_col('customer_portal_access','pin_hash','CHAR(64) NULL');
CALL ew_add_col('customer_portal_access','failed_attempts','INT NOT NULL DEFAULT 0');

-- ---- 2. Anrufe / Anfragenverwaltung ----
CREATE TABLE IF NOT EXISTS call_logs (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  caller_name VARCHAR(200) NULL,
  caller_phone VARCHAR(80) NULL,
  company VARCHAR(200) NULL,
  direction VARCHAR(20) NOT NULL DEFAULT 'inbound',
  subject VARCHAR(255) NULL,
  notes TEXT NULL,
  follow_up TINYINT(1) NOT NULL DEFAULT 0,
  project_id VARCHAR(255) NULL,
  note_id CHAR(32) NULL,
  handled_by VARCHAR(160) NULL,
  called_at DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_calls_tenant (tenant_id, created_at),
  KEY idx_calls_project (tenant_id, project_id),
  KEY idx_calls_followup (tenant_id, follow_up)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---- 3. Notizstrang (für Anruf-Notizen + Verlauf) sicherstellen ----
CREATE TABLE IF NOT EXISTS project_notes (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  project_id VARCHAR(255) NOT NULL,
  entry_type VARCHAR(40) NOT NULL DEFAULT 'note',
  author_id CHAR(32) NULL,
  author_name VARCHAR(160) NULL,
  body TEXT NULL,
  document_id CHAR(32) NULL,
  meta_json TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_project_notes (tenant_id, project_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---- 4. Benutzer: Erstanmeldung-Passwortwechsel ----
CALL ew_add_col('users','must_change_password','TINYINT(1) NOT NULL DEFAULT 0');

-- ---- 5. Dokumente: Kundensichtbarkeit fürs Portal ----
CALL ew_add_col('documents','customer_visible','TINYINT(1) NOT NULL DEFAULT 0');

-- ---- 6. Audit entfernen (Speicherersparnis) ----
DROP TABLE IF EXISTS audit_logs;

-- ---- 7. Performance-Indizes (häufige Filterkombinationen) ----
CALL ew_add_idx('projects','idx_projects_tenant_status','tenant_id, status');
CALL ew_add_idx('projects','idx_projects_tenant_customer','tenant_id, customer_id');
CALL ew_add_idx('projects','idx_projects_created','tenant_id, created_at');
CALL ew_add_idx('construction_sites','idx_sites_tenant_project','tenant_id, project_id');
CALL ew_add_idx('documents','idx_documents_tenant_project','tenant_id, project_id');
CALL ew_add_idx('documents','idx_documents_visible','tenant_id, customer_visible');
CALL ew_add_idx('planning_assignments','idx_plan_tenant_start','tenant_id, start_at');
CALL ew_add_idx('planning_assignments','idx_plan_crew','tenant_id, crew_id');
CALL ew_add_idx('project_notes','idx_notes_lookup','tenant_id, project_id, created_at');
CALL ew_add_idx('user_roles','idx_user_roles_user','user_id');
CALL ew_add_idx('user_roles','idx_user_roles_role','role_id');
CALL ew_add_idx('users','idx_users_tenant_active','tenant_id, is_active');
CALL ew_add_idx('call_logs','idx_calls_tenant','tenant_id, created_at');
CALL ew_add_idx('call_logs','idx_calls_project','tenant_id, project_id');

-- Aufräumen
DROP PROCEDURE IF EXISTS ew_add_col;
DROP PROCEDURE IF EXISTS ew_add_idx;

-- Fertig. Alle jüngsten Änderungen sind nachgepflegt.
