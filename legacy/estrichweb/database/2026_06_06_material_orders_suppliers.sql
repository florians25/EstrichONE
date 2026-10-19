-- EstrichWeb Hotfix 2026-06-06
-- Lieferanten-Stammdaten und Materialbestellungen in der Bauakte

CREATE TABLE IF NOT EXISTS suppliers (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  supplier_no VARCHAR(80) NULL,
  name VARCHAR(255) NOT NULL,
  contact_person VARCHAR(255) NULL,
  email VARCHAR(255) NULL,
  phone VARCHAR(80) NULL,
  address VARCHAR(255) NULL,
  zip VARCHAR(20) NULL,
  city VARCHAR(120) NULL,
  tax_no VARCHAR(120) NULL,
  iban VARCHAR(80) NULL,
  notes TEXT NULL,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_suppliers_tenant (tenant_id,name),
  KEY idx_suppliers_active (tenant_id,is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS material_orders (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  project_id CHAR(32) NOT NULL,
  site_id CHAR(32) NULL,
  supplier_id CHAR(32) NULL,
  order_no VARCHAR(80) NOT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'to_order',
  requested_delivery_date DATE NULL,
  delivery_address VARCHAR(255) NULL,
  contact_name VARCHAR(255) NULL,
  notes TEXT NULL,
  created_by CHAR(32) NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  UNIQUE KEY uq_material_order_no (tenant_id,order_no),
  KEY idx_material_orders_project (tenant_id,project_id,status),
  KEY idx_material_orders_supplier (tenant_id,supplier_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS material_order_items (
  id CHAR(32) PRIMARY KEY,
  tenant_id CHAR(32) NOT NULL,
  order_id CHAR(32) NOT NULL,
  position_no INT NOT NULL DEFAULT 1,
  description VARCHAR(255) NOT NULL,
  quantity DECIMAL(12,3) NULL,
  unit VARCHAR(40) NULL,
  notes TEXT NULL,
  created_at DATETIME NOT NULL,
  updated_at DATETIME NULL,
  deleted_at DATETIME NULL,
  KEY idx_material_order_items_order (tenant_id,order_id,position_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
