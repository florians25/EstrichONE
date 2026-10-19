-- EstrichWeb patch: projektgebundene Termine und Kolonnen-Wochenplaner
-- Mehrfach ausführbar auf MySQL/MariaDB. Falls Spalten/Indizes schon existieren, die jeweilige Meldung ignorieren.

ALTER TABLE `calendar_events` ADD COLUMN `project_id` VARCHAR(255) NULL AFTER `tenant_id`;
ALTER TABLE `calendar_events` ADD INDEX `idx_calendar_events_project` (`project_id`);
ALTER TABLE `calendar_events` ADD INDEX `idx_calendar_events_site` (`site_id`);

ALTER TABLE `planning_assignments` ADD COLUMN `project_id` VARCHAR(255) NULL AFTER `tenant_id`;
ALTER TABLE `planning_assignments` ADD INDEX `idx_planning_assignments_project` (`project_id`);
ALTER TABLE `planning_assignments` ADD INDEX `idx_planning_assignments_site` (`site_id`);
