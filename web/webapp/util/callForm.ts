/** Vollständiges Formular für Anruf oder Anfrage (Schnellerfassung siehe ShellBar „Telefon“). */
import { put } from "./api";
import { openForm } from "./forms";
import { uuidv7 } from "./ids";
import { callPriorities, callSources, options } from "./labels";
import type { CallKind, CallResponse } from "./models";

export function callForm(kind: CallKind, call: CallResponse | null): Promise<CallResponse | null> {
	const inquiry = kind === "inquiry";
	return openForm<CallResponse>({
		title: call ? (inquiry ? "Anfrage bearbeiten" : "Anruf bearbeiten") : (inquiry ? "Anfrage erfassen" : "Anruf erfassen"), width: "44rem",
		initial: call ? { ...call } : { direction: "inbound", source: inquiry ? "email" : "phone", priority: "normal", followUp: false },
		fields: [
			...(!inquiry ? [{ key: "direction", label: "Richtung", type: "segmented" as const, options: [{ key: "inbound", text: "Eingehend" }, { key: "outbound", text: "Ausgehend" }] }] : []),
			{ key: "source", label: "Kanal", type: "select", required: true, options: options(callSources) },
			{ key: "priority", label: "Priorität", type: "select", required: true, options: options(callPriorities) },
			{ key: "callerName", label: "Name", maxLength: 200 },
			{ key: "company", label: "Firma", maxLength: 200 },
			{ key: "callerPhone", label: "Telefon", maxLength: 50 },
			{ key: "callerEmail", label: "E-Mail", type: "email", maxLength: 320 },
			{ key: "customerId", label: "Kunde (Stammdaten)", type: "customer", displayText: call?.customerName ?? undefined,
				help: inquiry ? "Ohne Kunde wird beim Umwandeln einer aus Name/Firma angelegt." : undefined },
			{ key: "subject", label: inquiry ? "Anliegen" : "Betreff", maxLength: 200 },
			{ key: "notes", label: inquiry ? "Ticket-Notiz" : "Gesprächsnotiz", type: "textarea", rows: 4, maxLength: 4000 },
			...(inquiry ? [
				{ key: "siteTitle", label: "Baustelle laut Anfrage", type: "section" as const },
				{ key: "siteStreet", label: "Straße", maxLength: 200 },
				{ key: "sitePostalCode", label: "PLZ", maxLength: 10 },
				{ key: "siteCity", label: "Ort", maxLength: 100 },
				{ key: "areaSquareMeters", label: "Fläche (m²)", type: "number" as const, unit: "m²", min: 0 },
				{ key: "screedType", label: "Estrichart", placeholder: "z. B. CAF, CT", maxLength: 100 },
				{ key: "desiredStart", label: "Wunschtermin", type: "date" as const },
			] : []),
			{ key: "followTitle", label: "Wiedervorlage", type: "section" },
			{ key: "followUp", label: "Wiedervorlage nötig", type: "switch" },
			{ key: "dueDate", label: "Wiedervorlage am", type: "date" },
			{ key: "nextStep", label: "Nächster Schritt", placeholder: "z. B. Angebot schicken", maxLength: 200 },
		],
		submit: async (v) => {
			const id = call?.id ?? uuidv7();
			const text = (k: string) => (v[k] ? String(v[k]) : null);
			return put<CallResponse>(`calls/${id}`, {
				id, kind, direction: v.direction ?? call?.direction ?? "inbound", source: v.source, priority: v.priority, callerName: text("callerName"),
				callerPhone: text("callerPhone"), callerEmail: text("callerEmail"), company: text("company"), customerId: text("customerId"),
				bauakteId: call?.bauakteId ?? null, subject: text("subject"), notes: text("notes"), followUp: !!v.followUp, dueDate: text("dueDate"),
				nextStep: text("nextStep"), areaSquareMeters: v.areaSquareMeters ?? null, screedType: text("screedType"), desiredStart: text("desiredStart"),
				siteStreet: text("siteStreet"), sitePostalCode: text("sitePostalCode"), siteCity: text("siteCity"), calledAt: call?.calledAt ?? null,
			});
		},
	});
}

export const caller = (c: Pick<CallResponse, "callerName" | "company" | "customerName">) => [c.callerName, c.company ?? c.customerName].filter(Boolean).join(" · ") || "–";
export const callTime = (v: string) => new Date(v).toLocaleString("de-DE", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
