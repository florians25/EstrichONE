import { test } from '@playwright/test';

// Nur für die visuelle Durchsicht (SCREENSHOTS=1), nicht Teil der regulären Tests.
test.skip(!process.env['SCREENSHOTS'], 'nur auf Anforderung');

test('Bildschirmfotos', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/anmelden');
  await page.screenshot({ path: 'test-results/screenshots/login.png' });
  await page.getByLabel('Anmeldename oder E-Mail').fill('admin');
  await page.getByLabel('Passwort').fill('Estrich-Chefin-2026!');
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL('**/bauakten');
  await page.getByRole('link', { name: 'Benutzer' }).click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/users.png' });
  await page.getByRole('link', { name: 'Geräte' }).click();
  await page.getByRole('button', { name: 'Gerät einrichten' }).click();
  await page.getByRole('button', { name: 'Code erzeugen' }).click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/devices.png' });
});
