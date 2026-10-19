-- EstrichWeb calendar/workflow patch for existing installations
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `all_day` TINYINT(1) NOT NULL DEFAULT 0 AFTER `end_at`;
ALTER TABLE `calendar_events` ADD COLUMN IF NOT EXISTS `event_type` VARCHAR(80) NULL AFTER `all_day`;
CREATE INDEX IF NOT EXISTS idx_calendar_events_start ON `calendar_events` (`tenant_id`, `start_at`);
