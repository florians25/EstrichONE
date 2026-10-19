-- EstrichWeb Hotfix 2026-06-05
-- Trennt Anrufe und Nicht-Kunden-Anfragen (Ticket-Status) in call_logs.
-- Sicher mehrfach ausführbar auf MySQL 8 / MariaDB mit IF NOT EXISTS nicht überall verfügbar;
-- falls eine Spalte bereits existiert, die jeweilige ALTER-Anweisung einfach überspringen.

ALTER TABLE call_logs ADD COLUMN kind VARCHAR(20) NOT NULL DEFAULT 'call' AFTER tenant_id;
ALTER TABLE call_logs ADD COLUMN inquiry_status VARCHAR(30) NOT NULL DEFAULT 'open' AFTER follow_up;
ALTER TABLE call_logs ADD COLUMN converted_project_id VARCHAR(255) NULL AFTER inquiry_status;

UPDATE call_logs SET kind='call' WHERE kind IS NULL OR kind='';
UPDATE call_logs SET inquiry_status='open' WHERE inquiry_status IS NULL OR inquiry_status='';

CREATE INDEX idx_calls_kind ON call_logs (tenant_id, kind, inquiry_status);
