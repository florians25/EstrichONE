import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, openSection, signIn, toast } from './helpers';
import { LexwareStub, STUB_KEY } from './lexwareStub';

// Lexware Office ohne echtes Konto: Die API des Servers spricht mit einer nachgebildeten Lexware-API in diesem Testprozess
// (scripts/e2e.sh erlaubt dafür eine beliebige Basis-URL). Einrichten, Kunde mit Dublettenprüfung verknüpfen, Rechnungen und
// Angebote am Kunden und in der Bauakte anzeigen (nur lesend), zuordnen, Status abgleichen, Kundenabgleich in der Verwaltung.
test.describe.configure({ mode: 'serial' });

const stub = new LexwareStub();
let lexwareHuber = '';

test.beforeAll(async () => {
  await stub.start();
  lexwareHuber = stub.addContact({ name: 'HUBER BAU GMBH & Co. KG', email: 'buero@huber-bau.example', street: 'Am Anger 7', zip: '83022', city: 'Rosenheim' });
  stub.addContact({ name: 'Zimmerei Lang', email: 'info@zimmerei-lang.example', zip: '83024', city: 'Rosenheim' });
  stub.addContact({ name: 'Hans Gruber', company: false, phone: '+49 89 4441122', street: 'Lindenstr. 4', zip: '80335', city: 'München' });
});

test.afterAll(async () => {
  await stub.stop();
});

test('Lexware Office: ausgeschaltet unsichtbar, einrichten, Verbindung testen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);

  // Nach der Installation aus: keine Abschnitte, Endpunkte antworten mit „Modul deaktiviert“.
  const status = await apiCall<{ enabled: boolean; configured: boolean }>(page, 'GET', '/api/v1/lexware/status');
  expect(status.json).toMatchObject({ enabled: false, configured: false });
  const probe = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: probe, title: 'Probe ohne Lexware' })).status).toBe(201);
  await page.goto(`/bauakten/${probe}?abschnitt=rechnungen`);
  await expect(page.getByRole('tab', { name: 'Übersicht', exact: true }).first()).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByRole('tab', { name: 'Rechnungen und Angebote', exact: true })).toHaveCount(0);

  await page.goto('/verwaltung');
  await page.locator('.eoAdminTile').filter({ has: page.getByRole('heading', { name: 'Lexware Office', exact: true }) }).first().click();
  await expect(page.getByText('Nicht eingerichtet.')).toBeVisible();
  await expect(page.getByText('Das Modul ist ausgeschaltet.', { exact: false })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Schlüssel in Lexware Office erzeugen' })).toHaveAttribute('href', 'https://app.lexware.de/addons/public-api');
  await expect(page.getByText('Kein Schlüssel hinterlegt')).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Kundenabgleich' })).toHaveCount(0);

  // Falscher Schlüssel: verständliche Meldung.
  await page.locator('input[id$="lxKey-inner"]').fill('falscher-schluessel');
  await page.locator('input[id$="lxUrl-inner"]').fill(stub.url);
  await page.getByRole('button', { name: 'Verbindung testen' }).click();
  await expect(page.getByRole('alertdialog').getByText('Der API-Schlüssel wurde von Lexware Office abgelehnt.', { exact: false })).toBeVisible();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Schließen' }).click();
  await expect(page.locator('[id$="lexwareTestResult"]')).toContainText('fehlgeschlagen');

  // Richtiger Schlüssel: Firmenname der Organisation; der Schlüssel wird nie zurückgegeben.
  await page.locator('input[id$="lxKey-inner"]').fill(STUB_KEY);
  await page.getByRole('button', { name: 'Verbindung testen' }).click();
  await expect(toast(page)).toContainText('Verbindung in Ordnung: Estrichbau E2E GmbH');
  await expect(page.locator('span[id$="lxOrganization"]')).toHaveText('Estrichbau E2E GmbH');
  await expect(page.getByText('Schlüssel hinterlegt', { exact: true })).toBeVisible();
  await expect(page.locator('input[id$="lxKey-inner"]')).toHaveValue('');
  const settings = await apiCall<Record<string, unknown>>(page, 'GET', '/api/v1/settings/integrations/lexware');
  expect(JSON.stringify(settings.json)).not.toContain(STUB_KEY);
  expect(settings.json.hasApiKey).toBe(true);

  // Modul einschalten.
  await page.locator('.sapMSwtCont[id$="lxModule"]').click();
  await expect(toast(page)).toContainText('Modul eingeschaltet');
  await expect(page.getByRole('tab', { name: 'Kundenabgleich' })).toBeVisible();
  expect((await apiCall<{ enabled: boolean; configured: boolean }>(page, 'GET', '/api/v1/lexware/status')).json).toMatchObject({ enabled: true, configured: true });
});

test('Lexware Office: Kunde mit Dublettenprüfung verknüpfen, Rechnungen und Angebote anzeigen und zuordnen, Status abgleichen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const customer = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', {
    id: customer, kind: 'company', name: 'Huber Bau GmbH', email: 'buero@huber-bau.example', street: 'Am Anger 7', postalCode: '83022', city: 'Rosenheim',
  })).status).toBe(201);
  const bauakte = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', {
    id: bauakte, title: 'EFH Huber, Neubau', customerId: customer, street: 'Birkenweg 12', postalCode: '83026', city: 'Rosenheim',
    project: { totalArea: 182.5, nominalThicknessMm: 65, strengthClass: 'CT-C25-F4', underfloorHeating: true },
  })).status).toBe(201);

  // Kunde: Abschnitt „Lexware Office“, Dublettenprüfung nennt den Grund, verknüpfen.
  await page.goto(`/kunden/${customer}?abschnitt=lexware`);
  await expect(page.locator('[id$="lexwareLinkState"]')).toContainText('Nicht verknüpft');
  await page.getByRole('button', { name: 'Kontakt suchen und verknüpfen' }).click();
  const link = page.getByRole('dialog', { name: 'Huber Bau GmbH mit Lexware Office verknüpfen' });
  const candidate = link.getByRole('row').filter({ hasText: 'HUBER BAU GMBH & Co. KG' });
  await expect(candidate).toContainText('gleiche E-Mail-Adresse');
  await expect(candidate).toContainText('gleicher Name und PLZ');
  await expect(link.getByRole('row').filter({ hasText: 'Zimmerei Lang' })).toHaveCount(0);

  // Neu anlegen wird bei Treffern nicht ohne Rückfrage ausgeführt.
  await link.getByRole('button', { name: 'In Lexware anlegen' }).click();
  const question = page.getByRole('alertdialog');
  await expect(question).toContainText('einen möglichen doppelten Eintrag in Lexware Office');
  await question.getByRole('button', { name: 'Abbrechen' }).click();
  expect(stub.calls.filter((c) => c.method === 'POST' && c.path === '/v1/contacts')).toHaveLength(0);

  await candidate.getByRole('radio').click();
  await link.getByRole('button', { name: 'Mit Auswahl verknüpfen' }).click();
  await expect(toast(page)).toContainText('Verknüpft mit HUBER BAU GMBH & Co. KG');
  await expect(page.locator('[id$="lexwareLinkState"]')).toContainText('Verknüpft');
  await expect(page.locator('[id$="lexwareLinkText"]')).toContainText('Kundennummer 10001');
  await expect(page.getByRole('button', { name: 'In Lexware öffnen' })).toBeVisible();

  await expect(page.locator('[id$="lexwareSyncState"]')).toContainText('Abgleich eingeschaltet');

  // Einseitiger Kunde: bleibt nur in EstrichONE, bis der Abgleich bewusst eingeschaltet wird.
  const oneSided = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', { id: oneSided, kind: 'person', name: 'Privat Einseitig', lexwareSync: false })).status).toBe(201);
  expect((await apiCall<{ code: string }>(page, 'POST', `/api/v1/lexware/customers/${oneSided}/create`, { createAnyway: true })).json.code)
    .toBe('LEXWARE_SYNC_DISABLED');
  await page.goto(`/kunden/${oneSided}?abschnitt=lexware`);
  await expect(page.locator('[id$="lexwareSyncState"]')).toContainText('Nur in EstrichONE');
  await expect(page.locator('[id$="lexwareSyncHint"]')).toContainText('bleibt nur in EstrichONE');
  await expect(page.getByRole('button', { name: 'Kontakt suchen und verknüpfen' })).toHaveCount(0);
  await page.locator('.sapMSwtCont[id$="lexwareSyncSwitch"]').click();
  await expect(toast(page)).toContainText('Abgleich eingeschaltet');
  await expect(page.getByRole('button', { name: 'Kontakt suchen und verknüpfen' })).toBeVisible();
  await page.locator('.sapMSwtCont[id$="lexwareSyncSwitch"]').click();
  await expect(toast(page)).toContainText('Abgleich ausgeschaltet');

  // Dialog „Neuer Kunde“: Schalter mit der Vorgabe aus den Einstellungen (ja).
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  await expect(page.locator('#ncLexwareSync')).toHaveAttribute('aria-checked', 'true');
  await page.getByRole('dialog').getByRole('button', { name: 'Abbrechen' }).click();

  // Belege gibt es nur in Lexware Office: eine Rechnung im Entwurf, die die Bauakte nennt, ein Angebot und eine ältere Rechnung.
  const number = (await apiCall<{ number: string }>(page, 'GET', `/api/v1/bauakten/${bauakte}`)).json.number;
  const draft = stub.addVoucher({ type: 'invoice', status: 'draft', number: null, date: '2026-11-20', net: 4544.25, gross: 5407.66, contactId: lexwareHuber,
    introduction: `Bauvorhaben EFH Huber, Neubau\nBauakte: ${number}` });
  const quotation = stub.addVoucher({ type: 'quotation', status: 'accepted', number: 'AG0311', date: '2026-08-01', dueDate: '2026-08-31', net: 4500, gross: 5355,
    contactId: lexwareHuber });
  stub.addVoucher({ type: 'invoice', status: 'paid', number: 'RE0815', date: '2026-03-01', dueDate: '2026-03-15', net: 1000, gross: 1190, contactId: lexwareHuber });

  // Kunde: Belege des verknüpften Kontakts, nur Anzeige.
  await page.goto(`/kunden/${customer}?abschnitt=lexware`);
  const customerVouchers = page.locator('[id$="customerVouchers"]');
  // Die Liste ist kurz zwischengespeichert; „Aktualisieren“ holt die eben in Lexware entstandenen Belege.
  await page.locator('[id$="customerVouchersRefresh"]').click();
  await expect(customerVouchers.getByRole('row').filter({ hasText: 'AG0311' })).toContainText('Angebot');
  await expect(customerVouchers.getByRole('row').filter({ hasText: 'AG0311' })).toContainText('Angenommen');
  await expect(customerVouchers.getByRole('row').filter({ hasText: 'RE0815' })).toContainText('Bezahlt');
  await expect(customerVouchers.getByRole('row').filter({ hasText: 'wird automatisch vergeben' })).toContainText('Entwurf');
  await expect(page.locator('[id$="customerVoucherSummary"]')).toContainText('2 Rechnungen');
  await expect(page.locator('[id$="customerVoucherSummary"]')).toContainText('1 Angebot');

  // Bauakte: Abschnitt „Rechnungen und Angebote“. Es gibt keine Aktion, die einen Beleg erstellt.
  await page.goto(`/bauakten/${bauakte}`);
  await openSection(page, 'Rechnungen und Angebote');
  await expect(page.getByText('Dieser Bauakte ist noch keine Rechnung und kein Angebot zugeordnet.')).toBeVisible();
  await expect(page.getByText('werden ausschließlich in Lexware Office erstellt', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: /Rechnung erstellen|Angebot erstellen|Entwurf/ })).toHaveCount(0);

  // Beleg zuordnen: Belege des Kunden, der Beleg mit der Bauaktennummer steht als Vorschlag oben.
  await page.getByRole('button', { name: 'Beleg zuordnen' }).click();
  const assign = page.getByRole('dialog', { name: 'Beleg zuordnen' });
  await expect(assign.locator('tbody tr').first()).toContainText('Nennt diese Bauakte');
  await expect(assign.locator('tbody tr').first()).toContainText('wird automatisch vergeben');
  await expect(assign.getByRole('row').filter({ hasText: 'RE0815' })).not.toContainText('Nennt diese Bauakte');
  await assign.locator('tbody tr').first().getByRole('button', { name: 'Zuordnen' }).click();
  await expect(toast(page)).toContainText('Rechnung im Entwurf zugeordnet');

  const table = page.locator('[id$="invoiceTable"]');
  const invoice = table.locator('tbody tr').first();
  await expect(invoice).toContainText('Rechnung');
  await expect(invoice).toContainText('Entwurf');
  await expect(invoice).toContainText('5.407,66');

  // Angebot über die Belegnummer suchen und zuordnen.
  await page.getByRole('button', { name: 'Beleg zuordnen' }).click();
  await assign.getByRole('searchbox').fill('AG0311');
  await assign.getByRole('searchbox').press('Enter');
  await expect(assign.locator('tbody tr')).toHaveCount(1);
  await assign.getByRole('button', { name: 'Zuordnen' }).click();
  await expect(toast(page)).toContainText('Angebot AG0311 zugeordnet');
  await expect(table.getByRole('row').filter({ hasText: 'AG0311' })).toContainText('Angenommen');
  await expect(page.locator('[id$="invoiceSummary"]')).toContainText('1 Angebot: angenommen');

  // In Lexware abgeschlossen: „Aktualisieren“ zeigt Nummer und Status; nach der Fälligkeit überfällig.
  stub.change(draft, { status: 'open', number: 'RE1042', dueDate: '2099-12-31' });
  await page.getByRole('button', { name: 'Aktualisieren' }).click();
  await expect(table.getByRole('row').filter({ hasText: 'RE1042' })).toContainText('Offen');
  stub.change(draft, { dueDate: '2020-01-31' });
  await page.getByRole('button', { name: 'Aktualisieren' }).click();
  await expect(table.getByRole('row').filter({ hasText: 'RE1042' })).toContainText('Überfällig');

  // EstrichONE hat in Lexware Office nur gelesen: kein schreibender Aufruf, die Belege sind unverändert vorhanden.
  expect(stub.calls.filter((c) => c.method !== 'GET')).toEqual([]);
  expect(stub.vouchers.size).toBe(3);
  expect(quotation).toBeTruthy();
});

test('Lexware Office: Kundenabgleich mit Listen und Übernahme nach Dublettenprüfung, Modul wieder aus', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/lexware-office?reiter=kunden');
  const figures = page.locator('[id$="lexwareFigures"]');
  await expect(figures).toContainText('Verknüpft');
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Verknüpft' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('1');
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Nur in Lexware' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('2');

  // Bewusst nur in EstrichONE: eigene Gruppe, Abgleich für ausgewählte Kunden einschalten.
  await page.getByRole('option', { name: /Bewusst nur in EstrichONE/ }).click();
  const excluded = page.locator('[id$="lexwareExcluded"]');
  const oneSidedRow = excluded.getByRole('row').filter({ hasText: 'Privat Einseitig' });
  await expect(oneSidedRow).toBeVisible();
  await oneSidedRow.getByRole('checkbox').click();
  await page.getByRole('button', { name: 'Abgleich einschalten (1)' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Einschalten' }).click();
  await expect(toast(page)).toContainText('Abgleich für 1 Kunden eingeschaltet');
  await expect(oneSidedRow).toHaveCount(0);

  await page.getByRole('option', { name: /Nur in Lexware/ }).click();
  const remote = page.locator('[id$="lexwareOnlyRemote"]');
  await expect(remote.getByRole('row').filter({ hasText: 'Zimmerei Lang' })).toBeVisible();

  // Übernahme: ohne ähnlichen Kunden nach Rückfrage, Kundennummer aus dem Nummernkreis.
  await remote.getByRole('row').filter({ hasText: 'Hans Gruber' }).getByRole('button', { name: 'Prüfen und übernehmen' }).click();
  const confirm = page.getByRole('alertdialog');
  await expect(confirm).toContainText('Die Prüfung hat keinen ähnlichen Kunden gefunden.');
  await confirm.getByRole('button', { name: 'Kunde anlegen' }).click();
  await expect(toast(page)).toContainText('Hans Gruber als Kunde');
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Verknüpft' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('2');

  await page.getByRole('option', { name: /Verknüpft \(2\)/ }).click();
  await expect(page.locator('[id$="lexwareLinked"]').getByRole('row').filter({ hasText: 'Hans Gruber' })).toBeVisible();

  // Modul wieder aus: Abschnitte verschwinden, Verknüpfungen bleiben gespeichert.
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: false } })).status).toBe(200);
  const kept = await apiCall<{ linkedCustomers: number; linkedInvoices: number }>(page, 'GET', '/api/v1/settings/integrations/lexware');
  expect(kept.json).toMatchObject({ linkedCustomers: 2, linkedInvoices: 2 });
});
