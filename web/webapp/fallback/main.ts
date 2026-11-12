/**
 * Störungs-, Wartungs- und Update-Seite (stoerung.html) mit OpenUI5 wie die App: Caddy liefert sie, wenn die API nicht
 * antwortet, der Service Worker ohne Verbindung. Während eines Updates liest sie /update-status.json (Caddy, auch ohne
 * API) und zeigt denselben Wartungsbildschirm wie die App (fragment/UpdateScreen, util/updateView.ts); sonst
 * „nicht erreichbar“ bzw. „Wartungsarbeiten“ mit automatischem Neuversuch. Antwortet der Server wieder, geht es zurück.
 */
import Button from "sap/m/Button";
import FlexBox from "sap/m/FlexBox";
import IllustratedMessage from "sap/m/IllustratedMessage";
import Image from "sap/m/Image";
import Text from "sap/m/Text";
import VBox from "sap/m/VBox";
import Control from "sap/ui/core/Control";
import Fragment from "sap/ui/core/Fragment";
import Theming from "sap/ui/core/Theming";
import JSONModel from "sap/ui/model/json/JSONModel";
import { resolve, storedChoice } from "../util/theme";
import { isUpdateState, updateView, type UpdateState } from "../util/updateView";

const theme = resolve(storedChoice());
if (Theming.getTheme() !== theme) Theming.setTheme(theme);

const params = new URLSearchParams(location.search);
const back = params.get("zurueck") || (location.pathname === "/stoerung.html" ? "/" : location.pathname + location.search);
const since = new Intl.DateTimeFormat("de-DE", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }).format(new Date());
const model = new JSONModel({
	mode: "outage", title: "EstrichONE ist gerade nicht erreichbar",
	text: "Der Server antwortet im Moment nicht – zum Beispiel wegen eines Neustarts. Eingaben in der App bleiben auf dem Gerät gespeichert.",
	status: "", since: `Seit ${since} Uhr`, checking: false, upd: { page: false }, updateOffline: true,
});
let update: UpdateState | null = null;
let left = 30;

async function json<T>(url: string): Promise<{ ok: boolean; data: T | null }> {
	try {
		const response = await fetch(url, { cache: "no-store", credentials: "same-origin", headers: { Accept: "application/json" } });
		return { ok: response.ok, data: response.ok ? ((await response.json()) as T) : null };
	} catch {
		return { ok: false, data: null };
	}
}

function render(): void {
	if (update) {
		model.setProperty("/upd", updateView(update, new Date()));
		model.setProperty("/mode", "update");
	}
	const offline = typeof navigator !== "undefined" && navigator.onLine === false;
	model.setProperty("/status", update ? "" : offline ? `Keine Internetverbindung – Neuversuch in ${left} s` : `Automatischer Neuversuch in ${left} s`);
}

async function check(): Promise<void> {
	model.setProperty("/checking", true);
	const status = await json<unknown>("/update-status.json");
	if (isUpdateState(status.data)) {
		update = status.data;
	} else if (update) {
		location.replace(back);
		return;
	}
	const info = await json<unknown>("/api/v1/server-info");
	model.setProperty("/updateOffline", !info.ok);
	if (info.ok && !update) {
		const notices = await json<{ maintenance?: { active?: boolean; message?: string | null } | null; update?: unknown }>("/api/v1/system/notices");
		const fromServer = notices.data?.update;
		if (isUpdateState(fromServer)) {
			update = fromServer;
		} else if (notices.data?.maintenance?.active) {
			model.setProperty("/mode", "maintenance");
			model.setProperty("/title", "Wartungsarbeiten");
			model.setProperty("/text", notices.data.maintenance.message || "EstrichONE wird gerade gewartet. Danach geht es automatisch weiter.");
		} else {
			location.replace(back);
			return;
		}
	}
	model.setProperty("/checking", false);
	left = update ? 5 : 30;
	render();
}

async function start(): Promise<void> {
	const updateScreen = (await Fragment.load({ name: "estrichone.fragment.UpdateScreen", type: "XML" })) as Control;
	const outage = new IllustratedMessage({
		illustrationType: "{= ${notices>/mode} === 'maintenance' ? 'sapIllus-SimpleConnection' : 'sapIllus-UnableToLoad' }",
		title: "{notices>/title}", description: "{notices>/text}", enableDefaultTitleAndDescription: false,
		visible: "{= ${notices>/mode} !== 'update' }",
		additionalContent: [new VBox({ alignItems: "Center", items: [
			new Text({ text: "{notices>/since}" }).addStyleClass("sapUiSmallMarginBottom"),
			new Button({ text: "Jetzt erneut versuchen", icon: "sap-icon://refresh", type: "Emphasized", busy: "{notices>/checking}", press: () => void check() }),
			new Text({ text: "{notices>/status}" }).addStyleClass("sapUiSmallMarginTop"),
		] })],
	});
	updateScreen.bindProperty("visible", { path: "notices>/mode", formatter: (mode: string) => mode === "update" });
	const page = new FlexBox({
		direction: "Column", alignItems: "Center", justifyContent: "Center", height: "100vh", renderType: "Bare",
		items: [new Image({ src: "/logo-horizontal.svg", alt: "EstrichONE", height: "3rem" }).addStyleClass("sapUiMediumMarginBottom"), outage, updateScreen],
	});
	page.setModel(model, "notices");
	page.placeAt("content");
	document.getElementById("splash")?.remove();
	window.addEventListener("online", () => void check());
	setInterval(() => {
		left -= 1;
		if (left <= 0) {
			left = update ? 5 : 30;
			void check();
		}
		render();
	}, 1000);
	await check();
}

void start();
