import Control from "sap/ui/core/Control";
import type RenderManager from "sap/ui/core/RenderManager";
import type { MetadataOptions } from "sap/ui/core/Element";

/**
 * Unterschriftenfeld (Finger, Stift, Maus) über Pointer-Events auf einem Canvas. Liefert das Bild als PNG (Data-URL).
 * @namespace estrichone.control
 */
export default class SignaturePad extends Control {
	static readonly metadata: MetadataOptions = {
		properties: {
			height: { type: "sap.ui.core.CSSSize", defaultValue: "10rem" },
			label: { type: "string", defaultValue: "Unterschrift" },
		},
		events: { change: {} },
	};

	declare getHeight: () => string;
	declare getLabel: () => string;
	declare fireChange: () => this;

	private strokes = 0;
	private canvas: HTMLCanvasElement | null = null;

	static renderer = {
		apiVersion: 2,
		render(rm: RenderManager, control: SignaturePad): void {
			rm.openStart("div", control).class("eoSignature").style("height", control.getHeight()).openEnd();
			rm.openStart("canvas").attr("role", "img").attr("aria-label", control.getLabel()).attr("tabindex", "0").openEnd().close("canvas");
			rm.openStart("span").class("eoSignatureLine").openEnd().text("Hier unterschreiben").close("span");
			rm.close("div");
		},
	};

	onAfterRendering(): void {
		const host = this.getDomRef() as HTMLElement | null;
		const canvas = host?.querySelector("canvas");
		if (!host || !canvas || canvas === this.canvas) return;
		this.canvas = canvas;
		const ratio = window.devicePixelRatio || 1;
		const resize = () => {
			const box = host.getBoundingClientRect();
			canvas.width = Math.max(1, Math.round(box.width * ratio));
			canvas.height = Math.max(1, Math.round(box.height * ratio));
			const ctx = canvas.getContext("2d");
			if (ctx) {
				ctx.scale(ratio, ratio);
				ctx.lineWidth = 2.2;
				ctx.lineCap = "round";
				ctx.lineJoin = "round";
				ctx.strokeStyle = "#0b2d5b";
			}
			this.strokes = 0;
		};
		resize();
		let drawing = false;
		const point = (e: PointerEvent) => {
			const box = canvas.getBoundingClientRect();
			return { x: e.clientX - box.left, y: e.clientY - box.top };
		};
		canvas.addEventListener("pointerdown", (e) => {
			drawing = true;
			canvas.setPointerCapture(e.pointerId);
			const ctx = canvas.getContext("2d");
			const p = point(e);
			ctx?.beginPath();
			ctx?.moveTo(p.x, p.y);
			ctx?.lineTo(p.x + 0.1, p.y + 0.1);
			ctx?.stroke();
			e.preventDefault();
		});
		canvas.addEventListener("pointermove", (e) => {
			if (!drawing) return;
			const p = point(e);
			const ctx = canvas.getContext("2d");
			ctx?.lineTo(p.x, p.y);
			ctx?.stroke();
			e.preventDefault();
		});
		const end = () => {
			if (!drawing) return;
			drawing = false;
			this.strokes++;
			this.fireChange();
		};
		canvas.addEventListener("pointerup", end);
		canvas.addEventListener("pointercancel", end);
		canvas.addEventListener("pointerleave", end);
	}

	isEmpty(): boolean {
		return this.strokes === 0;
	}

	clear(): void {
		const canvas = this.canvas;
		if (!canvas) return;
		canvas.getContext("2d")?.clearRect(0, 0, canvas.width, canvas.height);
		this.strokes = 0;
		this.fireChange();
	}

	/** PNG als Data-URL oder null, wenn leer. */
	toPng(): string | null {
		return this.isEmpty() || !this.canvas ? null : this.canvas.toDataURL("image/png");
	}
}
