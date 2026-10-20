import { expect, test } from '@playwright/test';

const setupCode = process.env['E2E_SETUP_CODE'] ?? '';
const adminPassword = 'Estrich-Chefin-2026!';

test.describe.configure({ mode: 'serial' });

test('Ersteinrichtung im Browser und Anmeldung als Administrator', async ({ page }) => {
  await page.goto('/anmelden');
  await expect(page.getByText('EstrichONE ist noch nicht eingerichtet')).toBeVisible();
  await page.getByRole('link', { name: 'Jetzt einrichten' }).click();

  await page.getByLabel('Einrichtungscode').fill(setupCode);
  await page.getByLabel('Firmenname').fill('Estrichbau E2E GmbH');
  await page.getByLabel('Name', { exact: true }).fill('Anna Admin');
  await page.getByLabel('Anmeldename').fill('admin');
  await page.getByLabel('Passwort').fill(adminPassword);
  await page.getByRole('button', { name: 'Einrichten und anmelden' }).click();

  await expect(page.getByRole('heading', { name: 'Bauakten' })).toBeVisible();
  await expect(page.getByText('Anna Admin')).toBeVisible();
});

test('Benutzer anlegen, Einmalpasswort, Pflichtwechsel bei der ersten Anmeldung', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.getByRole('link', { name: 'Benutzer' }).click();
  await page.getByRole('button', { name: 'Benutzer anlegen' }).click();

  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Name', { exact: true }).fill('Lukas Brandl');
  await dialog.getByLabel('Anmeldename').fill('l.brandl');
  await dialog.getByText('Baustelle', { exact: true }).click();
  await dialog.getByRole('button', { name: 'Speichern' }).click();

  const oneTime = page.getByTestId('one-time-password');
  await expect(oneTime).toContainText('Lukas Brandl');
  const password = (await oneTime.locator('code').textContent())!.trim();
  await expect(page.getByRole('row', { name: /Lukas Brandl/ })).toContainText('Baustelle');

  await page.getByRole('button', { name: 'Abmelden' }).click();
  await signIn(page, 'l.brandl', password);
  await expect(page.getByText('Sie haben ein Einmalpasswort erhalten')).toBeVisible();
  await page.getByLabel('Bisheriges Passwort').fill(password);
  await page.getByLabel('Neues Passwort', { exact: true }).fill('Estrich-Lukas-2026!');
  await page.getByLabel('Neues Passwort wiederholen').fill('Estrich-Lukas-2026!');
  await page.getByRole('button', { name: 'Passwort ändern' }).click();

  await expect(page.getByRole('heading', { name: 'Bauakten' })).toBeVisible();
  // Rolle „Baustelle“: keine Verwaltung in der Navigation.
  await expect(page.getByRole('link', { name: 'Benutzer' })).toHaveCount(0);
});

test('Gerät einrichten: Einrichtungscode mit QR-Code', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.getByRole('link', { name: 'Geräte' }).click();
  await page.getByRole('button', { name: 'Gerät einrichten' }).click();
  await page.getByLabel('Poolgerät (mehrere Personen)').check();
  await page.getByLabel('Gerätename (optional)').fill('Fahrzeug 3');
  await page.getByRole('button', { name: 'Code erzeugen' }).click();

  await expect(page.getByAltText('QR-Code zur Einrichtung')).toBeVisible();
  await expect(page.getByTestId('enrollment-code')).toHaveText(/^[A-Z2-9]{4}-[A-Z2-9]{4}$/);
});

test('Falsches Passwort wird verständlich abgelehnt; Sicherung zeigt ihren Zustand', async ({ page }) => {
  await page.goto('/anmelden');
  await page.getByLabel('Anmeldename oder E-Mail').fill('admin');
  await page.getByLabel('Passwort').fill('falsch-falsch');
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page.getByRole('alert')).toHaveText('Anmeldename oder Passwort ist nicht korrekt.');

  await signIn(page, 'admin', adminPassword);
  await page.getByRole('link', { name: 'Sicherung' }).click();
  await expect(page.getByRole('heading', { name: 'Sicherung' })).toBeVisible();
  await expect(page.getByRole('row', { name: /Letzte Sicherung/ })).toBeVisible();
});

async function signIn(page: import('@playwright/test').Page, login: string, password: string) {
  await page.goto('/anmelden');
  await page.getByLabel('Anmeldename oder E-Mail').fill(login);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/anmelden'));
}
