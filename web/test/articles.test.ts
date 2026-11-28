import { describe, expect, it } from 'vitest';
import { changedPrice, filterArticles, grossOf, netOf, sortArticles, type CatalogItem } from '../webapp/util/quotations';

const item = (over: Partial<CatalogItem>): CatalogItem => ({
  id: 'x', title: 'Eintrag', description: null, kind: 'service', articleNumber: null, gtin: null, unit: 'm²', netPrice: 10, grossPrice: null, taxRate: 19,
  materialId: null, materialName: null, isActive: true, origin: 'estrichOne', lexwareId: null, lexwareUrl: null,
  sync: { state: 'local', syncedAt: null, deviations: [], error: null }, version: 1, ...over,
});

describe('Artikel und Leistungen: Preise, Filter, Sortierung', () => {
  it('rechnet brutto und netto um', () => {
    expect(grossOf(24.5, 19)).toBe(29.16);
    expect(netOf(29.16, 19)).toBe(24.5042);
    expect(grossOf(10, 7)).toBe(10.7);
  });

  it('ändert Preise um Prozent mit Rundung', () => {
    expect(changedPrice(24.5, 3)).toBe(25.24);
    expect(changedPrice(24.5, 3, 'tenCent')).toBe(25.2);
    expect(changedPrice(24.5, 3, 'euro')).toBe(25);
    expect(changedPrice(10, -5)).toBe(9.5);
  });

  it('filtert nach Art, Einheit, Abgleichstand, aktiv und Suche', () => {
    const items = [
      item({ id: 'a', title: 'Zementestrich', articleNumber: 'L-100', sync: { state: 'synced', syncedAt: null, deviations: [], error: null } }),
      item({ id: 'b', title: 'Randdämmstreifen', kind: 'product', unit: 'lfm' }),
      item({ id: 'c', title: 'Alt', isActive: false }),
    ];
    expect(filterArticles(items, { active: 'active' }).map((i) => i.id)).toEqual(['a', 'b']);
    expect(filterArticles(items, { active: 'inactive' }).map((i) => i.id)).toEqual(['c']);
    expect(filterArticles(items, { active: 'all', kind: 'product' }).map((i) => i.id)).toEqual(['b']);
    expect(filterArticles(items, { active: 'all', unit: 'm²', sync: 'synced' }).map((i) => i.id)).toEqual(['a']);
    expect(filterArticles(items, { active: 'all', query: 'l-100' }).map((i) => i.id)).toEqual(['a']);
  });

  it('sortiert nach Spalten, auch nach Preis brutto und absteigend', () => {
    const items = [item({ id: 'a', title: 'B', netPrice: 5 }), item({ id: 'b', title: 'a', netPrice: 20, taxRate: 7 }), item({ id: 'c', title: 'C', netPrice: 18 })];
    expect(sortArticles(items, 'title').map((i) => i.id)).toEqual(['b', 'a', 'c']);
    expect(sortArticles(items, 'grossPrice', true).map((i) => i.id)).toEqual(['c', 'b', 'a']);
  });
});
