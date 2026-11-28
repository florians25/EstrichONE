import { describe, expect, it } from 'vitest';
import {
  assignedText, createAnywayQuestion, dueLabel, euro, lastTestText, lexwareVisible, linkText, matchLabel, placeText, reconciliationFigures, syncFieldVisible,
  syncInitial, syncStateText, uniquePairs, voucherNumber, voucherStatus, voucherSummary, voucherTypeText, type LexwareReconciliation,
  changeSyncHelp, changeSyncOptions, changeSyncStand, changeSyncState, deviationSummary, deviationValue, pdfAvailable, PDF_DRAFT_HINT, resolveRequest,
  voucherPdfPath, voucherTitle, type LexwareSyncState, bauakteLexwareBlock, type BauakteLexware,
  creationProblem, creationStateText, enableSyncQuestion, existsQuestion, LEXWARE_CHECK_RUNNING, UNCHECKED_QUESTION, lexwareCandidateAction, lexwareCandidateLine, lexwareCheckActive, lexwareCheckRequest, lexwareCheckText, LEXWARE_EXISTS_WARNING,
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
    // Schreibende Aufrufe der Oberfläche betreffen nur Kunden (verknüpfen, anlegen, übernehmen, Abweichungen entscheiden) und die Zuordnung zur Bauakte.
    const posts = [...dialogs.matchAll(/post<[^>]+>\(`([^`]+)`/g)].map((m) => m[1].replace(/\$\{[^}]+\}/g, ':id'));
    expect([...new Set(posts)].sort()).toEqual([
      'bauakten/:id/lexware/vouchers/assign', 'lexware/contacts/:id/import', 'lexware/customers/:id/create', 'lexware/customers/:id/link',
      'lexware/customers/:id/sync/resolve',
    ]);
    // Das Beleg-PDF wird nur gelesen.
    expect(dialogs).toMatch(/getBlob\(voucher\.path\)/);
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
    expect(reconciliationFigures(rec).map((f) => `${f.label}=${f.value}`)).toEqual(['Verknüpft=12', 'Abweichungen=0', 'Mögliche Dubletten=3', 'Nur in EstrichONE=30', 'Nur in EstrichONE (bewusst)=4', 'Nur in Lexware=7']);
    expect(reconciliationFigures(rec)[2]).toMatchObject({ state: 'Warning', hint: '1 davon eindeutig' });
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
    expect(texts.length).toBe(23);
    expect(auditTexts['lexware.contact_synced']).toBe('Kundendaten mit Lexware Office abgeglichen');
    for (const [, text] of texts) expect(text).not.toMatch(/[–—·]/);
  });
});

describe('Lexware Office: Änderungsabgleich verknüpfter Kunden', () => {
  const at = (iso: string) => `am ${iso.slice(0, 10)}`;
  const sync = (state: string, extra: Partial<LexwareSyncState> = {}): LexwareSyncState => ({ state, syncedAt: '2026-11-26T10:00:00Z', deviations: [], error: null, ...extra });
  const street = { field: 'street', label: 'Straße', estrichOne: 'Altweg 1', lexware: 'Neuweg 2' };
  const phone = { field: 'phone', label: 'Telefon', estrichOne: null, lexware: '08031 1111' };

  it('Richtungen: vier Möglichkeiten, Vorgabe beide, mit Erläuterung', () => {
    expect(changeSyncOptions.map((o) => o.key)).toEqual(['both', 'toLexware', 'fromLexware', 'off']);
    expect(changeSyncOptions[0].text).toBe('Beide Richtungen');
    expect(changeSyncHelp('both')).toContain('entscheiden Sie im Kundenabgleich');
    expect(changeSyncHelp('toLexware')).toContain('nicht von selbst übernommen');
    expect(changeSyncHelp('fromLexware')).toContain('nicht von selbst übertragen');
    expect(changeSyncHelp('off')).toContain('nicht abgeglichen');
  });

  it('Stand des letzten Abgleichs in den Einstellungen', () => {
    expect(changeSyncStand({ changeSync: 'off', lastChangeSyncAt: null, lastChangeSyncError: 'x' }, at).text).toBe('');
    expect(changeSyncStand({ changeSync: 'both', lastChangeSyncAt: null, lastChangeSyncError: null }, at).text).toContain('in Kürze');
    expect(changeSyncStand({ changeSync: 'both', lastChangeSyncAt: '2026-11-26T10:00:00Z', lastChangeSyncError: null }, at))
      .toEqual({ text: 'Letzter vollständiger Änderungsabgleich: am 2026-11-26', state: 'None' });
    expect(changeSyncStand({ changeSync: 'both', lastChangeSyncAt: '2026-11-26T10:00:00Z', lastChangeSyncError: 'nicht erreichbar' }, at))
      .toMatchObject({ state: 'Error', text: 'Der letzte Änderungsabgleich ist fehlgeschlagen: nicht erreichbar' });
  });

  it('Stand am Kunden: abgeglichen, Abweichungen, ausstehend, Fehler, Kontakt fehlt oder archiviert', () => {
    expect(changeSyncState(null, at).text).toBe('');
    expect(changeSyncState(sync('synced'), at)).toMatchObject({ text: 'Abgeglichen', state: 'Success', hint: 'Letzter Abgleich: am 2026-11-26' });
    expect(changeSyncState(sync('deviations', { deviations: [street] }), at)).toMatchObject({ text: '1 Abweichung offen', state: 'Warning' });
    const two = changeSyncState(sync('deviations', { deviations: [street, phone] }), at);
    expect(two.text).toBe('2 Abweichungen offen');
    expect(two.hint).toContain('Es wurde nichts überschrieben.');
    expect(changeSyncState(sync('pending', { syncedAt: null }), at)).toMatchObject({ text: 'Abgleich steht aus', state: 'Information' });
    expect(changeSyncState(sync('pending', { syncedAt: null }), at).hint).toContain('Noch nicht abgeglichen');
    expect(changeSyncState(sync('error', { error: 'Lexware Office hat die Angaben nicht angenommen.' }), at).hint).toContain('nicht angenommen');
    expect(changeSyncState(sync('missing'), at)).toMatchObject({ text: 'Kontakt in Lexware Office nicht mehr vorhanden', state: 'Error' });
    expect(changeSyncState(sync('archived'), at)).toMatchObject({ text: 'Kontakt in Lexware Office archiviert', state: 'Warning' });
    expect(changeSyncState(sync('paused'), at).text).toBe('Abgleich ruht');
    expect(changeSyncState(sync('off'), at).text).toBe('Änderungsabgleich ausgeschaltet');
    expect(changeSyncState(sync('disabled'), at).text).toBe('');
  });

  it('Abweichungen: beide Werte, leere Werte benannt, Entscheidung je Feld', () => {
    expect(deviationValue(null)).toBe('(leer)');
    expect(deviationValue('  ')).toBe('(leer)');
    expect(deviationSummary([street, phone])).toBe('Straße: Altweg 1 / Neuweg 2; Telefon: (leer) / 08031 1111');
    expect(resolveRequest({ street: 'estrichOne', phone: '', city: 'lexware', email: undefined })).toEqual({ fields: { street: 'estrichOne', city: 'lexware' } });
    expect(resolveRequest({ street: '', phone: undefined })).toBeNull();
  });

  it('Abgleichübersicht zählt die Abweichungen', () => {
    const base = { linked: 3, possibleDuplicates: 0, uniqueMatches: 0, onlyEstrichOne: 0, onlyLexware: 0, excluded: 0 } as unknown as LexwareReconciliation;
    const tile = (r: LexwareReconciliation) => reconciliationFigures(r).find((f) => f.key === 'deviations');
    expect(tile({ ...base, deviations: 2, changeSync: 'both' })).toMatchObject({ label: 'Abweichungen', value: '2', state: 'Warning' });
    expect(tile({ ...base, deviations: 0, changeSync: 'off' })).toMatchObject({ value: '0', state: 'None', hint: 'Änderungsabgleich ausgeschaltet' });
    expect(tile(base)).toMatchObject({ value: '0' });
  });
});

describe('Lexware Office: Beleg als PDF', () => {
  it('nicht für Entwürfe, mit klarem Hinweis', () => {
    expect(pdfAvailable({ status: 'draft' })).toBe(false);
    expect(pdfAvailable({ status: 'open' })).toBe(true);
    expect(pdfAvailable({ status: 'voided' })).toBe(true);
    expect(PDF_DRAFT_HINT).toBe('Für Entwürfe stellt Lexware Office kein PDF bereit.');
  });

  it('Adresse am Kunden und an der Bauakte, Titel der Anzeige', () => {
    expect(voucherPdfPath({ customerId: 'k1', lexwareId: 'a/b' })).toBe('lexware/customers/k1/vouchers/a%2Fb/document');
    expect(voucherPdfPath({ bauakteId: 'b1', linkId: 'l1' })).toBe('bauakten/b1/lexware/vouchers/l1/document');
    expect(voucherTitle({ type: 'invoice', number: 'RE1042' })).toBe('Rechnung RE1042');
    expect(voucherTitle({ type: 'quotation', number: null })).toBe('Angebot');
  });
});

describe('Lexware Office: Block in der Übersicht der Bauakte', () => {
  const at = (iso: string) => `am ${iso.slice(0, 10)}`;
  const base: BauakteLexware = { bauakteId: 'b1', configured: true, canAssign: true, customer: { id: 'k1', name: 'Huber', lexwareContactId: null, syncEnabled: true }, vouchers: [], syncError: null };
  const contact = {
    lexwareContactId: 'l1', name: 'HUBER BAU GMBH', number: '10001', url: 'https://app.lexware.de/permalink/contacts/view/l1', street: 'Postfach 7', postalCode: '83001',
    city: 'Rosenheim', email: 'rechnung@huber.example', differingFields: ['name', 'email'],
    sync: { state: 'deviations', syncedAt: '2026-11-26T10:00:00Z', error: null, deviations: [{ field: 'name', label: 'Name', estrichOne: 'a', lexware: 'b' }, { field: 'email', label: 'E-Mail-Adresse', estrichOne: 'a', lexware: 'b' }] },
  };

  it('ohne Verknüpfung kein Block, höchstens ein Hinweis für Berechtigte', () => {
    expect(bauakteLexwareBlock(null, at)).toMatchObject({ linked: false, unlinkedHint: false });
    expect(bauakteLexwareBlock(base, at)).toMatchObject({ linked: false, unlinkedHint: true, customerId: 'k1' });
    expect(bauakteLexwareBlock({ ...base, canAssign: false }, at).unlinkedHint).toBe(false);
    expect(bauakteLexwareBlock({ ...base, customer: null }, at).unlinkedHint).toBe(false);
  });

  it('mit Verknüpfung: Kontakt, Abweichungen dezent, Abgleichstand und Summenzeile', () => {
    const block = bauakteLexwareBlock({ ...base, contact, summary: '4 Rechnungen: offen 15.767,50 €, davon überfällig 5.176,50 €. 1 Angebot: angenommen 5.355,00 €' }, at);
    expect(block).toMatchObject({
      linked: true, unlinkedHint: false, name: 'HUBER BAU GMBH', number: '10001', address: 'Postfach 7, 83001 Rosenheim', addressDiffers: false, email: 'rechnung@huber.example',
      emailDiffers: true, nameDiffers: true, stateText: '2 Abweichungen offen', stateState: 'Warning', stateHint: 'Zuletzt abgeglichen: am 2026-11-26',
    });
    expect(block.summary).toContain('davon überfällig 5.176,50 €');
    const synced = bauakteLexwareBlock({ ...base, contact: { ...contact, differingFields: [], sync: { state: 'synced', syncedAt: '2026-11-26T10:00:00Z', error: null, deviations: [] } } }, at);
    expect(synced).toMatchObject({ stateText: 'Abgeglichen', emailDiffers: false, summary: 'Dieser Bauakte ist noch kein Beleg zugeordnet.' });
    const off = bauakteLexwareBlock({ ...base, contact: { ...contact, sync: { state: 'disabled', syncedAt: null, error: null, deviations: [] } } }, at);
    expect(off.stateText).toBe('Abgleich ausgeschaltet');
  });
});

describe('Lexware Office: Prüfung beim Anlegen eines Kunden', () => {
  const contact = { id: 'l1', name: 'Malerbetrieb Kern GmbH', isCompany: true, customerNumber: '10003', street: 'Färberweg 3', postalCode: '83059', city: 'Kolbermoor',
    email: 'info@maler-kern.example', phone: null, url: 'https://app.lexware.de/permalink/contacts/view/l1', linkedContactId: null, linkedContactName: null };
  const candidate = { contact, score: 80, reasons: ['gleicher Name und PLZ'], strong: false };

  it('läuft mit Modul und Schlüssel, unabhängig vom Schalter am Kunden', () => {
    expect(lexwareCheckActive({ lexware: true }, { configured: true })).toBe(true);
    expect(lexwareCheckActive({ lexware: true }, { configured: false })).toBe(false);
    expect(lexwareCheckActive({ lexware: false }, { configured: true })).toBe(false);
    expect(lexwareCheckActive({ lexware: true }, null)).toBe(false);
  });

  it('Anfrage aus den Eingaben, erst ab drei Zeichen oder mit E-Mail-Adresse', () => {
    expect(lexwareCheckRequest({ kind: 'company', name: 'Ma' })).toBeNull();
    expect(lexwareCheckRequest({ kind: 'person', name: '', emails: ['a@b.example'] })).toMatchObject({ kind: 'person', email: 'a@b.example', name: null });
    expect(lexwareCheckRequest({ kind: 'company', name: ' Malerbetrieb Kern ', postalCode: '83059', street: null, phones: ['08031 1'], vatId: '' }))
      .toEqual({ name: 'Malerbetrieb Kern', kind: 'company', email: null, phone: '08031 1', street: null, postalCode: '83059', city: null, vatId: null });
  });

  it('deutliche Warnung bei Treffern, dezenter Hinweis ohne Verbindung', () => {
    expect(lexwareCheckText(null)).toEqual({ warning: '', note: '' });
    expect(lexwareCheckText({ checked: true, message: null, candidates: [], canImport: true }).warning).toBe('');
    expect(lexwareCheckText({ checked: true, message: null, candidates: [candidate], canImport: true }).warning).toBe(LEXWARE_EXISTS_WARNING);
    expect(LEXWARE_EXISTS_WARNING).toBe('Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.');
    expect(lexwareCheckText({ checked: true, message: null, candidates: [candidate, candidate], canImport: true }).warning).toContain('Es gibt 2 passende Kontakte.');
    const down = lexwareCheckText({ checked: false, message: 'Die Prüfung in Lexware Office ist derzeit nicht möglich.', candidates: [], canImport: false });
    expect(down.warning).toBe('');
    expect(down.note).toContain('derzeit nicht möglich');
    expect(down.note).toContain('Beim Anlegen wird erneut geprüft.');
  });

  it('Treffer: Kundennummer und Anschrift, Übernahme nur mit Recht und ohne bestehende Verknüpfung', () => {
    expect(lexwareCandidateLine(contact)).toBe('Kundennummer 10003, Färberweg 3, 83059 Kolbermoor, info@maler-kern.example');
    expect(lexwareCandidateAction(contact, true)).toBe('import');
    expect(lexwareCandidateAction(contact, false)).toBe('none');
    expect(lexwareCandidateAction({ ...contact, linkedContactId: 'k9', linkedContactName: 'Kern' }, true)).toBe('linked');
  });
});

describe('Lexware Office: automatische Anlage neuer Kunden', () => {
  it('Stand am Kunden: ausstehend, ähnlicher Kontakt, fehlgeschlagen mit erneutem Versuch', () => {
    expect(creationStateText('pending')).toMatchObject({ text: 'Anlage in Lexware Office ausstehend', state: 'Information', retry: false });
    expect(creationStateText('duplicates')).toMatchObject({ state: 'Warning', retry: false });
    expect(creationStateText('duplicates').text).toContain('ähnlicher Kontakt');
    const failed = creationStateText('failed', 'Lexware Office hat die Angaben nicht angenommen.');
    expect(failed).toMatchObject({ text: 'Anlage in Lexware Office fehlgeschlagen', state: 'Error', retry: true });
    expect(failed.hint).toBe('Lexware Office hat die Angaben nicht angenommen. EstrichONE versucht es später erneut.');
    expect(creationStateText('manual').text).toBe('');
    expect(creationStateText(null).text).toBe('');
  });

  it('Massenaktion nennt vorher, wie viele Kunden neu angelegt würden', () => {
    expect(enableSyncQuestion({ changed: 1, wouldCreate: 0, withMatches: 0 }))
      .toBe('Abgleich mit Lexware Office für den ausgewählten Kunden einschalten? Dadurch wird in Lexware Office kein Kunde neu angelegt.');
    expect(enableSyncQuestion({ changed: 5, wouldCreate: 3, withMatches: 2 })).toBe('Abgleich mit Lexware Office für 5 ausgewählte Kunden einschalten? '
      + 'Dadurch werden 3 Kunden automatisch in Lexware Office neu angelegt, weil es dort keinen ähnlichen Kontakt gibt. '
      + '2 Kunden haben dort einen ähnlichen Kontakt und erscheinen unter „Mögliche Dubletten“.');
    expect(enableSyncQuestion({ changed: 2, wouldCreate: 1, withMatches: 1 })).toContain('wird 1 Kunde automatisch');
  });
});

describe('Lexware Office: verbindliche Prüfung beim Klick auf Anlegen', () => {
  const candidate = { contact: { id: 'l1', name: 'Malerbetrieb Kern GmbH', isCompany: true, customerNumber: '10003', street: 'Färberweg 3', postalCode: '83059',
    city: 'Kolbermoor', email: null, phone: null, url: 'https://app.lexware.de/x', linkedContactId: null, linkedContactName: null }, score: 80,
    reasons: ['gleicher Name und PLZ'], strong: false };

  it('Ablehnung des Servers: Treffer mit Kandidaten, Prüfung nicht möglich, sonst nichts', () => {
    expect(creationProblem({ code: 'LEXWARE_CUSTOMER_EXISTS', problem: { candidates: [candidate], canImport: true } }))
      .toEqual({ kind: 'exists', check: { checked: true, message: null, candidates: [candidate], canImport: true } });
    expect(creationProblem({ code: 'LEXWARE_CUSTOMER_EXISTS', problem: {} })).toMatchObject({ kind: 'exists', check: { candidates: [], canImport: false } });
    expect(creationProblem({ code: 'LEXWARE_CHECK_UNAVAILABLE', problem: { detail: 'Die Prüfung in Lexware Office ist derzeit nicht möglich. Der Kunde wurde nicht angelegt.' } }))
      .toEqual({ kind: 'unavailable', detail: 'Die Prüfung in Lexware Office ist derzeit nicht möglich. Der Kunde wurde nicht angelegt.' });
    expect(creationProblem({ code: 'VERSION_CONFLICT' })).toBeNull();
    expect(creationProblem(null)).toBeNull();
  });

  it('Texte: Zustand der Prüfung und Rückfragen', () => {
    expect(LEXWARE_CHECK_RUNNING).toBe('Prüfung in Lexware Office läuft');
    expect(UNCHECKED_QUESTION).toContain('ohne Prüfung in Lexware Office anlegen');
    const question = existsQuestion({ checked: true, message: null, candidates: [candidate], canImport: true });
    expect(question).toContain('Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.');
    expect(question).toContain('Malerbetrieb Kern GmbH: Kundennummer 10003, Färberweg 3, 83059 Kolbermoor, gleicher Name und PLZ');
    expect(question).toContain('trotzdem neu anlegen');
    for (const text of [question, UNCHECKED_QUESTION, LEXWARE_CHECK_RUNNING]) expect(text).not.toMatch(/[–—·]/);
  });
});
