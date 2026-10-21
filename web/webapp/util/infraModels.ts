/** Typen für Archiv, Backup-Ziele und E-Mail (contracts/openapi/v1.json, Paket infra). Feldnamen 1:1 wie im Vertrag. */

export type ArchiveTrigger = "onUpload" | "onApproval" | "onBauakteCompleted" | "manual";
export type ArchiveRecordStatus = "pending" | "archived" | "failed";

export interface ArchiveConnectionDto {
	address: string; serverName: string; serverVersion: string; caFingerprint: string; fingerprintDisplay: string;
	pairedAt: string; lastContactAt: string | null; lastError: string | null; autoArchive: boolean; notifyEmail: string | null;
}

export interface ArchiveVerification {
	at: string; ok: boolean; ledgerEntries: number; ledgerHead: string; objectsChecked: number; bytesChecked: number; deleted: number;
	errors: string[];
}

export interface ArchiveServerStatus {
	role: string; name: string; version: string; now: string; clientId: string; clientName: string; pairedAt: string;
	archive: { objects: number; bytes: number; deleted: number; ledgerEntries: number; ledgerHead: string } | null;
	lastVerification: ArchiveVerification | null; diskFreeBytes: number | null; diskTotalBytes: number | null;
}

export interface ArchiveOverview {
	connected: boolean; connection: ArchiveConnectionDto | null; server: ArchiveServerStatus | null;
	counts: { pending: number; archived: number; failed: number };
}

export interface ArchiveRuleDto { documentType: string; label: string; enabled: boolean; retentionYears: number; trigger: ArchiveTrigger }

export interface ArchiveRecordDto {
	id: string; fileId: string; bauakteId: string | null; documentType: string; documentTypeLabel: string; name: string; sha256: string;
	sizeBytes: number; retentionYears: number; trigger: ArchiveTrigger; status: ArchiveRecordStatus; attempts: number; nextAttemptAt: string;
	lastError: string | null; queuedAt: string; archivedAt: string | null; retainUntil: string | null; ledgerSeq: number | null;
	ledgerHash: string | null; archiveServerName: string | null;
}

export interface BackupRunInfoDto { status: string; message: string; at: string }

export interface BackupTargetDto {
	id: string; name: string; address: string; serverVersion: string; fingerprintDisplay: string; pairedAt: string; lastContactAt: string | null;
	lastError: string | null; lastRun: BackupRunInfoDto | null; backupServiceConfigured: boolean;
	remote: { bytes: number; snapshots: number; lastSnapshotAt: string | null; appendOnly: boolean; serverRunning: boolean;
		diskFreeBytes: number | null; diskTotalBytes: number | null } | null;
}

export type MailSecurity = "startTls" | "sslOnConnect" | "none";
export type MailStatus = "queued" | "sent" | "failed";

export interface MailSettingsDto {
	configured: boolean; enabled: boolean; host: string; port: number; security: MailSecurity; userName: string | null; hasPassword: boolean;
	fromAddress: string; fromName: string | null; replyTo: string | null; lastTestAt: string | null; lastTestOk: boolean | null;
	lastTestError: string | null;
}

export interface TestMailResult { ok: boolean; error: string | null; at: string }

export interface MailTemplateDto { key: string; name: string; subject: string; body: string; customized: boolean; placeholders: string[] }

export interface MailLogItem {
	id: string; createdAt: string; to: string; subject: string; templateKey: string | null; category: string | null; relatedEntity: string | null;
	status: MailStatus; attempts: number; sentAt: string | null; error: string | null; attachmentNames: string | null;
}

export const triggerText: Record<ArchiveTrigger, string> = {
	onUpload: "Sofort nach dem Hochladen",
	onApproval: "Nach der Freigabe",
	onBauakteCompleted: "Wenn die Bauakte abgeschlossen ist",
	manual: "Nur manuell",
};

export const recordStatus: Record<ArchiveRecordStatus, { text: string; state: string }> = {
	pending: { text: "Wartet", state: "Information" },
	archived: { text: "Archiviert", state: "Success" },
	failed: { text: "Fehlgeschlagen – wird erneut versucht", state: "Error" },
};
