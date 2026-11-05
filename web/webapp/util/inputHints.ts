/**
 * Browser-Hinweise für Eingabefelder. UI5 schreibt CustomData mit writeToDom nur als data-Attribut an das äußere Element
 * und setzt am inneren <input> autocomplete="off" – Passwortmanager und Einmalcodes aus SMS/Authenticator greifen dann nicht.
 * Ein Beobachter überträgt data-autocomplete und data-inputmode auf das innere Eingabefeld und data-role (z. B. „alert“
 * an Fehlermeldungen) als echte Rolle für Screenreader. Auch nach jedem Neuzeichnen durch UI5.
 */
const SELECTOR = "[data-autocomplete],[data-inputmode],[data-role]";

function apply(host: Element): void {
	const auto = host.getAttribute("data-autocomplete");
	const mode = host.getAttribute("data-inputmode");
	const role = host.getAttribute("data-role");
	if (auto || mode) {
		const input = host.matches("input,textarea") ? host : host.querySelector("input,textarea");
		if (input) {
			if (auto && input.getAttribute("autocomplete") !== auto) input.setAttribute("autocomplete", auto);
			if (mode && input.getAttribute("inputmode") !== mode) input.setAttribute("inputmode", mode);
		}
	}
	if (role && host.getAttribute("role") !== role) host.setAttribute("role", role);
}

function scan(node: Node): void {
	if (!(node instanceof Element)) return;
	if (node.matches(SELECTOR)) apply(node);
	node.querySelectorAll(SELECTOR).forEach(apply);
	// Neu gezeichnetes inneres <input>: Hinweise des umgebenden Feldes erneut anwenden.
	const host = node.closest(SELECTOR);
	if (host && host !== node) apply(host);
}

let started = false;

export function startInputHints(): void {
	if (started || typeof MutationObserver === "undefined") return;
	started = true;
	scan(document.body);
	new MutationObserver((records) => {
		for (const record of records) {
			if (record.type === "attributes") {
				const target = record.target as Element;
				const host = target.closest(SELECTOR);
				if (host) apply(host);
				continue;
			}
			record.addedNodes.forEach(scan);
		}
	}).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["autocomplete", "role"] });
}
