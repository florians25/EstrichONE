import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, openSection, signIn, toast } from './helpers';

const title = 'Garage Erstbegehung entfällt';
const shots = process.env['E2E_SHOT_DIR'];

async function shot(page: Page, name: string) {
  if (shots) await page.waitForTimeout(600).then(() => page.screenshot({ path: `${shots}/${name}.png` }));
}

async function openAkte(page: Page) {
  await page.goto('/bauakten');
  const list = await apiCall<{ items: { title: string }[] }>(page, 'GET', `/api/v1/bauakten?search=${encodeURIComponent(title)}`);
  if (!list.json?.items?.some((b) => b.title === title)) {
    expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: crypto.randomUUID(), title, street: 'Hofweg 2', postalCode: '84028', city: 'Landshut' })).status).toBe(201);
    await page.reload();
  }
  await page.getByRole('link', { name: title }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible();
}

test('Erstbegehung nicht erforderlich: am Hinweis vermerken, in Prüfungen anzeigen und rückgängig machen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  const hint = page.locator('.eoHintBadges').getByText('Erstbegehung noch nicht ausgeführt', { exact: true }).filter({ visible: true });
  await expect(hint.first()).toBeVisible();
  await openSection(page, 'Dokumentation');
  await page.getByRole('tab', { name: /Prüfungen/ }).click();
  const waive = page.locator('[id$="waiveInitialButton"]');
  await expect(waive).toBeVisible();
  await shot(page, 'web-bauakte-erstbegehung-vorher');

  // Am Hinweis im Kopf: kurze Bestätigung mit optionaler Begründung.
  await page.locator('[id$="waiveInitialHintButton"]').click();
  const dialog = page.getByRole('dialog', { name: 'Erstbegehung nicht erforderlich' });
  await expect(dialog).toContainText('jederzeit rückgängig');
  await dialog.getByRole('textbox').fill('Bestandsgebäude, Untergrund bekannt');
  await dialog.getByRole('button', { name: 'Bestätigen' }).click();
  await expect(toast(page)).toContainText('Keine Erstbegehung erforderlich');

  await expect(hint).toHaveCount(0);
  await expect(page.locator('[id$="waiveInitialHintButton"]').filter({ visible: true })).toHaveCount(0);
  const strip = page.locator('[id$="waiverStatus"]');
  await expect(strip).toContainText(/Keine Erstbegehung erforderlich \(.+, \d{2}\.\d{2}\.\d{4}, Bestandsgebäude, Untergrund bekannt\)/);
  await expect(waive.filter({ visible: true })).toHaveCount(0);
  await shot(page, 'web-bauakte-erstbegehung-nachher');

  // Rückgängig: wieder offen, Schaltfläche wird erneut angeboten.
  await page.locator('[id$="undoWaiverButton"]').click();
  await expect(toast(page)).toContainText('Vermerk aufgehoben');
  await expect(hint.first()).toBeVisible();
  await expect(waive).toBeVisible();
  await expect(strip.filter({ visible: true })).toHaveCount(0);
});
