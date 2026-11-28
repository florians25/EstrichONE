import BaseController from "../BaseController";
import { get } from "../../util/api";
import { akte } from "../../util/akteStore";
import { isoDay } from "../../util/days";
import { area, cmNumber, date, dateTime, longDay, timeRange, weatherLine } from "../../util/format";
import { bauakteStatus, binderNames, checkResults, checkTitles, inspectionResult, roomStatus } from "../../util/labels";
import type { BauakteDetail, BauakteMember, ContactResponse, FileResponse } from "../../util/models";
import { escape as e } from "../../util/orders";
import { loadCompany } from "../../util/orderDialogs";
import type { RouteMatch } from "../../util/router";
import { session } from "../../util/session";
import { waitForImages } from "../../util/printImages";

type Kind = "bericht" | "abnahme" | "blatt";

const titles: Record<Kind, string> = { bericht: "Baustellenbericht", abnahme: "Abnahmeprotokoll", blatt: "Bauaktenblatt" };

type LogoInfo = { hasLogo: boolean; updatedAt: string | null };

/**
 * Bild eines Fotos für den Druck: Vorschaustufe (aufgerichtet, ohne Metadaten), sonst das Original. HEIC zeigen die
 * meisten Browser nicht an, Videos lassen sich nicht drucken: dafür ein Hinweisfeld statt eines kaputten Bildes.
 */
function photoContent(f: FileResponse): string {
	if (f.kind === "video") return `<div class="placeholder">Video „${e(f.originalName)}“. Die Wiedergabe ist nur in der Bauakte möglich.</div>`;
	const heic = /hei[cf]/i.test(f.contentType) || /\.hei[cf]$/i.test(f.originalName);
	if (f.variants === "ready") return `<img src="/api/v1/files/${f.id}/content?variant=display" alt="${e(f.caption ?? f.originalName)}" loading="eager"/>`;
	if (heic || f.variants === "failed") {
		return `<div class="placeholder">Foto im Format HEIC oder nicht lesbar. Bitte in der Bauakte öffnen oder als JPEG hochladen.</div>`;
	}
	return `<img src="/api/v1/files/${f.id}/content?variant=original" alt="${e(f.caption ?? f.originalName)}" loading="eager"/>`;
}

/**
 * Druckansichten der Bauakte (Browser-Druck, „Als PDF speichern“): Baustellenbericht für einen Zeitraum mit
 * Bautagebuch, Prüfungen, Mängeln und Fotos; Abnahmeprotokoll zum Unterschreiben; Bauaktenblatt.
 * @namespace estrichone.controller.bauakte
 */
export default class Report extends BaseController {
	private bauakteId = "";

	onInit(): void {
		this.local({ html: "", title: "", busy: false });
	}

	async onRouteMatched(match: RouteMatch): Promise<void> {
		this.bauakteId = match.params.id;
		const kind = (["bericht", "abnahme", "blatt"].includes(match.query.get("art") ?? "") ? match.query.get("art") : "bericht") as Kind;
		const m = this.model();
		m.setProperty("/title", titles[kind]);
		m.setProperty("/html", "");
		m.setProperty("/busy", true);
		try {
			m.setProperty("/html", await this.build(kind, match.query));
			document.title = titles[kind];
		} catch (error) {
			this.showError(error);
		} finally {
			m.setProperty("/busy", false);
		}
	}

	private async build(kind: Kind, query: URLSearchParams): Promise<string> {
		const id = this.bauakteId;
		await session.people();
		const b = await get<BauakteDetail>(`bauakten/${id}`);
		const [company, logo, rooms, files, diary, inspections, work, members, customer] = await Promise.all([
			loadCompany(), get<LogoInfo>("company/logo/info").catch(() => null), akte.rooms(id), akte.files(id), akte.diary(id), akte.inspections(id),
			session.has("work.read") ? akte.work(id) : Promise.resolve([]),
			get<BauakteMember[]>(`bauakten/${id}/members`).catch(() => [] as BauakteMember[]),
			b.customerId && session.has("contacts.read") ? get<ContactResponse>(`contacts/${b.customerId}`).catch(() => null) : Promise.resolve(null),
		]);
		const from = query.get("von") || "0000-01-01";
		const to = query.get("bis") || "9999-12-31";
		const withPhotos = query.get("fotos") !== "0";
		const withWork = query.get("aufgaben") !== "0";
		const photo = (f: FileResponse) => {
			const room = rooms.rooms.find((r) => r.id === f.roomId);
			const caption = [
				`<strong>${f.kind === "video" ? "Video" : "Foto"}, ${e(dateTime(f.capturedAt ?? f.createdAt))}</strong>`,
				room ? `Raum: ${e(room.name)}${room.floor ? ` (${e(room.floor)})` : ""}` : "",
				f.uploadedByUserId ? `Aufnahme: ${e(session.name(f.uploadedByUserId))}` : "",
				f.caption ? e(f.caption) : "",
			].filter(Boolean).join("<br/>");
			return `<figure>${photoContent(f)}<figcaption>${caption}</figcaption></figure>`;
		};
		// Firmenlogo aus den Einstellungen; ohne Logo steht der Firmenname rechts als Text (kein anderes Logo).
		const logoHtml = logo?.hasLogo ? `<img class="logo" src="/api/v1/company/logo?v=${encodeURIComponent(logo.updatedAt ?? "")}" alt="${e(company?.name ?? "Firmenlogo")}"/>` : "";
		const head = `<div class="head"><div>${logoHtml}<h1>${e(titles[kind])}</h1><div><strong>${e(b.number ?? "")} ${e(b.title)}</strong></div>
<div class="muted">${e([b.street, [b.postalCode, b.city].filter(Boolean).join(" ")].filter(Boolean).join(", "))}</div></div>
<div style="text-align:right"><strong>${e(company?.name)}</strong><div class="muted">${e([company?.street, [company?.postalCode, company?.city].filter(Boolean).join(" ")].filter(Boolean).join(", "))}</div>
<div class="muted">${e([company?.phone && `Tel. ${company.phone}`, company?.email].filter(Boolean).join(", "))}</div>
<div class="muted">Stand ${e(dateTime(new Date().toISOString()))}</div></div></div>`;
		const facts = `<h2>Stammdaten</h2><div class="grid">
<div>Kunde: <strong>${e(customer?.name ?? "–")}</strong></div><div>Status: ${e(bauakteStatus[b.status]?.text)}</div>
<div>Zuständig: ${e(session.name(b.responsibleUserId))}</div><div>Ausführung: ${e(b.plannedStart || b.plannedEnd ? `${date(b.plannedStart)} bis ${date(b.plannedEnd)}` : "–")}</div>
<div>Ansprechpartner vor Ort: ${e(b.siteContactName ?? "–")} ${e(b.siteContactPhone ?? "")}</div><div>Mitarbeiter: ${e(members.map((m) => m.displayName).join(", ") || "–")}</div>
</div>${b.siteNotes ? `<p class="pre"><strong>Hinweise und Zugang:</strong> ${e(b.siteNotes)}</p>` : ""}`;
		const roomTable = `<h2>Räume und Flächen</h2><table><thead><tr><th>Raum</th><th>Geschoss</th><th class="num">Fläche</th><th>Estrich</th><th class="num">Dicke</th><th>Heizung</th><th>Status</th>${kind === "abnahme" ? "<th>Abgenommen</th>" : ""}</tr></thead><tbody>
${rooms.rooms.map((r) => `<tr><td>${e(r.name)}</td><td>${e(r.floor ?? "")}</td><td class="num">${e(area(r.areaSquareMeters))}</td><td>${e(r.screedTypeName ?? "–")}</td><td class="num">${r.thicknessMillimeters ?? "–"} mm</td><td>${r.underfloorHeating ? "ja" : "nein"}</td><td>${e(roomStatus[r.status]?.text)}</td>${kind === "abnahme" ? "<td>☐ ja ☐ nein</td>" : ""}</tr>`).join("") || '<tr><td colspan="7">Keine Räume erfasst.</td></tr>'}
</tbody><tfoot><tr><td colspan="2">Summe</td><td class="num">${e(area(rooms.totalAreaSquareMeters))}</td><td colspan="${kind === "abnahme" ? 5 : 4}">davon beheizt ${e(area(rooms.heatedAreaSquareMeters))}</td></tr></tfoot></table>`;
		const cm = inspections.filter((i) => i.kind === "cmMeasurement").sort((x, y) => y.performedAt.localeCompare(x.performedAt));
		const initial = inspections.filter((i) => i.kind === "initialInspection").sort((x, y) => y.performedAt.localeCompare(x.performedAt));
		const checks = `<h2>Prüfungen</h2>${initial[0] ? `<p><strong>Erstbegehung</strong> am ${e(dateTime(initial[0].performedAt))} durch ${e(session.name(initial[0].performedByUserId))}: ${e(inspectionResult(initial[0]).text)}</p>
<table><tbody>${Object.entries(checkTitles).map(([k, t]) => `<tr><td>${e(t)}</td><td>${e(initial[0].checks[k] ? checkResults[initial[0].checks[k]]?.text : "–")}</td></tr>`).join("")}</tbody></table>
${initial[0].remarks ? `<p class="pre">Bemerkungen: ${e(initial[0].remarks)}</p>` : ""}${initial[0].notes ? `<p class="pre"><strong>Notizen:</strong> ${e(initial[0].notes)}</p>` : ""}` : "<p>Erstbegehung noch nicht ausgeführt.</p>"}
${cm.length ? `<table><thead><tr><th>Datum</th><th>Messstelle</th><th>Estrich</th><th class="num">Messwert</th><th class="num">Grenzwert</th><th>Ergebnis</th></tr></thead><tbody>
${cm.map((i) => `<tr><td>${e(dateTime(i.performedAt))}</td><td>${e(i.location)}</td><td>${e(i.binder ? binderNames[i.binder] : "–")}${i.heated ? ", beheizt" : ""}</td><td class="num">${cmNumber(i.measuredCmPercent)} CM-%</td><td class="num">${cmNumber(i.limitCmPercent)} CM-%</td><td>${i.isReadyForCovering ? "Belegreif" : "Noch nicht belegreif"}</td></tr>`).join("")}</tbody></table>` : "<p>Keine CM-Messungen.</p>"}`;
		const openWork = work.filter((w) => w.status !== "done");
		const workTable = (items: typeof work, title: string) => `<h2>${e(title)}</h2>${items.length ? `<table><thead><tr><th>Art</th><th>Beschreibung</th><th>Fällig</th><th>Zuständig</th><th>Status</th></tr></thead><tbody>
${items.map((w) => `<tr><td>${w.kind === "defect" ? "Mangel" : "Aufgabe"}</td><td>${e(w.title)}</td><td>${e(date(w.dueDate))}</td><td>${e(session.name(w.assigneeUserId))}</td><td>${w.status === "done" ? "erledigt" : w.status === "inProgress" ? "in Arbeit" : "offen"}</td></tr>`).join("")}</tbody></table>` : "<p>Keine.</p>"}`;
		const photos = files.filter((f) => (f.kind === "photo" || f.kind === "video") && f.state === "stored");

		if (kind === "blatt") {
			return `<div class="eoSheet">${head}${facts}${b.description ? `<h2>Beschreibung</h2><p class="pre">${e(b.description)}</p>` : ""}${roomTable}${checks}${withWork ? workTable(openWork, "Offene Aufgaben und Mängel") : ""}</div>`;
		}
		if (kind === "abnahme") {
			const items = ["Oberfläche eben, ohne Risse und Ausbrüche", "Estrichdicke laut Aufbau eingehalten", "Randdämmstreifen vorhanden, Überstand nicht abgeschnitten",
				"Bewegungs- und Scheinfugen nach Plan", "Heizestrich: Aufheizprotokoll übergeben", "Baustelle gereinigt übergeben"];
			return `<div class="eoSheet">${head}${facts}${roomTable}
<h2>Prüfpunkte bei der Abnahme</h2><table><tbody>${items.map((i) => `<tr><td>${e(i)}</td><td>☐ in Ordnung ☐ Mangel ☐ entfällt</td></tr>`).join("")}</tbody></table>
${withWork ? workTable(openWork.filter((w) => w.kind === "defect"), "Bekannte offene Mängel") : ""}
<h2>Festgestellte Mängel / Vorbehalte</h2><div style="height:6rem;border:1px solid #ccc"></div>
<p>Die Leistung wird ☐ ohne Vorbehalt ☐ mit den oben genannten Vorbehalten abgenommen.</p>
<div class="sign"><div>Ort, Datum, Unterschrift Auftraggeber</div><div>Ort, Datum, Unterschrift Auftragnehmer (${e(company?.name ?? "")})</div></div>
<p class="muted" style="margin-top:1.5rem">Das unterschriebene Protokoll unter „Dokumente › Abnahme“ in der Bauakte ablegen.</p></div>`;
		}
		const days = diary.filter((d) => d.day >= from && d.day <= to).sort((x, y) => x.day.localeCompare(y.day));
		const periodPhotos = photos.filter((p) => {
			const d = isoDay(new Date(p.capturedAt ?? p.createdAt));
			return d >= from && d <= to;
		});
		const period = query.get("von") || query.get("bis") ? `Zeitraum ${date(query.get("von"))} bis ${date(query.get("bis"))}` : "Gesamter Zeitraum";
		const diaryHtml = `<h2>Bautagebuch (${days.length} Einträge)</h2>${days.map((d) => {
			const linked = (d.photoFileIds ?? []).map((pid) => photos.find((p) => p.id === pid)).filter((p): p is FileResponse => !!p);
			return `<div class="entry"><strong>${e(longDay(d.day))}</strong> <span class="muted">${e([weatherLine(d), timeRange(d.startTime, d.endTime), d.crew].filter(Boolean).join(", "))}</span>
<div class="pre">${e(d.text)}</div><div class="muted">${e(session.name(d.authorUserId))}</div>${withPhotos && linked.length ? `<div class="photos">${linked.map(photo).join("")}</div>` : ""}</div>`;
		}).join("") || "<p>Keine Einträge im Zeitraum.</p>"}`;
		return `<div class="eoSheet">${head}<p><strong>${e(period)}</strong></p>${facts}${roomTable}${diaryHtml}${checks}
${withWork ? workTable(work, "Aufgaben und Mängel") : ""}
${withPhotos ? `<h2>Fotos im Zeitraum (${periodPhotos.length})</h2><div class="photos">${periodPhotos.slice(0, 60).map(photo).join("")}</div>${periodPhotos.length > 60 ? `<p class="muted">… und ${periodPhotos.length - 60} weitere.</p>` : ""}` : ""}
<p class="muted" style="margin-top:1.5rem">Erstellt am ${e(new Date().toLocaleDateString("de-DE", { day: "2-digit", month: "2-digit", year: "numeric" }))}.</p></div>`;
	}

	/** Erst drucken, wenn Firmenlogo und alle Fotos geladen und dekodiert sind (sonst fehlen sie im PDF). */
	async onPrint(): Promise<void> {
		const m = this.model();
		m.setProperty("/busy", true);
		try {
			const missing = await waitForImages(Array.from(document.querySelectorAll<HTMLImageElement>(".eoSheet img")));
			if (missing > 0) this.toast(`${missing} Bild(er) konnten nicht geladen werden und fehlen im Ausdruck.`);
		} finally {
			m.setProperty("/busy", false);
		}
		window.print();
	}

	onBack(): void {
		this.nav(`/bauakten/${this.bauakteId}?abschnitt=dokumentation`);
	}
}
