/** Abnahme starten (aus der Dokumentation oder dem Aktionen-Menü) und das Ergebnis anzeigen. */
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import EventBus from "sap/ui/core/EventBus";
import { get } from "./api";
import { openAcceptanceWizard } from "./acceptanceWizard";
import { akteContextOf } from "./akteHeader";
import { akte } from "./akteStore";
import { dateTime } from "./format";
import { session } from "./session";

type AkteData = Parameters<typeof akteContextOf>[0] & { customer?: { name: string; email: string | null } | null };

export function showAcceptanceLink(link: { url: string; expiresAt: string; emailed: boolean }): void {
	MessageBox.information(`${link.emailed ? "Der Link wurde per E-Mail an den Kunden geschickt." : "Kein Versand per E-Mail (keine Adresse oder kein E-Mail-Konto eingerichtet) – Link bitte weitergeben:"}\n\n${link.url}\n\nGültig bis ${dateTime(link.expiresAt)}.`, {
		title: "Freigabelink für den Kunden",
		actions: ["Link kopieren", MessageBox.Action.CLOSE],
		emphasizedAction: "Link kopieren",
		onClose: (action: unknown) => {
			if (action === "Link kopieren") void navigator.clipboard?.writeText(link.url).then(() => MessageToast.show("Link kopiert"));
		},
	});
}

export async function runAcceptance(bauakteId: string, data: AkteData | undefined): Promise<boolean> {
	const [company, rooms, files] = await Promise.all([
		get<{ name: string }>("company").catch(() => ({ name: "" })),
		akte.rooms(bauakteId).catch(() => null),
		akte.files(bauakteId).catch(() => []),
	]);
	const me = session.data.user?.displayName;
	const saved = await openAcceptanceWizard({
		bauakteId, context: akteContextOf(data), customerName: data?.customer?.name ?? null, customerEmail: data?.customer?.email ?? null,
		contractorName: me ? `${me}${company.name ? ` (${company.name})` : ""}` : company.name,
		rooms, photos: files.filter((f) => f.kind === "photo"),
	});
	if (!saved) return false;
	akte.invalidate(bauakteId, "inspections", "work", "files");
	EventBus.getInstance().publish("bauakte", "reload", { id: bauakteId });
	if (saved.link) showAcceptanceLink(saved.link);
	else MessageToast.show(saved.signed ? "Abnahme gespeichert – das PDF liegt unter „Dokumente → Abnahmen“." : "Abnahme gespeichert");
	return true;
}
