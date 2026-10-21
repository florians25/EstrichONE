/**
 * Ziehen mit Zeigerereignissen (Maus, Stift, Touch) für Karten und Listen außerhalb der Plantafel – wie dort, weil
 * Chromium in UI5-Seitencontainern natives HTML5-Ziehen nicht zuverlässig startet. Quellen tragen `data-dragkind`
 * und `data-dragid` (optional `data-dragfrom`), Ziele `data-dropsite`.
 */
export interface DragSource { kind: string; id: string; from: string | null; label: string }

export function enablePointerDrag(root: HTMLElement, onDrop: (source: DragSource, target: string) => void, enabled: () => boolean): void {
	if (root.dataset.pointerDrag) return;
	root.dataset.pointerDrag = "1";
	root.addEventListener("pointerdown", (event) => {
		if (event.button !== 0 || !enabled()) return;
		const handle = (event.target as HTMLElement).closest<HTMLElement>("[data-dragkind]");
		if (!handle || (event.target as HTMLElement).closest("a, input, textarea, .sapMTokenIcon, [data-nodrag]")) return;
		const source: DragSource = {
			kind: handle.dataset.dragkind ?? "", id: handle.dataset.dragid ?? "", from: handle.dataset.dragfrom || null,
			label: handle.textContent?.trim() ?? "",
		};
		const startX = event.clientX;
		const startY = event.clientY;
		let ghost: HTMLElement | null = null;
		let over: HTMLElement | null = null;
		const targetAt = (x: number, y: number) => {
			ghost?.style.setProperty("display", "none");
			const hit = document.elementFromPoint(x, y) as HTMLElement | null;
			ghost?.style.setProperty("display", "");
			return hit?.closest<HTMLElement>("[data-dropsite]") ?? null;
		};
		const move = (e: PointerEvent) => {
			if (!ghost) {
				if (Math.abs(e.clientX - startX) + Math.abs(e.clientY - startY) < 6) return;
				ghost = document.createElement("div");
				ghost.className = "eoDragGhost";
				ghost.textContent = source.label;
				document.body.append(ghost);
				handle.classList.add("eoDragging");
			}
			ghost.style.left = `${e.clientX + 10}px`;
			ghost.style.top = `${e.clientY + 10}px`;
			const target = targetAt(e.clientX, e.clientY);
			if (target !== over) {
				over?.classList.remove("eoDropOver");
				target?.classList.add("eoDropOver");
				over = target;
			}
			e.preventDefault();
		};
		const end = (e: PointerEvent) => {
			window.removeEventListener("pointermove", move, true);
			window.removeEventListener("pointerup", end, true);
			window.removeEventListener("pointercancel", end, true);
			if (!ghost) return;
			ghost.remove();
			handle.classList.remove("eoDragging");
			over?.classList.remove("eoDropOver");
			// Klick nach dem Ziehen nicht als Klick werten.
			const swallow = (c: MouseEvent) => { c.stopPropagation(); c.preventDefault(); };
			window.addEventListener("click", swallow, { capture: true, once: true });
			setTimeout(() => window.removeEventListener("click", swallow, { capture: true }), 300);
			const target = e.type === "pointerup" ? targetAt(e.clientX, e.clientY) : null;
			if (target?.dataset.dropsite) onDrop(source, target.dataset.dropsite);
		};
		window.addEventListener("pointermove", move, true);
		window.addEventListener("pointerup", end, true);
		window.addEventListener("pointercancel", end, true);
	});
}
