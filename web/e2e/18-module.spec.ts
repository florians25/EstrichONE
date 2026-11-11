import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, nav, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

// Bildschirmfotos der Module nur auf Anforderung (SCREENSHOTS=1, Ordner SCREENSHOT_DIR).
const shot = async (page: Page, name: string) => {
  if (!process.env['SCREENSHOTS']) return;
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${process.env['SCREENSHOT_DIR'] ?? 'test-results/screenshots'}/${name}.png` });
};

const card = (page: Page, key: string) => page.locator(`.eoModuleCard[data-module="${key}"]`);

async function confirmDialog(page: Page, title: string, action: string, text?: RegExp) {
  const dialog = page.getByRole('alertdialog', { name: title }).or(page.getByRole('dialog', { name: title }));
  await expect(dialog).toBeVisible();
  if (text) await expect(dialog).toContainText(text);
  await dialog.getByRole('button', { name: action }).click();
}

test('Module: ausschalten mit Rückfrage, Navigation und Direktaufruf, Abhängigkeiten, wieder einschalten', async ({ page }) => {
  test.setTimeout(150_000);
  await signIn(page, 'admin', adminPassword);
  try {
    await page.goto('/system/firmendaten?bereich=module');
    await expect(card(page, 'calls')).toBeVisible();
    await expect(card(page, 'bauakten')).toContainText('Immer aktiv');
    await expect(card(page, 'absence')).toContainText('Benötigt: Personal');
    await shot(page, 'web-module-einstellungen');

    // Anrufe und Anfragen ausschalten: Rückfrage, dann verschwindet „Anrufe“ aus der Navigation.
    await expect(nav(page, 'Anrufe')).toBeVisible();
    await card(page, 'calls').getByRole('switch').click();
    await confirmDialog(page, 'Modul ausschalten', 'Ausschalten', /Daten bleiben erhalten/);
    await expect(toast(page)).toContainText('Anrufe und Anfragen ausgeschaltet');
    await expect(card(page, 'calls')).toHaveAttribute('data-state', 'off');
    await expect(nav(page, 'Anrufe')).toHaveCount(0);

    // Direktaufruf (Lesezeichen): klare Hinweisseite statt Fehler.
    await page.goto('/anrufe');
    await expect(page.getByText('Modul „Anrufe und Anfragen“ ist deaktiviert')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Module verwalten' })).toBeVisible();
    await shot(page, 'web-modul-deaktiviert');
    // Schreiben ist auch für Administratoren gesperrt (Lesen vorhandener Daten bleibt ihnen erlaubt).
    const api = await apiCall<{ code?: string }>(page, 'PUT', `/api/v1/calls/${crypto.randomUUID()}`, { kind: 'call' });
    expect(api.status).toBe(409);
    expect(api.json.code).toBe('MODULE_DISABLED');

    // Personal ausschalten nimmt Urlaub und Abwesenheit mit (Abhängigkeit wird erklärt).
    await page.getByRole('button', { name: 'Module verwalten' }).click();
    await card(page, 'staff').getByRole('switch').click();
    await confirmDialog(page, 'Modul ausschalten', 'Ausschalten', /Urlaub und Abwesenheit/);
    await expect(toast(page)).toContainText('mit 1 abhängigen Modul');
    await expect(card(page, 'absence')).toHaveAttribute('data-state', 'off');
    await expect(nav(page, 'Mitarbeiter')).toHaveCount(0);

    // Urlaub wieder einschalten: Personal kommt nach Rückfrage mit.
    await card(page, 'absence').getByRole('switch').click();
    await confirmDialog(page, 'Modul einschalten', 'Einschalten', /benötigt „Personal“/);
    await expect(card(page, 'staff')).toHaveAttribute('data-state', 'on');
    await expect(card(page, 'absence')).toHaveAttribute('data-state', 'on');
    await expect(nav(page, 'Mitarbeiter')).toBeVisible();

    await card(page, 'calls').getByRole('switch').click();
    await expect(toast(page)).toContainText('Anrufe und Anfragen eingeschaltet');
    await expect(nav(page, 'Anrufe')).toBeVisible();
  } finally {
    // Spätere Tests brauchen alle Module.
    await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { calls: true, staff: true, absence: true } }).catch(() => undefined);
  }
});
