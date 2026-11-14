import { describe, expect, it } from "vitest";
import { cssSizePattern, largeDialogSize } from "../webapp/util/dialogSize";

describe("großes Formularfenster", () => {
	it("liefert gültige CSSSize-Werte (sonst öffnet sap.m.Dialog nicht)", () => {
		for (const w of [390, 1280, 1440, 1920, 2560]) {
			const s = largeDialogSize(w);
			expect(s.contentWidth).toMatch(cssSizePattern);
		}
		expect("min(90rem, 92vw)").not.toMatch(cssSizePattern);
	});

	it("nutzt 92 % der Fensterbreite, höchstens 90rem", () => {
		expect(largeDialogSize(1280).contentWidth).toBe("1178px");
		expect(largeDialogSize(1920).contentWidth).toBe("1440px");
	});
});
