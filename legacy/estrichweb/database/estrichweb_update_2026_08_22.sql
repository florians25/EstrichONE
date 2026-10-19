-- ============================================================================
-- EstrichWeb – Datenbank-Update, Stand 22.08.2026
-- ----------------------------------------------------------------------------
-- Bündelt alle Änderungen der letzten Runden in EINEM Skript:
--
--   1. Ort für Kalendertermine          (calendar_events.location)
--   2. Zuständigkeit für Termine        (calendar_events.assigned_user_id)
--   3. Indizes für schnellere Listen    (tenant_id + deleted_at u. a.)
--   4. Aufräumen: endgültig gelöschte Datensätze entfernen
--   5. Platz freigeben                  (OPTIMIZE TABLE)
--
-- EIGENSCHAFTEN
--   * Wiederholbar: Jeder Schritt prüft zuerst, ob er nötig ist. Ein
--     zweiter Lauf ändert nichts und schlägt nicht fehl.
--   * Keine Datenverluste in den Schritten 1–3.
--   * Schritt 4 ist standardmäßig AUSGESCHALTET (siehe unten).
--
-- ANWENDUNG (phpMyAdmin oder Konsole)
--   mysql -u BENUTZER -p DATENBANK < estrichweb_update_2026_08_20.sql
--
-- VORHER BITTE EINE DATENSICHERUNG ANLEGEN.
-- ============================================================================

SET @EW_PURGE_DELETED = 0;   -- auf 1 setzen, um Schritt 4 auszuführen

-- ---------------------------------------------------------------------------
-- Hilfsprozeduren
-- ---------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS ew_add_column;
DROP PROCEDURE IF EXISTS ew_add_index;

DELIMITER //

CREATE PROCEDURE ew_add_column(IN tbl VARCHAR(64), IN col VARCHAR(64), IN def TEXT)
BEGIN
  DECLARE tbl_exists INT DEFAULT 0;
  DECLARE col_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO tbl_exists FROM information_schema.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl;
  IF tbl_exists > 0 THEN
    SELECT COUNT(*) INTO col_exists FROM information_schema.COLUMNS
      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col;
    IF col_exists = 0 THEN
      SET @s = CONCAT('ALTER TABLE `', tbl, '` ADD COLUMN `', col, '` ', def);
      PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
      SELECT CONCAT('Spalte ergänzt: ', tbl, '.', col) AS Schritt;
    END IF;
  END IF;
END //

CREATE PROCEDURE ew_add_index(IN tbl VARCHAR(64), IN idx VARCHAR(64), IN cols VARCHAR(255))
BEGIN
  DECLARE tbl_exists INT DEFAULT 0;
  DECLARE idx_exists INT DEFAULT 0;
  SELECT COUNT(*) INTO tbl_exists FROM information_schema.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl;
  IF tbl_exists > 0 THEN
    SELECT COUNT(*) INTO idx_exists FROM information_schema.STATISTICS
      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND INDEX_NAME = idx;
    IF idx_exists = 0 THEN
      SET @s = CONCAT('ALTER TABLE `', tbl, '` ADD INDEX `', idx, '` (', cols, ')');
      PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
      SELECT CONCAT('Index ergänzt: ', tbl, '.', idx) AS Schritt;
    END IF;
  END IF;
END //

DELIMITER ;

-- ---------------------------------------------------------------------------
-- 1 + 2: Fehlende Spalten
-- ---------------------------------------------------------------------------
SELECT '--- Schritt 1/2: Spalten ---' AS Schritt;

-- Ort eines Kalendertermins. Das Feld gab es im Dialog seit jeher, die
-- Spalte fehlte – der Wert ging beim Speichern verloren.
CALL ew_add_column('calendar_events', 'location', 'VARCHAR(255) NULL AFTER `description`');

-- Zuständige Person für einen Termin.
CALL ew_add_column('calendar_events', 'assigned_user_id', 'VARCHAR(255) NULL AFTER `site_id`');

-- Anrufe: Kundenverknüpfung und Zusatzangaben der Schnellerfassung.
CALL ew_add_column('call_logs', 'customer_id', 'VARCHAR(255) NULL AFTER `caller_email`');
CALL ew_add_column('call_logs', 'caller_email', 'VARCHAR(200) NULL AFTER `caller_phone`');

-- Räume: Bemerkungsfeld (falls eine ältere Installation es nicht hat).
CALL ew_add_column('rooms', 'notes', 'TEXT NULL');

-- ---------------------------------------------------------------------------
-- 3: Indizes
-- ---------------------------------------------------------------------------
SELECT '--- Schritt 3: Indizes ---' AS Schritt;

-- Fast jede Abfrage filtert nach Mandant und Löschkennzeichen. Ohne Index
-- liest MySQL die ganze Tabelle – der Hauptgrund für träge Listen.
CALL ew_add_index('projects',             'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('construction_sites',   'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('customers',            'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('documents',            'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('photos',               'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('rooms',                'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('call_logs',            'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('material_orders',      'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');
CALL ew_add_index('project_notes',        'idx_ew_tenant_deleted', '`tenant_id`, `deleted_at`');

-- Die häufigsten Verknüpfungen
CALL ew_add_index('construction_sites',   'idx_ew_project',  '`project_id`');
CALL ew_add_index('documents',            'idx_ew_project',  '`project_id`');
CALL ew_add_index('photos',               'idx_ew_project',  '`project_id`');
CALL ew_add_index('rooms',                'idx_ew_site',     '`site_id`');
CALL ew_add_index('project_notes',        'idx_ew_project',  '`project_id`, `deleted_at`');
CALL ew_add_index('projects',             'idx_ew_customer', '`customer_id`');
CALL ew_add_index('call_logs',            'idx_ew_customer', '`tenant_id`, `customer_id`');

-- Zeitraumabfragen in Planung, Kalender und Tagesberichten
CALL ew_add_index('planning_assignments', 'idx_ew_start', '`tenant_id`, `start_at`');
CALL ew_add_index('calendar_events',      'idx_ew_start', '`tenant_id`, `start_at`');
CALL ew_add_index('daily_reports',        'idx_ew_date',  '`tenant_id`, `report_date`');

-- Anmeldesperre: Die Zählung der Fehlversuche fragt nach E-Mail bzw.
-- Herkunftsadresse und Zeitpunkt. Ohne Index wird bei jedem Anmeldeversuch
-- die ganze Protokolltabelle gelesen.
CALL ew_add_index('login_logs', 'idx_ew_email_time', '`email`, `created_at`');
CALL ew_add_index('login_logs', 'idx_ew_ip_time',    '`ip`, `created_at`');

-- ---------------------------------------------------------------------------
-- Schnellanmeldung mit PIN (optional, standardmäßig ausgeschaltet)
-- ---------------------------------------------------------------------------
SELECT '--- Schnellanmeldung ---' AS Schritt;

CREATE TABLE IF NOT EXISTS `quick_logins` (
  `id` CHAR(32) PRIMARY KEY,
  `tenant_id` CHAR(32) NOT NULL,
  `user_id` CHAR(32) NOT NULL,
  `device_id` VARCHAR(64) NOT NULL,
  `device_label` VARCHAR(120) NULL,
  `pin_hash` VARCHAR(255) NOT NULL,
  `failed_attempts` INT NOT NULL DEFAULT 0,
  `locked_at` DATETIME NULL,
  `last_used_at` DATETIME NULL,
  `created_at` DATETIME NOT NULL,
  `updated_at` DATETIME NULL,
  `revoked_at` DATETIME NULL,
  UNIQUE KEY `uq_quick_device` (`device_id`),
  KEY `idx_quick_user` (`tenant_id`,`user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Zeitpunkt der letzten Passwortänderung (für die Profilseite)
CALL ew_add_column('users', 'password_changed_at', 'DATETIME NULL');

-- ---------------------------------------------------------------------------
-- 4: Altlasten entfernen  (nur wenn @EW_PURGE_DELETED = 1)
-- ---------------------------------------------------------------------------
-- ACHTUNG: Entfernt alle Zeilen, die früher nur als gelöscht MARKIERT
-- wurden. Das ist nicht umkehrbar.
--
-- WICHTIG: Die zugehörigen DATEIEN werden hier NICHT entfernt. Dafür bitte
-- vorher `php api/tools/purge_deleted.php --force` ausführen – das Werkzeug
-- löscht Dateien und Zeilen gemeinsam und hinterlässt keine Reste.
-- ---------------------------------------------------------------------------
SELECT CASE WHEN @EW_PURGE_DELETED = 1
            THEN '--- Schritt 4: Altlasten werden entfernt ---'
            ELSE '--- Schritt 4: übersprungen (@EW_PURGE_DELETED = 0) ---'
       END AS Schritt;

DROP PROCEDURE IF EXISTS ew_purge_deleted;
DELIMITER //
CREATE PROCEDURE ew_purge_deleted()
BEGIN
  DECLARE done INT DEFAULT 0;
  DECLARE tname VARCHAR(64);
  DECLARE cur CURSOR FOR
    SELECT DISTINCT TABLE_NAME FROM information_schema.COLUMNS
      WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME = 'deleted_at';
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;
  IF @EW_PURGE_DELETED = 1 THEN
    OPEN cur;
    read_loop: LOOP
      FETCH cur INTO tname;
      IF done = 1 THEN LEAVE read_loop; END IF;
      SET @s = CONCAT('DELETE FROM `', tname, '` WHERE deleted_at IS NOT NULL');
      PREPARE st FROM @s; EXECUTE st; DEALLOCATE PREPARE st;
    END LOOP;
    CLOSE cur;
  END IF;
END //
DELIMITER ;

CALL ew_purge_deleted();
DROP PROCEDURE IF EXISTS ew_purge_deleted;

-- ---------------------------------------------------------------------------
-- Aufräumen
-- ---------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS ew_add_column;
DROP PROCEDURE IF EXISTS ew_add_index;

SELECT '--- Fertig ---' AS Schritt;
SELECT 'Bei Schritt 4: anschliessend OPTIMIZE TABLE ausfuehren, damit der Platz freigegeben wird.' AS Hinweis;
