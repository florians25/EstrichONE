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
const reduced = () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;

/** Schlüssel eines verschiebbaren Elements für die Gleit-Animation (FLIP) über ein Neuzeichnen hinweg. */
const flipKey = (el: HTMLElement) => el.dataset.flipKey ?? (el.dataset.dragkind ? `${el.dataset.dragkind}:${el.dataset.dragid}:${el.dataset.dragfrom ?? ""}` : "");

/**
 * Nach dem Ablegen: Positionen aller Karten merken und nach dem Neuzeichnen von dort an die neue Stelle gleiten lassen.
 * Erkennt das Neuzeichnen über einen MutationObserver (höchstens 1,5 s).
 */
function glideAfterRender(): void {
	if (reduced()) return;
	const selector = "[data-flip-key], [data-dragkind]";
	const before = new Map<string, DOMRect>();
	for (const el of document.querySelectorAll<HTMLElement>(selector)) {
		const key = flipKey(el);
		if (key) before.set(key, el.getBoundingClientRect());
	}
	let timer = 0;
	const play = () => {
		observer.disconnect();
		for (const el of document.querySelectorAll<HTMLElement>(selector)) {
			const old = before.get(flipKey(el));
			if (!old) continue;
			const now = el.getBoundingClientRect();
			const dx = old.left - now.left;
			const dy = old.top - now.top;
			if (Math.abs(dx) < 2 && Math.abs(dy) < 2) continue;
			el.animate([{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "translate(0, 0)" }], { duration: 220, easing: "cubic-bezier(0.2, 0, 0, 1)" });
		}
	};
	const observer = new MutationObserver(() => {
		clearTimeout(timer);
		timer = window.setTimeout(play, 40);
	});
	observer.observe(document.body, { childList: true, subtree: true });
	window.setTimeout(() => observer.disconnect(), 1500);
}

/** Geist zu einem Rechteck gleiten lassen (Einrasten bzw. Rückfedern) und danach entfernen. */
function settle(ghost: HTMLElement, to: DOMRect | null, done: () => void): void {
	if (!to || reduced()) {
		done();
		return;
	}
	const from = ghost.getBoundingClientRect();
	const animation = ghost.animate([
		{ left: `${from.left}px`, top: `${from.top}px`, opacity: 0.9, transform: "rotate(-1.5deg) scale(1.03)" },
		{ left: `${to.left + (to.width - from.width) / 2}px`, top: `${to.top + Math.min(8, Math.max(0, (to.height - from.height) / 2))}px`, opacity: 0.2, transform: "rotate(0) scale(0.96)" },
	], { duration: 180, easing: "cubic-bezier(0.2, 0, 0, 1)", fill: "forwards" });
	animation.onfinish = done;
	animation.oncancel = done;
}
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
	// Geist folgt weich (Annäherung je Bild) statt ruckartig.
	let gx = event.clientX;
	let gy = event.clientY;
	let follow = 0;
	const smooth = () => {
		follow = 0;
		if (!ghost) return;
		const tx = last.clientX + 12;
		const ty = last.clientY + 12;
		gx += (tx - gx) * (reduced() ? 1 : 0.45);
		gy += (ty - gy) * (reduced() ? 1 : 0.45);
		ghost.style.left = `${gx}px`;
		ghost.style.top = `${gy}px`;
		if (Math.abs(tx - gx) > 0.5 || Math.abs(ty - gy) > 0.5) follow = requestAnimationFrame(smooth);
	};
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
		gx = r.left;
		gy = r.top;
		ghost.style.left = `${gx}px`;
		ghost.style.top = `${gy}px`;
		document.body.append(ghost);
		if (!reduced()) ghost.animate([{ transform: "rotate(0) scale(1)", opacity: 1 }, { transform: "rotate(-1.5deg) scale(1.03)", opacity: 0.9 }], { duration: 140, easing: "ease-out" });
		if (options.draggingClass) source.classList.add(options.draggingClass);
	};
	const move = (e: PointerEvent) => {
		last = e;
		if (!ghost) {
			if (Math.abs(e.clientX - startX) + Math.abs(e.clientY - startY) < THRESHOLD) return;
			start();
		}
		if (!follow) follow = requestAnimationFrame(smooth);
		highlight(e.clientX, e.clientY);
		if (!frame) frame = requestAnimationFrame(autoScroll);
		e.preventDefault();
	};
	/** Aufräumen; der Geist rastet in das Ziel ein bzw. federt zur Quelle zurück. */
	const cleanup = (to: HTMLElement | null) => {
		window.removeEventListener("pointermove", move, true);
		window.removeEventListener("pointerup", end, true);
		window.removeEventListener("pointercancel", end, true);
		window.removeEventListener("keydown", key, true);
		if (frame) cancelAnimationFrame(frame);
		if (follow) cancelAnimationFrame(follow);
		document.body.classList.remove("is-dragging");
		if (!ghost) return false;
		const g = ghost;
		ghost = null;
		settle(g, (to ?? source).getBoundingClientRect(), () => {
			g.remove();
			if (options.draggingClass) source.classList.remove(options.draggingClass);
		});
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
		if (cleanup(target) && target) {
			glideAfterRender();
			options.onDrop(target, e);
		}
	};
	const key = (e: KeyboardEvent) => {
		if (e.key === "Escape" && ghost) {
			e.preventDefault();
			e.stopPropagation();
			cleanup(null);
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
