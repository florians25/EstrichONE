import { describe, expect, it } from 'vitest';
import {
  convertText, filterItems, lineNet, numberText, periodText, planProgress, searchCatalog, selectionText, statusText, subtotal, totals, type SalesLine, type SalesListItem,
} from '../webapp/util/sales';

const item = (quantity: number, netPrice: number, taxRate = 19, discountPercent: number | null = null): SalesLine =>
  ({ kind: 'item', name: 'x', quantity, netPrice, taxRate, discountPercent });

describe('Verkauf: Summen, Zwischensummen und Abzüge', () => {
  it('rechnet Positionen, Rabatt und Abzug wie der Server', () => {
    expect(lineNet(item(120.5, 24))).toBe(2892);
    expect(lineNet(item(10, 3.333, 7, 10))).toBe(30);
    expect(lineNet({ kind: 'deduction', quantity: 5, netPrice: -1000, discountPercent: 50 })).toBe(-1000);
    expect(lineNet({ kind: 'text' })).toBe(0);
  });

  it('bildet Zwischensummen seit der letzten Zwischensumme', () => {
    const lines: SalesLine[] = [item(10, 10), item(2, 5), { kind: 'subtotal', name: 'Zwischensumme' }, item(1, 50), { kind: 'subtotal', name: 'Zwischensumme' }];
    expect(subtotal(lines, 2)).toBe(110);
    expect(subtotal(lines, 4)).toBe(50);
  });

  it('weist Steuer je Satz aus, Abzüge mindern, steuerfrei und § 13b ohne Steuer', () => {
    const lines: SalesLine[] = [item(100, 25), item(10, 10, 7), { kind: 'deduction', name: 'Abzug', netPrice: -1000, taxRate: 19 }];
    const net = totals(lines, 'net');
    expect(net.taxLines).toEqual([{ rate: 19, net: 1500, tax: 285 }, { rate: 7, net: 100, tax: 7 }]);
    expect(net.gross).toBe(1892);
    expect(totals(lines, 'vatfree').tax).toBe(0);
    expect(totals(lines, 'constructionService13b').gross).toBe(1600);
  });
});

describe('Verkauf: Übersicht und Texte', () => {
  const rows: SalesListItem[] = [
    { id: '1', lexwareId: 'a', type: 'invoice', typeName: 'Rechnung', number: 'RE-1', title: 'Halle', contactId: 'c1', customerName: 'Huber Bau', bauakteId: 'b1',
      bauakteNumber: '2026-041', bauakteTitle: 'Halle Huber', voucherDate: '2026-11-02', dueDate: '2026-11-16', net: 1000, gross: 1190, openAmount: 1190, status: 'overdue', lexwareUrl: null },
    { id: null, lexwareId: 'b', type: 'quotation', typeName: 'Angebot', number: 'AG-7', title: null, contactId: 'c2', customerName: 'Moser', bauakteId: null,
      bauakteNumber: null, bauakteTitle: null, voucherDate: '2026-10-01', dueDate: null, net: 500, gross: 595, openAmount: null, status: 'accepted', lexwareUrl: 'x' },
    { id: '3', lexwareId: null, type: 'invoice', typeName: 'Abschlagsrechnung', number: null, title: '1. Abschlagsrechnung', contactId: 'c1', customerName: 'Huber Bau',
      bauakteId: 'b1', bauakteNumber: '2026-041', bauakteTitle: 'Halle Huber', voucherDate: null, dueDate: null, net: 400, gross: 476, openAmount: null, status: 'local', lexwareUrl: null },
  ];

  it('filtert nach Belegart, Status, Kunde, Bauakte und Suche', () => {
    expect(filterItems(rows, { type: 'invoice' })).toHaveLength(2);
    expect(filterItems(rows, { status: 'open' }).map((r) => r.number)).toEqual(['RE-1']);
    expect(filterItems(rows, { status: 'draft' })).toHaveLength(1);
    expect(filterItems(rows, { customer: 'c2' })).toHaveLength(1);
    expect(filterItems(rows, { bauakte: 'b1' })).toHaveLength(2);
    expect(filterItems(rows, { query: '2026-041' })).toHaveLength(2);
  });

  it('fasst die Auswahl als Textzeile zusammen', () => {
    expect(selectionText(rows).replace(/\u00a0/g, ' ')).toBe('3 Belege, netto 1.900,00 €, brutto 2.261,00 €, davon offen 1.190,00 €.');
    expect(selectionText([])).toBe('Keine Belege in dieser Auswahl.');
  });

  it('zeigt Status, Nummer, Zeitraum, Fortschritt und Umwandlung verständlich', () => {
    expect(statusText('local')).toEqual({ text: 'In EstrichONE', state: 'None' });
    expect(statusText('open', 'orderconfirmation').text).toBe('Erstellt');
    expect(statusText('overdue', 'invoice').state).toBe('Error');
    expect(numberText(null)).toBe('wird automatisch vergeben');
    expect(periodText('2026-11-02', '2026-11-20', (d) => d.split('-').reverse().join('.'))).toBe('02.11.2026 bis 20.11.2026');
    expect(periodText('2026-11-02', null, (d) => d)).toBe('2026-11-02');
    expect(planProgress({ orderNet: 10000, invoicedNet: 6500, paidGross: 4760, orderGross: 11900 })).toEqual({ billed: 65, paid: 40 });
    expect(planProgress({ orderNet: null, invoicedNet: 0, paidGross: 0, orderGross: null })).toEqual({ billed: 0, paid: 0 });
    expect(convertText('invoice')).toBe('In Rechnung umwandeln');
  });

  it('schlägt Artikel nach Bezeichnung und Nummer vor', () => {
    const catalog = [
      { id: 'a', title: 'Zementestrich CT-C25-F4', description: null, kind: 'service' as const, articleNumber: 'L-100', unit: 'm²', netPrice: 24.5, taxRate: 19, inLexware: true },
      { id: 'b', title: 'Randdämmstreifen', description: null, kind: 'product' as const, articleNumber: 'L-200', unit: 'lfm', netPrice: 0.95, taxRate: 19, inLexware: false },
    ];
    expect(searchCatalog(catalog, 'zement').map((c) => c.id)).toEqual(['a']);
    expect(searchCatalog(catalog, 'l-200').map((c) => c.id)).toEqual(['b']);
    expect(searchCatalog(catalog, '')).toHaveLength(2);
  });
});
