import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, openSection, signIn, toast } from './helpers';
import { LexwareStub, STUB_KEY } from './lexwareStub';

// Lexware Office ohne echtes Konto: Die API des Servers spricht mit einer nachgebildeten Lexware-API in diesem Testprozess
// (scripts/e2e.sh erlaubt dafür eine beliebige Basis-URL). Einrichten, Kunde mit Dublettenprüfung verknüpfen, Rechnungen und
// Angebote am Kunden und in der Bauakte anzeigen (nur lesend, auch als PDF), zuordnen, Status abgleichen, spätere Änderungen an
// verknüpften Kunden abgleichen (mit Abweichungen und Entscheidung je Feld), Kundenabgleich in der Verwaltung.
test.describe.configure({ mode: 'serial' });

const stub = new LexwareStub();
let lexwareHuber = '';
let huberCustomer = '';
let huberBauakte = '';

test.beforeAll(async () => {
  await stub.start();
  lexwareHuber = stub.addContact({ name: 'HUBER BAU GMBH & Co. KG', email: 'buero@huber-bau.example', street: 'Am Anger 7', zip: '83022', city: 'Rosenheim' });
  stub.addContact({ name: 'Zimmerei Lang', email: 'info@zimmerei-lang.example', zip: '83024', city: 'Rosenheim' });
  stub.addContact({ name: 'Malerbetrieb Kern GmbH', email: 'info@maler-kern.example', street: 'Färberweg 3', zip: '83059', city: 'Kolbermoor' });
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

  // Neue Kunden ohne Treffer automatisch anlegen: Vorgabe an. Für die folgenden Abläufe aus, damit sie nur das zeigen, was sie prüfen;
  // die automatische Anlage hat weiter unten ihren eigenen Ablauf.
  await expect(page.locator('[id$="lxAutoCreate"]').first()).toHaveAttribute('aria-checked', 'true');
  await page.locator('.sapMSwtCont[id$="lxAutoCreate"]').click();
  await page.locator('[id$="lexwareSave"]').click();
  await expect(toast(page)).toContainText('Gespeichert');
  expect((await apiCall<{ autoCreate: boolean }>(page, 'GET', '/api/v1/settings/integrations/lexware')).json.autoCreate).toBe(false);
});

test('Lexware Office: Kunde mit Dublettenprüfung verknüpfen, Rechnungen und Angebote anzeigen und zuordnen, Status abgleichen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const customer = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', {
    id: customer, kind: 'company', name: 'Huber Bau GmbH', email: 'buero@huber-bau.example', street: 'Am Anger 7', postalCode: '83022', city: 'Rosenheim',
    lexwareCreateAnyway: true,
  })).status).toBe(201);
  const bauakte = crypto.randomUUID();
  huberCustomer = customer;
  huberBauakte = bauakte;
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

test('Lexware Office: spätere Änderungen abgleichen, Abweichungen entscheiden, Richtung einstellen, Beleg als PDF', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page, 'admin', adminPassword);
  const customer = huberCustomer;
  type Contact = Record<string, unknown> & { version: number };
  const readCustomer = async () => (await apiCall<Contact>(page, 'GET', `/api/v1/contacts/${customer}`)).json;
  const editCustomer = async (values: Record<string, unknown>) => {
    const result = await apiCall<Contact>(page, 'PUT', `/api/v1/contacts/${customer}`, { ...(await readCustomer()), ...values });
    expect(result.status, JSON.stringify(result.json)).toBe(200);
  };
  const lexwareName = () => (stub.contacts.get(lexwareHuber)!.company as { name: string }).name;
  const puts = () => stub.calls.filter((c) => c.method === 'PUT');

  // Neuer Kunde: Gibt es ihn in Lexware Office schon, warnt der Dialog deutlich, unabhängig vom Schalter „Mit Lexware Office abgleichen“.
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  const fresh = page.locator('#newCustomerDialog');
  await fresh.getByRole('option', { name: 'Gewerbekunde' }).click();
  await page.locator('#ncLexwareSync').click();
  await page.locator('#ncName-inner').fill('Malerbetrieb Kern');
  await page.locator('#ncPostalCode-inner').fill('83059');
  await expect(page.locator('#ncLexwareWarning')).toContainText('Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.');
  const hit = page.locator('#ncLexwareHits');
  await expect(hit).toContainText('Malerbetrieb Kern GmbH');
  await expect(hit).toContainText('Kundennummer 10003');
  await expect(hit).toContainText('Färberweg 3, 83059 Kolbermoor');
  await expect(hit).toContainText('gleicher Name und PLZ');
  // Ohne bewusste Entscheidung wird nicht angelegt.
  await page.locator('#ncCreate').click();
  await expect(page.locator('#ncError')).toContainText('bereits in Lexware Office vorhanden');
  await expect(page.locator('#ncLexwareForce')).toBeVisible();
  // Übernehmen: Der Kunde entsteht mit den Daten aus Lexware Office, ist verknüpft und wird abgeglichen.
  await hit.getByRole('button', { name: 'Aus Lexware Office übernehmen' }).click();
  await expect(fresh).toHaveCount(0);
  const kern = (await apiCall<{ items: { name: string; linkedContactId: string | null }[] }>(page, 'GET', '/api/v1/lexware/contacts?q=Kern')).json.items[0];
  expect(kern.linkedContactId).toBeTruthy();
  expect((await apiCall<Record<string, unknown>>(page, 'GET', `/api/v1/contacts/${kern.linkedContactId}`)).json)
    .toMatchObject({ name: 'Malerbetrieb Kern GmbH', street: 'Färberweg 3', city: 'Kolbermoor', email: 'info@maler-kern.example', lexwareSync: true });
  expect(stub.calls.filter((c) => c.method !== 'GET')).toEqual([]);

  // Verbindliche Prüfung beim Klick auf „Kunde anlegen“: Der Server prüft in Lexware Office und speichert erst danach.
  const exists = await apiCall<{ code: string; candidates: { contact: { name: string } }[] }>(page, 'POST', '/api/v1/contacts',
    { id: crypto.randomUUID(), kind: 'company', name: 'Zimmerei Lang' });
  expect(exists.status).toBe(409);
  expect(exists.json.code).toBe('LEXWARE_CUSTOMER_EXISTS');
  expect(exists.json.candidates[0].contact.name).toBe('Zimmerei Lang');
  const customerNames = async () => (await apiCall<{ name: string }[]>(page, 'GET', '/api/v1/contacts?limit=500')).json.map((c) => c.name);
  expect(await customerNames()).not.toContain('Zimmerei Lang');

  // Kein Treffer: Während der Prüfung steht „Prüfung in Lexware Office läuft“ und die Schaltfläche ist gesperrt, danach wird angelegt.
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  await page.locator('#newCustomerDialog').getByRole('option', { name: 'Gewerbekunde' }).click();
  await page.locator('#ncName-inner').fill('Bodenleger Neuhaus GmbH');
  await page.waitForTimeout(1200);
  stub.delayMs = 1500;
  await page.locator('#ncCreate').click();
  await expect(page.locator('#ncLexwareRunning')).toContainText('Prüfung in Lexware Office läuft');
  await expect(page.locator('#ncCreate')).toBeDisabled();
  await expect(page.locator('#newCustomerDialog')).toHaveCount(0, { timeout: 30_000 });
  stub.delayMs = 0;
  expect(await customerNames()).toContain('Bodenleger Neuhaus GmbH');

  // Lexware Office nicht erreichbar: nicht stillschweigend anlegen, sondern Rückfrage „Ohne Prüfung anlegen“.
  stub.down = true;
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  await page.locator('#newCustomerDialog').getByRole('option', { name: 'Gewerbekunde' }).click();
  await page.locator('#ncName-inner').fill('Estrich Sonnleitner GmbH');
  await page.locator('#ncCreate').click();
  const unchecked = page.getByRole('alertdialog');
  await expect(unchecked).toContainText('Die Prüfung in Lexware Office ist derzeit nicht möglich.', { timeout: 60_000 });
  await expect(unchecked).toContainText('Der Kunde wurde nicht angelegt.');
  await unchecked.getByRole('button', { name: 'Abbrechen' }).click();
  await expect(page.locator('#ncError')).toContainText('nicht angelegt');
  expect(await customerNames()).not.toContain('Estrich Sonnleitner GmbH');
  await page.locator('#ncCreate').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Ohne Prüfung anlegen' }).click({ timeout: 60_000 });
  await expect(page.locator('#newCustomerDialog')).toHaveCount(0, { timeout: 30_000 });
  expect(await customerNames()).toContain('Estrich Sonnleitner GmbH');
  stub.down = false;

  // Erstabgleich beim Verknüpfen: Der unterschiedliche Name wurde nicht überschrieben, sondern steht als Abweichung am Kunden.
  await page.goto(`/kunden/${customer}?abschnitt=lexware`);
  await expect(page.locator('[id$="lexwareChangeState"]')).toContainText('1 Abweichung offen');
  await expect(page.locator('[id$="lexwareChangeHint"]')).toContainText('Letzter Abgleich:');
  const customerDeviations = page.locator('[id$="lexwareDeviations"]');
  await expect(customerDeviations.getByRole('row').filter({ hasText: 'Name' })).toContainText('HUBER BAU GMBH & Co. KG');
  expect(lexwareName()).toBe('HUBER BAU GMBH & Co. KG');
  expect((await readCustomer()).name).toBe('Huber Bau GmbH');

  // Einstellungen: Richtung des Änderungsabgleichs, Vorgabe beide Richtungen.
  await page.goto('/system/lexware-office');
  const direction = page.locator('[id$="lxChangeSync"]').first();
  await expect(direction).toContainText('Beide Richtungen');
  await expect(page.locator('[id$="lxChangeSyncHelp"]')).toContainText('entscheiden Sie im Kundenabgleich');
  await direction.click();
  await page.getByRole('option', { name: 'Nur von Lexware Office nach EstrichONE' }).click();
  await expect(page.locator('[id$="lxChangeSyncHelp"]')).toContainText('nicht von selbst übertragen');
  await page.locator('[id$="lexwareSave"]').click();
  await expect(toast(page)).toContainText('Gespeichert');
  expect((await apiCall<{ changeSync: string }>(page, 'GET', '/api/v1/settings/integrations/lexware')).json.changeSync).toBe('fromLexware');
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { changeSync: 'both', baseUrl: stub.url })).status).toBe(200);
  expect((await apiCall<{ changeSync: string; customersWithDeviations: number }>(page, 'GET', '/api/v1/settings/integrations/lexware')).json)
    .toMatchObject({ changeSync: 'both', customersWithDeviations: 1 });

  // Abgleichübersicht: Gruppe „Abweichungen“ mit beiden Werten; je Feld entscheiden.
  await page.goto('/system/lexware-office?reiter=kunden');
  const figures = page.locator('[id$="lexwareFigures"]');
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Abweichungen' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('1');
  const deviations = page.locator('[id$="lexwareDeviations"]');
  const row = deviations.locator('tbody tr').filter({ hasText: 'Huber Bau GmbH' }).first();
  await expect(row).toContainText('Name');
  await expect(row).toContainText('HUBER BAU GMBH & Co. KG');
  await row.getByRole('button', { name: 'Je Feld entscheiden' }).click();
  const decide = page.getByRole('dialog', { name: 'Abweichungen: Huber Bau GmbH' });
  await expect(decide).toContainText('EstrichONE hat nichts überschrieben.');
  // Ohne Auswahl wird nichts übernommen.
  await decide.getByRole('button', { name: 'Auswahl übernehmen' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'OK' }).click();
  expect(puts()).toHaveLength(0);
  await decide.getByRole('row').filter({ hasText: 'Name' }).getByRole('option', { name: 'EstrichONE' }).click();
  await decide.getByRole('button', { name: 'Auswahl übernehmen' }).click();
  await expect(toast(page)).toContainText('Abweichungen übernommen');
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Abweichungen' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('0');
  // Geschrieben wurde in Lexware Office genau der Kontakt (PUT mit Version); Kundennummer und Notiz sind unverändert.
  expect(puts()).toEqual([{ method: 'PUT', path: `/v1/contacts/${lexwareHuber}` }]);
  expect(lexwareName()).toBe('Huber Bau GmbH');
  expect(stub.contacts.get(lexwareHuber)).toMatchObject({ version: 2, note: 'Stammkunde', roles: { customer: { number: 10001 } } });

  // Spätere Änderung in EstrichONE: Der Hintergrunddienst überträgt sie, ohne dass jemand etwas anstößt.
  await editCustomer({ street: 'Am Anger 9' });
  await expect.poll(() => stub.billing(lexwareHuber).street, { timeout: 75_000, intervals: [1000] }).toBe('Am Anger 9');
  expect(stub.billing(lexwareHuber)).toMatchObject({ zip: '83022', city: 'Rosenheim', countryCode: 'DE' });

  // Spätere Änderung in Lexware Office: „Jetzt abgleichen“ am Kunden übernimmt sie.
  stub.edit(lexwareHuber, (c) => {
    ((c.addresses as { billing: Record<string, unknown>[] }).billing[0]).city = 'Kolbermoor';
    c.phoneNumbers = { business: ['08031 44120'] };
  });
  await page.goto(`/kunden/${customer}?abschnitt=lexware`);
  await page.locator('[id$="lexwareSyncNow"]').click();
  await expect(toast(page)).toContainText('Mit Lexware Office abgeglichen');
  await expect(page.locator('[id$="lexwareChangeState"]')).toContainText('Abgeglichen');
  expect(await readCustomer()).toMatchObject({ city: 'Kolbermoor', phone: '08031 44120', street: 'Am Anger 9', name: 'Huber Bau GmbH' });

  // Beide Seiten ändern dasselbe Feld unterschiedlich: nichts wird überschrieben, der Nutzer entscheidet.
  stub.edit(lexwareHuber, (c) => { c.emailAddresses = { business: ['rechnung@huber-bau.example'] }; });
  await editCustomer({ email: 'kontakt@huber-bau.example', emails: [{ address: 'kontakt@huber-bau.example' }] });
  await page.locator('[id$="lexwareSyncNow"]').click();
  await expect(toast(page)).toContainText('Abgeglichen, es gibt Abweichungen');
  await expect(page.locator('[id$="lexwareChangeState"]')).toContainText('1 Abweichung offen');
  const mail = customerDeviations.getByRole('row').filter({ hasText: 'E-Mail-Adresse' });
  await expect(mail).toContainText('kontakt@huber-bau.example');
  await expect(mail).toContainText('rechnung@huber-bau.example');
  expect((await readCustomer()).email).toBe('kontakt@huber-bau.example');
  expect(JSON.stringify(stub.contacts.get(lexwareHuber)!.emailAddresses)).toBe('{"business":["rechnung@huber-bau.example"]}');
  await page.locator('[id$="lexwareTakeRemote"]').click();
  const takeOver = page.getByRole('alertdialog');
  await expect(takeOver).toContainText('aus Lexware Office nach EstrichONE übernehmen');
  await takeOver.getByRole('button', { name: 'Lexware übernehmen' }).click();
  await expect(toast(page)).toContainText('Abweichungen übernommen');
  await expect(page.locator('[id$="lexwareChangeState"]')).toContainText('Abgeglichen');
  expect((await readCustomer()).email).toBe('rechnung@huber-bau.example');

  // Neuer Kunde ohne ähnlichen Kontakt: wird im Hintergrund automatisch in Lexware Office angelegt und verknüpft, genau einmal.
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { autoCreate: true, baseUrl: stub.url })).status).toBe(200);
  const auto = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', {
    id: auto, kind: 'company', name: 'Fliesen Brandl GmbH', email: 'info@fliesen-brandl.example', street: 'Hofweg 2', postalCode: '83080', city: 'Oberaudorf', lexwareSync: true,
  })).status).toBe(201);
  const offOnly = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', { id: offOnly, kind: 'company', name: 'Nur Hier Bau GmbH', lexwareSync: false })).status).toBe(201);
  const created = () => [...stub.contacts.values()].filter((c) => (c.company as { name?: string } | undefined)?.name === 'Fliesen Brandl GmbH');
  await expect.poll(() => created().length, { timeout: 75_000, intervals: [1000] }).toBe(1);
  expect(created()[0]).toMatchObject({ addresses: { billing: [{ street: 'Hofweg 2', zip: '83080', city: 'Oberaudorf', countryCode: 'DE' }] }, emailAddresses: { business: ['info@fliesen-brandl.example'] } });
  await page.goto(`/kunden/${auto}?abschnitt=lexware`);
  await expect(page.locator('[id$="lexwareLinkState"]')).toContainText('Verknüpft');
  await expect(page.locator('[id$="lexwareLinkText"]')).toContainText('aus EstrichONE in Lexware Office angelegt');
  // Schalter aus: nichts wird angelegt. Und kein zweiter Kontakt für den ersten Kunden.
  await page.goto(`/kunden/${offOnly}?abschnitt=lexware`);
  await expect(page.locator('[id$="lexwareSyncState"]')).toContainText('Nur in EstrichONE');
  expect(stub.calls.filter((c) => c.method === 'POST')).toHaveLength(1);
  expect([...stub.contacts.values()].some((c) => (c.company as { name?: string } | undefined)?.name === 'Nur Hier Bau GmbH')).toBe(false);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { autoCreate: false, baseUrl: stub.url })).status).toBe(200);
  await page.goto(`/kunden/${customer}?abschnitt=lexware`);

  // Beleg als PDF am Kunden: Anzeige in der Vorschau, Herunterladen, für Entwürfe kein PDF.
  const draft = stub.addVoucher({ type: 'invoice', status: 'draft', number: null, date: '2026-11-25', net: 100, gross: 119, contactId: lexwareHuber });
  await page.locator('[id$="customerVouchersRefresh"]').click();
  const vouchers = page.locator('[id$="customerVouchers"]');
  const invoice = vouchers.getByRole('row').filter({ hasText: 'RE1042' });
  await expect(vouchers.getByRole('row').filter({ hasText: 'wird automatisch vergeben' }).getByRole('button', { name: 'PDF anzeigen' })).toBeDisabled();
  await invoice.getByRole('button', { name: 'PDF anzeigen' }).click();
  const viewer = page.locator('.eoPreviewDialog');
  await expect(viewer.getByRole('heading', { name: 'Rechnung RE1042' })).toBeVisible();
  const frame = viewer.locator('iframe.eoVoucherPdf');
  await expect(frame).toHaveAttribute('src', new RegExp(`/api/v1/lexware/customers/${customer}/vouchers/[0-9a-f-]+/document$`));
  const source = (await frame.getAttribute('src'))!;
  const [download] = await Promise.all([page.waitForEvent('download'), viewer.getByRole('button', { name: 'Herunterladen' }).click()]);
  expect(download.suggestedFilename()).toBe('Rechnung-RE1042.pdf');
  await expect(viewer.getByRole('button', { name: 'In Lexware öffnen' })).toBeVisible();
  await viewer.getByRole('button', { name: 'Schließen' }).click();
  await expect(viewer).toHaveCount(0);
  const served = await page.request.get(source);
  expect(served.headers()['content-type']).toBe('application/pdf');
  expect((await served.body()).subarray(0, 5).toString('latin1')).toBe('%PDF-');
  const noDraft = await apiCall<{ code: string; detail: string }>(page, 'GET', `/api/v1/lexware/customers/${customer}/vouchers/${draft}/document`);
  expect(noDraft.status).toBe(409);
  expect(noDraft.json).toMatchObject({ code: 'LEXWARE_NO_DOCUMENT', detail: 'Für Entwürfe stellt Lexware Office kein PDF bereit.' });

  // Abruf nicht möglich: verständliche Meldung, „In Lexware öffnen“ bleibt.
  const old = [...stub.vouchers.values()].find((v) => v.voucherNumber === 'RE0815')!.id as string;
  stub.change(old, { status: 'draft' });
  await vouchers.getByRole('row').filter({ hasText: 'RE0815' }).getByRole('button', { name: 'PDF anzeigen' }).click();
  const failed = page.getByRole('alertdialog');
  await expect(failed).toContainText('Für Entwürfe stellt Lexware Office kein PDF bereit.');
  await expect(failed.getByRole('button', { name: 'In Lexware öffnen' })).toBeVisible();
  await failed.getByRole('button', { name: 'Schließen' }).click();
  stub.change(old, { status: 'paid' });

  // In der Bauakte: PDF des zugeordneten Angebots; kennt Lexware den neuen Pfad nicht, greift der ältere Weg.
  stub.fileRoute = false;
  await page.goto(`/bauakten/${huberBauakte}`);
  // Übersicht der Bauakte: Block „Lexware Office“ mit Kontakt, Abgleichstand und Summenzeile der zugeordneten Belege.
  const block = page.locator('[id$="overviewLexware"]');
  await expect(block.locator('span[id$="overviewLexwareName"]')).toHaveText('Huber Bau GmbH');
  await expect(block).toContainText('10001');
  await expect(block).toContainText('Am Anger 9, 83022 Kolbermoor');
  await expect(block).toContainText('rechnung@huber-bau.example');
  await expect(block.locator('.sapMObjStatus[id$="overviewLexwareState"]')).toContainText('Abgeglichen');
  await expect(block.locator('span[id$="overviewLexwareSummary"]')).toContainText('1 Rechnung: offen 5.407,66 €, davon überfällig 5.407,66 €. 1 Angebot: angenommen 5.355,00 €');
  await expect(block.getByRole('link', { name: 'In Lexware öffnen' })).toHaveAttribute('href', new RegExp(`/permalink/contacts/view/${lexwareHuber}$`));
  await block.getByRole('link', { name: 'Rechnungen und Angebote' }).click();
  await expect(page.getByRole('tab', { name: 'Rechnungen und Angebote', exact: true })).toHaveAttribute('aria-selected', 'true');
  const table = page.locator('[id$="invoiceTable"]');
  await table.getByRole('row').filter({ hasText: 'AG0311' }).getByRole('button', { name: 'PDF anzeigen' }).click();
  await expect(viewer.getByRole('heading', { name: 'Angebot AG0311' })).toBeVisible();
  await expect(viewer.locator('iframe.eoVoucherPdf')).toHaveAttribute('src', new RegExp(`/api/v1/bauakten/${huberBauakte}/lexware/vouchers/[0-9a-f-]+/document$`));
  await viewer.getByRole('button', { name: 'Schließen' }).click();
  const [second] = await Promise.all([page.waitForEvent('download'), table.getByRole('row').filter({ hasText: 'AG0311' }).getByRole('button', { name: 'PDF herunterladen' }).click()]);
  expect(second.suggestedFilename()).toBe('Angebot-AG0311.pdf');
  expect(stub.calls.some((c) => c.path.endsWith('/document'))).toBe(true);
  expect(stub.calls.some((c) => c.path.startsWith('/v1/files/file-'))).toBe(true);
  stub.fileRoute = true;

  // Geschrieben wurde in Lexware Office nur an diesem einen Kontakt; Belege wurden ausschließlich gelesen.
  expect(puts().length).toBeGreaterThanOrEqual(2);
  expect(puts().every((c) => c.path === `/v1/contacts/${lexwareHuber}`)).toBe(true);
  expect(stub.calls.filter((c) => c.method !== 'GET' && c.method !== 'PUT')).toEqual([{ method: 'POST', path: '/v1/contacts' }]);
  stub.vouchers.delete(draft);
});

test('Lexware Office: Kundenabgleich mit Listen und Übernahme nach Dublettenprüfung, Modul wieder aus', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/lexware-office?reiter=kunden');
  const figures = page.locator('[id$="lexwareFigures"]');
  await expect(figures).toContainText('Verknüpft');
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Verknüpft' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('3');
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
  await expect(figures.locator('.eoKpiItem').filter({ hasText: 'Verknüpft' }).locator('.eoKpiFigure .sapMObjStatusText')).toHaveText('4');

  await page.getByRole('option', { name: /Verknüpft \(4\)/ }).click();
  await expect(page.locator('[id$="lexwareLinked"]').getByRole('row').filter({ hasText: 'Hans Gruber' })).toBeVisible();

  // Modul wieder aus: Abschnitte verschwinden, Verknüpfungen bleiben gespeichert.
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: false } })).status).toBe(200);
  const kept = await apiCall<{ linkedCustomers: number; linkedInvoices: number }>(page, 'GET', '/api/v1/settings/integrations/lexware');
  expect(kept.json).toMatchObject({ linkedCustomers: 4, linkedInvoices: 2 });
});
