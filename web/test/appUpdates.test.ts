import { describe, expect, it } from "vitest";
import {
	compareVersions, isVersion, overviewIssues, sortDevices, stateText, validateSettings, wouldBlock, defaultAppUpdateSettings,
	type AppUpdateDevice, type AppUpdateOverview,
} from "../webapp/util/appUpdates";
import { adminAreas, matchesArea } from "../webapp/util/adminCatalog";

const device = (over: Partial<AppUpdateDevice>): AppUpdateDevice => ({
	id: crypto.randomUUID(), name: "Pixel", platform: "android", kind: "personal", status: "active", model: null, appVersion: "1.0.12",
	state: "current", assignedUserName: "Max Huber", lastSeenAt: null, ...over,
});

const overview = (over: Partial<AppUpdateOverview> = {}): AppUpdateOverview => ({
	serverVersion: "1.0.12", settings: { ...defaultAppUpdateSettings },
	android: { platform: "android", latestVersion: "1.0.12", minimumVersion: "1.0.0", download: { kind: "package", url: "/api/v1/app-updates/android/package" } },
	ios: { platform: "ios", latestVersion: "1.0.12", minimumVersion: "1.0.0", download: { kind: "store", url: null } },
	androidPackage: { version: "1.0.12", versionCode: 10012, size: 1, sha256: "ab", releaseSigned: true, signerSha256: null, valid: true, error: null },
	devices: [], counts: { total: 0, current: 0, recommended: 0, required: 0, newerThanServer: 0, unknown: 0 }, ...over,
});

describe("App-Updates", () => {
	it("vergleicht Versionen wie der Server", () => {
		expect(compareVersions("1.0.9", "1.0.10")).toBe(-1);
		expect(compareVersions("1.1.0-preview.2", "1.1.0")).toBe(-1);
		expect(compareVersions("1.1.0-preview.10", "1.1.0-preview.9")).toBe(1);
		expect(compareVersions("v1.0.5", "1.0.5")).toBe(0);
		expect(isVersion("1.0.12")).toBe(true);
		expect(isVersion("1.1.0-preview.2")).toBe(true);
		expect(isVersion("neu")).toBe(false);
	});

	it("prüft Eingaben und leert leere Felder", () => {
		const ok = validateSettings({ ...defaultAppUpdateSettings, minimumVersion: " 1.0.10 ", message: "  ", iosUpdateUrl: "https://testflight.apple.com/join/abc" });
		expect(ok.errors).toEqual([]);
		expect(ok.clean.minimumVersion).toBe("1.0.10");
		expect(ok.clean.message).toBeNull();
		const bad = validateSettings({ ...defaultAppUpdateSettings, minimumVersion: "bald", androidUpdateUrl: "http://mdm.local/app.apk" });
		expect(bad.errors).toHaveLength(2);
	});

	it("zeigt Handlungsbedarf in der Statuszeile", () => {
		const o = overview({
			devices: [device({ state: "required", appVersion: "1.0.0" }), device({ state: "recommended", appVersion: "1.0.11" }), device({ platform: "ios", state: "newerThanServer" })],
			counts: { total: 3, current: 0, recommended: 1, required: 1, newerThanServer: 1, unknown: 0 },
			settings: { ...defaultAppUpdateSettings, enforceMinimum: true },
		});
		const texts = overviewIssues(o).map((i) => i.text);
		expect(texts).toContain("1 Gerät ist gesperrt, bis die App aktualisiert ist");
		expect(texts).toContain("1 Gerät hat eine ältere App");
		expect(texts.some((t) => t.includes("Server aktualisieren"))).toBe(true);
		expect(texts.some((t) => t.includes("kein Link für iOS"))).toBe(true);
		expect(overviewIssues(overview({ androidPackage: { ...overview().androidPackage!, valid: false, error: "Die Prüfsumme (SHA-256) stimmt nicht" } }))[0])
			.toEqual({ text: "Android-Paket wird nicht ausgeliefert: Die Prüfsumme (SHA-256) stimmt nicht", level: "error" });
		expect(overviewIssues(overview({ androidPackage: { ...overview().androidPackage!, releaseSigned: false } }))[0].text).toContain("Testversion");
	});

	it("zählt, was eine Mindestversion sperren würde, und sortiert nach Dringlichkeit", () => {
		const list = [device({ name: "B", appVersion: "1.0.11", state: "recommended" }), device({ name: "A", appVersion: "1.0.12" }),
			device({ name: "C", appVersion: "1.0.9", state: "required" }), device({ name: "D", appVersion: "1.0.9", status: "locked", state: "required" })];
		expect(wouldBlock(list, "1.0.12")).toBe(2);
		expect(wouldBlock(list, "1.0.12", "ios")).toBe(0);
		expect(sortDevices(list).map((d) => d.name)).toEqual(["C", "D", "B", "A"]);
		expect(stateText(null).text).toBe("Version unbekannt");
	});

	it("ist in der Verwaltung zu finden", () => {
		const area = adminAreas.find((a) => a.key === "appUpdates")!;
		expect(area.path).toBe("/it-center/app-updates");
		expect(matchesArea(area, "testflight")).toBe(true);
		expect(matchesArea(area, "apk")).toBe(true);
	});
});
