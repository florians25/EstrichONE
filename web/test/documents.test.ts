import { describe, expect, it } from 'vitest';
import { documentRows, folderTree, inFolder, sortRows } from '../webapp/util/documents';
import { requirements } from '../webapp/util/needs';
import { sha256Fallback } from '../webapp/util/sha256';
import { resolve, logos } from '../webapp/util/theme';
import { matchRoute } from '../webapp/util/router';
import { routes } from '../webapp/util/routes';
import type { FileResponse, RoomOverview } from '../webapp/util/models';

const file = (id: string, extra: Partial<FileResponse> = {}): FileResponse => ({
  id, bauakteId: 'b', kind: 'document', state: 'stored', variants: 'notApplicable', originalName: `${id}.pdf`, contentType: 'application/pdf',
  sizeBytes: 100, caption: null, capturedAt: null, createdAt: `2026-10-0${id.length}T08:00:00Z`, uploadedByUserId: 'u', category: 'offer',
  folder: null, roomId: null, workItemId: null, previousVersionId: null, latitude: null, longitude: null, version: 1, ...extra,
});

describe('Dokumentablage', () => {
  it('erkennt Fassungen über den Vorgänger', () => {
    const rows = documentRows([file('a'), file('bb', { previousVersionId: 'a' }), file('ccc', { previousVersionId: 'bb' })]);
    const latest = rows.find((r) => r.id === 'ccc')!;
    expect(latest.versionNumber).toBe(3);
    expect(latest.superseded).toBe(false);
    expect(latest.chain).toEqual(['ccc', 'bb', 'a']);
    expect(rows.find((r) => r.id === 'a')!.superseded).toBe(true);
  });

  it('baut den Ordnerbaum mit Unterordnern und zählt nur aktuelle Fassungen', () => {
    const rows = documentRows([file('a', { folder: 'Pläne/EG' }), file('bb', { folder: 'Pläne/OG' }), file('ccc', { category: 'plan' })]);
    const tree = folderTree(rows);
    expect(tree.count).toBe(3);
    const offer = tree.nodes.find((n) => n.key === 'offer')!;
    expect(offer.count).toBe(2);
    expect(offer.nodes[0].text).toBe('Pläne');
    expect(offer.nodes[0].count).toBe(2);
    expect(offer.nodes[0].nodes.map((n) => n.text)).toEqual(['EG', 'OG']);
    expect(rows.filter((r) => inFolder(r, 'offer', 'Pläne')).length).toBe(2);
    expect(sortRows(rows, 'name').map((r) => r.id)).toEqual(['a', 'bb', 'ccc']);
  });
});

describe('Materialbedarf', () => {
  it('Volumen aus Fläche und Dicke mit Reserve, auf 0,5 m³ aufgerundet', () => {
    const rooms = { sections: [], totalAreaSquareMeters: 42.5, heatedAreaSquareMeters: 42.5, rooms: [
      { id: 'r', sectionId: null, name: 'Wohnen', floor: 'EG', areaSquareMeters: 42.5, screedTypeId: null, screedTypeName: 'CT-C25-F4',
        thicknessMillimeters: 65, insulationMillimeters: 30, underfloorHeating: true, status: 'planned', notes: null, version: 1 },
    ] } as RoomOverview;
    const needs = requirements(rooms, 5);
    expect(needs[0]).toMatchObject({ description: 'CT-C25-F4', amount: 3, unit: 'm³' });
    expect(needs[1]).toMatchObject({ description: 'Dämmung 30 mm', amount: 45, unit: 'm²' });
  });
});

describe('SHA-256 ohne WebCrypto', () => {
  it('liefert die bekannten Prüfsummen', () => {
    expect(sha256Fallback(new TextEncoder().encode(''))).toBe('e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
    expect(sha256Fallback(new TextEncoder().encode('abc'))).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
  });
});

describe('Themes und Logos', () => {
  it('Systemeinstellung folgt hell/dunkel, Kontrast-Themes nutzen einfarbige Logos', () => {
    expect(resolve('system', true)).toBe('sap_horizon_dark');
    expect(resolve('system', false)).toBe('sap_horizon');
    expect(resolve('sap_fiori_3_hcb')).toBe('sap_fiori_3_hcb');
    expect(resolve('sap_belize')).toBe('sap_horizon');
    expect(logos('sap_horizon_hcb').shell).toBe('/logo-mark-white.svg');
    expect(logos('sap_fiori_3_hcw').shell).toBe('/logo-mark-black.svg');
    expect(logos('sap_horizon_dark').horizontal).toBe('/logo-horizontal-white.svg');
  });
});

describe('Pfad-Router', () => {
  it('ordnet Deep-Links den Seiten zu', () => {
    expect(matchRoute(routes, '/bauakten/karte')?.route.name).toBe('siteMap');
    expect(matchRoute(routes, '/bauakten/123')?.params).toEqual({ id: '123' });
    expect(matchRoute(routes, '/bauakten/123/datei/456')?.params).toEqual({ id: '123', section: 'datei', item: '456' });
    expect(matchRoute(routes, '/bauakten/123/bericht')?.route.name).toBe('report');
    expect(matchRoute(routes, '/unbekannt')).toBeNull();
  });
});
