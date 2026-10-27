import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, nav, openSection, signIn } from './helpers';

const title = 'Abnahme Neubau Seidl';
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');

async function openAkte(page: Page) {
  await page.goto('/bauakten');
  const list = await apiCall<{ items: { title: string }[] }>(page, 'GET', `/api/v1/bauakten?search=${encodeURIComponent(title)}`);
  if (!list.json?.items?.some((b) => b.title === title)) {
    expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: crypto.randomUUID(), title, street: 'Gartenweg 3', postalCode: '84028', city: 'Landshut' })).status).toBe(201);
    await page.reload();
  }
  await page.getByRole('link', { name: title }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible();
}

async function settings(page: Page, tab: string) {
  await page.goto(`/system/firmendaten?bereich=${tab}`);
  await expect(page.getByRole('heading', { name: 'Systemeinstellungen' })).toBeVisible();
}

test('Einstellungen: Materialmodul ab- und wieder einschalten, Firmenlogo hochladen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await settings(page, 'module');
  await page.getByRole('switch', { name: /Material \(/ }).click();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('.sapMMessageToast').last()).toContainText('Einstellungen gespeichert');
  await expect(nav(page, 'Materialstamm')).toHaveCount(0);
  await openAkte(page);
  await expect(page.locator('.sapUxAPAnchorBar').getByText('Material', { exact: true })).toHaveCount(0);

  await settings(page, 'module');
  await page.getByRole('switch', { name: /Material \(/ }).click();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(nav(page, 'Materialstamm')).toBeVisible();

  await settings(page, 'firma');
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Logo hochladen …' }).click();
  await (await chooser).setFiles({ name: 'logo.png', mimeType: 'image/png', buffer: png });
  await expect(page.locator('.sapMMessageToast').last()).toContainText('Logo gespeichert');
  // Firmenlogo nur in der Vorschau (für PDFs/Briefe), nicht in der Kopfleiste.
  await expect(page.locator('img.eoLogoPreview').first()).toBeVisible();
  await expect(page.locator('.sapFShellBar img[id$="companyLogo"]')).toHaveCount(0);
});

test('Abnahmeprotokoll: Freigabe per Link durch den Kunden, PDF unter „Abnahmen“', async ({ page, browser }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Abnahmeprotokoll erstellen …' }).click();
  const wizard = page.getByRole('dialog', { name: 'Abnahmeprotokoll' });
  await expect(wizard.locator('.eoAkteHeader')).toContainText(title);
  await wizard.locator('input[id="accClient-inner"]').fill('Frau Kraus');
  for (let i = 0; i < 2; i++) await wizard.getByRole('button', { name: 'Weiter' }).click();
  await wizard.getByText('Abgenommen mit Vorbehalt', { exact: true }).click();
  await wizard.getByRole('button', { name: 'Mangel hinzufügen' }).click();
  await wizard.getByPlaceholder(/Mangel oder Restarbeit/).fill('Randfuge im Bad nachschneiden');
  for (let i = 0; i < 2; i++) await wizard.getByRole('button', { name: 'Weiter' }).click();
  await wizard.getByLabel('Notizen (frei, beliebig lang)').fill('Heizprotokoll übergeben.');
  await wizard.getByRole('button', { name: 'Weiter' }).click();
  // Auftragnehmer unterschreibt auf dem Gerät, der Kunde gibt per Link frei.
  const pad = wizard.locator('[id="accContractorSignature"] canvas');
  const box = (await pad.boundingBox())!;
  await page.mouse.move(box.x + 20, box.y + 30);
  await page.mouse.down();
  await page.mouse.move(box.x + 120, box.y + 60, { steps: 8 });
  await page.mouse.move(box.x + 200, box.y + 25, { steps: 8 });
  await page.mouse.up();
  await wizard.getByText('Auftraggeber nicht vor Ort: Freigabe per Link an den Kunden').click();
  await wizard.getByRole('button', { name: 'Abnahme speichern' }).click();
  const info = page.getByRole('alertdialog', { name: /Freigabelink für den Kunden/ });
  await expect(info).toBeVisible();
  const url = (await info.textContent())!.match(/https?:\/\/\S+\/abnahme\/[A-Za-z0-9_-]+/)![0];
  await info.getByRole('button', { name: 'Schließen' }).click();

  // Kunde: ohne Anmeldung, nur dieses Protokoll.
  const customer = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage();
  await customer.goto(new URL(new URL(url).pathname, page.url()).toString());
  await expect(customer.getByText('Abgenommen mit Vorbehalt')).toBeVisible();
  await expect(customer.getByText('Randfuge im Bad nachschneiden')).toBeVisible();
  await customer.getByLabel('Ihr Name').fill('Petra Kraus');
  await customer.getByRole('button', { name: 'Freigeben' }).click();
  await expect(customer.getByText(/das Abnahmeprotokoll ist freigegeben/)).toBeVisible();
  await customer.close();

  await openAkte(page);
  await openSection(page, 'Dokumentation');
  await page.getByRole('tab', { name: /Abnahmen/ }).click();
  await expect(page.getByText('Abgeschlossen, PDF unter „Abnahmen“').filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/Petra Kraus/).filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/Abnahme mit Vorbehalt – Akte bleibt offen/).filter({ visible: true }).first()).toBeVisible();
  await openSection(page, 'Dokumente');
  await expect(page.locator('.sapMTreeItemBase').filter({ hasText: 'Abnahmen (1)' })).toBeVisible();
});
