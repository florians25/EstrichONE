import { describe, expect, it } from 'vitest';
import {
  assignedText, createAnywayQuestion, dueLabel, euro, lastTestText, lexwareVisible, linkText, matchLabel, placeText, reconciliationFigures, syncFieldVisible,
  syncInitial, syncStateText, uniquePairs, voucherNumber, voucherStatus, voucherSummary, voucherTypeText, type LexwareReconciliation,
} from '../webapp/util/lexware';
import { adminAreas, matchesArea } from '../webapp/util/adminCatalog';
import { routes } from '../webapp/util/routes';
import { filterPermissions } from '../webapp/util/modules';
import { auditTexts } from '../webapp/util/labels';

const nbsp = (text: string) => text.replace(/ /g, ' ');
describe('Lexware Office: Belege (nur Anzeige)', () => {
  it('Belegart als Text', () => {
    expect(voucherTypeText('invoice')).toBe('Rechnung');
    expect(voucherTypeText('quotation')).toBe('Angebot');
    expect(voucherTypeText('creditnote')).toBe('Gutschrift');
    expect(voucherTypeText('orderconfirmation')).toBe('Auftragsbestätigung');
    expect(voucherTypeText('neu')).toBe('Beleg');
    expect(dueLabel('quotation')).toBe('gültig bis');
    expect(dueLabel('invoice')).toBe('fällig');
  });

  it('Status mit Text und Farbe, bei Angeboten angenommen und abgelehnt', () => {
    expect(voucherStatus('draft')).toMatchObject({ text: 'Entwurf', state: 'None' });
    expect(voucherStatus('open')).toMatchObject({ text: 'Offen', state: 'Information' });
    expect(voucherStatus('overdue')).toMatchObject({ text: 'Überfällig', state: 'Error' });
    expect(voucherStatus('paid')).toMatchObject({ text: 'Bezahlt', state: 'Success' });
    expect(voucherStatus('paidoff').text).toBe('Bezahlt');
    expect(voucherStatus('accepted')).toMatchObject({ text: 'Angenommen', state: 'Success' });
    expect(voucherStatus('rejected')).toMatchObject({ text: 'Abgelehnt', state: 'Error' });
    expect(voucherStatus('voided')).toMatchObject({ text: 'Storniert', state: 'Warning' });
    expect(voucherStatus('neu-von-lexware').text).toBe('Unbekannt');
  });

  it('Nummer: Entwürfe haben noch keine', () => {
    expect(voucherNumber({ number: null, status: 'draft' })).toBe('wird automatisch vergeben');
    expect(voucherNumber({ number: 'RE1042', status: 'open' })).toBe('RE1042');
    expect(voucherNumber({ number: null, status: 'open' })).toBe('ohne Nummer');
  });

  it('Beträge in Euro, leer ohne Wert', () => {
    expect(nbsp(euro(5508.81))).toBe('5.508,81 €');
    expect(nbsp(euro(0))).toBe('0,00 €');
    expect(euro(null)).toBe('');
    expect(euro(undefined)).toBe('');
  });

  it('Summen als dezenter Text: Rechnungen und Angebote getrennt', () => {
    expect(voucherSummary([])).toBe('');
    expect(nbsp(voucherSummary([{ type: 'invoice', status: 'draft', grossAmount: 100 }]))).toBe('1 Rechnung: 1 im Entwurf');
    expect(nbsp(voucherSummary([
      { type: 'invoice', status: 'open', grossAmount: 1000 }, { type: 'invoice', status: 'overdue', grossAmount: 500.5 },
      { type: 'invoice', status: 'paid', grossAmount: 2000 }, { type: 'invoice', status: 'voided', grossAmount: 99 },
      { type: 'quotation', status: 'open', grossAmount: 700 }, { type: 'quotation', status: 'accepted', grossAmount: 3570 },
      { type: 'quotation', status: 'rejected', grossAmount: 1 }, { type: 'creditnote', status: 'open', grossAmount: 50 },
    ]))).toBe('4 Rechnungen: offen 1.500,50 €, davon überfällig 500,50 €, bezahlt 2.000,00 €. 3 Angebote: 1 offen, angenommen 3.570,00 €, 1 abgelehnt. 1 weiterer Beleg');
    expect(voucherSummary([{ type: 'quotation', status: 'draft', grossAmount: 5 }])).toBe('1 Angebot');
  });

  it('Zuordnungsdialog: Hinweis, wenn der Beleg schon zugeordnet ist', () => {
    expect(assignedText({ assignedBauakteId: null, assignedBauakte: null }, 'b1')).toBe('');
    expect(assignedText({ assignedBauakteId: 'b1', assignedBauakte: '2026-014 EFH Huber' }, 'b1')).toBe('Dieser Bauakte bereits zugeordnet');
    expect(assignedText({ assignedBauakteId: 'b2', assignedBauakte: '2026-020 MFH Lang' }, 'b1')).toBe('Zugeordnet: Bauakte 2026-020 MFH Lang');
  });

  it('EstrichONE kennt keinen Dialog und keine Logik zum Anlegen von Belegen', async () => {
    const logic = await import('../webapp/util/lexware') as Record<string, unknown>;
    for (const name of ['buildInvoiceRequest', 'draftTotals', 'toDraftLine', 'emptyLine', 'lineNet']) expect(logic[name]).toBeUndefined();
    const { readFileSync } = await import('node:fs');
    const dialogs = readFileSync(`${process.cwd()}/webapp/util/lexwareDialogs.ts`, 'utf8');
    expect(dialogs).not.toMatch(/openInvoiceDialog|Rechnung erstellen|Entwurf in Lexware anlegen/);
    // Schreibende Aufrufe der Oberfläche betreffen nur Kunden (verknüpfen, anlegen, übernehmen) und die Zuordnung zur Bauakte.
    const posts = [...dialogs.matchAll(/post<[^>]+>\(`([^`]+)`/g)].map((m) => m[1].replace(/\$\{[^}]+\}/g, ':id'));
    expect([...new Set(posts)].sort()).toEqual([
      'bauakten/:id/lexware/vouchers/assign', 'lexware/contacts/:id/import', 'lexware/customers/:id/create', 'lexware/customers/:id/link',
    ]);
  });
});

describe('Lexware Office: Kunden und Dublettenprüfung', () => {
  it('Übereinstimmung: eindeutig, wahrscheinlich, möglich, reiner Suchtreffer', () => {
    expect(matchLabel({ score: 100, strong: true, reasons: ['gleiche E-Mail-Adresse', 'gleicher Name und PLZ'] }))
      .toEqual({ text: 'gleiche E-Mail-Adresse, gleicher Name und PLZ', state: 'Error' });
    expect(matchLabel({ score: 80, strong: false, reasons: ['gleicher Name und PLZ'] }).state).toBe('Warning');
    expect(matchLabel({ score: 40, strong: false, reasons: ['ähnlicher Name'] }).state).toBe('Information');
    expect(matchLabel({ score: 0, strong: false, reasons: [] })).toEqual({ text: 'Suchtreffer', state: 'None' });
  });

  it('Texte zur Verknüpfung und zur bewussten Neuanlage', () => {
    expect(linkText(null)).toBe('Nicht mit Lexware Office verknüpft');
    expect(linkText({ lexwareContactId: 'x', name: 'Huber Bau GmbH', number: '10042', url: 'u', origin: 'createdInLexware', linkedAt: '2026-11-26T10:00:00Z' }))
      .toBe('Huber Bau GmbH, Kundennummer 10042, aus EstrichONE in Lexware Office angelegt');
    expect(createAnywayQuestion('Huber Bau', 1, 'lexware')).toContain('einen möglichen doppelten Eintrag in Lexware Office');
    expect(createAnywayQuestion('Huber Bau', 3, 'estrichOne')).toContain('3 mögliche doppelte Einträge in EstrichONE');
    expect(placeText({ street: 'Am Anger 7', postalCode: '83022', city: 'Rosenheim' })).toBe('Am Anger 7, 83022 Rosenheim');
    expect(placeText({ street: null, postalCode: null, city: 'Rosenheim' })).toBe('Rosenheim');
  });

  it('Abgleichübersicht: Zähler und eindeutige Treffer für die Massenaktion', () => {
    const customer = { id: 'c1', name: 'Huber', isCompany: true, customerNumber: null, street: null, postalCode: null, city: null, email: null, phone: null, archived: false, linkedLexwareContactId: null };
    const lexware = { id: 'l1', name: 'Huber', isCompany: true, customerNumber: '1', street: null, postalCode: null, city: null, email: null, phone: null, url: 'u', linkedContactId: null, linkedContactName: null };
    const rec: LexwareReconciliation = {
      loadedAt: '2026-11-26T10:00:00Z', linked: 12, onlyEstrichOne: 30, onlyLexware: 7, possibleDuplicates: 3, uniqueMatches: 1,
      linkedItems: [], onlyEstrichOneItems: [], onlyLexwareItems: [], internalDuplicates: [], truncated: false, excluded: 4, excludedItems: [],
      suggestions: [
        { customer, lexware, score: 100, reasons: ['gleiche E-Mail-Adresse'], strong: true, unique: true },
        { customer: { ...customer, id: 'c2' }, lexware: { ...lexware, id: 'l2' }, score: 55, reasons: ['gleicher Name'], strong: false, unique: false },
      ],
    };
    expect(reconciliationFigures(rec).map((f) => `${f.label}=${f.value}`)).toEqual(['Verknüpft=12', 'Mögliche Dubletten=3', 'Nur in EstrichONE=30', 'Nur in EstrichONE (bewusst)=4', 'Nur in Lexware=7']);
    expect(reconciliationFigures(rec)[1]).toMatchObject({ state: 'Warning', hint: '1 davon eindeutig' });
    expect(uniquePairs(rec)).toEqual([{ contactId: 'c1', lexwareContactId: 'l1' }]);
    expect(reconciliationFigures(null)).toEqual([]);
    expect(uniquePairs(null)).toEqual([]);
  });
});

describe('Lexware Office: Schalter „Mit Lexware Office abgleichen“', () => {
  it('nur mit eingeschaltetem Modul sichtbar', () => {
    expect(syncFieldVisible({ lexware: true })).toBe(true);
    expect(syncFieldVisible({ lexware: false })).toBe(false);
    expect(syncFieldVisible({})).toBe(false);
  });

  it('Vorbelegung: beim Anlegen die Vorgabe, beim Bearbeiten der Stand des Kunden', () => {
    expect(syncInitial(null, { syncDefault: true })).toBe(true);
    expect(syncInitial(null, { syncDefault: false })).toBe(false);
    expect(syncInitial(null, null)).toBe(false);
    expect(syncInitial({ lexwareSync: false }, { syncDefault: true })).toBe(false);
    expect(syncInitial({ lexwareSync: true }, { syncDefault: false })).toBe(true);
    expect(syncInitial({}, { syncDefault: true })).toBe(false); // älterer Server ohne das Feld
  });

  it('Stand in der Kundenansicht, bestehende Verknüpfung bleibt sichtbar', () => {
    const link = { lexwareContactId: 'x', name: 'Huber', number: '1', url: 'u', origin: 'linked' as const, linkedAt: '2026-11-26T10:00:00Z' };
    expect(syncStateText(true, link)).toEqual({ text: 'Abgleich eingeschaltet', state: 'Success', hint: '' });
    expect(syncStateText(true, null).state).toBe('Information');
    expect(syncStateText(false, null)).toMatchObject({ text: 'Nur in EstrichONE', state: 'None' });
    expect(syncStateText(false, null).hint).toContain('bleibt nur in EstrichONE');
    expect(syncStateText(false, link)).toMatchObject({ text: 'Abgleich ausgeschaltet', state: 'Warning' });
    expect(syncStateText(false, link).hint).toContain('die Verknüpfung besteht aber noch');
  });
});

describe('Lexware Office: Einstellungen, Modul und Rechte', () => {
  it('Ergebnis des Verbindungstests', () => {
    const at = () => '26.11.2026, 10:00';
    expect(lastTestText({ lastTestAt: null, lastTestOk: null, lastTestError: null, organizationName: null }, at)).toBe('');
    expect(lastTestText({ lastTestAt: 'x', lastTestOk: true, lastTestError: null, organizationName: 'Estrichbau Muster GmbH' }, at))
      .toBe('Letzter Verbindungstest am 26.11.2026, 10:00 erfolgreich: Estrichbau Muster GmbH');
    expect(lastTestText({ lastTestAt: 'x', lastTestOk: false, lastTestError: 'Der API-Schlüssel wurde abgelehnt.', organizationName: null }, at))
      .toBe('Letzter Verbindungstest am 26.11.2026, 10:00 fehlgeschlagen: Der API-Schlüssel wurde abgelehnt.');
  });

  it('Abschnitte nur mit eingeschaltetem Modul und Leserecht', () => {
    expect(lexwareVisible({ lexware: true }, { 'invoices.read': true })).toBe(true);
    expect(lexwareVisible({ lexware: false }, { 'invoices.read': true })).toBe(false);
    expect(lexwareVisible({}, { 'invoices.read': true })).toBe(false); // älterer Server ohne das Modul
    expect(lexwareVisible({ lexware: true }, {})).toBe(false);
    expect(filterPermissions({ lexware: false }, ['invoices.read', 'invoices.assign', 'contacts.read'])).toEqual(['contacts.read']);
    expect(filterPermissions({ lexware: true }, ['invoices.read', 'contacts.read'])).toEqual(['invoices.read', 'contacts.read']);
  });

  it('Seite unter Integrationen mit eigenem Recht und auffindbar', () => {
    const area = adminAreas.find((a) => a.key === 'lexwareSettings')!;
    expect(area.group).toBe('groupAdminIntegrations');
    expect(area.permission).toBe('settings.integrations');
    expect(routes.find((r) => r.pattern === area.path)?.permission).toBe('settings.integrations');
    expect(matchesArea(area, 'lexoffice')).toBe(true);
    expect(matchesArea(area, 'e-rechnung')).toBe(true);
  });

  it('Protokolltexte vorhanden und ohne Gedankenstriche oder Trennpunkte', () => {
    const texts = Object.entries(auditTexts).filter(([key]) => key.startsWith('lexware.'));
    expect(texts.length).toBe(10);
    for (const [, text] of texts) expect(text).not.toMatch(/[–—·]/);
  });
});
