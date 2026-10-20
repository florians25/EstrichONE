import { expect, test, type Page } from '@playwright/test';
import { adminPassword, signIn } from './helpers';

test.describe.configure({ mode: 'serial' });

/** Instanz-Schlüssel der Hosting-Konsole (EstrichOne__Notices__ManagementToken in der E2E-Umgebung). */
const token = process.env['E2E_NOTICES_TOKEN'] ?? '';

async function setNotices(page: Page, document: unknown) {
  const status = await page.evaluate(async ([body, key]) => {
    const response = await fetch('/api/v1/system/notices', {
      method: 'PUT', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${key}` }, body: JSON.stringify(body),
    });
    return response.status;
  }, [document, token] as const);
  expect(status).toBe(204);
}

const inMinutes = (minutes: number) => new Date(Date.now() + minutes * 60_000).toISOString();

test.skip(!token, 'E2E_NOTICES_TOKEN fehlt (Systemhinweise lassen sich nur mit Instanz-Schlüssel setzen).');

test('Ankündigung und geplante Wartung erscheinen als Banner und lassen sich für die Sitzung ausblenden', async ({ page }) => {
  await page.goto('/anmelden');
  await setNotices(page, {
    announcements: [{ id: 'e2e-info', title: 'Neue Funktion', text: 'Bauakten lassen sich jetzt als PDF ausdrucken.', severity: 'info' }],
    maintenance: { message: 'Update auf die neue Version', startsAt: inMinutes(60 * 24), endsAt: inMinutes(60 * 25), adminsMayWork: true },
  });
  await signIn(page, 'admin', adminPassword);
  const info = page.locator('.eoMain .sapMMsgStrip').filter({ hasText: 'Neue Funktion: Bauakten lassen sich jetzt als PDF ausdrucken.' });
  await expect(info).toBeVisible();
  await expect(page.locator('.eoMain .sapMMsgStrip').filter({ hasText: /Geplante Wartung .*Update auf die neue Version/ })).toBeVisible();
  await info.getByRole('button').click();
  await expect(info).toHaveCount(0);
  await page.reload();
  await expect(page.locator('.eoMain .sapMMsgStrip').filter({ hasText: /Geplante Wartung/ })).toBeVisible();
  await expect(page.locator('.eoMain .sapMMsgStrip').filter({ hasText: 'Neue Funktion' })).toHaveCount(0);
});

test('Laufende Wartung: Benutzer sehen die Wartungsseite, Administratoren arbeiten mit Hinweis weiter', async ({ page }) => {
  await page.goto('/anmelden');
  await setNotices(page, {
    announcements: [],
    maintenance: { message: 'Datenbank wird umgezogen', startsAt: inMinutes(-5), endsAt: inMinutes(30), adminsMayWork: true },
  });
  await page.reload();
  await expect(page.getByText('Wartungsarbeiten')).toBeVisible();
  await expect(page.getByText('Datenbank wird umgezogen')).toBeVisible();
  await expect(page.getByText(/Voraussichtlich wieder erreichbar/)).toBeVisible();

  // Administrator: Anmeldung bleibt möglich, die Oberfläche zeigt den Wartungsmodus deutlich an.
  await setNotices(page, {
    announcements: [],
    maintenance: { message: 'Datenbank wird umgezogen', startsAt: inMinutes(-5), endsAt: inMinutes(30), adminsMayWork: true },
  });
  await page.getByRole('button', { name: 'Als Administrator anmelden' }).click();
  await page.getByLabel('Anmeldename oder E-Mail').fill('admin');
  await page.getByLabel('Passwort').fill(adminPassword);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/anmelden'));
  await page.goto('/start');
  await expect(page.locator('.eoAdminMaintenance')).toContainText('Wartungsmodus aktiv');
  await expect(page.locator('.eoMaintenance')).toBeHidden();

  // Wartung beendet: „Erneut prüfen“ bzw. die automatische Prüfung gibt die Oberfläche frei.
  await setNotices(page, { announcements: [], maintenance: null });
  await page.reload();
  await expect(page.locator('.eoAdminMaintenance')).toBeHidden();
});
