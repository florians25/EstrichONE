import { describe, expect, it } from 'vitest';
import {
  articleSyncHelp, articleSyncOptions, catalogSyncText, lineFromCatalog, lineFromSuggestion, lineNet, quotationStateText, quotationTotals, reviewHint, syncResultText,
  BETA_TEXT, statusRuleTexts, taxTypeOptions,
} from '../webapp/util/quotations';
import { filterPermissions, modulesOfPath, pathEnabled } from '../webapp/util/modules';
import { routes } from '../webapp/util/routes';

describe('Angebote (Beta): Summen', () => {
  it('rechnet Positionen mit Rabatt und Steuer je Satz', () => {
    expect(lineNet({ quantity: 120.5, netPrice: 24, discountPercent: null })).toBe(2892);
    expect(lineNet({ quantity: 10, netPrice: 3.333, discountPercent: 10 })).toBe(30);
    const totals = quotationTotals([
      { kind: 'item', name: 'Estrich', quantity: 120.5, netPrice: 24, taxRate: 19 },
      { kind: 'item', name: 'Folie', quantity: 10, netPrice: 3.333, taxRate: 7, discountPercent: 10 },
      { kind: 'text', name: 'Hinweis' },
    ], 'net');
    expect(totals.net).toBe(2922);
    expect(totals.tax).toBe(549.48 + 2.1);
    expect(totals.gross).toBe(totals.net + totals.tax);
    expect(totals.taxLines.map((t) => t.rate)).toEqual([19, 7]);
  });

  it('weist bei § 13b keine Steuer aus', () => {
    const totals = quotationTotals([{ kind: 'item', name: 'Estrich', quantity: 30, netPrice: 25, taxRate: 19 }], 'constructionService13b');
    expect(totals).toMatchObject({ net: 750, tax: 0, gross: 750 });
    expect(taxTypeOptions.map((o) => o.key)).toEqual(['net', 'constructionService13b', 'vatfree']);
  });

  it('übernimmt Katalogeinträge und Vorschläge als Positionen', () => {
    expect(lineFromCatalog({ id: 'c1', title: 'Zementestrich', description: null, kind: 'service', articleNumber: 'L-1', unit: 'm²', netPrice: 24, taxRate: 19, inLexware: true }, 2))
      .toMatchObject({ kind: 'item', catalogItemId: 'c1', quantity: 2, unit: 'm²', netPrice: 24 });
    expect(lineFromSuggestion({ source: 'rooms', hint: 'Summe', name: 'Estrich', description: null, quantity: 142.5, unit: 'm²', catalogItemId: null, netPrice: null, taxRate: null }))
      .toMatchObject({ quantity: 142.5, netPrice: 0, taxRate: 19 });
  });

  it('nennt Stand und Beta-Kennzeichnung', () => {
    expect(quotationStateText('sent').text).toBe('An Lexware Office gesendet');
    expect(quotationStateText('failed').state).toBe('Error');
    expect(BETA_TEXT).toContain('Entwurf');
  });
});

describe('Artikel und Leistungen', () => {
  it('zeigt den Abgleichstand', () => {
    expect(catalogSyncText({ state: 'deviations', syncedAt: null, deviations: [{ field: 'unitName', label: 'Einheit', estrichOne: 'qm', lexware: 'm²' }], error: null }).text)
      .toBe('Abweichungen (1)');
    expect(catalogSyncText({ state: 'local', syncedAt: null, deviations: [], error: null }).text).toBe('Nur in EstrichONE');
    expect(articleSyncOptions[0].key).toBe('fromLexware');
    expect(articleSyncHelp('fromLexware')).toContain('schreibt EstrichONE nichts');
    expect(syncResultText({ imported: 2, created: 0, updated: 0, deviations: 0, duplicates: 1, at: '' })).toContain('2 übernommen');
  });
});

describe('Module, Rechte und Seiten', () => {
  it('blendet Angebote und Artikel mit ihren Modulen aus', () => {
    expect(modulesOfPath('/angebote/abc')).toEqual(['sales']);
    expect(pathEnabled({ quotations: false, lexwareArticles: false, sales: false }, '/artikel')).toBe(false);
    expect(pathEnabled({ quotations: true, lexwareArticles: false }, '/artikel')).toBe(true);
    expect(filterPermissions({ quotations: false }, ['quotations.prepare', 'invoices.read'])).toEqual(['invoices.read']);
    expect(routes.find((r) => r.name === 'quotation')?.anyPermission).toContain('quotations.prepare');
    expect(routes.find((r) => r.name === 'sales')?.view).toBe('sales.Sales');
  });

  it('erklärt Regeln und Kennzeichen „Zur Abschlussprüfung“', () => {
    expect(reviewHint({ reviewRequestedAt: '2026-11-27T10:00:00Z', reviewReason: 'Rechnungen vollständig bezahlt, bitte prüfen und abschließen' })).toContain('bezahlt');
    expect(reviewHint({ reviewRequestedAt: null })).toBe('');
    expect(statusRuleTexts.invoices).toContain('bewusste Handlung');
  });

  it('enthält keine Gedankenstriche in den neuen Texten', () => {
    const texts = [BETA_TEXT, ...Object.values(statusRuleTexts), ...articleSyncOptions.map((o) => o.text), ...taxTypeOptions.map((o) => o.text),
      articleSyncHelp('both'), articleSyncHelp('off'), articleSyncHelp('fromLexware')];
    for (const t of texts) expect(t).not.toMatch(/[–—·]/);
  });
});
