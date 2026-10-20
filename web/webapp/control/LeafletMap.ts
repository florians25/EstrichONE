import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import includeStylesheet from "sap/ui/dom/includeStylesheet";
import type { MetadataOptions } from "sap/ui/core/Element";
import L from "leaflet";

export interface MapMarker {
	id: string;
	latitude: number;
	longitude: number;
	color?: string;
	label: string;
	/** Zeilen im Popup; die erste mit `href` wird ein Link (z. B. zur Bauakte). */
	popup?: { text: string; href?: string; strong?: boolean }[];
}

let cssLoaded: Promise<void> | null = null;
const fallbackCenter: L.LatLngTuple = [51.16, 10.45];

/**
 * Karte mit OpenStreetMap-Kacheln (Leaflet) als UI5-Steuerelement. Marker als Vektorpunkte, Popups ohne
 * Inline-Skripte (CSP). Mit pickMode meldet ein Klick in die Karte einen neuen Standort.
 * @namespace estrichone.control
 */
export default class LeafletMap extends Control {
	static readonly metadata: MetadataOptions = {
		properties: {
			height: { type: "sap.ui.core.CSSSize", defaultValue: "20rem" },
			width: { type: "sap.ui.core.CSSSize", defaultValue: "100%" },
			markers: { type: "object", defaultValue: [] },
			zoom: { type: "int", defaultValue: 15 },
			pickMode: { type: "boolean", defaultValue: false },
			interactive: { type: "boolean", defaultValue: true },
		},
		events: {
			pick: { parameters: { latitude: { type: "float" }, longitude: { type: "float" } } },
			markerPress: { parameters: { id: { type: "string" } } },
		},
	};

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: LeafletMap): void {
			rm.openStart("div", control);
			rm.class("eoMap");
			rm.style("height", control.getHeight());
			rm.style("width", control.getWidth());
			rm.attr("role", "region");
			rm.attr("aria-label", "Karte");
			rm.openEnd();
			rm.close("div");
		},
	};

	private map: L.Map | null = null;
	private host: HTMLDivElement | null = null;
	private layer: L.LayerGroup | null = null;
	private fittedKey = "";

	// Von UI5 erzeugte Zugriffe (Eigenschaften aus metadata)
	declare getHeight: () => string;
	declare getWidth: () => string;
	declare getMarkers: () => MapMarker[];
	declare setMarkers: (markers: MapMarker[]) => this;
	declare getZoom: () => number;
	declare getPickMode: () => boolean;
	declare setPickMode: (value: boolean) => this;
	declare getInteractive: () => boolean;
	declare firePick: (parameters: { latitude: number; longitude: number }) => this;
	declare fireMarkerPress: (parameters: { id: string }) => this;

	onAfterRendering(): void {
		const dom = this.getDomRef() as HTMLElement | null;
		if (!dom) return;
		cssLoaded ??= includeStylesheet(sap.ui.require.toUrl("estrichone/thirdparty/leaflet.css")) as unknown as Promise<void>;
		if (!this.host) {
			this.host = document.createElement("div");
			this.host.style.width = "100%";
			this.host.style.height = "100%";
		}
		dom.appendChild(this.host);
		if (!this.map) {
			const interactive = this.getInteractive();
			this.map = L.map(this.host, { zoomControl: interactive, dragging: interactive, scrollWheelZoom: interactive, doubleClickZoom: interactive,
				attributionControl: true, keyboard: interactive }).setView(fallbackCenter, 6);
			L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
				maxZoom: 19,
				attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap</a>-Mitwirkende',
			}).addTo(this.map);
			this.layer = L.layerGroup().addTo(this.map);
			this.map.on("click", (event: L.LeafletMouseEvent) => {
				if (this.getPickMode()) {
					this.firePick({ latitude: Number(event.latlng.lat.toFixed(6)), longitude: Number(event.latlng.lng.toFixed(6)) });
				}
			});
			this.fittedKey = "";
		}
		setTimeout(() => this.map?.invalidateSize(), 0);
		this.draw();
	}

	setProperty(name: string, value: unknown, suppress?: boolean): this {
		// Marker und Auswahlmodus ohne Neuaufbau der Karte aktualisieren.
		if (name === "markers" || name === "pickMode") {
			super.setProperty(name, value, true);
			if (this.map) {
				this.draw();
				this.host?.style.setProperty("cursor", this.getPickMode() ? "crosshair" : "");
			}
			return this;
		}
		return super.setProperty(name, value, suppress);
	}

	private draw(): void {
		if (!this.map || !this.layer) return;
		this.layer.clearLayers();
		const markers = this.getMarkers() ?? [];
		for (const m of markers) {
			const marker = L.circleMarker([m.latitude, m.longitude], { radius: 9, color: "#fff", weight: 2, fillColor: m.color ?? "#0B2D5B", fillOpacity: 0.95 });
			marker.bindTooltip(m.label, { direction: "top", offset: [0, -8] });
			if (m.popup?.length) {
				const box = document.createElement("div");
				box.className = "eoMapPopup";
				for (const line of m.popup) {
					const element = document.createElement(line.href ? "a" : line.strong ? "strong" : "div");
					element.textContent = line.text;
					if (line.href) (element as HTMLAnchorElement).href = line.href;
					element.style.display = "block";
					box.appendChild(element);
				}
				marker.bindPopup(box);
			}
			marker.on("click", () => this.fireMarkerPress({ id: m.id }));
			marker.addTo(this.layer);
		}
		const key = markers.map((m) => `${m.id}:${m.latitude}:${m.longitude}`).join("|");
		if (key !== this.fittedKey) {
			this.fittedKey = key;
			if (markers.length === 1) {
				this.map.setView([markers[0].latitude, markers[0].longitude], this.getZoom());
			} else if (markers.length > 1) {
				this.map.fitBounds(markers.map((m) => [m.latitude, m.longitude] as L.LatLngTuple), { padding: [32, 32], maxZoom: 15 });
			}
		}
	}

	exit(): void {
		this.map?.remove();
		this.map = null;
		this.host = null;
	}
}
