import { Tag } from 'antd';
import type { CompanyResponse, MaterialCategory, OrderResponse, OrderStatus } from '../../api/models';
import { parseDay } from '../../shared/days';

export const categoryLabels: Record<MaterialCategory, string> = {
  screed: 'Estrichmörtel', binder: 'Bindemittel', aggregate: 'Zuschlag (Sand/Kies)', insulation: 'Dämmung', membrane: 'Folie / Abdeckung',
  edgeStrip: 'Randdämmstreifen', additive: 'Zusatzmittel / Fasern', other: 'Sonstiges',
};
export const categoryColors: Record<MaterialCategory, string> = {
  screed: 'geekblue', binder: 'blue', aggregate: 'gold', insulation: 'cyan', membrane: 'purple', edgeStrip: 'magenta', additive: 'lime', other: 'default',
};

export const orderStatusLabels: Record<OrderStatus, string> = {
  draft: 'Entwurf', ordered: 'Bestellt', confirmed: 'Bestätigt', delivered: 'Geliefert', cancelled: 'Storniert',
};
const orderStatusColors: Record<OrderStatus, string> = { draft: 'default', ordered: 'blue', confirmed: 'cyan', delivered: 'green', cancelled: 'red' };

/** Übliche Einheiten im Estrichbau; frei erweiterbar. */
export const units = ['t', 'm³', 'Sack', 'Palette', 'm²', 'lfm', 'Rolle', 'Stk', 'kg', 'l', 'Eimer'];
export const deliveryWindows = ['6:00–7:00', '7:00–8:00', '8:00–10:00', 'vormittags', 'nachmittags', 'nach telefonischer Absprache'];

export function OrderStatusTag({ status }: { status: OrderStatus }) {
  return <Tag color={orderStatusColors[status]} bordered={false} style={{ fontWeight: 600 }}>{orderStatusLabels[status]}</Tag>;
}

export function CategoryTag({ category }: { category: MaterialCategory }) {
  return <Tag color={categoryColors[category]} bordered={false}>{categoryLabels[category]}</Tag>;
}

const euro = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' });
const amount = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 3 });
export const money = (v: number | null | undefined) => (v == null ? '–' : euro.format(v));
export const quantity = (v: number) => amount.format(v);
export const day = (v: string | null) => (v ? parseDay(v).toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' }) : '–');

/** Summe der Bestellung; fehlen Preise, ist sie nur ein Teilbetrag und wird so gekennzeichnet. */
export function OrderTotal({ order }: { order: Pick<OrderResponse, 'total' | 'lines'> }) {
  if (order.total == null) return <span className="tabular">–</span>;
  const partial = order.lines.some((l) => l.unitPrice == null);
  return partial
    ? <span className="tabular" title="Nicht alle Positionen haben einen Preis">{money(order.total)}*</span>
    : <span className="tabular">{money(order.total)}</span>;
}

export function deliveryAddress(o: Pick<OrderResponse, 'deliveryStreet' | 'deliveryPostalCode' | 'deliveryCity'>): string {
  return [o.deliveryStreet, [o.deliveryPostalCode, o.deliveryCity].filter(Boolean).join(' ')].filter(Boolean).join(', ');
}

/** Bestellung als Klartext für E-Mail oder Zwischenablage. */
export function orderText(o: OrderResponse, company: CompanyResponse | null): string {
  const lines = o.lines.map((l) => `${l.position}. ${quantity(l.quantity)} ${l.unit}  ${l.description}${l.articleNumber ? ` (Art.-Nr. ${l.articleNumber})` : ''}`);
  return [
    'Guten Tag,',
    '',
    `hiermit bestellen wir${o.supplierCustomerNumber ? ` (Kundennummer ${o.supplierCustomerNumber})` : ''}:`,
    '',
    ...lines,
    '',
    `Liefertermin: ${o.deliveryDate ? day(o.deliveryDate) : 'nach Absprache'}${o.deliveryTime ? `, ${o.deliveryTime}` : ''}`,
    deliveryAddress(o) ? `Lieferadresse: ${deliveryAddress(o)}` : null,
    o.siteContact ? `Ansprechpartner vor Ort: ${o.siteContact}` : null,
    o.bauakteNumber ? `Bauvorhaben: ${o.bauakteNumber} ${o.bauakteTitle ?? ''}`.trim() : null,
    o.notes ? `\nHinweise: ${o.notes}` : null,
    '',
    'Bitte bestätigen Sie uns den Liefertermin.',
    '',
    'Mit freundlichen Grüßen',
    company?.name ?? '',
    company?.phone ? `Tel. ${company.phone}` : null,
  ].filter((l) => l !== null).join('\n');
}

export function mailto(o: OrderResponse, company: CompanyResponse | null): string {
  const subject = `Bestellung ${o.number}${o.bauakteTitle ? ` – ${o.bauakteTitle}` : ''}`;
  return `mailto:${encodeURIComponent(o.supplierEmail ?? '')}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(orderText(o, company))}`;
}

/** Bestellschein im Briefformat; auch Vorschau im Detail. */
export function OrderSheet({ order: o, company }: { order: OrderResponse; company: CompanyResponse | null }) {
  const priced = o.lines.some((l) => l.unitPrice != null);
  return (
    <div className="order-sheet">
      <header className="sheet-head">
        <div>
          <div className="sheet-company">{company?.name ?? ''}</div>
          <div className="sheet-small">{[company?.street, [company?.postalCode, company?.city].filter(Boolean).join(' ')].filter(Boolean).join(' · ')}</div>
          <div className="sheet-small">{[company?.phone && `Tel. ${company.phone}`, company?.email].filter(Boolean).join(' · ')}</div>
        </div>
        <div className="sheet-title">
          <div>Bestellung</div>
          <strong>{o.number}</strong>
        </div>
      </header>
      <section className="sheet-grid">
        <div>
          <div className="sheet-label">Lieferant</div>
          <div className="sheet-strong">{o.supplierName}</div>
          {o.supplierCustomerNumber && <div>Unsere Kundennummer: {o.supplierCustomerNumber}</div>}
        </div>
        <dl className="sheet-facts">
          <dt>Datum</dt><dd>{new Date(o.orderedAt ?? o.updatedAt).toLocaleDateString('de-DE')}</dd>
          <dt>Liefertermin</dt><dd>{o.deliveryDate ? day(o.deliveryDate) : 'nach Absprache'}{o.deliveryTime ? `, ${o.deliveryTime}` : ''}</dd>
          {o.bauakteNumber && <><dt>Bauvorhaben</dt><dd>{o.bauakteNumber} {o.bauakteTitle}</dd></>}
          <dt>Besteller</dt><dd>{o.createdByName ?? '–'}</dd>
        </dl>
      </section>
      <section className="sheet-delivery">
        <div><span className="sheet-label">Lieferadresse</span>{deliveryAddress(o) || 'wird telefonisch mitgeteilt'}</div>
        {o.siteContact && <div><span className="sheet-label">Ansprechpartner vor Ort</span>{o.siteContact}</div>}
      </section>
      <table className="sheet-table">
        <thead>
          <tr><th>Pos.</th><th>Art.-Nr.</th><th>Bezeichnung</th><th className="num">Menge</th><th>Einheit</th>{priced && <><th className="num">Preis</th><th className="num">Summe</th></>}</tr>
        </thead>
        <tbody>
          {o.lines.map((l) => (
            <tr key={l.id}>
              <td>{l.position}</td><td>{l.articleNumber ?? ''}</td><td>{l.description}</td><td className="num">{quantity(l.quantity)}</td><td>{l.unit}</td>
              {priced && <><td className="num">{l.unitPrice != null ? money(l.unitPrice) : ''}</td><td className="num">{l.total != null ? money(l.total) : ''}</td></>}
            </tr>
          ))}
          {o.lines.length === 0 && <tr><td colSpan={priced ? 7 : 5} className="sheet-empty">Noch keine Positionen.</td></tr>}
        </tbody>
        {priced && o.total != null && (
          <tfoot><tr><td colSpan={6}>{o.lines.every((l) => l.unitPrice != null) ? 'Summe netto' : 'Summe netto der Positionen mit Preis'}</td><td className="num">{money(o.total)}</td></tr></tfoot>
        )}
      </table>
      {o.notes && <section className="sheet-notes"><span className="sheet-label">Hinweise</span><div className="pre">{o.notes}</div></section>}
      <footer className="sheet-foot">Bitte bestätigen Sie uns den Liefertermin. Vielen Dank.</footer>
    </div>
  );
}
