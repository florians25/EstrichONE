/**
 * Kurzmeldungen („Gespeichert“, „Kopiert“ …) als kleine Karten oben rechts unter der Kopfzeile statt sap.m.MessageToast
 * unten mittig: Symbol und Farbakzent je Art (Erfolg, Info, Warnung, Fehler), mehrere Meldungen untereinander gestapelt,
 * blenden nach einigen Sekunden aus (pausiert bei Zeiger oder Fokus), Klick schließt, optional eine Aktion
 * („Rückgängig“, „Öffnen“). Für Screenreader in einer aria-live-Region. Reines DOM ohne UI5 (CSP-tauglich, Farben über
 * Klassen, Lage über CSSOM) – Fehler mit Rückfrage bleiben Dialoge.
 */

export type ToastType = "success" | "info" | "warning" | "error";

export interface ToastOptions {
	/** Art der Meldung; ohne Angabe aus dem Text abgeleitet (siehe toastType). */
	type?: ToastType;
	/** Schaltfläche in der Meldung, z. B. „Rückgängig“; schließt die Meldung nach dem Auslösen. */
	action?: { text: string; press: () => void };
	/** Anzeigedauer in ms; ohne Angabe nach Textlänge (3,5 bis 8 s). */
	duration?: number;
}

/** Höchstens so viele Meldungen gleichzeitig; ältere weichen. */
export const MAX_TOASTS = 4;

const WARNING = new RegExp(
	[
		"^Bitte ", "nicht möglich", "nicht verfügbar", "nicht gefunden", "nicht unterstützt", "nicht freigegeben", "dürfen Sie nicht",
		"nicht (mehr )?vorhanden", "nicht verschoben werden", "abgelaufen", "unterbrochen", "^Das ist keine", "^Nur eigene", "noch keine",
		"Konflikt", "Hinweis:", "Sie wurden entfernt", "braucht (der|ein) Einsatz", "^Keine weiteren", "^Ohne ", "fehlgeschlagen",
	].join("|"),
	"i",
);
const ERROR = /^Fehler|konnte nicht|nicht gespeichert|nicht erreichbar/i;
const INFO = /…$|\.\.\.$|Strg\+Z|^Rückgängig:|^Wiederholt:|wird erstellt|^Einsätze plant|^Benutzer können|bereits|^An diesem Tag ist nichts|in wenigen Sekunden|eingereiht/i;

/** Art einer Meldung aus ihrem Text: Hinweise auf Unmögliches als Warnung, laufende Vorgänge als Info, sonst Erfolg. */
export function toastType(text: string): ToastType {
	if (ERROR.test(text)) return "error";
	if (WARNING.test(text)) return "warning";
	if (INFO.test(text)) return "info";
	return "success";
}

/** Anzeigedauer: 3,5 s für kurze Meldungen, länger für lange Texte (rund 15 Zeichen je Sekunde), höchstens 8 s. */
export function toastDuration(text: string, type: ToastType = "success"): number {
	const base = type === "error" || type === "warning" ? 5000 : 3500;
	return Math.min(8000, Math.max(base, 1500 + text.length * 65));
}

// Symbole als SVG (24er-Raster), Farbe über currentColor.
const ICONS: Record<ToastType, string> = {
	success: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm-1.2 14.2-4-4 1.4-1.4 2.6 2.6 5.6-5.6 1.4 1.4-7 7Z",
	info: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm1 15h-2v-6h2v6Zm0-8h-2V7h2v2Z",
	warning: "M12 2.5 1.5 21h21L12 2.5ZM13 18h-2v-2h2v2Zm0-4h-2V9h2v5Z",
	error: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm4.3 12.9-1.4 1.4L12 13.4l-2.9 2.9-1.4-1.4 2.9-2.9-2.9-2.9 1.4-1.4 2.9 2.9 2.9-2.9 1.4 1.4-2.9 2.9 2.9 2.9Z",
};
const LABELS: Record<ToastType, string> = { success: "Erfolg", info: "Hinweis", warning: "Warnung", error: "Fehler" };
const SVG = "http://www.w3.org/2000/svg";

function icon(type: ToastType): SVGSVGElement {
	const svg = document.createElementNS(SVG, "svg");
	svg.setAttribute("viewBox", "0 0 24 24");
	svg.setAttribute("aria-hidden", "true");
	svg.setAttribute("class", "eoToastIcon");
	const path = document.createElementNS(SVG, "path");
	path.setAttribute("d", ICONS[type]);
	path.setAttribute("fill", "currentColor");
	svg.appendChild(path);
	return svg;
}

/** Abstand unter der Kopfzeile (ShellBar) bzw. oben am Fenster, wenn keine da ist (Anmeldeseite). */
function topOffset(): number {
	const header = [...document.querySelectorAll<HTMLElement>(".sapTntToolPageHeaderWrapper, .sapFShellBar")]
		.find((e) => e.getBoundingClientRect().height > 0);
	return Math.max(0, Math.round(header?.getBoundingClientRect().bottom ?? 0));
}

/** Automatisierter Browser (Playwright): Meldungen lassen Klicks durch und pausieren nicht, sonst hielte der Zeiger eines
 *  Tests über einer Schaltfläche oben rechts die Meldung dauerhaft offen und verdeckte die Schaltfläche. */
function automated(): boolean {
	return typeof navigator !== "undefined" && navigator.webdriver === true;
}

function region(): HTMLElement {
	let host = document.getElementById("eoToasts");
	if (!host) {
		host = document.createElement("div");
		host.id = "eoToasts";
		host.className = automated() ? "eoToasts eoToastsPassive" : "eoToasts";
		host.setAttribute("aria-live", "polite");
		host.setAttribute("aria-relevant", "additions");
		document.body.appendChild(host);
	}
	host.style.top = `${topOffset()}px`;
	return host;
}

function dismiss(toast: HTMLElement): void {
	if (toast.dataset["closing"]) return;
	toast.dataset["closing"] = "1";
	window.clearTimeout(Number(toast.dataset["timer"] ?? 0));
	toast.classList.add("eoToastLeave");
	const remove = () => toast.remove();
	const reduced = typeof window.matchMedia === "function" && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
	if (reduced) remove();
	else window.setTimeout(remove, 180);
}

/** Zeigt eine Kurzmeldung; gibt das Element zurück (zum vorzeitigen Schließen mit closeToast). */
export function showToast(text: string, options: ToastOptions = {}): HTMLElement {
	const type = options.type ?? toastType(text);
	const host = region();
	const toast = document.createElement("div");
	toast.className = `eoToast eoToast-${type}`;
	toast.setAttribute("role", type === "error" || type === "warning" ? "alert" : "status");
	toast.appendChild(icon(type));
	const hidden = document.createElement("span");
	hidden.className = "eoToastSr";
	hidden.textContent = `${LABELS[type]}: `;
	const body = document.createElement("div");
	body.className = "eoToastText";
	body.textContent = text;
	toast.append(hidden, body);
	if (options.action) {
		const action = options.action;
		const button = document.createElement("button");
		button.type = "button";
		button.className = "eoToastAction";
		button.textContent = action.text;
		button.addEventListener("click", (event) => {
			event.stopPropagation();
			dismiss(toast);
			action.press();
		});
		toast.appendChild(button);
	}
	const close = document.createElement("button");
	close.type = "button";
	close.className = "eoToastClose";
	close.setAttribute("aria-label", "Meldung schließen");
	close.title = "Schließen";
	close.textContent = "×";
	toast.appendChild(close);
	toast.addEventListener("click", () => dismiss(toast));

	const duration = options.duration ?? toastDuration(text, type);
	const start = () => {
		window.clearTimeout(Number(toast.dataset["timer"] ?? 0));
		toast.dataset["timer"] = String(window.setTimeout(() => dismiss(toast), duration));
	};
	const pause = () => window.clearTimeout(Number(toast.dataset["timer"] ?? 0));
	if (!automated()) {
		toast.addEventListener("mouseenter", pause);
		toast.addEventListener("mouseleave", start);
		toast.addEventListener("focusin", pause);
		toast.addEventListener("focusout", start);
	}

	host.appendChild(toast);
	const open = [...host.querySelectorAll<HTMLElement>(".eoToast:not(.eoToastLeave)")];
	for (const old of open.slice(0, Math.max(0, open.length - MAX_TOASTS))) dismiss(old);
	start();
	return toast;
}

export function closeToast(toast: HTMLElement): void {
	dismiss(toast);
}
