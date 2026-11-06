import { describe, expect, it } from "vitest";
import { AUTO_SCALE, resolveScale } from "../webapp/util/theme";

describe("Darstellungsgröße", () => {
	it("vergrößert automatisch nur auf sehr breiten Bildschirmen", () => {
		expect(resolveScale(AUTO_SCALE, 1920)).toBe(100);
		expect(resolveScale(AUTO_SCALE, 2560)).toBe(112.5);
		expect(resolveScale(AUTO_SCALE, 3000)).toBe(125);
		expect(resolveScale(AUTO_SCALE, 3840, 2050)).toBe(150);
	});
	it("vergrößert breite Curved-/Ultrawide-Monitore", () => {
		expect(resolveScale(AUTO_SCALE, 3440, 1300)).toBe(125);
		expect(resolveScale(AUTO_SCALE, 5120, 1300)).toBe(125);
		expect(resolveScale(AUTO_SCALE, 5120, 2050)).toBe(150);
		expect(resolveScale(AUTO_SCALE, 2560, 1300)).toBe(112.5);
	});
	it("übernimmt eine feste Auswahl unabhängig von der Breite", () => {
		expect(resolveScale("100", 3840)).toBe(100);
		expect(resolveScale("112", 1280)).toBe(112.5);
		expect(resolveScale("175", 1280)).toBe(175);
	});
});
