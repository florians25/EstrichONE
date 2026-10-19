-- 2026-06-07: Anrufe optional mit Kunden verknüpfen
ALTER TABLE call_logs
  ADD COLUMN IF NOT EXISTS customer_id VARCHAR(255) NULL AFTER caller_email;
CREATE INDEX IF NOT EXISTS idx_calls_customer ON call_logs (tenant_id, customer_id);
