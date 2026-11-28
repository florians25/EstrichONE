import { describe, expect, it } from 'vitest';
import {
  documentState, filterPurchases, headerFacts, lineRows, purchaseStatus, purchaseSummary, supplierText, totalsText, unitName, uploadProblem,
  type EInvoiceData, type PurchaseItem,
} from '../webapp/util/purchases';
import { uploadText } from '../webapp/util/purchaseUpload';

const item = (over: Partial<PurchaseItem>): PurchaseItem => ({
  lexwareId: 'x', type: 'purchaseinvoice', typeName: 'Eingangsrechnung', number: 'ER-1', voucherDate: '2026-11-10', dueDate: null, gross: 100, openAmount: 100,
  status: 'open', contactName: 'Baustoffe Inn GmbH', lexwareContactId: 'c1', supplierId: null, supplierName: null, supplierLinkedByHand: false, bauakteId: null,
  bauakteNumber: null, bauakteTitle: null, lexwareUrl: null, ...over,
});

const sp = (v: string) => v.replace(/\u00a0/g, ' ');
const data: EInvoiceData = {
  format: 'xrechnung-ubl', creditNote: false, number: 'RE-88', issueDate: '2026-11-20', dueDate: null, sellerName: 'Baustoffe Inn GmbH', sellerVatId: 'DE123456789',
  sellerStreet: 'Werkstraße 5', sellerPostalCode: '83026', sellerCity: 'Rosenheim', buyerName: null, buyerReference: '04011000-12345-67', currency: 'EUR',
  netTotal: 100, taxTotal: 19, grossTotal: 119, duePayable: 119, lines: [{ name: 'Randdämmstreifen', quantity: 4, unit: 'C62', netPrice: 25, lineNet: 100, taxRate: 19 }],
  problems: [],
};

describe('Eingangsbelege', () => {
  it('zeigt Status und Stand verständlich, unbekannte Werte neutral', () => {
    expect(purchaseStatus('overdue')).toEqual({ text: 'Überfällig', state: 'Error' });
    expect(purchaseStatus('paidoff').text).toBe('Bezahlt');
    expect(purchaseStatus('neu').state).toBe('None');
    expect(documentState({ state: 'uploaded', problemCount: 2, format: 'zugferd' }).text).toBe('Hochgeladen, Angaben prüfen');
    expect(documentState({ state: 'sent', problemCount: 0, format: 'none' }).state).toBe('Success');
  });

  it('prüft Dateien vor dem Hochladen', () => {
    expect(uploadProblem({ name: 'beleg.PDF', size: 10 })).toBeNull();
    expect(uploadProblem({ name: 'rechnung.xml', size: 10 })).toBeNull();
    expect(uploadProblem({ name: 'tabelle.xlsx', size: 10 })).toContain('PDF, Foto (JPEG oder PNG) oder XRechnung');
    expect(uploadProblem({ name: 'foto.jpg', size: 6 * 1024 * 1024 })).toContain('größer als 5 MB');
  });

  it('filtert und fasst die Auswahl als Textzeile zusammen (Gutschriften negativ, stornierte nicht)', () => {
    const items = [item({}), item({ lexwareId: 'y', type: 'purchasecreditnote', gross: 20, openAmount: 0, bauakteId: 'b1' }), item({ lexwareId: 'z', status: 'voided', gross: 500 })];
    expect(sp(purchaseSummary(items))).toBe('3 Belege, brutto 80,00 €, davon offen 100,00 €, 2 ohne Bauakte.');
    const filter = { tab: 'lexware' as const, query: '', supplier: '', bauakte: 'none', status: 'all' };
    expect(filterPurchases(items, filter).map((i) => i.lexwareId)).toEqual(['x', 'z']);
    expect(filterPurchases(items, { ...filter, bauakte: '', status: 'open' }).map((i) => i.lexwareId)).toEqual(['x', 'y']);
    expect(supplierText(item({ supplierName: 'Inn Baustoffe' }))).toBe('Baustoffe Inn GmbH (Lieferant Inn Baustoffe)');
  });

  it('stellt die E-Rechnung dar: Kopfdaten, Positionen mit Einheiten, Summen', () => {
    expect(headerFacts(data).map((f) => f.label)).toEqual(['Rechnungssteller', 'Anschrift', 'USt-IdNr.', 'Leitweg-ID bzw. Käuferreferenz', 'Rechnungsnummer', 'Rechnungsdatum']);
    expect(Object.fromEntries(Object.entries(lineRows(data)[0]).map(([k, v]) => [k, sp(v)]))).toEqual({ name: 'Randdämmstreifen', quantity: '4 Stück', price: '25,00 €', net: '100,00 €', tax: '19 %' });
    expect(unitName('MTK')).toBe('m²');
    expect(unitName('XYZ')).toBe('XYZ');
    expect(sp(totalsText(data))).toBe('Netto 100,00 €, Umsatzsteuer 19,00 €, Brutto 119,00 €');
    expect(uploadText({ uploaded: [{ document: {} as never, note: null, data: { ...data, problems: ['x'] }, canEdit: true }], duplicates: 1, problems: [] }))
      .toBe('1 Beleg hochgeladen. 1 Datei war schon vorhanden. Bei 1 Beleg fehlen Pflichtangaben; bitte prüfen.');
  });
});
