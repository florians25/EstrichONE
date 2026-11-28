import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, signIn, toast } from './helpers';
import { LexwareStub, STUB_KEY } from './lexwareStub';

// Modul „Eingangsbelege“ gegen die nachgebildete Lexware-API: Belegliste der Lieferanten mit Filtern und Summenzeile,
// Zuordnung zur Bauakte (Kosten der Bauakte als Textzeile), XRechnung hochladen, gelesen und geprüft als Objektseite,
// Übergabe an Lexware Office genau einmal.
test.describe.configure({ mode: 'serial' });

const stub = new LexwareStub();
let bauakte = '';

const xrechnung = `<?xml version="1.0" encoding="UTF-8"?>
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
  <cbc:ID>RE-4711</cbc:ID><cbc:IssueDate>2026-11-20</cbc:IssueDate><cbc:DueDate>2026-12-04</cbc:DueDate><cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>
  <cbc:BuyerReference>Bauakte Seeweg</cbc:BuyerReference>
  <cac:AccountingSupplierParty><cac:Party><cac:PostalAddress><cbc:StreetName>Werkstraße 5</cbc:StreetName><cbc:CityName>Rosenheim</cbc:CityName>
    <cbc:PostalZone>83026</cbc:PostalZone></cac:PostalAddress><cac:PartyTaxScheme><cbc:CompanyID>DE123456789</cbc:CompanyID></cac:PartyTaxScheme>
    <cac:PartyLegalEntity><cbc:RegistrationName>Baustoffe Inn GmbH</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingSupplierParty>
  <cac:AccountingCustomerParty><cac:Party><cac:PartyLegalEntity><cbc:RegistrationName>Estrichbau E2E GmbH</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingCustomerParty>
  <cac:TaxTotal><cbc:TaxAmount currencyID="EUR">114.00</cbc:TaxAmount></cac:TaxTotal>
  <cac:LegalMonetaryTotal><cbc:TaxExclusiveAmount currencyID="EUR">600.00</cbc:TaxExclusiveAmount><cbc:TaxInclusiveAmount currencyID="EUR">714.00</cbc:TaxInclusiveAmount>
    <cbc:PayableAmount currencyID="EUR">714.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
  <cac:InvoiceLine><cbc:InvoicedQuantity unitCode="XBG">40</cbc:InvoicedQuantity><cbc:LineExtensionAmount currencyID="EUR">400.00</cbc:LineExtensionAmount>
    <cac:Item><cbc:Name>Zement CEM II 25 kg</cbc:Name><cac:ClassifiedTaxCategory><cbc:Percent>19</cbc:Percent></cac:ClassifiedTaxCategory></cac:Item>
    <cac:Price><cbc:PriceAmount currencyID="EUR">10.00</cbc:PriceAmount></cac:Price></cac:InvoiceLine>
  <cac:InvoiceLine><cbc:InvoicedQuantity unitCode="MTR">400</cbc:InvoicedQuantity><cbc:LineExtensionAmount currencyID="EUR">200.00</cbc:LineExtensionAmount>
    <cac:Item><cbc:Name>Randdämmstreifen 10 mm</cbc:Name><cac:ClassifiedTaxCategory><cbc:Percent>19</cbc:Percent></cac:ClassifiedTaxCategory></cac:Item>
    <cac:Price><cbc:PriceAmount currencyID="EUR">0.50</cbc:PriceAmount></cac:Price></cac:InvoiceLine>
</Invoice>`;

test.beforeAll(async () => {
  await stub.start();
  stub.addVoucher({ type: 'purchaseinvoice', status: 'open', number: 'ER-2026-31', date: '2026-11-10', dueDate: '2026-11-24', net: 1000, gross: 1190,
    contactName: 'Transportbeton Süd GmbH' });
  stub.addVoucher({ type: 'purchaseinvoice', status: 'paid', number: 'ER-2026-30', date: '2026-11-02', net: 200, gross: 238, contactName: 'Baustoffe Inn GmbH' });
});

test.afterAll(async () => {
  await stub.stop();
});

test('Eingangsbelege: Liste, Zuordnung zur Bauakte, Kosten der Bauakte', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/integrations/lexware', { apiKey: STUB_KEY, baseUrl: stub.url })).status).toBe(200);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: true, purchases: true } })).status).toBe(200);
  const customer = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/contacts', { id: customer, kind: 'company', name: 'Seeweg Wohnbau GmbH' })).status).toBe(201);
  bauakte = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', {
    id: bauakte, title: 'Wohnanlage Seeweg Eingang', customerId: customer, status: 'planned', street: 'Seeweg 11', postalCode: '83043', city: 'Bad Aibling',
  })).status).toBe(201);

  await page.goto('/eingangsbelege');
  const table = page.locator('[id$="purchaseTable"]');
  await expect(table).toContainText('ER-2026-31');
  await expect(table.getByRole('row').filter({ hasText: 'ER-2026-31' })).toContainText('Überfällig');
  await expect(page.locator('[id$="purchaseSummary"]')).toContainText('2 Belege, brutto 1.428,00');
  await page.locator('[id$="purchaseStatus"]').getByText('Bezahlt').click();
  await expect(table).not.toContainText('ER-2026-31');
  await page.locator('[id$="purchaseStatus"]').getByText('Alle').click();
  await page.screenshot({ path: 'test-results/eingangsbelege-liste.png' });

  // Zuordnen: Dialog mit Bauakte und Lieferant.
  await table.getByRole('row').filter({ hasText: 'ER-2026-31' }).click();
  const dialog = page.getByRole('dialog', { name: /Eingangsrechnung ER-2026-31/ });
  await dialog.getByRole('combobox').first().fill('Wohnanlage Seeweg Eingang');
  await page.getByRole('option', { name: /Wohnanlage Seeweg Eingang/ }).first().click();
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Gespeichert');
  await expect(table.getByRole('row').filter({ hasText: 'ER-2026-31' })).toContainText('Wohnanlage Seeweg Eingang');

  // Bauakte: Kosten als Textzeile.
  await page.goto(`/bauakten/${bauakte}?abschnitt=rechnungen`);
  await expect(page.locator('[id$="purchaseCostsText"]')).toContainText('Erlöse 0,00 €, Kosten 1.190,00 €, Differenz -1.190,00 €');
  await expect(page.locator('[id$="purchaseCostsTable"]')).toContainText('Transportbeton Süd GmbH');
});

test('Belegeingang: XRechnung hochladen, prüfen, an Lexware Office übergeben', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/eingangsbelege');
  const chooser = page.waitForEvent('filechooser');
  await page.locator('[id$="purchaseUpload"]').getByRole('button').click();
  await (await chooser).setFiles({ name: 'RE-4711.xml', mimeType: 'application/xml', buffer: Buffer.from(xrechnung, 'utf8') });

  // Ein Beleg: die Objektseite öffnet sich mit gelesener E-Rechnung.
  await expect(page).toHaveURL(/\/eingangsbelege\/[0-9a-f-]{36}/);
  await expect(page.locator('[id$="docTitle"]')).toHaveText('Rechnung RE-4711 von Baustoffe Inn GmbH');
  await expect(page.locator('[id$="docOk"]')).toBeVisible();
  await expect(page.locator('[id$="docLines"]')).toContainText('Zement CEM II 25 kg');
  await expect(page.locator('[id$="docLines"]')).toContainText('40 Sack');
  await expect(page.locator('[id$="docTotals"]')).toContainText('Brutto 714,00');
  await expect(page.locator('.eoFactList')).toContainText('DE123456789');
  await page.locator('[id$="docBauakte"]').locator('input').fill('Wohnanlage Seeweg Eingang');
  await page.getByRole('option', { name: /Wohnanlage Seeweg Eingang/ }).first().click();
  await page.screenshot({ path: 'test-results/eingangsbeleg-xrechnung.png', fullPage: true });

  await page.locator('[id$="docSend"]').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Übergeben' }).click();
  await expect(toast(page)).toContainText('An Lexware Office übergeben');
  await expect(page.locator('[id$="docSent"]')).toContainText('zu prüfen');
  await expect(page.locator('[id$="docSend"]')).toBeHidden();
  expect(stub.files.map((f) => f.name)).toEqual(['RE-4711.xml']);

  // Dieselbe Datei noch einmal: kein zweiter Beleg.
  await page.goto('/eingangsbelege?reiter=eingang');
  const again = page.waitForEvent('filechooser');
  await page.locator('[id$="purchaseUpload"]').getByRole('button').click();
  await (await again).setFiles({ name: 'RE-4711-nochmal.xml', mimeType: 'application/xml', buffer: Buffer.from(xrechnung, 'utf8') });
  await expect(toast(page)).toContainText('1 Datei war schon vorhanden');
  await expect(page.locator('[id$="purchaseDocuments"]').getByRole('row').filter({ hasText: 'RE-4711' })).toHaveCount(1);
  expect(stub.files).toHaveLength(1);

  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { purchases: false } })).status).toBe(200);
});
