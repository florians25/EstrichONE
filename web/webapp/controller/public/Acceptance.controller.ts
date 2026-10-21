import BaseController from "../BaseController";
import { api, problemMessage } from "../../util/api";
import { acceptanceResults, type AcceptanceResult } from "../../util/acceptanceWizard";
import { date } from "../../util/format";
import type { RouteMatch } from "../../util/router";
import type SignaturePad from "../../control/SignaturePad";

interface PublicView {
	companyName: string;
	hasCompanyLogo: boolean;
	result: AcceptanceResult;
	photoFileIds: string[];
	defects: { title: string; dueDate: string | null }[];
	state: "recorded" | "awaitingCustomer" | "completed";
	decision: "approved" | "rejected" | null;
}

/**
 * Öffentliche Freigabeseite für Kunden (ohne Anmeldung, Link mit Token, befristet): Protokoll prüfen, freigeben oder
 * mit Begründung ablehnen, optional unterschreiben.
 * @namespace estrichone.controller.public
 */
export default class Acceptance extends BaseController {
	private token = "";

	onInit(): void {
		this.local({ v: null, state: "loading", busy: false, name: "", comment: "", error: "", decision: null, photos: [], signatureUrl: "",
			resultText: "", resultState: "None" }, "pub");
	}

	private base(): string {
		return `public/acceptances/${encodeURIComponent(this.token)}`;
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.token = match.params.token ?? "";
		const m = this.model("pub");
		m.setData({ ...m.getData(), v: null, state: "loading", busy: true, error: "", decision: null });
		try {
			const v = await api<PublicView>(this.base(), { quiet: true });
			this.apply(v);
		} catch (error) {
			m.setProperty("/state", "invalid");
			m.setProperty("/error", problemMessage(error));
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private apply(v: PublicView): void {
		const m = this.model("pub");
		m.setProperty("/v", { ...v, defects: v.defects.map((d) => ({ ...d, dueText: date(d.dueDate) })) });
		m.setProperty("/resultText", acceptanceResults[v.result].text);
		m.setProperty("/resultState", acceptanceResults[v.result].state);
		m.setProperty("/photos", v.photoFileIds.map((id) => ({ url: `/api/v1/${this.base()}/photos/${id}` })));
		m.setProperty("/signatureUrl", `/api/v1/${this.base()}/signature`);
		m.setProperty("/state", v.decision ? "done" : "open");
		m.setProperty("/decision", v.decision);
	}

	onClearSignature(): void {
		(this.v().byId("pubSignature") as unknown as SignaturePad).clear();
	}

	private async decide(approve: boolean): Promise<void> {
		const m = this.model("pub");
		const name = String(m.getProperty("/name") ?? "").trim();
		const comment = String(m.getProperty("/comment") ?? "").trim();
		if (!approve && !comment) {
			m.setProperty("/error", "Bitte kurz begründen, warum Sie die Abnahme ablehnen.");
			return;
		}
		m.setProperty("/busy", true);
		m.setProperty("/error", "");
		try {
			const signature = (this.v().byId("pubSignature") as unknown as SignaturePad).toPng();
			const v = await api<PublicView>(`${this.base()}/decision`, { method: "POST", body: { approve, name, comment: comment || null, signaturePng: signature }, quiet: true });
			this.apply(v);
			m.setProperty("/state", "done");
			m.setProperty("/decision", approve ? "approved" : "rejected");
		} catch (error) {
			m.setProperty("/error", problemMessage(error));
		} finally {
			m.setProperty("/busy", false);
		}
	}

	onApprove(): void {
		void this.decide(true);
	}

	onReject(): void {
		void this.decide(false);
	}
}
