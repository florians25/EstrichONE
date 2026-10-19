SET NAMES utf8mb4;

DROP PROCEDURE IF EXISTS ew_add_column;
DELIMITER //
CREATE PROCEDURE ew_add_column(IN p_table VARCHAR(64), IN p_column VARCHAR(64), IN p_def VARCHAR(1000))
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = p_table AND COLUMN_NAME = p_column
  ) THEN
    SET @ddl = CONCAT('ALTER TABLE `', REPLACE(p_table,'`','``'), '` ADD COLUMN `', REPLACE(p_column,'`','``'), '` ', p_def);
    PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
  END IF;
END //
DELIMITER ;

CALL ew_add_column('customers','contact_person','VARCHAR(255) NULL AFTER `name`');
CALL ew_add_column('lexware_connections','last_sync_status','VARCHAR(40) NULL AFTER `last_auto_sync_at`');
CALL ew_add_column('lexware_connections','last_sync_summary','TEXT NULL AFTER `last_sync_status`');

-- Kalender-Termine dürfen auch ohne Bauakte angelegt werden.
ALTER TABLE `calendar_events` MODIFY COLUMN `project_id` VARCHAR(255) NULL;

-- Bestehende Lexware-Erfolgslogs können optional stark reduziert werden.
-- Fehler bleiben erhalten. Bei Bedarf vorher exportieren.
DELETE FROM `lexware_sync_logs`
WHERE `status` IN ('success','ok','completed')
  AND `created_at` < DATE_SUB(NOW(), INTERVAL 7 DAY);

DROP PROCEDURE IF EXISTS ew_add_column;
