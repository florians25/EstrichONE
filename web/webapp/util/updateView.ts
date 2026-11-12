/**
 * Update-Ablauf (deploy/README.md, „Update-Ablauf“): Aufbereitung des Stands aus GET /api/v1/system/notices (Feld „update“)
 * bzw. /update-status.json (Caddy, auch ohne API) für Hinweisleiste und Wartungsbildschirm – gleiche Texte wie die Apps
 * (UpdatePresentation in EstrichCore bzw. core/UpdateMaintenance.kt).
 *   announce – Leiste „Update auf Version X beginnt in 0:59 – bitte Arbeit speichern“, danach Wartungsbildschirm
 *   update   – Wartungsbildschirm „Update wird eingespielt“ (Sitzungen beendet), auch ohne erreichbare API
 *   finish   – „Update wird abgeschlossen … Freigabe in 0:59“
 *   rollback / failed – Rücknahme bzw. Fehlschlag mit Meldung
 */
export type UpdatePhase = "announce" | "update" | "finish" | "rollback" | "failed";

export interface UpdateState {
	id: string;
	phase: UpdatePhase;
	fromVersion?: string | null;
	targetVersion?: string | null;
	startedAt?: string | null;
	phaseStartedAt?: string | null;
	phaseEndsAt?: string | null;
	message?: string | null;
}

export interface UpdateStep {
	label: string;
	state: "done" | "now" | "todo";
}

export interface UpdateView {
	/** Leiste über dem Inhalt (Ankündigung, solange der Countdown läuft). */
	banner: boolean;
	bannerText: string;
	/** Wartungsbildschirm (Update, Abschluss, Rücknahme, Fehlschlag – und nach Ablauf der Ankündigung). */
	page: boolean;
	title: string;
	text: string;
	/** „0:59“ bzw. leer, wenn kein Ende bekannt ist. */
	countdown: string;
	countdownLabel: string;
	/** 0–100: Fortschritt der aktuellen Phase. */
	percent: number;
	steps: UpdateStep[];
	failed: boolean;
	version: string;
}

const PHASES: UpdatePhase[] = ["announce", "update", "finish", "rollback", "failed"];

export function isUpdateState(value: unknown): value is UpdateState {
	const v = value as UpdateState | null;
	return !!v && typeof v === "object" && typeof v.id === "string" && PHASES.includes(v.phase);
}

/** Sekunden als „m:ss“ (aufgerundet, nie negativ). */
export function clock(seconds: number): string {
	const s = Math.max(0, Math.ceil(seconds));
	return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

/** Sekunden bis zum Ende der Phase (null ohne Ende). */
export function secondsLeft(state: UpdateState, now: Date): number | null {
	const end = state.phaseEndsAt ? Date.parse(state.phaseEndsAt) : NaN;
	return Number.isNaN(end) ? null : (end - now.getTime()) / 1000;
}

// Nicht „version“ in Vorlagen-Platzhaltern nennen: der UI5-Build ersetzt diesen Platzhalter durch die App-Version.
export function updateView(state: UpdateState, now: Date): UpdateView {
	const target = state.targetVersion ? `Version ${state.targetVersion}` : "die neue Version";
	const left = secondsLeft(state, now);
	const start = state.phaseStartedAt ? Date.parse(state.phaseStartedAt) : NaN;
	const end = state.phaseEndsAt ? Date.parse(state.phaseEndsAt) : NaN;
	const percent = !Number.isNaN(start) && !Number.isNaN(end) && end > start
		? Math.round(Math.min(100, Math.max(0, ((now.getTime() - start) / (end - start)) * 100)))
		: state.phase === "update" || state.phase === "rollback" ? 100 : 0;
	const running = left !== null && left > 0;
	// Ankündigung abgelaufen: Bildschirm schon zeigen, bevor der Server die Update-Phase meldet.
	const announcing = state.phase === "announce" && (left === null || left > 0);
	const index = state.phase === "announce" ? 0 : state.phase === "update" ? 1 : 2;
	const third = state.phase === "rollback" ? "Rücknahme" : state.phase === "failed" ? "Fehlgeschlagen" : "Abschluss";
	const steps: UpdateStep[] = ["Ankündigung", "Update", third].map((label, i) => ({
		label, state: i < index ? "done" : i === index ? "now" : "todo",
	}));
	const base = { version: state.targetVersion ?? "", steps, percent, failed: state.phase === "failed" };
	if (announcing) {
		const text = running
			? `Update auf ${target} beginnt in ${clock(left)} – bitte Arbeit speichern. Danach ist eine neue Anmeldung nötig.`
			: `Update auf ${target} beginnt gleich – bitte Arbeit speichern.`;
		return { ...base, banner: true, bannerText: text, page: false, title: "Update angekündigt", text,
			countdown: running ? clock(left) : "", countdownLabel: "Beginnt in" };
	}
	switch (state.phase) {
		case "announce":
		case "update":
			return { ...base, banner: false, bannerText: "", page: true, title: "Update wird eingespielt",
				text: `EstrichONE wird auf ${target} aktualisiert. Alle Sitzungen wurden beendet; Ihre gespeicherten Daten sind sicher.`,
				countdown: running ? clock(left) : "", countdownLabel: running ? "Mindestens noch" : "Läuft …",
				steps: state.phase === "announce" ? steps.map((s, i) => ({ ...s, state: i === 0 ? "done" : i === 1 ? "now" : "todo" })) : steps };
		case "finish":
			return { ...base, banner: false, bannerText: "", page: true, title: "Update wird abgeschlossen",
				text: `${target} läuft. Letzte Prüfungen – danach wird EstrichONE freigegeben und Sie können sich neu anmelden.`,
				countdown: running ? clock(left) : "", countdownLabel: running ? "Freigabe in" : "Gleich geschafft …" };
		case "rollback":
			return { ...base, banner: false, bannerText: "", page: true, title: "Update wird zurückgenommen",
				text: `Das Update auf ${target} hat nicht geklappt. Der bisherige Stand wird wiederhergestellt.`,
				countdown: "", countdownLabel: "Läuft …" };
		default:
			return { ...base, banner: false, bannerText: "", page: true, title: "Update fehlgeschlagen",
				text: `${state.message || `Das Update auf ${target} konnte nicht eingespielt werden.`} Der Administrator ist informiert.`,
				countdown: running ? clock(left) : "", countdownLabel: running ? "Freigabe in" : "Gesperrt, bis das Problem behoben ist" };
	}
}

/** Nach der Freigabe einmalig auf der Anmeldeseite zeigen (sessionStorage überdauert das Neuladen). */
export const doneKey = "eo.update.done";

export function doneText(target: string | null | undefined, failed: boolean): string {
	if (failed) return "Das Update konnte nicht eingespielt werden – EstrichONE läuft mit dem bisherigen Stand weiter. Bitte erneut anmelden.";
	return target
		? `EstrichONE wurde auf Version ${target} aktualisiert. Bitte erneut anmelden.`
		: "EstrichONE wurde aktualisiert. Bitte erneut anmelden.";
}
