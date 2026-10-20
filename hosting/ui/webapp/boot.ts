import ComponentContainer from "sap/ui/core/ComponentContainer";

/** Startet die Konsole nach dem Laden von UI5 und blendet den Ladebildschirm aus. */
new ComponentContainer({
	name: "estrichone.hosting",
	manifest: true,
	async: true,
	height: "100%",
	settings: { id: "hosting" },
	componentCreated: () => document.getElementById("splash")?.remove()
}).placeAt("content");
