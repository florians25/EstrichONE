import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, signIn, toast } from './helpers';
import { LexwareStub, STUB_KEY } from './lexwareStub';

// Modul „Verkauf“ gegen die nachgebildete Lexware-API: Angebot im Belegeditor mit Tastatur erfassen, als Entwurf senden,
// in Lexware annehmen, Auftragsbestätigung festschreiben (nur mit Bestätigung), Rechnungsplan der Bauakte mit Abschlag nach
// Leistungsstand und Schlussrechnung mit Abzugszeilen, Übersicht mit Reitern, Filtern und Summenzeile.
test.describe.configure({ mode: 'serial' });

const stub = new LexwareStub();
let contact = '';
let customer = '';
let bauakte = '';
let quotation = '';

test.beforeAll(async () => {
  await stub.start();
  contact = stub.addContact({ name: 'Talblick Wohnbau GmbH', email: 'bau@talblick.example', street: 'Seeweg 9', zip: '83043', city: 'Bad Aibling' });
  stub.addArticle({ title: 'Fließestrich CA-C30-F5 liefern und einbauen', number: 'F-100', net: 24.5, unit: 'm²', description: 'Nenndicke 65 mm, auf Dämmung' });
  stub.addArticle({ title: 'Randdämmstreifen 10 mm', type: 'PRODUCT', number: 'F-200', net: 1.05, unit: 'lfm' });
});

test.afterAll(async () => {
  await stub.stop();
});

test('Angebot im Belegeditor: Tastatur, Vorschlagsliste, Zwischensumme, Summen live, senden', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { apiKey: STUB_KEY, baseUrl: stub.url })).status).toBe(200);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: {
    lexware: true, lexwareArticles: true, sales: true, quotations: true, salesOrderConfirmations: true, salesInvoices: true, salesCreditNotes: true,
    salesFinalize: true, salesPayments: true,
  } })).status).toBe(200);
  expect((await apiCall(page, 'POST', '/api/v1/lexware/articles/sync')).status).toBe(200);
  customer = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', { id: customer, kind: 'company', name: 'Talblick Wohnbau GmbH', lexwareCreateAnyway: true })).status).toBe(201);
  expect((await apiCall(page, 'POST', `/api/v1/lexware/customers/${customer}/link`, { lexwareContactId: contact })).status).toBe(200);
  bauakte = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', {
    id: bauakte, title: 'Wohnanlage Seeweg', customerId: customer, status: 'planned', street: 'Seeweg 11', postalCode: '83043', city: 'Bad Aibling',
    project: { totalArea: 400, nominalThicknessMm: 65 },
  })).status).toBe(201);
  for (const [name, area, status] of [['Haus A', 160, 'laid'], ['Haus B', 240, 'planned']] as const) {
    const id = crypto.randomUUID();
    expect((await apiCall(page, 'PUT', `/api/v1/bauakten/${bauakte}/rooms/${id}`, { id, name, areaSquareMeters: area, underfloorHeating: true, status })).status)
      .toBeLessThan(300);
  }

  // Aus der Bauakte: Rechnungsplan mit Aktionen, „Angebot erstellen“ öffnet den Belegeditor.
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  await expect(page.locator('[id$="billingPlan"]')).toContainText('Auftragssumme noch offen');
  await page.locator('[id$="salesQuotation"]').click();
  await expect(page).toHaveURL(/\/verkauf\//);
  quotation = page.url().split('/verkauf/')[1];
  await expect(page.getByText('wird automatisch vergeben').first()).toBeVisible();

  // Eingabezeile: Artikel über die Vorschlagsliste, freie Position mit Enter, ohne Maus.
  const quick = page.locator('input[id$="sQuick-inner"]');
  await quick.fill('Fließ');
  await page.getByRole('option', { name: /Fließestrich CA-C30-F5/ }).first().click();
  await quick.fill('Anfahrt und Baustelleneinrichtung');
  await quick.press('Enter');
  const lines = page.locator('[id$="salesLines"]');
  await expect(lines.getByRole('row')).toHaveCount(3);
  const rows = lines.getByRole('row');
  await rows.nth(1).locator('input[type="text"]').nth(1).fill('400');
  await rows.nth(2).locator('input[type="text"]').nth(1).fill('1');
  await rows.nth(2).locator('input[type="text"]').nth(2).fill('pauschal');
  await rows.nth(2).locator('input[type="text"]').nth(3).fill('350');
  await page.locator('[id$="sAddSubtotal"]').click();
  await expect(page.locator('span[id$="sNet"]')).toContainText('10.150,00');
  await expect(lines.getByRole('row').last()).toContainText('10.150,00');
  await page.keyboard.press('Control+s');
  await expect(toast(page)).toContainText('Beleg gespeichert');
  await expect(page.locator('span[id$="sGross"]')).toContainText('12.078,50');
  await page.screenshot({ path: 'test-results/verkauf-angebot.png', fullPage: false });

  await page.locator('[id$="salesSend"]').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Senden' }).click();
  await expect(toast(page)).toContainText('Als Entwurf an Lexware Office gesendet');
  await expect(page.locator('[id$="salesSent"]')).toContainText('Lexware Office erlaubt keine Änderung über die Schnittstelle');
  const sent = stub.created.find((c) => c.type === 'quotation')!;
  expect(sent.query).toBe('');
  expect((sent.body.lineItems as { type: string }[]).map((l) => l.type)).toEqual(['service', 'custom', 'text']);
});

test('Auftragsbestätigung aus dem Angebot, festschreiben nur mit Bestätigung', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const lexwareId = stub.created.length ? [...stub.vouchers.values()].find((v) => v.voucherType === 'quotation')!.id as string : '';
  stub.change(lexwareId, { status: 'accepted', number: 'AG-2026-0042' });
  await page.goto(`/verkauf/${quotation}`);
  await page.locator('[id$="salesMore"]').click();
  await page.getByRole('menuitem', { name: 'In Auftragsbestätigung umwandeln' }).click();
  await expect(toast(page)).toContainText('Auftragsbestätigung angelegt');
  await expect(page.locator('[id$="salesTitle"]')).toContainText('Auftragsbestätigung');
  await expect(page.locator('[id$="salesChain"]')).toContainText('Angebot');
  await page.locator('[id$="salesFinalize"]').click();
  const confirm = page.getByRole('alertdialog', { name: /festschreiben/ });
  await expect(confirm.getByRole('button', { name: 'Festschreiben' })).toBeDisabled();
  await confirm.getByRole('checkbox').click();
  await confirm.getByRole('button', { name: 'Festschreiben' }).click();
  await expect(toast(page)).toContainText('In Lexware Office festgeschrieben');
  await expect(page.locator('[id$="salesNumber"]')).toContainText('AB-2026-');
  const order = stub.created.find((c) => c.type === 'orderconfirmation')!;
  expect(order.query).toContain('finalize=true');
  expect(order.query).toContain(`precedingSalesVoucherId=${lexwareId}`);
});

test('Rechnungsplan: Abschlag nach Leistungsstand, Schlussrechnung mit Abzügen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  const plan = page.locator('[id$="billingPlan"]');
  await expect(plan).toContainText('Auftragssumme 10.150,00');
  await expect(plan).toContainText('noch nicht berechnet 10.150,00');
  await expect(plan).toContainText('40 % der Fläche eingebracht');

  await page.locator('[id$="salesDownPayment"]').click();
  const dialog = page.getByRole('dialog', { name: 'Abschlag erstellen' });
  await expect(dialog).toContainText('Vorschlag nach Leistungsstand: 40 %');
  await dialog.getByRole('button', { name: 'Abschlag anlegen' }).click();
  await expect(page).toHaveURL(/\/verkauf\//);
  await expect(page.locator('[id$="salesTitle"]')).toContainText('Abschlagsrechnung');
  await expect(page.locator('span[id$="sNet"]')).toContainText('4.060,00');
  await page.screenshot({ path: 'test-results/verkauf-rechnung.png' });
  await page.locator('[id$="salesFinalize"]').click();
  const confirm = page.getByRole('alertdialog', { name: /festschreiben/ });
  await confirm.getByRole('checkbox').click();
  await confirm.getByRole('button', { name: 'Festschreiben' }).click();
  await expect(toast(page)).toContainText('In Lexware Office festgeschrieben');

  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  await expect(plan).toContainText('1 Abschlag 4.060,00');
  await expect(plan).toContainText('noch nicht berechnet 6.090,00');
  await page.screenshot({ path: 'test-results/verkauf-rechnungsplan.png' });

  await page.locator('[id$="salesFinalInvoice"]').click();
  await expect(page).toHaveURL(/\/verkauf\//);
  await expect(page.locator('[id$="salesTitle"]')).toContainText('Schlussrechnung');
  const lines = page.locator('[id$="salesLines"]');
  const names = await lines.locator('input[type="text"]').evaluateAll((els) => els.map((e) => (e as HTMLInputElement).value));
  expect(names).toContainEqual(expect.stringMatching(/^Abzüglich 1\. Abschlagsrechnung Nr\. RE-2026-\d+ vom \d\d\.\d\d\.\d{4}$/));
  await expect(page.locator('span[id$="sNet"]')).toContainText('6.090,00');
  await page.locator('[id$="secLines"]').scrollIntoViewIfNeeded();
  await page.screenshot({ path: 'test-results/verkauf-schlussrechnung.png' });
  await page.locator('[id$="salesSend"]').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Senden' }).click();
  await expect(toast(page)).toContainText('Als Entwurf an Lexware Office gesendet');
  const final = stub.created.filter((c) => c.type === 'invoice').at(-1)!;
  const deductions = (final.body.lineItems as { unitPrice?: { netAmount: number } }[]).filter((l) => (l.unitPrice?.netAmount ?? 0) < 0);
  expect(deductions.map((l) => l.unitPrice!.netAmount)).toEqual([-4060]);
});

test('Übersicht Verkauf: Reiter, Status, Filter, Summenzeile', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const invoice = stub.addVoucher({ type: 'invoice', status: 'open', number: 'RE-2026-0900', date: '2026-10-01', dueDate: '2026-10-15', net: 1000, gross: 1190, contactId: contact });
  stub.openAmounts.set(invoice, 1190);
  await page.goto('/verkauf');
  const table = page.locator('[id$="salesTable"]');
  await expect(table).toContainText('RE-2026-0900');
  await expect(page.locator('[id$="salesSummary"]')).toContainText('Belege, netto');
  await page.locator('[id$="salesTypes"]').getByText('Rechnungen').click();
  await expect(table).not.toContainText('Auftragsbestätigung');
  await page.locator('[id$="salesStatus"]').getByText('Offen', { exact: true }).click();
  await expect(table.getByRole('row').filter({ hasText: 'RE-2026-0900' })).toContainText('Überfällig');
  await page.locator('[id$="salesStatus"]').getByText('Alle', { exact: true }).click();
  await page.locator('input[id$="salesSearch-I"]').fill('Seeweg');
  await expect(table).not.toContainText('RE-2026-0900');
  await page.locator('input[id$="salesSearch-I"]').fill('');
  await page.screenshot({ path: 'test-results/verkauf-uebersicht.png' });

  // Automatische Zuordnung: Rechnung aus Lexware Office mit der Bauaktennummer im Titel, beim Aktualisieren in der Bauakte.
  const number = (await apiCall<{ number: string }>(page, 'GET', `/api/v1/bauakten/${bauakte}`)).json.number;
  stub.addVoucher({ type: 'invoice', status: 'open', number: 'RE-2026-0950', date: '2026-11-20', dueDate: '2026-12-04', net: 500, gross: 595, contactId: contact,
    title: `Nachtrag Bauakte ${number}` });
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  await page.locator('[id$="invoiceRefresh"]').click();
  await expect(page.locator('[id$="invoiceTable"]').getByRole('row').filter({ hasText: 'RE-2026-0950' }))
    .toContainText('automatisch zugeordnet: Bauaktennummer im Beleg');
  await expect(page.locator('[id$="invoiceTable"]')).not.toContainText('RE-2026-0900');

  // Nie geändert oder gelöscht: nur POST auf Belege, keine PUT/DELETE auf Belege.
  expect(stub.calls.filter((c) => ['PUT', 'DELETE', 'PATCH'].includes(c.method) && !c.path.startsWith('/v1/contacts') && !c.path.startsWith('/v1/articles'))).toHaveLength(0);

  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: {
    lexware: false, lexwareArticles: false, lexwareStatus: false, sales: false, quotations: false, salesOrderConfirmations: false, salesInvoices: false,
    salesCreditNotes: false, salesFinalize: false, salesPayments: false,
  } })).status).toBe(200);
});
