import { describe, expect, it } from "vitest";
import { balancedColumns } from "../webapp/util/cardGrid";

describe("Startkarten: gleichmäßige Spalten", () => {
	const min = 224, gap = 16; // 14rem, 1rem
	it("sechs Karten: eine Reihe, wenn sie passen, sonst 3 + 3 statt 5 + 1", () => {
		expect(balancedColumns(6, 1568, min, gap)).toBe(6); // 1920 × 1080
		expect(balancedColumns(6, 1424, min, gap)).toBe(6);
		expect(balancedColumns(6, 1360, min, gap)).toBe(3); // 1680 × 1050: Platz für fünf
		expect(balancedColumns(6, 1216, min, gap)).toBe(3); // 1920 × 1080 mit 125 %
		expect(balancedColumns(6, 960, min, gap)).toBe(3); // Platz für vier
		expect(balancedColumns(6, 740, min, gap)).toBe(3);
		expect(balancedColumns(6, 480, min, gap)).toBe(2);
		expect(balancedColumns(6, 300, min, gap)).toBe(1);
	});
	it("weniger Karten: nie mehr Spalten als Karten, Reihen gleichmäßig", () => {
		expect(balancedColumns(5, 1568, min, gap)).toBe(5);
		expect(balancedColumns(5, 960, min, gap)).toBe(3);
		expect(balancedColumns(4, 1568, min, gap)).toBe(4);
		expect(balancedColumns(4, 900, min, gap)).toBe(2);
		expect(balancedColumns(1, 1568, min, gap)).toBe(1);
		expect(balancedColumns(0, 1568, min, gap)).toBe(1);
	});
});
