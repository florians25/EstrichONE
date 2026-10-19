-- Bauakte Pro: Aufgaben/Auftragsbezug und Uploads
ALTER TABLE `tasks` ADD COLUMN `project_id` CHAR(32) NULL AFTER `tenant_id`;
CREATE INDEX idx_tasks_project ON `tasks` (`project_id`);
-- Falls Spalten/Index bereits existieren, die Meldung ignorieren oder Befehl überspringen.
