/**
 * Wartet, bis alle Bilder geladen sind (höchstens 60 Sekunden). Liefert die Zahl der Bilder, die nicht geladen werden konnten.
 */
export async function waitForImages(images: HTMLImageElement[], timeoutMs = 60_000): Promise<number> {
	const one = (img: HTMLImageElement) => new Promise<boolean>((resolve) => {
		img.loading = "eager";
		if (img.complete) {
			resolve(img.naturalWidth > 0);
			return;
		}
		img.addEventListener("load", () => resolve(true), { once: true });
		img.addEventListener("error", () => resolve(false), { once: true });
	}).then(async (ok) => {
		if (ok && typeof img.decode === "function") await img.decode().catch(() => undefined);
		return ok;
	});
	const timeout = new Promise<boolean[]>((resolve) => setTimeout(() => resolve(images.map((img) => img.complete && img.naturalWidth > 0)), timeoutMs));
	const results = await Promise.race([Promise.all(images.map(one)), timeout]);
	return results.filter((ok) => !ok).length;
}
