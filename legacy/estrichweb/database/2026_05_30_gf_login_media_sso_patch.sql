CREATE TABLE IF NOT EXISTS sso_login_tickets (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  user_id CHAR(32) NOT NULL,
  ticket_hash CHAR(64) NOT NULL,
  provider VARCHAR(40) NOT NULL,
  expires_at DATETIME NOT NULL,
  used_at DATETIME NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  KEY idx_sso_ticket_hash(ticket_hash),
  KEY idx_sso_user(tenant_id,user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS document_shares (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  document_id CHAR(32) NOT NULL,
  shared_with_email VARCHAR(180) NULL,
  share_token_hash CHAR(64) NULL,
  expires_at DATETIME NULL,
  can_download TINYINT(1) DEFAULT 0,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_doc_shares(tenant_id,document_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

ALTER TABLE documents MODIFY COLUMN mime_type VARCHAR(120) NULL;
ALTER TABLE documents MODIFY COLUMN storage_path VARCHAR(500) NOT NULL;
