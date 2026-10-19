-- EstrichWeb Hotfix 2026-06-06
-- Erweiterte Anfrage-Details / Ticket-Stepper.
-- Sicher mehrfach ausführbar über die API-Runtime: api/index.php legt fehlende Spalten automatisch an.
-- Wenn diese SQL manuell ausgeführt wird und eine Spalte bereits existiert, die jeweilige ALTER-Anweisung überspringen.

ALTER TABLE call_logs ADD COLUMN caller_email VARCHAR(200) NULL AFTER caller_phone;
ALTER TABLE call_logs ADD COLUMN address VARCHAR(255) NULL AFTER company;
ALTER TABLE call_logs ADD COLUMN zip VARCHAR(20) NULL AFTER address;
ALTER TABLE call_logs ADD COLUMN city VARCHAR(160) NULL AFTER zip;
ALTER TABLE call_logs ADD COLUMN source VARCHAR(40) NULL AFTER direction;
ALTER TABLE call_logs ADD COLUMN priority VARCHAR(20) NOT NULL DEFAULT 'normal' AFTER source;
ALTER TABLE call_logs ADD COLUMN desired_start DATE NULL AFTER priority;
ALTER TABLE call_logs ADD COLUMN area_m2 DECIMAL(10,2) NULL AFTER desired_start;
ALTER TABLE call_logs ADD COLUMN screed_type VARCHAR(120) NULL AFTER area_m2;
ALTER TABLE call_logs ADD COLUMN next_step VARCHAR(255) NULL AFTER screed_type;
ALTER TABLE call_logs ADD COLUMN due_date DATE NULL AFTER next_step;

UPDATE call_logs SET source='phone' WHERE source IS NULL OR source='';
UPDATE call_logs SET priority='normal' WHERE priority IS NULL OR priority='';
