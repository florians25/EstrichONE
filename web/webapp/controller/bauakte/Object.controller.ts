import BaseController from "../BaseController";
import type View from "sap/ui/core/mvc/View";
import type Event from "sap/ui/base/Event";
import type ObjectPageLayout from "sap/uxap/ObjectPageLayout";
import type ObjectPageSection from "sap/uxap/ObjectPageSection";
import type MenuItem from "sap/m/MenuItem";
import EventBus from "sap/ui/core/EventBus";
import Dialog from "sap/m/Dialog";
import Button from "sap/m/Button";
import Input from "sap/m/Input";
import Label from "sap/m/Label";
import Text from "sap/m/Text";
import MessageStrip from "sap/m/MessageStrip";
import VBox from "sap/m/VBox";
import { ValueState } from "sap/ui/core/library";
import { get, post, problemMessage, put } from "../../util/api";
import { akte } from "../../util/akteStore";
import { saveBauakte, type BauakteChanges } from "../../util/bauakte";
import { isoDay, parseDay } from "../../util/days";
import { address, area, cmNumber, date } from "../../util/format";
import { openForm } from "../../util/forms";
import { assignmentKinds, bauakteStatus, flowStatuses, statusMarkerColors } from "../../util/labels";
import type {
	BauakteDetail, BauakteHint, BauakteMember, BauakteStatus, ContactResponse, DirectoryUser,
} from "../../util/models";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";
import { openActivityDialog } from "../../util/activityDialog";
import { runAcceptance } from "../../util/acceptanceFlow";
import { openInitialInspectionWizard } from "../../util/inspectionWizard";
import { openArchiveProof } from "../../util/archiveProof";
import { akteContextOf } from "../../util/akteHeader";
import { catalog, type BauakteTemplate, type TextBlock } from "../../util/catalog";
import { projectBadges, projectView } from "../../util/project";
import { badges, bauakteSchema, viewGroups } from "../../util/formSchema";
import { openMapDialog } from "../../util/mapDialog";
import { dossierStrip } from "../../util/dossier";

/** Abschnitte der Objektseite: Schlüssel (Adresse ?abschnitt=…) → eingebettete View. */
const sections: Record<string, string> = {
	uebersicht: "secOverview", dokumente: "secDocuments", fotos: "secPhotos", dokumentation: "secDocumentation", raeume: "secRooms",
	aufgaben: "secWork", material: "secMaterial", einsaetze: "secAssignments", kontakte: "secContacts", notizen: "secNotes",
};

export interface SectionController {
	show(bauakteId: string): Promise<void> | void;
}

const hintTypes: Record<string, string> = { info: "Information", success: "Success", warning: "Warning", danger: "Error" };

/**
 * Bauakte als Objektseite (sap.uxap): dynamischer Kopf mit Status-Workflow, Kennzahlen, Mini-Karte und Hinweisen;
 * Abschnitte als eigene Views, die beim Öffnen laden.
 * @namespace estrichone.controller.bauakte
 */
export default class ObjectController extends BaseController {
	private id = "";
	private section = "uebersicht";

	onInit(): void {
		this.local({ busy: false, listShown: false, bauakte: null, customer: null, members: [], hints: [], markers: [], kpi: {} }, "akte");
		const bus = EventBus.getInstance();
		bus.subscribe("bauakte", "updated", (_c: string, _e: string, data: object) => {
			const updated = (data as { bauakte: BauakteDetail }).bauakte;
			if (updated.id === this.id) {
				this.apply(updated);
				void this.loadSide();
			}
		});
		bus.subscribe("bauakte", "changed", (_c: string, _e: string, data: object) => {
			if ((data as { id: string }).id === this.id) {
				void this.loadHints();
				void this.loadKpis();
			}
		});
		// Serverseitige Folgen (z. B. Status nach Abnahme): Akte neu laden.
		bus.subscribe("bauakte", "reload", (_c: string, _e: string, data: object) => {
			if ((data as { id: string }).id === this.id) void this.onReload();
		});
	}

	private m() {
		return this.model("akte");
	}

	private cur(): BauakteDetail {
		return this.m().getProperty("/bauakte") as BauakteDetail;
	}

	async show(match: RouteMatch): Promise<void> {
		const id = match.params.id;
		const wanted = match.query.get("abschnitt") ?? (match.route.name === "bauakteItem" ? match.params.section : null);
		this.m().setProperty("/listShown", match.query.get("liste") === "1");
		const section = wanted && sections[wanted] ? wanted : wanted === "datei" ? this.section : "uebersicht";
		if (id !== this.id) {
			this.id = id;
			this.section = section;
			akte.clear(id);
			this.m().setData({ busy: true, listShown: match.query.get("liste") === "1", bauakte: null, customer: null, members: [], hints: [], markers: [], kpi: {} });
			try {
				this.apply(await get<BauakteDetail>(`bauakten/${id}`));
			} catch (error) {
				this.m().setProperty("/busy", false);
				this.showError(error, this.t("akte.notFound"));
				this.nav("/bauakten", true);
				return;
			}
			this.m().setProperty("/busy", false);
			void this.loadSide();
			void this.loadHints();
			void this.loadKpis();
		}
		this.select(section);
		if (match.query.get("erstbegehung") === "1") {
			// Aus dem Anlage-Assistenten: nahtlos in die Erstbegehung der neuen Akte.
			this.nav(`/bauakten/${id}?abschnitt=dokumentation`, true);
			void this.startInspection(id);
		}
	}

	private async startInspection(id: string): Promise<void> {
		const files = await akte.files(id).catch(() => []);
		if (await openInitialInspectionWizard({ bauakteId: id, photos: files.filter((f) => f.kind === "photo"), corrects: null })) {
			this.toast("Erstbegehung gespeichert");
			akte.invalidate(id, "inspections", "work", "files");
			EventBus.getInstance().publish("bauakte", "reload", { id });
		}
	}

	private page(): ObjectPageLayout {
		return this.v().byId("objectPage") as ObjectPageLayout;
	}

	private select(key: string): void {
		this.section = key;
		const target = this.v().byId(key) as ObjectPageSection;
		if (this.page().getSelectedSection() !== target.getId()) {
			this.page().setSelectedSection(target);
		}
		void this.activate(key);
	}

	private async activate(key: string): Promise<void> {
		const view = this.v().byId(sections[key]) as View;
		await view.loaded();
		const controller = view.getController() as unknown as SectionController;
		try {
			await controller.show(this.id);
		} catch (error) {
			this.showError(error);
		}
	}

	onSectionNavigate(event: Event): void {
		const section = (event as unknown as { getParameter(n: string): ObjectPageSection }).getParameter("section");
		const key = Object.keys(sections).find((k) => this.v().byId(k) === section) ?? "uebersicht";
		this.setQuery({ abschnitt: key });
		this.section = key;
		void this.activate(key);
	}

	/** Bauakte übernehmen und abgeleitete Kopfangaben berechnen. */
	private apply(b: BauakteDetail): void {
		const m = this.m();
		m.setProperty("/bauakte", b);
		m.setProperty("/canEdit", session.has("bauakten.edit"));
		const place = address(b);
		m.setProperty("/routeUrl", b.latitude !== null && b.longitude !== null
			? `https://www.openstreetmap.org/directions?to=${b.latitude}%2C${b.longitude}`
			: place !== "–" ? `https://www.openstreetmap.org/search?query=${encodeURIComponent(place)}` : "");
		m.setProperty("/placeText", place);
		m.setProperty("/projectView", projectView(b.project));
		m.setProperty("/projectBadges", projectBadges(b.project));
		void bauakteSchema().then((schema) => {
			if (!schema || this.cur()?.id !== b.id) return;
			m.setProperty("/projectBadges", badges(schema, b));
			m.setProperty("/projectGroups", viewGroups(schema, b));
		});
		m.setProperty("/subtitle", "");
		if (b.templateKey) {
			void catalog<BauakteTemplate>("bauakteTemplate").then((list) => {
				const t = list.find((x) => x.id === b.templateKey);
				if (t && this.cur()?.id === b.id) m.setProperty("/subtitle", `Vorlage: ${t.name}`);
			}).catch(() => undefined);
		}
		m.setProperty("/periodText", b.plannedStart || b.plannedEnd ? `${date(b.plannedStart)} – ${date(b.plannedEnd)}` : "–");
		m.setProperty("/markers", b.latitude !== null && b.longitude !== null
			? [{ id: b.id, latitude: b.latitude, longitude: b.longitude, color: statusMarkerColors[b.status], label: b.title }] : []);
		this.updateContactFacts();
		document.title = `${b.number ?? ""} ${b.title} – EstrichONE`.trim();
	}

	private updateContactFacts(): void {
		const m = this.m();
		const b = this.cur();
		const customer = m.getProperty("/customer") as ContactResponse | null;
		m.setProperty("/phone", b?.siteContactPhone ?? customer?.phone ?? "");
		m.setProperty("/mail", b?.siteContactEmail ?? customer?.email ?? "");
		const members = (m.getProperty("/members") as BauakteMember[]) ?? [];
		const responsible = session.name(b?.responsibleUserId);
		m.setProperty("/responsibleName", responsible);
		m.setProperty("/createdDate", b ? new Intl.DateTimeFormat("de-DE", { dateStyle: "medium" }).format(new Date(b.createdAt)) : "");
		m.setProperty("/createdText", b ? `${session.name(b.createdByUserId)} · ${new Intl.DateTimeFormat("de-DE", { dateStyle: "medium" }).format(new Date(b.createdAt))}` : "");
		m.setProperty("/assignedText", members.length === 0 ? responsible : members.length === 1 ? members[0].displayName : `${responsible} +${members.length - 1}`);
		const me = session.userId;
		m.setProperty("/canAssignMe", !!b && !!me && b.status !== "cancelled" && b.responsibleUserId !== me && !members.some((x) => x.userId === me));
	}

	/** „Mir zuweisen“: sich selbst als Mitarbeiter eintragen (wie in der App). */
	async onAssignMe(): Promise<void> {
		const b = this.cur();
		try {
			await post(`bauakten/${b.id}/members/me`);
			this.toast("Die Bauakte ist Ihnen jetzt zugewiesen.");
			await this.loadSide();
		} catch (error) {
			this.showError(error);
		}
	}

	/** Letzte Aktivitäten im eigenen Fenster (Wer/Wann/Was, alt → neu). */
	async onActivities(): Promise<void> {
		const b = this.cur();
		try {
			const entries = await akte.history(b.id);
			openActivityDialog(entries, akteContextOf(this.m().getData() as Parameters<typeof akteContextOf>[0]));
		} catch (error) {
			this.showError(error);
		}
	}

	private async loadSide(): Promise<void> {
		const b = this.cur();
		await session.people();
		const [members, customer] = await Promise.all([
			get<BauakteMember[]>(`bauakten/${b.id}/members`).catch(() => [] as BauakteMember[]),
			b.customerId && session.has("contacts.read") ? get<ContactResponse>(`contacts/${b.customerId}`).catch(() => null) : Promise.resolve(null),
		]);
		this.m().setProperty("/members", members);
		this.m().setProperty("/customer", customer);
		void this.showDossier(b.id);
		this.updateContactFacts();
		EventBus.getInstance().publish("bauakte", "side", { id: b.id });
	}

	private async loadHints(): Promise<void> {
		const hints = await get<BauakteHint[]>(`bauakten/${this.id}/hints`).catch(() => [] as BauakteHint[]);
		this.m().setProperty("/hints", hints.map((h) => ({ text: h.text, type: hintTypes[h.severity] ?? "Information" })));
		void get<{ items: unknown[] }>("documents", { bauakteId: this.id, approval: "pending" })
			.then((r) => this.m().setProperty("/approvalsText", r.items.length ? `${r.items.length} Freigabe${r.items.length === 1 ? "" : "n"} ausstehend` : ""))
			.catch(() => this.m().setProperty("/approvalsText", ""));
	}

	/** Kennzahlen im Kopf: Fläche, offene Aufgaben/Mängel, Dateien, Belegreife, nächster Einsatz. */
	private async loadKpis(): Promise<void> {
		const id = this.id;
		const today = isoDay(new Date());
		const kpi: Record<string, unknown> = { area: "–", openWork: "–", defects: 0, overdue: 0, filesText: "–", nextText: "nicht eingeplant" };
		const [rooms, work, files, inspections, planning] = await Promise.allSettled([
			akte.rooms(id), session.has("work.read") ? akte.work(id) : Promise.resolve([]), akte.files(id), akte.inspections(id),
			session.has("planning.read") ? akte.planning(id) : Promise.resolve(null),
		]);
		if (id !== this.id) return;
		if (rooms.status === "fulfilled") {
			kpi.area = area(rooms.value.totalAreaSquareMeters).replace(" m²", "");
			kpi.screeds = [...new Set(rooms.value.rooms.map((r) => r.screedTypeName).filter(Boolean))].join(", ");
		}
		if (work.status === "fulfilled" && session.has("work.read")) {
			const open = work.value.filter((w) => w.status !== "done");
			kpi.openWork = String(open.length);
			kpi.defects = open.filter((w) => w.kind === "defect").length;
			kpi.defectsText = `${kpi.defects} Mängel`;
			kpi.overdue = open.filter((w) => w.dueDate && w.dueDate < today).length;
		}
		if (files.status === "fulfilled") {
			const photos = files.value.filter((f) => f.kind === "photo").length;
			const docs = files.value.filter((f) => f.kind === "document").length;
			kpi.filesText = `${photos} Fotos · ${docs} Dokumente`;
			kpi.photos = photos;
			kpi.docs = docs;
		}
		if (inspections.status === "fulfilled") {
			const cm = inspections.value.filter((i) => i.kind === "cmMeasurement").sort((a, b) => b.performedAt.localeCompare(a.performedAt))[0];
			if (cm) {
				kpi.readyText = `${cm.isReadyForCovering ? "Belegreif" : "Noch nicht belegreif"} (${cmNumber(cm.measuredCmPercent)} CM-%)`;
				kpi.readyState = cm.isReadyForCovering ? "Success" : "Warning";
			}
		}
		if (planning.status === "fulfilled" && planning.value) {
			const next = planning.value.assignments.filter((a) => a.day >= today)
				.sort((a, b) => (a.day + (a.startTime ?? "")).localeCompare(b.day + (b.startTime ?? "")))[0];
			if (next) {
				kpi.nextText = `${parseDay(next.day).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit" })} ${next.startTime?.slice(0, 5) ?? ""} · ${assignmentKinds[next.kind]?.text ?? next.activity}${next.crewName ? ` · ${next.crewName}` : ""}`;
			}
		}
		this.m().setProperty("/kpi", kpi);
	}

	// Aktionen im Kopf

	private async save(changes: BauakteChanges, success: string): Promise<boolean> {
		this.m().setProperty("/busy", true);
		try {
			await saveBauakte(this.cur(), changes);
			akte.invalidate(this.id);
			this.toast(success);
			return true;
		} catch (error) {
			this.showError(error);
			return false;
		} finally {
			this.m().setProperty("/busy", false);
		}
	}

	onStatusMenu(event: Event): void {
		const item = (event as unknown as { getParameter(n: string): MenuItem }).getParameter("item");
		const status = item.getKey() as BauakteStatus;
		if (status !== this.cur().status) {
			void this.save({ status }, `Status: ${bauakteStatus[status].text}`);
		}
	}

	onTakeOver(): void {
		void this.save({ status: "planned" }, this.t("akte.takenOver"));
	}

	onComplete(): void {
		void this.confirm(this.t("akte.completeConfirm"), { action: this.t("akte.complete") })
			.then((ok) => ok && this.save({ status: "completed" }, this.t("akte.completed")));
	}

	onReactivate(): void {
		void this.save({ status: "planned", cancellationReason: null }, this.t("akte.reactivated"));
	}

	async onCancel(): Promise<void> {
		await openForm({
			title: this.t("akte.cancelTitle"), submitText: this.t("akte.cancelSubmit"), danger: true,
			intro: this.t("akte.cancelIntro"),
			fields: [{ key: "reason", label: this.t("akte.cancelReason"), type: "textarea", required: true, maxLength: 500, placeholder: "z. B. Kunde hat abgesagt" }],
			submit: async (v) => {
				await saveBauakte(this.cur(), { status: "cancelled", cancellationReason: String(v.reason) });
				akte.invalidate(this.id);
				this.toast(this.t("akte.cancelled"));
			},
		});
	}

	async onEdit(): Promise<void> {
		const b = this.cur();
		const people = (await session.people()).filter((p) => p.isActive);
		const customer = this.m().getProperty("/customer") as ContactResponse | null;
		await openForm({
			title: this.t("akte.editTitle"), width: "44rem",
			initial: { ...b, status: b.status === "cancelled" ? "planned" : b.status },
			fields: [
				{ key: "base", label: "Basisdaten", type: "section" },
				{ key: "title", label: "Bauvorhaben", required: true, maxLength: 200 },
				{ key: "status", label: "Status", type: "select", required: true, options: flowStatuses.map((s) => ({ key: s, text: bauakteStatus[s].text })) },
				{ key: "customerId", label: "Kunde", type: "customer", displayText: customer?.name },
				{ key: "responsibleUserId", label: "Zuständig", type: "select", options: people.map((p: DirectoryUser) => ({ key: p.id, text: p.displayName })) },
				{ key: "plannedStart", label: "Ausführung ab", type: "date" },
				{ key: "plannedEnd", label: "Ausführung bis", type: "date",
					validate: (v, all) => (v && all.plannedStart && String(v) < String(all.plannedStart) ? "Das Ende liegt vor dem Beginn." : null) },
				{ key: "site", label: "Baustelle", type: "section" },
				{ key: "street", label: "Straße", maxLength: 200 },
				{ key: "postalCode", label: "PLZ", maxLength: 10 },
				{ key: "city", label: "Ort", maxLength: 100 },
				{ key: "siteNotes", label: "Hinweise / Behinderungen", type: "textarea", maxLength: 2000,
					help: "Wird in der Bauakte und in den Apps hervorgehoben (Zufahrt, Schlüssel, Strom …)." },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 5, maxLength: 4000 },
			],
			submit: async (v) => {
				const text = (key: string) => (v[key] ? String(v[key]) : null);
				await saveBauakte(b, {
					title: String(v.title), status: (b.status === "cancelled" ? b.status : v.status) as BauakteStatus, customerId: text("customerId"),
					responsibleUserId: text("responsibleUserId"), plannedStart: text("plannedStart"), plannedEnd: text("plannedEnd"),
					street: text("street"), postalCode: text("postalCode"), city: text("city"), siteNotes: text("siteNotes"), description: text("description"),
				});
				akte.invalidate(this.id);
				this.toast(this.t("common.saved"));
			},
		});
	}

	async onAssign(): Promise<void> {
		const b = this.cur();
		const people = (await session.people()).filter((p) => p.isActive).map((p) => ({ key: p.id, text: p.displayName }));
		const members = (this.m().getProperty("/members") as BauakteMember[]).filter((x) => x.role === "member").map((x) => x.userId);
		await openForm({
			title: this.t("akte.assignTitle"), submitText: this.t("akte.assignSubmit"),
			initial: { responsibleUserId: b.responsibleUserId, memberUserIds: members },
			fields: [
				{ key: "responsibleUserId", label: "Zuständig (Bauleitung)", type: "select", options: people },
				{ key: "memberUserIds", label: "Zugewiesene Mitarbeiter", type: "multi", options: people,
					help: "Zugewiesene Mitarbeiter sehen die Bauakte auch ohne das Recht „alle Bauakten sehen“, z. B. in der App." },
			],
			submit: async (v) => {
				const responsible = (v.responsibleUserId as string | null) ?? null;
				if (responsible !== b.responsibleUserId) {
					await saveBauakte(b, { responsibleUserId: responsible });
				}
				await put(`bauakten/${b.id}/members`, { userIds: ((v.memberUserIds as string[]) ?? []).filter((u) => u !== responsible) });
				this.toast(this.t("akte.assigned.saved"));
				akte.invalidate(this.id);
				await this.loadSide();
			},
		});
	}

	/** Löschen mit Passwortbestätigung; falsches Passwort bleibt im Dialog sichtbar. */
	onDelete(): void {
		const password = new Input(this.createId("deletePassword"), { type: "Password", width: "100%" });
		const error = new MessageStrip({ type: "Error", showIcon: true, visible: false });
		const confirm = new Button({
			text: this.t("akte.deleteSubmit"), type: "Reject", enabled: false,
			press: async () => {
				dialog.setBusy(true);
				try {
					await post(`bauakten/${this.id}/delete`, { password: password.getValue() });
					dialog.close();
					this.toast(this.t("akte.deleted"));
					this.id = "";
					this.nav("/bauakten", true);
				} catch (e) {
					error.setText(problemMessage(e));
					error.setVisible(true);
					password.setValueState(ValueState.Error);
				} finally {
					dialog.setBusy(false);
				}
			},
		});
		password.attachLiveChange(() => confirm.setEnabled(!!password.getValue()));
		const dialog = new Dialog({
			title: this.t("akte.deleteTitle"), type: "Message", state: "Warning", contentWidth: "28rem",
			content: [new VBox({ items: [
				new Text({ text: this.t("akte.deleteText", [this.cur().title]) }),
				new Label({ text: this.t("akte.deletePassword"), labelFor: password }).addStyleClass("sapUiSmallMarginTop"), password, error,
			] })],
			beginButton: confirm,
			endButton: new Button({ text: this.t("common.cancel"), press: () => dialog.close() }),
			initialFocus: password,
			afterClose: () => dialog.destroy(),
		});
		dialog.open();
	}

	/** Serverseitiges PDF (Deckblatt, Räume, Tagebuch, Prüfungen, Mängel, Fotodokumentation). */
	/** Server-PDF herunterladen; Berichte legt der Server zusätzlich in der Bauakte ab (Protokolle / Berichte). */
	private download(path: string, stored: boolean): void {
		const a = document.createElement("a");
		a.href = `/api/v1/${path}`;
		a.download = "";
		a.click();
		this.toast(stored ? "PDF wird erstellt und unter „Protokolle / Berichte“ abgelegt …" : "PDF wird erstellt …");
		if (stored) setTimeout(() => akte.invalidate(this.id, "files"), 4000);
	}

	onPdf(): void {
		this.download(`bauakten/${this.id}/report.pdf`, true);
	}

	onReport(): void {
		this.download(`bauakten/${this.id}/report.pdf?parts=uebersicht,raeume,tagebuch,pruefungen,maengel&maxPhotos=0`, true);
	}

	onArchiveProof(): void {
		const b = this.cur();
		void openArchiveProof({ bauakteId: b.id, title: `${b.number ?? ""} ${b.title}`.trim() });
	}

	/** Digitales Abnahmeprotokoll (Assistent). */
	async onAcceptance(): Promise<void> {
		await runAcceptance(this.cur().id, this.m().getData() as Parameters<typeof runAcceptance>[1]);
	}

	onAcceptanceSheet(): void {
		this.download(`bauakten/${this.id}/acceptance-form.pdf`, false);
	}

	onSheet(): void {
		this.download(`bauakten/${this.id}/report.pdf?parts=uebersicht,raeume&maxPhotos=0`, true);
	}

	onRoute(): void {
		window.open(this.m().getProperty("/routeUrl"), "_blank", "noopener");
	}

	onCall(): void {
		window.location.href = `tel:${this.m().getProperty("/phone")}`;
	}

	onMail(): void {
		window.location.href = `mailto:${this.m().getProperty("/mail")}?subject=${encodeURIComponent(`${this.cur().number ?? ""} ${this.cur().title}`.trim())}`;
	}

	async onReload(): Promise<void> {
		const id = this.id;
		akte.clear(id);
		this.apply(await get<BauakteDetail>(`bauakten/${id}`));
		void this.loadSide();
		void this.loadHints();
		void this.loadKpis();
		void this.activate(this.section);
	}

	onToggleList(): void {
		const shown = this.m().getProperty("/listShown") as boolean;
		this.nav(`/bauakten/${this.id}${shown ? "" : "?liste=1"}${this.section !== "uebersicht" ? `${shown ? "?" : "&"}abschnitt=${this.section}` : ""}`, true);
	}

	onClose(): void {
		this.nav("/bauakten");
	}

	/** Baustelle auf der Karte im Dialog (die Karte gehört nicht in den Kopf). */
	onShowMap(): void {
		openMapDialog(this.m().getProperty("/placeText") as string, this.m().getProperty("/markers") as unknown[], this.m().getProperty("/routeUrl") as string);
	}

	/** Erstbegehungsprotokoll (neueste Erstbegehung) als PDF in neuem Tab. */
	async onInspectionPdf(): Promise<void> {
		const url = `/api/v1/bauakten/${this.id}/initial-inspection.pdf`;
		const head = await fetch(url, { method: "GET", credentials: "same-origin" }).catch(() => null);
		if (!head || head.status === 404) {
			this.toast("Für diese Bauakte ist noch keine Erstbegehung erfasst.");
			return;
		}
		window.open(URL.createObjectURL(await head.blob()), "_blank", "noopener");
	}

	/** Schreiben an den Kunden nach DIN 5008 aus Textbausteinen (B10); PDF landet unter Kundendokumente/Schreiben. */
	async onLetter(): Promise<void> {
		const blocks = await catalog<TextBlock>("textBlock").catch(() => [] as TextBlock[]);
		const customer = this.m().getProperty("/customer") as ContactResponse | null;
		const result = await openForm<{ fileId: string; emailed: boolean; emailTo: string | null }>({
			title: "Schreiben an den Kunden", width: "44rem",
			intro: "Platzhalter: {Kunde}, {Bauvorhaben}, {Nummer}, {Baustelle}, {Beginn}. Absätze mit einer Leerzeile trennen.",
			initial: { salutation: "Sehr geehrte Damen und Herren,", sendEmail: false, emailTo: customer?.email ?? "" },
			fields: [
				{ key: "block", label: "Textbaustein einfügen", type: "select", placeholder: "–", options: blocks.filter((b) => b.isActive).map((b) => ({ key: b.id, text: b.name })),
					onChange: (v, form) => {
						const b = blocks.find((x) => x.id === v);
						if (!b) return;
						const body = String(form.get("body") ?? "").trim();
						form.set("body", body ? `${body}\n\n${b.content.text}` : b.content.text);
						if (!String(form.get("subject") ?? "").trim()) form.set("subject", b.name);
					} },
				{ key: "subject", label: "Betreff", required: true, maxLength: 200, placeholder: "z. B. Terminbestätigung Estricharbeiten {Bauvorhaben}" },
				{ key: "salutation", label: "Anrede", maxLength: 200 },
				{ key: "body", label: "Text", type: "textarea", rows: 10, required: true, maxLength: 20000 },
				{ key: "sendEmail", label: "Zusätzlich per E-Mail senden (PDF im Anhang)", type: "switch" },
				{ key: "emailTo", label: "E-Mail an", type: "email", maxLength: 320 },
			],
			submit: (v) => post(`bauakten/${this.id}/letters`, {
				subject: v.subject, body: v.body, salutation: v.salutation || null, sendEmail: !!v.sendEmail, emailTo: v.emailTo || null,
			}),
		});
		if (!result) return;
		this.toast(result.emailed ? `Schreiben abgelegt und an ${result.emailTo} gesendet` : "Schreiben unter Kundendokumente/Schreiben abgelegt");
		window.open(`/api/v1/files/${result.fileId}/content`, "_blank", "noopener");
		akte.invalidate?.(this.id);
		EventBus.getInstance().publish("bauakte", "changed", { id: this.id });
	}

	/** Aktenarchiv: Zustand ausgelagerter Akten mit „Akte abrufen“ / „Entsperren“ im Kopf. */
	private async showDossier(id: string): Promise<void> {
		const box = this.byId("dossierBox") as VBox;
		const strip = await dossierStrip(id, () => void this.onReload());
		if (id !== this.id) { strip?.destroy(); return; }
		box.destroyItems();
		if (strip) box.addItem(strip);
	}
}
