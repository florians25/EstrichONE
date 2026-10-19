-- ============================================================
-- EstrichWeb – Update-Skript 2026-06-02
-- Neuerungen: App-Einstellungen (Firmendaten/Meldungen/Changelog),
-- Notizen/Verlauf je Bauakte, Estrichpumpen, Fahrzeug-Dokumente,
-- erweiterte Fahrzeug-Stammdaten.
--
-- HINWEIS: Die Anwendung legt diese Strukturen beim ersten Aufruf
-- automatisch an (ensure_*-Helfer). Dieses Skript ist optional und
-- dient zur manuellen/kontrollierten Migration. Alle Anweisungen sind
-- idempotent (IF NOT EXISTS / additive Spalten). Für MySQL 8 / MariaDB 10.4+.
-- ============================================================

-- ---- 1. App-/Firmeneinstellungen (Key-Value je Mandant) ----
CREATE TABLE IF NOT EXISTS app_settings (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NULL,
  setting_key VARCHAR(80) NOT NULL,
  value_json LONGTEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  UNIQUE KEY uq_app_setting (tenant_id, setting_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---- 2. Notizen / Verlauf je Bauakte (Ticket-Stil) ----
CREATE TABLE IF NOT EXISTS project_notes (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  project_id VARCHAR(255) NOT NULL,
  entry_type VARCHAR(40) NOT NULL DEFAULT 'note',   -- note | system | assignment | status
  author_id CHAR(32) NULL,
  author_name VARCHAR(160) NULL,
  body TEXT NULL,
  document_id CHAR(32) NULL,                         -- verknüpftes Dokument (liegt auch in documents)
  meta_json TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_project_notes (tenant_id, project_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---- 3. Estrichpumpen ----
CREATE TABLE IF NOT EXISTS pumps (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  name VARCHAR(160) NOT NULL,
  manufacturer VARCHAR(120) NULL,
  model VARCHAR(120) NULL,
  serial_no VARCHAR(120) NULL,
  type VARCHAR(80) NULL,
  status VARCHAR(60) NULL DEFAULT 'available',
  max_output_l_min DECIMAL(10,2) NULL,
  max_pressure_bar DECIMAL(10,2) NULL,
  max_distance_m DECIMAL(10,2) NULL,
  hose_length_m DECIMAL(10,2) NULL,
  operating_hours INT NULL,
  year_built INT NULL,
  inspection_due DATE NULL,
  next_service DATE NULL,
  notes TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_pumps_tenant (tenant_id),
  KEY idx_pumps_status (tenant_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---- 4. Fahrzeug-Dokumente: Zuordnung in documents ----
-- (Spalte vehicle_id zu documents hinzufügen, falls nicht vorhanden)
SET @col := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documents' AND COLUMN_NAME = 'vehicle_id');
SET @sql := IF(@col = 0, 'ALTER TABLE documents ADD COLUMN vehicle_id CHAR(32) NULL', 'SELECT 1');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- ---- 5. Erweiterte Fahrzeug-Stammdaten (additiv) ----
-- Fügt fehlende Spalten einzeln hinzu (idempotent).
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

CALL ew_add_col('vehicles', 'make', 'VARCHAR(120) NULL');
CALL ew_add_col('vehicles', 'model', 'VARCHAR(120) NULL');
CALL ew_add_col('vehicles', 'year_built', 'INT NULL');
CALL ew_add_col('vehicles', 'vin', 'VARCHAR(60) NULL');
CALL ew_add_col('vehicles', 'mileage_km', 'INT NULL');
CALL ew_add_col('vehicles', 'fuel_type', 'VARCHAR(40) NULL');
CALL ew_add_col('vehicles', 'holder', 'VARCHAR(160) NULL');
CALL ew_add_col('vehicles', 'insurer', 'VARCHAR(160) NULL');
CALL ew_add_col('vehicles', 'policy_no', 'VARCHAR(120) NULL');
CALL ew_add_col('vehicles', 'insurance_due', 'DATE NULL');
CALL ew_add_col('vehicles', 'inspection_due', 'DATE NULL');
CALL ew_add_col('vehicles', 'next_service', 'DATE NULL');
CALL ew_add_col('vehicles', 'purchase_date', 'DATE NULL');

DROP PROCEDURE IF EXISTS ew_add_col;

-- Fertig.
