import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, signIn, toast } from './helpers';
import { LexwareStub, STUB_KEY } from './lexwareStub';

// Fehler aus Lexware Office verständlich und behebbar: Die nachgebildete Organisation lehnt die Steuerart § 13b ab, genau
// mit der Antwort eines echten Kontos. EstrichONE zeigt die Meldung auf Deutsch, den Originaltext nur aufklappbar, bietet
// „Steuerart Netto übernehmen und erneut senden“ an, merkt sich die Ablehnung und lässt sie in den Einstellungen erneut prüfen.
test.describe.configure({ mode: 'serial' });

const stub = new LexwareStub();
let contact = '';

test.beforeAll(async () => {
  await stub.start();
  contact = stub.addContact({ name: 'Bergblick Bau GmbH', email: 'einkauf@bergblick.example', street: 'Almweg 1', zip: '83024', city: 'Rosenheim' });
  stub.rejectedTaxTypes.add('constructionService13b');
});

test.afterAll(async () => {
  await stub.stop();
});

test('Angebot mit § 13b: Ablehnung verständlich, Behebung mit einem Klick', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { apiKey: STUB_KEY, baseUrl: stub.url })).status).toBe(200);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: true, quotations: true } })).status).toBe(200);
  const customer = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', { id: customer, kind: 'company', name: 'Bergblick Bau GmbH', lexwareCreateAnyway: true })).status).toBe(201);
  expect((await apiCall(page, 'POST', `/api/v1/lexware/customers/${customer}/link`, { lexwareContactId: contact })).status).toBe(200);
  const id = crypto.randomUUID();
  const created = await apiCall<{ version: number; validUntil: string }>(page, 'POST', '/api/v1/lexware/quotations', { id, contactId: customer });
  expect(created.status).toBe(201);
  expect((await apiCall(page, 'PUT', `/api/v1/lexware/quotations/${id}`, {
    version: created.json.version, title: 'Angebot Tiefgarage', validUntil: created.json.validUntil, taxType: 'constructionService13b',
    lines: [{ kind: 'item', name: 'Zementestrich liefern und einbauen', quantity: 420, unit: 'm²', netPrice: 21.5, taxRate: 19 }],
  })).status).toBe(200);

  await page.goto(`/angebote/${id}`);
  await page.locator('[id$="quotationSend"]').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Senden' }).click();

  // Meldung auf Deutsch mit Behebung; der englische Originaltext steht nur unter „Technische Einzelheiten“.
  const dialog = page.getByRole('alertdialog', { name: /Senden an Lexware Office/ });
  await expect(dialog).toContainText('Lexware Office lässt die Steuerart Bauleistung nach § 13b UStG für Ihr Unternehmen nicht zu. Prüfen Sie die Steuereinstellungen in Lexware Office oder senden Sie das Angebot mit der Steuerart Netto.');
  await expect(dialog.getByText('Tax type', { exact: false })).toBeHidden();
  await dialog.getByRole('button', { name: /Technische Einzelheiten/ }).click();
  await expect(dialog).toContainText("Tax type 'constructionService13b' not allowed for this organization");
  await page.screenshot({ path: 'test-results/lexware-fehler-13b.png' });
  await dialog.getByRole('button', { name: 'Abbrechen' }).click();

  // Auf der Seite bleibt die Meldung mit derselben Behebung; die Steuerart ist für Angebote gesperrt, mit Hinweis.
  await expect(page.locator('[id$="quotationFailed"]')).toContainText('§ 13b UStG für Ihr Unternehmen nicht zu');
  await expect(page.locator('[id$="quotationFailed"]')).not.toContainText('not allowed');
  await expect(page.locator('[id$="qTaxHint"]')).toContainText('abgelehnt');
  await page.screenshot({ path: 'test-results/lexware-fehler-13b-seite.png' });
  await page.locator('[id$="quotationFix"]').click();
  await expect(toast(page)).toContainText('Als Entwurf an Lexware Office gesendet');
  await expect(page.locator('[id$="quotationSent"]')).toBeVisible();
  expect(stub.quotations).toHaveLength(1);
  expect((stub.quotations[0] as { taxConditions: { taxType: string } }).taxConditions.taxType).toBe('net');

  // Gemerkt: Die Auswahl bietet § 13b für Angebote nicht mehr an.
  const options = await apiCall<{ taxTypes: { key: string; allowed: boolean }[] }>(page, 'GET', '/api/v1/lexware/quotations/options');
  expect(options.json.taxTypes.find((t) => t.key === 'constructionService13b')?.allowed).toBe(false);
});

test('Einstellungen: erlaubte Steuerarten erneut prüfen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/lexware-office');
  await expect(page.locator('[id$="lxTaxTypes"]')).toContainText('Bauleistung nach § 13b UStG für Angebote');
  stub.rejectedTaxTypes.clear();
  await page.locator('[id$="lxTaxRecheck"]').click();
  await expect(toast(page)).toContainText('Erlaubte Steuerarten geprüft');
  await expect(page.locator('[id$="lxTaxTypes"]')).not.toContainText('Abgelehnt am');
  await page.screenshot({ path: 'test-results/lexware-steuerarten-einstellungen.png' });
  const options = await apiCall<{ taxTypes: { key: string; allowed: boolean }[] }>(page, 'GET', '/api/v1/lexware/quotations/options');
  expect(options.json.taxTypes.every((t) => t.allowed)).toBe(true);

});

test('Bauakte: Auftragsbestätigung und Lieferschein heißen „Erstellt“, nicht „Offen“', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const customer = (await apiCall<{ id: string; name: string }[]>(page, 'GET', '/api/v1/contacts?search=Bergblick')).json.find((c) => c.name === 'Bergblick Bau GmbH')!.id;
  const bauakte = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: bauakte, title: 'Tiefgarage Bergblick', customerId: customer, status: 'planned' })).status).toBe(201);
  const order = stub.addVoucher({ type: 'orderconfirmation', status: 'open', number: 'AB-2026-0007', date: '2026-11-20', net: 9030, gross: 10745.7, contactId: contact });
  const delivery = stub.addVoucher({ type: 'deliverynote', status: 'open', number: 'LS-2026-0003', date: '2026-11-24', net: 0, gross: 0, contactId: contact });
  const invoice = stub.addVoucher({ type: 'invoice', status: 'open', number: 'RE-2026-0101', date: '2026-11-25', dueDate: '2026-12-09', net: 4000, gross: 4760, contactId: contact });
  for (const [lexwareId, type] of [[order, 'orderconfirmation'], [delivery, 'deliverynote'], [invoice, 'invoice']]) {
    expect((await apiCall(page, 'POST', `/api/v1/bauakten/${bauakte}/lexware/vouchers/assign`, { lexwareId, type })).status).toBe(201);
  }
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  const table = page.locator('[id$="invoiceTable"]');
  await expect(table.getByRole('row').filter({ hasText: 'AB-2026-0007' })).toContainText('Erstellt');
  await expect(table.getByRole('row').filter({ hasText: 'AB-2026-0007' })).not.toContainText('Offen');
  await expect(table.getByRole('row').filter({ hasText: 'LS-2026-0003' })).toContainText('Lieferschein');
  await expect(table.getByRole('row').filter({ hasText: 'LS-2026-0003' })).toContainText('Erstellt');
  await expect(table.getByRole('row').filter({ hasText: 'RE-2026-0101' })).toContainText('Offen');
  // Offene Beträge nur aus der Rechnung, nicht aus der Auftragsbestätigung.
  await expect(page.getByText(/offen 4\.760,00/)).toBeVisible();
  await page.screenshot({ path: 'test-results/lexware-status-belegarten.png' });

  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: false, quotations: false } })).status).toBe(200);
});
