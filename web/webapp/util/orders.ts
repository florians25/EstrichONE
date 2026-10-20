/** Materialbestellungen: Klartext für E-Mail, Bestellschein als HTML (Vorschau und Druck). Ohne UI5-Abhängigkeit. */
import { parseDay } from "./days";
import { money, quantity } from "./format";
import type { CompanyResponse, OrderResponse } from "./models";

export const day = (v: string | null) => (v ? parseDay(v).toLocaleDateString("de-DE", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" }) : "–");

export function deliveryAddress(o: Pick<OrderResponse, "deliveryStreet" | "deliveryPostalCode" | "deliveryCity">): string {
	return [o.deliveryStreet, [o.deliveryPostalCode, o.deliveryCity].filter(Boolean).join(" ")].filter(Boolean).join(", ");
}

/** Summe; fehlen Preise, ist sie nur ein Teilbetrag und wird mit * gekennzeichnet. */
export function orderTotal(o: Pick<OrderResponse, "total" | "lines">): string {
	if (o.total == null) return "–";
	return o.lines.some((l) => l.unitPrice == null) ? `${money(o.total)}*` : money(o.total);
}

export function orderText(o: OrderResponse, company: CompanyResponse | null): string {
	const lines = o.lines.map((l) => `${l.position}. ${quantity(l.quantity)} ${l.unit}  ${l.description}${l.articleNumber ? ` (Art.-Nr. ${l.articleNumber})` : ""}`);
	return [
		"Guten Tag,", "",
		`hiermit bestellen wir${o.supplierCustomerNumber ? ` (Kundennummer ${o.supplierCustomerNumber})` : ""}:`, "",
		...lines, "",
		`Liefertermin: ${o.deliveryDate ? day(o.deliveryDate) : "nach Absprache"}${o.deliveryTime ? `, ${o.deliveryTime}` : ""}`,
		deliveryAddress(o) ? `Lieferadresse: ${deliveryAddress(o)}` : null,
		o.siteContact ? `Ansprechpartner vor Ort: ${o.siteContact}` : null,
		o.bauakteNumber ? `Bauvorhaben: ${o.bauakteNumber} ${o.bauakteTitle ?? ""}`.trim() : null,
		o.notes ? `\nHinweise: ${o.notes}` : null,
		"", "Bitte bestätigen Sie uns den Liefertermin.", "", "Mit freundlichen Grüßen", company?.name ?? "",
		company?.phone ? `Tel. ${company.phone}` : null,
	].filter((l) => l !== null).join("\n");
}

export function mailto(o: OrderResponse, company: CompanyResponse | null): string {
	const subject = `Bestellung ${o.number}${o.bauakteTitle ? ` – ${o.bauakteTitle}` : ""}`;
	return `mailto:${encodeURIComponent(o.supplierEmail ?? "")}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(orderText(o, company))}`;
}

export function escape(value: string | null | undefined): string {
	return (value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c] ?? c);
}

/** Bestellschein im Briefformat (alle Werte maskiert). */
export function orderSheet(o: OrderResponse, company: CompanyResponse | null): string {
	const e = escape;
	const priced = o.lines.some((l) => l.unitPrice != null);
	const rows = o.lines.map((l) => `<tr><td>${l.position}</td><td>${e(l.articleNumber)}</td><td>${e(l.description)}</td><td class="num">${quantity(l.quantity)}</td><td>${e(l.unit)}</td>${priced ? `<td class="num">${l.unitPrice != null ? money(l.unitPrice) : ""}</td><td class="num">${l.total != null ? money(l.total) : ""}</td>` : ""}</tr>`).join("");
	return `<div class="eoSheet order-sheet">
<div class="head"><div><h1>${e(company?.name)}</h1><div class="muted">${e([company?.street, [company?.postalCode, company?.city].filter(Boolean).join(" ")].filter(Boolean).join(" · "))}</div>
<div class="muted">${e([company?.phone && `Tel. ${company.phone}`, company?.email].filter(Boolean).join(" · "))}</div></div>
<div style="text-align:right"><div>Bestellung</div><h1>${e(o.number)}</h1></div></div>
<div class="grid" style="margin-top:1rem"><div><div class="muted">Lieferant</div><strong>${e(o.supplierName)}</strong>${o.supplierCustomerNumber ? `<div>Unsere Kundennummer: ${e(o.supplierCustomerNumber)}</div>` : ""}</div>
<div><div>Datum: ${new Date(o.orderedAt ?? o.updatedAt).toLocaleDateString("de-DE")}</div><div>Liefertermin: ${e(o.deliveryDate ? day(o.deliveryDate) : "nach Absprache")}${o.deliveryTime ? `, ${e(o.deliveryTime)}` : ""}</div>
${o.bauakteNumber ? `<div>Bauvorhaben: ${e(o.bauakteNumber)} ${e(o.bauakteTitle)}</div>` : ""}<div>Besteller: ${e(o.createdByName ?? "–")}</div></div></div>
<h2>Lieferadresse</h2><div>${e(deliveryAddress(o) || "wird telefonisch mitgeteilt")}</div>${o.siteContact ? `<div>Ansprechpartner vor Ort: ${e(o.siteContact)}</div>` : ""}
<h2>Positionen</h2><table><thead><tr><th>Pos.</th><th>Art.-Nr.</th><th>Bezeichnung</th><th class="num">Menge</th><th>Einheit</th>${priced ? '<th class="num">Preis</th><th class="num">Summe</th>' : ""}</tr></thead>
<tbody>${rows || `<tr><td colspan="${priced ? 7 : 5}">Noch keine Positionen.</td></tr>`}</tbody>
${priced && o.total != null ? `<tfoot><tr><td colspan="6">${o.lines.every((l) => l.unitPrice != null) ? "Summe netto" : "Summe netto der Positionen mit Preis"}</td><td class="num">${money(o.total)}</td></tr></tfoot>` : ""}</table>
${o.notes ? `<h2>Hinweise</h2><div class="pre">${e(o.notes)}</div>` : ""}
<p style="margin-top:2rem">Bitte bestätigen Sie uns den Liefertermin. Vielen Dank.</p></div>`;
}
