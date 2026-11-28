import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, signIn, toast } from './helpers';
import { LexwareStub, STUB_KEY } from './lexwareStub';

// Artikelabgleich, Angebote (Beta) und Status der Bauakte gegen die nachgebildete Lexware-API dieses Testprozesses:
// Artikel übernehmen, Angebot aus der Bauakte mit Vorschlägen vorbereiten, als Entwurf senden (ohne finalize, nur einmal),
// Beleg in der Bauakte, angenommenes Angebot setzt die Anfrage auf „Geplant“, bezahlte Rechnungen stellen zur Abschlussprüfung.
test.describe.configure({ mode: 'serial' });

const stub = new LexwareStub();
let contact = '';
let customer = '';
let bauakte = '';
let quotation = '';

test.beforeAll(async () => {
  await stub.start();
  contact = stub.addContact({ name: 'Sonnenhang GmbH', email: 'einkauf@sonnenhang.example', street: 'Talweg 2', zip: '83022', city: 'Rosenheim' });
  stub.addArticle({ title: 'Zementestrich CT-C25-F4 liefern und einbauen', number: 'L-100', net: 24.5, unit: 'm²', description: 'Nenndicke 65 mm, auf Dämmung' });
  stub.addArticle({ title: 'Randdämmstreifen 8 mm', type: 'PRODUCT', number: 'L-200', net: 0.95, unit: 'lfm' });
});

test.afterAll(async () => {
  await stub.stop();
});

test('Artikel und Leistungen: Abgleich aus Lexware Office', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { apiKey: STUB_KEY, baseUrl: stub.url })).status).toBe(200);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: true, lexwareArticles: true, sales: true, quotations: true, lexwareStatus: true } })).status)
    .toBe(200);

  // Einstellungen: Richtung des Artikelabgleichs (Vorgabe aus Lexware Office) und Regeln des Bauaktenstatus.
  await page.goto('/system/lexware-office');
  await expect(page.locator('[id$="lxArticleSync"]').first()).toContainText('Aus Lexware Office nach EstrichONE');
  await expect(page.locator('[id$="lxRuleQuotation"]').first()).toHaveAttribute('aria-checked', 'true');
  await expect(page.locator('[id$="lxRuleInvoices"]').first()).toHaveAttribute('aria-checked', 'true');

  await page.goto('/artikel');
  await expect(page.locator('[id$="articleTable"]')).toContainText('Noch keine Einträge');
  await page.locator('[id$="articleSyncNow"]').click();
  await expect(toast(page)).toContainText('2 übernommen');
  const table = page.locator('[id$="articleTable"]');
  await expect(table.getByRole('row').filter({ hasText: 'Zementestrich CT-C25-F4' })).toContainText('Abgeglichen');
  await expect(table.getByRole('row').filter({ hasText: 'Randdämmstreifen 8 mm' })).toContainText('Material');
  await expect(page.locator('[id$="articleSyncStand"]')).toContainText('aus Lexware Office nach EstrichONE');
  await page.screenshot({ path: 'test-results/artikel-abgleich.png' });

  // Vorgabe „aus Lexware Office“: In Lexware Office wurde nichts geschrieben.
  expect(stub.calls.filter((c) => c.method !== 'GET')).toHaveLength(0);
});

test('Angebot im Verkauf: aus der Bauakte erfassen und als Entwurf senden', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  customer = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', { id: customer, kind: 'company', name: 'Sonnenhang GmbH', lexwareCreateAnyway: true })).status)
    .toBe(201);
  bauakte = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', {
    id: bauakte, title: 'Reihenhäuser Sonnenhang', customerId: customer, status: 'inquiry', street: 'Talweg 4', postalCode: '83022', city: 'Rosenheim',
    project: { totalArea: 286.4, nominalThicknessMm: 65 },
  })).status).toBe(201);

  // Ohne Verknüpfung: Hinweis mit Weg zum Kunden.
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  await page.locator('[id$="salesQuotation"]').click();
  const hint = page.getByRole('alertdialog');
  await expect(hint).toContainText('noch nicht mit Lexware Office verknüpft');
  await expect(hint.getByRole('button', { name: 'Zum Kunden' })).toBeVisible();
  await hint.getByRole('button', { name: 'Schließen' }).click();

  expect((await apiCall(page, 'POST', `/api/v1/lexware/customers/${customer}/link`, { lexwareContactId: contact })).status).toBe(200);
  await page.reload();
  await page.locator('[id$="salesQuotation"]').click();
  await expect(page).toHaveURL(/\/verkauf\//);
  quotation = page.url().split('/verkauf/')[1];
  await expect(page.locator('[id$="salesTitle"]')).toContainText('Angebot');
  await expect(page.getByText('wird automatisch vergeben').first()).toBeVisible();

  // Vorschlag aus der Bauakte: Fläche aus den Kennwerten, Preis aus dem einzigen Katalogeintrag je m².
  await page.locator('[id$="sSuggest"]').click();
  const suggest = page.getByRole('dialog', { name: 'Vorschläge aus der Bauakte' });
  await expect(suggest).toContainText('286,4 m²');
  await expect(suggest).toContainText('Randdämmstreifen werden nicht vorgeschlagen');
  await suggest.getByRole('button', { name: 'Übernehmen' }).click();

  // Aus Artikeln und Leistungen, dazu eine Textposition.
  await page.locator('[id$="sCatalog"]').click();
  const pick = page.getByRole('dialog', { name: 'Artikel suchen' });
  await pick.getByRole('listitem').filter({ hasText: 'Randdämmstreifen 8 mm' }).getByRole('checkbox').click();
  await pick.getByRole('button', { name: /^(OK|Auswählen|Übernehmen)$/ }).click();
  await page.locator('[id$="sAddText"]').click();
  const lines = page.locator('[id$="salesLines"]');
  await lines.getByRole('row').last().locator('input[type="text"]').first().fill('Bauseits Strom und Wasser');

  await expect(page.locator('span[id$="sNet"]')).toContainText('7.017,75');
  await page.locator('[id$="sPayment"]').first().click();
  await page.getByRole('option', { name: /14 Tagen ohne Abzug/ }).click();
  await page.locator('[id$="salesSave"]').click();
  await expect(toast(page)).toContainText('Beleg gespeichert');
  await expect(page.locator('span[id$="sGross"]')).toContainText('€');
  await page.screenshot({ path: 'test-results/angebot-maske.png' });

  await page.locator('[id$="salesSend"]').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Senden' }).click();
  await expect(toast(page)).toContainText('Als Entwurf an Lexware Office gesendet');
  await expect(page.locator('[id$="salesSent"]')).toBeVisible();
  await expect(page.locator('input[id$="sTitle-inner"]')).not.toBeEditable();

  // Genau ein Entwurf, ohne finalize; Steuerart netto; Zahlungsbedingung aus Lexware Office.
  expect(stub.quotations).toHaveLength(1);
  expect(stub.calls.filter((c) => c.method === 'POST' && c.path === '/v1/quotations')).toHaveLength(1);
  const sent = stub.quotations[0] as { lineItems: { type: string; name: string }[]; taxConditions: { taxType: string }; paymentConditions: { paymentTermDuration: number } };
  expect(sent.taxConditions.taxType).toBe('net');
  expect(sent.paymentConditions.paymentTermDuration).toBe(14);
  // Katalogeinträge mit Lexware-Verknüpfung gehen als Artikel (Leistung bzw. Material), der Text als Textposition.
  expect(sent.lineItems.map((l) => l.type)).toEqual(['service', 'material', 'text']);

  // Erneut senden legt nichts doppelt an.
  expect((await apiCall(page, 'POST', `/api/v1/sales/vouchers/${quotation}/send`)).status).toBe(200);
  expect(stub.quotations).toHaveLength(1);
});

test('Gesendetes Angebot in der Bauakte, Status aus Lexware Office', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  const vouchers = page.locator('[id$="invoiceTable"]');
  const row = vouchers.getByRole('row').filter({ hasText: 'Angebot' }).filter({ hasText: 'Entwurf' });
  await expect(row).toBeVisible();
  await expect(row.getByRole('button', { name: 'In Lexware öffnen' })).toBeVisible();
  await page.screenshot({ path: 'test-results/angebot-bauakte.png' });

  // Angebot in Lexware Office angenommen: Die Anfrage wird „Geplant“.
  const lexwareId = [...stub.vouchers.values()].find((v) => v.voucherType === 'quotation')!.id as string;
  stub.change(lexwareId, { status: 'accepted', number: 'AG-2026-0042' });
  await page.locator('[id$="invoiceRefresh"]').click();
  await expect(vouchers).toContainText('AG-2026-0042');
  await expect.poll(async () => (await apiCall<{ status: string }>(page, 'GET', `/api/v1/bauakten/${bauakte}`)).json.status).toBe('planned');

  // Alle Rechnungen bezahlt: zur Abschlussprüfung, Abschließen bleibt von Hand.
  const invoice = stub.addVoucher({ type: 'invoice', status: 'paid', number: 'RE-2026-0100', date: '2026-11-20', dueDate: '2026-12-04', net: 7000, gross: 8330, contactId: contact });
  expect((await apiCall(page, 'POST', `/api/v1/bauakten/${bauakte}/lexware/vouchers/assign`, { lexwareId: invoice, type: 'invoice' })).status).toBe(201);
  await page.reload();
  await page.locator('[id$="invoiceRefresh"]').click();
  await page.reload();
  await expect(page.locator('[id$="reviewStrip"]')).toContainText('Rechnungen vollständig bezahlt, bitte prüfen und abschließen');
  await expect(page.locator('[id$="reviewBadge"]')).toBeVisible();
  await page.screenshot({ path: 'test-results/bauakte-abschlusspruefung.png' });
  expect((await apiCall<{ status: string }>(page, 'GET', `/api/v1/bauakten/${bauakte}`)).json.status).toBe('planned');

  await page.goto('/bauakten?status=review');
  await expect(page.getByRole('row').filter({ hasText: 'Reihenhäuser Sonnenhang' })).toContainText('Zur Abschlussprüfung');

  await page.goto(`/bauakten/${bauakte}`);
  await page.locator('[id$="reviewDismiss"]').click();
  await expect(toast(page)).toContainText('Prüfung zurückgestellt');
  await expect(page.locator('[id$="reviewStrip"]')).toBeHidden();

  // Module wieder aus, damit die übrigen Tests unverändert laufen.
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', {
    modules: {
      lexware: false, lexwareArticles: false, lexwareStatus: false, sales: false, quotations: false, salesOrderConfirmations: false, salesInvoices: false,
      salesCreditNotes: false, salesFinalize: false, salesPayments: false,
    },
  })).status).toBe(200);
});
