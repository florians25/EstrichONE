-- 2026-05-31 Bauakte fix: quality_checks table
-- The /api/projects/{id}/file (Bauakte) endpoint reads from quality_checks,
-- but the table was missing from the base schema. On any project that already
-- had a construction site this produced "HTTP 500 Internal Server Error" and
-- the Bauakte failed to load. This migration creates the table so the QM data
-- set is available. The API also creates it on demand (ensure_quality_checks_table).

CREATE TABLE IF NOT EXISTS quality_checks (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  site_id VARCHAR(255) NULL,
  project_id VARCHAR(255) NULL,
  room_id VARCHAR(255) NULL,
  check_type VARCHAR(80) NULL,
  title VARCHAR(255) NULL,
  status VARCHAR(80) NOT NULL DEFAULT 'open',
  result VARCHAR(80) NULL,
  measured_value VARCHAR(120) NULL,
  target_value VARCHAR(120) NULL,
  notes TEXT NULL,
  checked_by CHAR(32) NULL,
  checked_at DATETIME NULL,
  created_by CHAR(32) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_quality_checks_site (tenant_id, site_id),
  KEY idx_quality_checks_project (tenant_id, project_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
