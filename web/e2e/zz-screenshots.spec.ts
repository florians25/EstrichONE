import { test, type Page } from '@playwright/test';

const nav = (page: Page, name: string) => page.locator('.ant-pro-sider').getByText(name, { exact: true });

// Nur für die visuelle Durchsicht (SCREENSHOTS=1), nicht Teil der regulären Tests.
test.skip(!process.env['SCREENSHOTS'], 'nur auf Anforderung');

test('Bildschirmfotos', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/anmelden');
  await page.screenshot({ path: 'test-results/screenshots/login.png' });
  await page.getByLabel('Anmeldename oder E-Mail').fill('admin');
  await page.getByLabel('Passwort').fill('Estrich-Chefin-2026!');
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL('**/start');
  await page.waitForTimeout(600);
  await page.screenshot({ path: 'test-results/screenshots/start.png', fullPage: true });
  await nav(page, 'Benutzer').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/users.png' });
  await nav(page, 'Geräte').click();
  await page.getByRole('button', { name: 'Gerät einrichten' }).click();
  await page.getByRole('button', { name: 'Code erzeugen' }).click();
  await page.getByAltText('QR-Code zur Einrichtung').waitFor();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/devices.png' });
  await page.getByRole('button', { name: 'Fertig' }).click();
  await nav(page, 'Planung').click();
  await page.waitForTimeout(800);
  await page.screenshot({ path: 'test-results/screenshots/planning.png', fullPage: true });
  await nav(page, 'Bauakten').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/bauakten.png', fullPage: true });
  await page.getByRole('link', { name: /Anfrage Familie Kraus/ }).click();
  await page.getByRole('tab', { name: 'Räume und Flächen' }).click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/rooms.png', fullPage: true });
  await nav(page, 'Helpdesk').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/helpdesk.png', fullPage: true });
  await page.getByRole('tab', { name: 'Alle' }).click();
  await page.getByRole('row', { name: /Drucker/ }).getByRole('link').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/ticket.png', fullPage: true });
  await nav(page, 'Kunden').click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/customers.png' });
  await nav(page, 'Bauakten').click();
  await page.getByRole('link', { name: /EFH Prüfung E2E/ }).click();
  await page.getByRole('tab', { name: 'Prüfungen und Bautagebuch' }).click();
  await page.waitForTimeout(500);
  await page.screenshot({ path: 'test-results/screenshots/records.png', fullPage: true });
});
