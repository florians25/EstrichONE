import { expect, test, type Page } from '@playwright/test';
import { adminPassword, nav, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

// Bildschirmfotos nur auf Anforderung (SCREENSHOTS=1, Ordner SCREENSHOT_DIR).
const shot = async (page: Page, name: string) => {
  if (!process.env['SCREENSHOTS']) return;
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${process.env['SCREENSHOT_DIR'] ?? 'test-results/screenshots'}/${name}.png`, fullPage: true });
};

const field = (page: Page, id: string) => page.locator(`input[id$="--${id}-inner"], textarea[id$="--${id}-inner"]`);

test('App-Updates: Versionen, Regeln mit Prüfung, erzwungene Mindestversion sperrt alte Apps (426), Web arbeitet weiter', async ({ page }) => {
  test.setTimeout(120_000);
  await signIn(page, 'admin', adminPassword);
  try {
    // Über die Navigation und die Systemeinstellungen erreichbar.
    await expect(nav(page, 'App-Updates')).toBeVisible();
    await page.goto('/system/firmendaten?bereich=system');
    await page.locator('.sapMLIB').filter({ hasText: 'iOS- und Android-App: Versionen' }).click();
    await expect(page).toHaveURL(/\/it-center\/app-updates/);
    await expect(page.getByRole('heading', { name: 'App-Updates', level: 1 })).toBeVisible();
    await expect(page.locator('span[id$="--appServerVersion"]')).not.toBeEmpty();
    await expect(page.getByText('Regeln', { exact: true })).toBeVisible();
    await shot(page, 'web-app-updates');

    // Unsicherer Link wird abgelehnt.
    await page.getByRole('switch', { name: 'Mindestversion erzwingen' }).click();
    await field(page, 'appMinimum').fill('99.0.0');
    await field(page, 'appMessage').fill('Bitte bis Freitag aktualisieren');
    await field(page, 'appIosUrl').fill('http://testflight.example.com/join/abc');
    await page.locator('button[id$="--appSave"]').click();
    const dialog = page.getByRole('alertdialog', { name: 'Bitte prüfen' }).or(page.getByRole('dialog', { name: 'Bitte prüfen' }));
    await expect(dialog).toContainText('nur https-Adressen');
    await dialog.getByRole('button').first().click();

    // Gültig speichern; die Mindestversion liegt nie über der verfügbaren Version (hier: Serverversion).
    await field(page, 'appIosUrl').fill('https://testflight.apple.com/join/abc');
    await page.locator('button[id$="--appSave"]').click();
    await expect(toast(page)).toContainText('App-Updates gespeichert');
    await shot(page, 'web-app-updates-regeln');

    const check = await page.request.get('/api/v1/app-updates/check?platform=ios&version=0.0.1');
    expect(check.status()).toBe(200);
    const result = await check.json() as { status: string; message: string; minimumVersion: string; latestVersion: string; download: { url: string } };
    expect(result.status).toBe('required');
    expect(result.message).toBe('Bitte bis Freitag aktualisieren');
    expect(result.download.url).toBe('https://testflight.apple.com/join/abc');
    expect(result.minimumVersion).toBe(result.latestVersion);

    // Alte App: 426 mit Code; dieselbe Sitzung ohne App-Kennung (Web) arbeitet weiter.
    const old = await page.request.get('/api/v1/settings', { headers: { 'X-EstrichONE-Client': 'ios/0.0.1' } });
    expect(old.status()).toBe(426);
    expect((await old.json() as { code: string }).code).toBe('CLIENT_UPDATE_REQUIRED');
    expect((await page.request.get('/api/v1/settings')).status()).toBe(200);
  } finally {
    // Zurücksetzen für die folgenden Tests.
    await page.goto('/it-center/app-updates');
    const enforce = page.getByRole('switch', { name: 'Mindestversion erzwingen' });
    if ((await enforce.getAttribute('aria-checked')) === 'true') {
      await field(page, 'appMinimum').fill('');
      await enforce.click();
      await page.locator('button[id$="--appSave"]').click();
      await expect(toast(page)).toContainText('App-Updates gespeichert');
    }
  }
});
