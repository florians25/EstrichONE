/**
 * Ziehen mit Zeigerereignissen (Maus, Stift, Touch) – gemeinsamer Kern für Plantafel, „Wer fährt wohin“ und Listen.
 * (Chromium startet in UI5-Seitencontainern natives HTML5-Ziehen nicht zuverlässig.)
 * - kein Markieren von Text: Zieh-Elemente mit user-select/touch-action none, Markierung beim Start aufheben,
 *   während des Ziehens body.is-dragging (Cursor „grabbing“);
 * - Schwelle von 5 px, halbtransparenter Geist mit Karteninhalt, hervorgehobene Ablagezone,
 *   Auto-Scroll am Rand des Fensters bzw. des scrollbaren Bereichs, Escape bricht ab, kein Klick nach dem Ziehen.
 * Quellen für {@link enablePointerDrag} tragen `data-dragkind` und `data-dragid` (optional `data-dragfrom`), Ziele `data-dropsite`.
 */
export interface DragSource { kind: string; id: string; from: string | null; label: string }

export interface TrackOptions {
	/** Ablagezonen (CSS-Selektor). */
	target: string;
	/** Klasse der hervorgehobenen Ablagezone. */
	overClass: string;
	/** Klasse der Quelle während des Ziehens. */
	draggingClass?: string;
	onDrop: (target: HTMLElement, event: PointerEvent) => void;
}

const THRESHOLD = 5;
const EDGE = 48;
/** Bedienelemente in einer Karte: starten kein Ziehen. Links dürfen gezogen werden (Klick bleibt Klick). */
const INTERACTIVE = "input, textarea, select, button, [role='button'], .sapMBtn, .sapMTokenIcon, [data-nodrag]";

let lastDragEnd = 0;
/** Kurz nach einem Ziehen keinen Klick werten (z. B. Karte öffnen). */
export const justDragged = (): boolean => Date.now() - lastDragEnd < 300;

/** Erster scrollbarer Vorfahre (für Auto-Scroll), sonst das Dokument. */
function scroller(el: HTMLElement | null): HTMLElement {
	for (let e = el; e && e !== document.body; e = e.parentElement) {
		const style = getComputedStyle(e);
		if (/(auto|scroll)/.test(style.overflowY + style.overflowX) && (e.scrollHeight > e.clientHeight || e.scrollWidth > e.clientWidth)) return e;
	}
	return document.scrollingElement as HTMLElement;
}

/**
 * Ziehen einer Quelle verfolgen. Aufruf im pointerdown. Interaktive Elemente IN der Quelle (Links, Knöpfe, Felder)
 * starten kein Ziehen und behalten ihr normales Verhalten.
 */
export function trackDrag(event: PointerEvent, source: HTMLElement, options: TrackOptions): void {
	if (event.button !== 0) return;
	const interactive = (event.target as HTMLElement).closest<HTMLElement>(INTERACTIVE);
	if (interactive && interactive !== source && source.contains(interactive)) return;
	// Kein Markieren beginnen (Maus); Links behalten Fokus und Klick, Touch/Stift ihr Verhalten bis zur Schwelle.
	if (event.pointerType === "mouse" && !(event.target as HTMLElement).closest("a")) event.preventDefault();
	const startX = event.clientX;
	const startY = event.clientY;
	let ghost: HTMLElement | null = null;
	let over: HTMLElement | null = null;
	let last: PointerEvent = event;
	let frame = 0;
	const targetAt = (x: number, y: number) => {
		ghost?.style.setProperty("display", "none");
		const hit = document.elementFromPoint(x, y) as HTMLElement | null;
		ghost?.style.setProperty("display", "");
		return hit?.closest<HTMLElement>(options.target) ?? null;
	};
	const highlight = (x: number, y: number) => {
		const target = targetAt(x, y);
		if (target !== over) {
			over?.classList.remove(options.overClass);
			target?.classList.add(options.overClass);
			over = target;
		}
	};
	// Auto-Scroll, solange der Zeiger am Rand steht.
	const autoScroll = () => {
		frame = 0;
		if (!ghost) return;
		const { clientX: x, clientY: y } = last;
		const box = scroller(targetAt(x, y) ?? source);
		const rect = box === document.scrollingElement ? new DOMRect(0, 0, window.innerWidth, window.innerHeight) : box.getBoundingClientRect();
		const speed = (d: number) => Math.ceil((EDGE - d) / 4);
		let dx = 0;
		let dy = 0;
		if (y - rect.top < EDGE) dy = -speed(y - rect.top);
		else if (rect.bottom - y < EDGE) dy = speed(rect.bottom - y);
		if (x - rect.left < EDGE) dx = -speed(x - rect.left);
		else if (rect.right - x < EDGE) dx = speed(rect.right - x);
		if (dx || dy) {
			box.scrollBy(dx, dy);
			highlight(x, y);
			frame = requestAnimationFrame(autoScroll);
		}
	};
	const start = () => {
		window.getSelection()?.removeAllRanges();
		document.body.classList.add("is-dragging");
		ghost = source.cloneNode(true) as HTMLElement;
		ghost.removeAttribute("id");
		for (const el of ghost.querySelectorAll("[id]")) el.removeAttribute("id");
		ghost.classList.add("eoDragGhostCard");
		const r = source.getBoundingClientRect();
		ghost.style.width = `${Math.min(r.width, 280)}px`;
		ghost.setAttribute("aria-hidden", "true");
		document.body.append(ghost);
		if (options.draggingClass) source.classList.add(options.draggingClass);
	};
	const move = (e: PointerEvent) => {
		last = e;
		if (!ghost) {
			if (Math.abs(e.clientX - startX) + Math.abs(e.clientY - startY) < THRESHOLD) return;
			start();
		}
		ghost!.style.left = `${e.clientX + 12}px`;
		ghost!.style.top = `${e.clientY + 12}px`;
		highlight(e.clientX, e.clientY);
		if (!frame) frame = requestAnimationFrame(autoScroll);
		e.preventDefault();
	};
	const cleanup = () => {
		window.removeEventListener("pointermove", move, true);
		window.removeEventListener("pointerup", end, true);
		window.removeEventListener("pointercancel", end, true);
		window.removeEventListener("keydown", key, true);
		if (frame) cancelAnimationFrame(frame);
		document.body.classList.remove("is-dragging");
		if (!ghost) return false;
		ghost.remove();
		ghost = null;
		if (options.draggingClass) source.classList.remove(options.draggingClass);
		over?.classList.remove(options.overClass);
		lastDragEnd = Date.now();
		// Klick nach dem Ziehen nicht als Klick werten.
		const swallow = (c: MouseEvent) => { c.stopPropagation(); c.preventDefault(); };
		window.addEventListener("click", swallow, { capture: true, once: true });
		setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 300);
		return true;
	};
	const end = (e: PointerEvent) => {
		const target = e.type === "pointerup" && ghost ? targetAt(e.clientX, e.clientY) : null;
		if (cleanup() && target) options.onDrop(target, e);
	};
	const key = (e: KeyboardEvent) => {
		if (e.key === "Escape" && ghost) {
			e.preventDefault();
			e.stopPropagation();
			cleanup();
		}
	};
	window.addEventListener("pointermove", move, true);
	window.addEventListener("pointerup", end, true);
	window.addEventListener("pointercancel", end, true);
	window.addEventListener("keydown", key, true);
}

/** Ziehen für Karten und Listen unter `root` (Quellen data-dragkind, Ziele data-dropsite). */
export function enablePointerDrag(root: HTMLElement, onDrop: (source: DragSource, target: string) => void, enabled: () => boolean): void {
	if (root.dataset.pointerDrag) return;
	root.dataset.pointerDrag = "1";
	root.addEventListener("pointerdown", (event) => {
		if (!enabled()) return;
		const handle = (event.target as HTMLElement).closest<HTMLElement>("[data-dragkind]");
		if (!handle) return;
		const source: DragSource = {
			kind: handle.dataset.dragkind ?? "", id: handle.dataset.dragid ?? "", from: handle.dataset.dragfrom || null,
			label: handle.textContent?.trim() ?? "",
		};
		trackDrag(event, handle, {
			target: "[data-dropsite]", overClass: "eoDropOver", draggingClass: "eoDragging",
			onDrop: (target) => { if (target.dataset.dropsite) onDrop(source, target.dataset.dropsite); },
		});
	});
}
