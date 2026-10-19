-- EstrichWeb Professional UX + speicherschonende Fotoablage
-- Kann auf bestehender DB mehrfach versucht werden; bei Duplicate column die betreffende Zeile ignorieren.

ALTER TABLE `photos` ADD COLUMN `thumbnail_path` VARCHAR(500) NULL AFTER `compressed_path`;
ALTER TABLE `photos` ADD COLUMN `original_size_bytes` DECIMAL(14,2) NULL AFTER `thumbnail_path`;
ALTER TABLE `photos` ADD COLUMN `compressed_size_bytes` DECIMAL(14,2) NULL AFTER `original_size_bytes`;
ALTER TABLE `photos` ADD COLUMN `thumbnail_size_bytes` DECIMAL(14,2) NULL AFTER `compressed_size_bytes`;
ALTER TABLE `photos` ADD COLUMN `width_px` INT NULL AFTER `thumbnail_size_bytes`;
ALTER TABLE `photos` ADD COLUMN `height_px` INT NULL AFTER `width_px`;
ALTER TABLE `photos` MODIFY COLUMN `original_path` VARCHAR(500) NULL;
ALTER TABLE `photos` MODIFY COLUMN `compressed_path` VARCHAR(500) NULL;
CREATE INDEX idx_photos_site_created ON `photos` (`tenant_id`, `site_id`, `created_at`);
CREATE INDEX idx_reports_site_type_created ON `reports` (`tenant_id`, `site_id`, `report_type`, `created_at`);
