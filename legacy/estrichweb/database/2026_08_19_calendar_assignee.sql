-- ============================================================================
-- EstrichWeb – Zuständigkeit für Kalendertermine
-- ----------------------------------------------------------------------------
-- Wiederholbar: legt die Spalte nur an, wenn sie fehlt.
-- Die API zieht sie zusätzlich beim ersten Aufruf selbst nach, ein manueller
-- Schritt ist also nicht zwingend nötig.
-- ============================================================================
SET @col := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'calendar_events'
    AND COLUMN_NAME = 'assigned_user_id'
);
SET @sql := IF(@col = 0,
  'ALTER TABLE `calendar_events` ADD COLUMN `assigned_user_id` VARCHAR(255) NULL AFTER `site_id`',
  'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
