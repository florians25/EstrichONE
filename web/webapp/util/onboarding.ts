/**
 * Onboarding neuer Mitarbeiter: Benutzerkonto anlegen bzw. verknüpfen (Rollen, Kolonnen, E-Mail), optional
 * Geräte-Einrichtungscode mit QR fürs Handy, Onboarding-PDF (DIN 5008) in die Mitarbeiterakte, optional per E-Mail.
 * Mit bestehendem Konto: neues Einmalpasswort und Unterlagen erneut erzeugen.
 */
import MessageBox from "sap/m/MessageBox";
import QRCode from "qrcode";
import { get, post, put } from "./api";
import { openForm } from "./forms";
import { fileUrl } from "./looseUpload";
import { planningLookups } from "./assignmentDialog";
import type { EmployeeDetail } from "./planningModels";
import { session } from "./session";

interface Role { id: string; name: string }
interface UserItem { id: string; displayName: string; userName: string }
interface Enrollment { code: string; expiresAt: string; link: string | null }
interface OnboardingResult { userId: string; userName: string; oneTimePassword: string; createdAccount: boolean; documentFileId: string; emailed: boolean }

const plain = (v: string) => v.toLowerCase().replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss")
	.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]/g, "");

/** Vorschlag „v.nachname“ aus Vor- und Nachname. */
export function suggestUserName(firstName: string | null | undefined, lastName: string | null | undefined): string {
	const first = plain(firstName ?? "");
	const last = plain(lastName ?? "");
	return first && last ? `${first[0]}.${last}` : first || last;
}

export function canOnboard(): boolean {
	return session.has("hr.manage") && session.has("users.manage");
}

/** Liefert true, wenn ein Konto angelegt/verknüpft bzw. die Unterlagen neu erzeugt wurden. */
export async function openOnboarding(e: EmployeeDetail): Promise<boolean> {
	const hasAccount = !!e.userId;
	const [roles, users, lookups] = await Promise.all([
		hasAccount ? Promise.resolve([] as Role[]) : get<Role[]>("access/roles"),
		hasAccount ? Promise.resolve([] as UserItem[]) : get<UserItem[]>("users"),
		planningLookups(true),
	]);
	const siteRole = roles.find((r) => /baustelle/i.test(r.name));
	const devices = session.has("devices.manage");
	const result = await openForm<OnboardingResult>({
		title: hasAccount ? `${e.displayName}: Onboarding-PDF erneut erzeugen` : `${e.displayName}: Benutzerkonto anlegen`, width: "38rem",
		intro: hasAccount
			? `Für das Konto „${e.userName ?? ""}“ wird ein neues Einmalpasswort vergeben (alte Sitzungen enden) und ein neues Onboarding-PDF erzeugt.`
			: "Legt das Benutzerkonto verknüpft mit dem Personalstamm an. Das Onboarding-PDF (Zugangsdaten, App-Einrichtung, erste Schritte) wird in der Mitarbeiterakte abgelegt.",
		initial: { mode: "new", userName: suggestUserName(e.firstName, e.lastName), roleIds: siteRole ? [siteRole.id] : [], email: e.email,
			crewIds: e.crews.map((c) => c.id), device: false, sendEmail: false },
		fields: [
			...(hasAccount ? [] : [
				{ key: "mode", label: "Konto", type: "segmented" as const, options: [{ key: "new", text: "Neu anlegen" }, { key: "link", text: "Vorhandenes verknüpfen" }] },
				{ key: "userName", label: "Benutzername", maxLength: 64, placeholder: "z. B. m.mustermann",
					validate: (v: unknown, all: Record<string, unknown>) => (all.mode === "new" && !String(v ?? "").trim() ? "Bitte einen Benutzernamen angeben." : null) },
				{ key: "roleIds", label: "Rollen", type: "multi" as const, options: roles.map((r) => ({ key: r.id, text: r.name })),
					validate: (v: unknown, all: Record<string, unknown>) => (all.mode === "new" && !(v as string[] | null)?.length ? "Bitte mindestens eine Rolle wählen." : null) },
				{ key: "email", label: "E-Mail (optional)", type: "email" as const, maxLength: 320 },
				{ key: "linkUserId", label: "Vorhandenes Konto", type: "select" as const, options: users.map((u) => ({ key: u.id, text: `${u.displayName} (${u.userName})` })),
					validate: (v: unknown, all: Record<string, unknown>) => (all.mode === "link" && !v ? "Bitte ein Konto wählen." : null) },
				{ key: "crewIds", label: "Kolonnen", type: "multi" as const, options: lookups.crews.filter((c) => c.isActive).map((c) => ({ key: c.id, text: c.name })) },
			]),
			...(devices ? [{ key: "device", label: "Handy gleich einrichten", type: "checkbox" as const, placeholder: "Geräte-Einrichtungscode mit QR-Code ins PDF (15 Minuten gültig)" }] : []),
			{ key: "sendEmail", label: "Per E-Mail senden", type: "checkbox" as const, placeholder: "Unterlagen zusätzlich an die E-Mail-Adresse des Mitarbeiters" },
		],
		submitText: hasAccount ? "Neu erzeugen" : "Konto anlegen",
		submit: async (v) => {
			let device: { deviceCode?: string; deviceLink?: string | null; deviceCodeExpiresAt?: string; deviceQrPng?: string | null } = {};
			if (v.device) {
				const code = await post<Enrollment>("devices/enrollment-codes", { kind: "personal", deviceName: `Handy ${e.displayName}`.slice(0, 100) });
				const qr = code.link ? await QRCode.toDataURL(code.link, { width: 400, margin: 1, errorCorrectionLevel: "M" }) : null;
				device = { deviceCode: code.code, deviceLink: code.link, deviceCodeExpiresAt: code.expiresAt, deviceQrPng: qr };
			}
			const linkMode = !hasAccount && v.mode === "link";
			const res = await post<OnboardingResult>(`staff/employees/${e.id}/onboarding`, {
				userName: !hasAccount && !linkMode ? v.userName : null, roleIds: !hasAccount && !linkMode ? v.roleIds : null,
				email: !hasAccount && !linkMode ? v.email || null : null, linkUserId: linkMode ? v.linkUserId : null,
				sendEmail: !!v.sendEmail, ...device,
			});
			const crews = (v.crewIds as string[] | null) ?? [];
			if (!hasAccount && crews.length) await put(`staff/employees/${e.id}/crews`, { crewIds: crews });
			return res;
		},
	});
	if (!result) return false;
	await new Promise<void>((resolve) => {
		MessageBox.success([
			result.createdAccount ? `Benutzerkonto „${result.userName}“ angelegt und mit dem Personalstamm verknüpft.` : `Neues Einmalpasswort für „${result.userName}“.`,
			`Einmalpasswort: ${result.oneTimePassword}`,
			"Das Onboarding-PDF liegt in der Mitarbeiterakte (Dokumente).",
			result.emailed ? "Die Unterlagen wurden per E-Mail verschickt." : "",
		].filter(Boolean).join("\n"), {
			title: "Onboarding", actions: ["PDF öffnen", MessageBox.Action.CLOSE], emphasizedAction: "PDF öffnen",
			onClose: (action: string) => {
				if (action === "PDF öffnen") window.open(fileUrl(result.documentFileId), "_blank", "noopener");
				resolve();
			},
		});
	});
	return true;
}
