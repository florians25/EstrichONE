/** Dokumentfreigaben: anfragen, genehmigen/ablehnen (mit Kommentar), zurückziehen. */
import { get, post } from "./api";
import { openForm } from "./forms";
import { uuidv7 } from "./ids";
import { session } from "./session";
import type { RoleResponse } from "./models";

export type ApprovalStatus = "pending" | "approved" | "rejected" | "cancelled";

export interface ApprovalResponse {
	id: string;
	fileId: string;
	fileName: string;
	bauakteId: string;
	bauakteNumber: string | null;
	bauakteTitle: string;
	status: ApprovalStatus;
	requestedByUserId: string;
	requestedByName: string;
	requestedAt: string;
	assigneeUserId: string | null;
	assigneeRoleId: string | null;
	assigneeName: string;
	message: string | null;
	dueDate: string | null;
	decidedByUserId: string | null;
	decidedByName: string | null;
	decidedAt: string | null;
	decisionComment: string | null;
	canDecide: boolean;
	version: number;
}

export const approvalStatus: Record<ApprovalStatus, { text: string; state: "Warning" | "Success" | "Error" | "None" }> = {
	pending: { text: "Wartet auf Freigabe", state: "Warning" },
	approved: { text: "Freigegeben", state: "Success" },
	rejected: { text: "Abgelehnt", state: "Error" },
	cancelled: { text: "Zurückgezogen", state: "None" },
};

let rolesCache: Promise<RoleResponse[]> | null = null;
function roles(): Promise<RoleResponse[]> {
	rolesCache ??= get<RoleResponse[]>("access/roles").catch(() => {
		rolesCache = null;
		return [] as RoleResponse[];
	});
	return rolesCache;
}

/** Freigabe anfragen; liefert die neue Freigabe oder null (abgebrochen). */
export async function requestApproval(file: { id: string; originalName: string }): Promise<ApprovalResponse | null> {
	const [people, roleList] = await Promise.all([session.people(), roles()]);
	const me = session.model.getProperty("/user/id") as string | undefined;
	const options = [
		...people.filter((p) => p.isActive && p.id !== me).map((p) => ({ key: `u:${p.id}`, text: p.displayName, additionalText: "Person" })),
		...roleList.map((r) => ({ key: `r:${r.id}`, text: `Rolle ${r.name}`, additionalText: "alle Mitglieder" })),
	];
	return openForm<ApprovalResponse>({
		title: "Freigabe anfordern", submitText: "Anfordern", width: "34rem",
		intro: `Dokument: ${file.originalName}`,
		fields: [
			{ key: "target", label: "Freigabe durch", type: "combo", required: true, options },
			{ key: "message", label: "Nachricht", type: "textarea", rows: 3, maxLength: 2000, placeholder: "z. B. Bitte Preise und Mengen prüfen" },
			{ key: "dueDate", label: "Bis (optional)", type: "date" },
		],
		submit: (v) => {
			const [kind, id] = String(v.target).split(":");
			return post<ApprovalResponse>(`files/${file.id}/approvals`, {
				id: uuidv7(), assigneeUserId: kind === "u" ? id : null, assigneeRoleId: kind === "r" ? id : null,
				message: (v.message as string) || null, dueDate: (v.dueDate as string) || null,
			});
		},
	});
}

/** Genehmigen oder ablehnen; beim Ablehnen ist ein Kommentar Pflicht. */
export function decideApproval(approval: ApprovalResponse, approve: boolean): Promise<ApprovalResponse | null> {
	return openForm<ApprovalResponse>({
		title: approve ? "Dokument freigeben" : "Freigabe ablehnen", submitText: approve ? "Freigeben" : "Ablehnen", danger: !approve, width: "32rem",
		intro: `${approval.fileName} · ${approval.bauakteNumber ?? ""} ${approval.bauakteTitle}${approval.message ? `\nAnfrage von ${approval.requestedByName}: ${approval.message}` : ""}`,
		fields: [{ key: "comment", label: approve ? "Kommentar (optional)" : "Begründung", type: "textarea", rows: 3, maxLength: 2000, required: !approve }],
		submit: (v) => post<ApprovalResponse>(`approvals/${approval.id}/${approve ? "approve" : "reject"}`, { comment: (v.comment as string) || null }),
	});
}

export function cancelApproval(approval: ApprovalResponse): Promise<ApprovalResponse> {
	return post<ApprovalResponse>(`approvals/${approval.id}/cancel`);
}
