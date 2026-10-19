-- EstrichWeb Hotfix 2026-05-31
-- Ergänzt Baustellenkontakt-E-Mail, falls noch nicht vorhanden.
-- MySQL-kompatibel und idempotent.

DROP PROCEDURE IF EXISTS ew_add_column;
DELIMITER //
CREATE PROCEDURE ew_add_column(
  IN p_table VARCHAR(64),
  IN p_column VARCHAR(64),
  IN p_def VARCHAR(1000)
)
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = p_table
      AND COLUMN_NAME = p_column
  ) THEN
    SET @ddl = CONCAT('ALTER TABLE `', REPLACE(p_table, '`', '``'), '` ADD COLUMN `', REPLACE(p_column, '`', '``'), '` ', p_def);
    PREPARE stmt FROM @ddl;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
  END IF;
END //
DELIMITER ;

CALL ew_add_column('construction_sites', 'site_email', 'VARCHAR(180) NULL AFTER `phone`');

DROP PROCEDURE IF EXISTS ew_add_column;
