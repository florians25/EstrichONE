-- ============================================================================
-- EstrichWeb – Ort für Kalendertermine
-- ----------------------------------------------------------------------------
-- Der Termindialog erfasst seit jeher ein Feld "Ort", die Tabelle
-- calendar_events hatte dafür jedoch keine Spalte. Der Wert wurde beim
-- Speichern verworfen und tauchte weder auf dem Termin noch im Detailfenster
-- auf.
--
-- Diese Migration ist gefahrlos wiederholbar: Sie legt die Spalte nur an,
-- wenn sie noch fehlt. Bestehende Daten bleiben unberührt.
-- ============================================================================

SET @col := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'calendar_events'
    AND COLUMN_NAME = 'location'
);
SET @sql := IF(@col = 0,
  'ALTER TABLE `calendar_events` ADD COLUMN `location` VARCHAR(255) NULL AFTER `description`',
  'SELECT 1');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
