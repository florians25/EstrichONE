import { describe, expect, it } from 'vitest';
import { fixAction, quotationTotals, taxTypeHint, taxTypeOptions, withoutTax, type TaxTypeOption } from '../webapp/util/quotations';
import { taxTypesText } from '../webapp/util/lexware';

describe('Lexware Office: Fehler beheben und erlaubte Steuerarten', () => {
  it('bietet bei abgelehnter Steuerart § 13b die Netto-Behebung an', () => {
    expect(fixAction('taxTypeNet')).toEqual({ text: 'Steuerart Netto übernehmen und erneut senden', taxType: 'net' });
    expect(fixAction('taxTypeVatfree')?.taxType).toBe('vatfree');
    expect(fixAction(null)).toBeNull();
    expect(fixAction('unbekannt')).toBeNull();
  });

  it('zeigt den Hinweis der gesperrten Steuerart', () => {
    const options: TaxTypeOption[] = taxTypeOptions.map((o) => o.key === 'constructionService13b'
      ? { ...o, allowed: false, hint: 'Lexware Office hat diese Steuerart am 28.11.2026 für Angebote abgelehnt.' } : o);
    expect(taxTypeHint(options, 'constructionService13b')).toContain('abgelehnt');
    expect(taxTypeHint(options, 'net')).toBe('Bauleistung nach § 13b UStG: Lexware Office hat diese Steuerart am 28.11.2026 für Angebote abgelehnt.');
    expect(taxTypeHint(taxTypeOptions, 'net')).toBe('');
  });

  it('rechnet steuerfrei und § 13b ohne Umsatzsteuer', () => {
    const lines = [{ kind: 'item' as const, name: 'Estrich', quantity: 10, netPrice: 20, taxRate: 19 }];
    expect(withoutTax('vatfree')).toBe(true);
    expect(withoutTax('net')).toBe(false);
    expect(quotationTotals(lines, 'vatfree')).toMatchObject({ net: 200, tax: 0, gross: 200 });
    expect(quotationTotals(lines, 'net')).toMatchObject({ net: 200, tax: 38, gross: 238 });
  });

  it('beschreibt den Stand der Steuerarten in den Einstellungen', () => {
    const format = (iso: string) => iso.slice(0, 10);
    expect(taxTypesText({ smallBusiness: true, profileTaxType: 'vatfree', checkedAt: '2026-11-28T10:00:00Z', rejections: [] }, format)).toContain('Kleinunternehmer');
    const text = taxTypesText({
      smallBusiness: false, profileTaxType: 'net', checkedAt: '2026-11-28T10:00:00Z',
      rejections: [{ voucherType: 'quotation', voucherName: 'Angebote', taxType: 'constructionService13b', taxTypeName: 'Bauleistung nach § 13b UStG', at: '2026-11-27T09:00:00Z' }],
    }, format);
    expect(text).toContain('Abgelehnt am 2026-11-27: Bauleistung nach § 13b UStG für Angebote.');
    expect(text).toContain('Zuletzt geprüft: 2026-11-28.');
    expect(taxTypesText(null, format)).toBe('');
  });
});

import { voucherStatus, voucherTypeText } from '../webapp/util/lexware';

describe('Lexware Office: Status je Belegart', () => {
  it('zeigt Auftragsbestätigung und Lieferschein nie als offen', () => {
    expect(voucherStatus('open', 'orderconfirmation')).toMatchObject({ text: 'Erstellt', state: 'None' });
    expect(voucherStatus('open', 'deliverynote').text).toBe('Erstellt');
    expect(voucherStatus('voided', 'deliverynote').text).toBe('Storniert');
    expect(voucherStatus('draft', 'orderconfirmation').text).toBe('Entwurf');
    expect(voucherTypeText('deliverynote')).toBe('Lieferschein');
  });

  it('behält die Begriffe bei Rechnung und Angebot', () => {
    expect(voucherStatus('open', 'invoice').text).toBe('Offen');
    expect(voucherStatus('overdue', 'invoice').text).toBe('Überfällig');
    expect(voucherStatus('open', 'quotation').text).toBe('Offen');
    expect(voucherStatus('accepted', 'quotation').text).toBe('Angenommen');
    expect(voucherStatus('etwasneues', 'invoice')).toMatchObject({ text: 'Unbekannt', state: 'None' });
  });
});
