import { expect, test, type Page } from '@playwright/test';

/**
 * Fehlerbild 0.1.0: „Sitzung im Web wird sofort beendet, bei erneuter Anmeldung hängt man im Anmeldebildschirm.“
 * Installer mit ESTRICHONE_DOMAIN=estrichone.deinefirma.de, Aufruf über Nginx Proxy Manager als app.estrichone.de bzw.
 * direkt per http://IP:Port. Geprüft werden Ersteinrichtung, Arbeiten, Neuladen, Abmelden und erneute Anmeldung.
 */
test.describe.configure({ mode: 'serial' });

const password = 'Estrich-Chefin-2026!';
const setupCode = process.env['E2E_SETUP_CODE'] ?? '';
const directUrl = process.env['E2E_DIRECT_URL'] ?? '';

async function signIn(page: Page) {
  const user = page.locator('input[id$="loginUser-inner"]');
  const secret = page.locator('input[id$="loginPassword-inner"]');
  await expect(async () => {
    await user.fill('admin');
    await secret.fill(password);
    await expect(user).toHaveValue('admin', { timeout: 500 });
    await expect(secret).toHaveValue(password, { timeout: 500 });
  }).toPass({ timeout: 15_000 });
  await page.getByRole('button', { name: 'Anmelden' }).click();
}

async function expectWorking(page: Page) {
  await expect(page.getByRole('heading', { name: 'Guten Tag, Anna Admin' })).toBeVisible();
  await expect(page.getByLabel('Benutzermenü Anna Admin')).toBeVisible();
  // Folgeanfragen sind angemeldet: eine Seite mit Daten öffnen und neu laden.
  await page.goto('/bauakten');
  await expect(page).toHaveURL(/\/bauakten$/);
  await page.reload();
  await expect(page).toHaveURL(/\/bauakten$/);
  await expect(page.getByLabel('Benutzermenü Anna Admin')).toBeVisible();
  await expect(page.getByText('Sitzung abgelaufen')).toHaveCount(0);
}

test('Ersteinrichtung über Nginx Proxy Manager mit abweichender Domain', async ({ page, context }) => {
  const unauthorized: string[] = [];
  page.on('response', (r) => {
    if (r.status() === 401 && !/auth\/me$/.test(r.url())) unauthorized.push(r.url());
  });
  await page.goto('/anmelden');
  await expect(page.getByText('EstrichONE ist noch nicht eingerichtet.')).toBeVisible();
  await page.getByRole('link', { name: 'Einrichtung starten' }).click();
  await page.getByLabel('Einrichtungscode').fill(setupCode);
  await page.getByLabel('Firmenname').fill('Estrichbau Proxy GmbH');
  await page.getByLabel('Name', { exact: true }).fill('Anna Admin');
  await page.getByLabel('Anmeldename').fill('admin');
  await page.getByLabel('Passwort', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Einrichten und anmelden' }).click();
  await expectWorking(page);

  // Cookies host-only für die aufgerufene Domain, Secure (HTTPS über den Proxy).
  const cookies = await context.cookies();
  expect(cookies.map((c) => c.name).sort()).toEqual(['XSRF-TOKEN', 'eo_at', 'eo_rt']);
  for (const cookie of cookies) {
    expect(cookie.domain).toBe('app.estrichone.de');
    expect(cookie.secure).toBe(true);
  }
  expect(unauthorized).toEqual([]);

  // Administratoren sehen, dass die eingestellte Adresse abweicht – samt Abhilfe.
  await page.goto('/start');
  await expect(page.getByText(/Konfigurierte Adresse weicht ab/)).toBeVisible();
  await expect(page.getByText(/ESTRICHONE_DOMAIN=app\.estrichone\.de/)).toBeVisible();
});

test('Abmelden und erneute Anmeldung über den Proxy', async ({ page }) => {
  await page.goto('/anmelden');
  await signIn(page);
  await expectWorking(page);
  await page.getByLabel('Benutzermenü Anna Admin').click();
  await page.getByRole('menuitem', { name: 'Abmelden' }).click();
  await expect(page).toHaveURL(/\/anmelden/);
  await signIn(page);
  await expect(page).not.toHaveURL(/\/anmelden/);
  await expectWorking(page);
});

test('Direkter Aufruf per http://IP:Port (am Proxy vorbei) sperrt nicht aus und warnt', async ({ page }) => {
  test.skip(!directUrl, 'E2E_DIRECT_URL fehlt');
  await page.goto(`${directUrl}/anmelden`);
  await signIn(page);
  await expect(page).not.toHaveURL(/\/anmelden/);
  await expect(page.getByRole('heading', { name: 'Guten Tag, Anna Admin' })).toBeVisible();
  await page.reload();
  await expect(page.getByLabel('Benutzermenü Anna Admin')).toBeVisible();
  await expect(page.getByText(/Unverschlüsselte Verbindung/)).toBeVisible();
});

test('Abgelehnte Cookies: klare Meldung statt Hängen', async ({ page, context }) => {
  // Browser verwirft die Sitzungs-Cookies (z. B. Cookies blockiert): Set-Cookie der Anmeldung entfernen.
  await context.route('**/api/v1/auth/web/login', async (route) => {
    // Der Testprozess kennt die Domain nicht (nur der Browser über --host-resolver-rules): direkt an den Proxy.
    const url = new URL(route.request().url());
    const response = await route.fetch({ url: url.href.replace(url.hostname, '127.0.0.1'), headers: { ...route.request().headers(), host: url.host } });
    const headers = Object.fromEntries(Object.entries(response.headers()).filter(([name]) => name.toLowerCase() !== 'set-cookie'));
    await route.fulfill({ response, headers });
  });
  await page.goto('/anmelden');
  await signIn(page);
  await expect(page.locator('.sapMMsgStrip').filter({ hasText: 'der Browser hat die Sitzung nicht gespeichert' })).toBeVisible();
  await expect(page).toHaveURL(/\/anmelden/);
  await expect(page.getByRole('button', { name: 'Anmelden' })).toBeEnabled();
});
