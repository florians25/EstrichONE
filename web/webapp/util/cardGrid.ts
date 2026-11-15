/**
 * Spaltenzahl für ein Raster gleich großer Karten (Startseite): so viele Spalten wie passen, aber gleichmäßig auf die
 * Reihen verteilt. Ein reines CSS-Raster (auto-fill) stellt bei sechs Karten und Platz für fünf eine einzelne Karte in
 * die zweite Reihe (z. B. 1680 × 1050 oder 1920 × 1080 mit 125 %); hier stattdessen 3 + 3.
 */
export function balancedColumns(count: number, width: number, minWidth: number, gap: number): number {
	if (count <= 1 || width <= 0 || minWidth <= 0) return 1;
	const fit = Math.max(1, Math.min(count, Math.floor((width + gap) / (minWidth + gap))));
	const rows = Math.ceil(count / fit);
	return Math.ceil(count / rows);
}

/** Mindestbreite einer Startkarte in rem (CSS-Rückfall in style.css: .eoStartCards). */
export const START_CARD_MIN_REM = 14;

/** Spalten des Rasters `grid` an seine Breite und die Zahl der Karten anpassen (bei jeder Größenänderung). */
export function followBalancedColumns(grid: HTMLElement, minRem = START_CARD_MIN_REM): ResizeObserver | undefined {
	if (typeof ResizeObserver !== "function") return undefined;
	const apply = () => {
		const style = getComputedStyle(grid);
		const rem = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
		const gap = parseFloat(style.columnGap) || 0;
		const count = grid.children.length;
		const columns = balancedColumns(count, grid.clientWidth, minRem * rem, gap);
		const value = `repeat(${columns}, minmax(0, 1fr))`;
		if (grid.style.gridTemplateColumns !== value) grid.style.gridTemplateColumns = value;
	};
	const observer = new ResizeObserver(apply);
	observer.observe(grid);
	apply();
	return observer;
}
