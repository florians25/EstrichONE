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

test('Bauakte im Browser: Prüfungen, Bautagebuch und Verlauf aus der App erscheinen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  // Wie die App: Erfassen über den Sync (Browser-Sitzung mit CSRF-Schutz).
  const bauakte = crypto.randomUUID();
  const results = await page.evaluate(async (id) => {
    const xsrf = decodeURIComponent(document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='))?.split('=')[1] ?? '');
    const op = (type: string, entityId: string, payload: unknown) => ({ opId: crypto.randomUUID(), type, entityId, baseVersion: null, payload });
    const response = await fetch('/api/v1/sync/push', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': xsrf },
      body: JSON.stringify({
        operations: [
          op('bauakte.create', id, { title: 'EFH Prüfung E2E', city: 'Landshut' }),
          op('diary.create', crypto.randomUUID(), { bauakteId: id, day: '2026-10-20', text: 'Estrich im EG eingebracht', weather: 'rain', temperatureCelsius: 9 }),
          op('inspection.create', crypto.randomUUID(), { bauakteId: id, kind: 'cmMeasurement', location: 'Bad OG', binder: 'cement', heated: true, measuredCmPercent: 1.6 }),
          op('inspection.create', crypto.randomUUID(), { bauakteId: id, kind: 'initialInspection', checks: { substrate: 'ok', edgeInsulation: 'notOk' }, remarks: 'Randdämmung fehlt' }),
        ],
      }),
    });
    return (await response.json()).results.map((r: { status: string }) => r.status);
  }, bauakte);
  expect(results).toEqual(['applied', 'applied', 'applied', 'applied']);

  await page.goto(`/bauakten/${bauakte}`);
  await expect(page.getByRole('heading', { name: 'EFH Prüfung E2E' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Prüfungen (2)' })).toBeVisible();
  await expect(page.getByText('Bad OG: 1,6 CM-% (Grenzwert 1,8)')).toBeVisible();
  await expect(page.getByText('Belegreif', { exact: true })).toBeVisible();
  await expect(page.getByText('Randdämmstreifen vorhanden')).toBeVisible();
  await expect(page.getByText('Estrich im EG eingebracht')).toBeVisible();
  await expect(page.getByText('Regen · 9 °C')).toBeVisible();
  await expect(page.getByText('Anna Admin hat eine CM-Messung erfasst: 1,6 CM-% bei Grenzwert 1,8 CM-% – belegreif.')).toBeVisible();
});

test('Helpdesk: Meldung bearbeiten, interne Notiz, Lösung dokumentieren', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const status = await page.evaluate(async () => {
    const xsrf = decodeURIComponent(document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='))?.split('=')[1] ?? '');
    const response = await fetch('/api/v1/tickets', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': xsrf },
      body: JSON.stringify({ id: crypto.randomUUID(), title: 'Drucker im Büro druckt nicht', category: 'officeIt', source: 'web', reporterBlocked: true,
        diagnostics: { appVersion: '1.0.0', platform: 'Web' } }),
    });
    return response.status;
  });
  expect(status).toBe(201);

  await page.getByRole('link', { name: 'Helpdesk' }).click();
  await expect(page.getByRole('heading', { name: 'Helpdesk' })).toBeVisible();
  const row = page.getByRole('row', { name: /Drucker im Büro druckt nicht/ });
  await expect(row.getByText('Kann nicht arbeiten')).toBeVisible();
  await row.getByRole('link').click();

  await page.getByLabel('Antwort oder Notiz').fill('Toner prüfen, vermutlich leer');
  await page.getByLabel('Interne Notiz (für den Melder unsichtbar)').check();
  await page.getByRole('button', { name: 'Notiz speichern' }).click();
  await expect(page.getByText('Toner prüfen, vermutlich leer')).toBeVisible();
  await expect(page.getByText('Interne Notiz', { exact: true })).toBeVisible();

  await page.getByLabel('Status').selectOption({ label: 'Gelöst' });
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('alert')).toContainText('Lösung');
  await page.getByLabel('Lösung (Pflicht)').fill('Toner gewechselt.');
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('.head').getByText('Gelöst')).toBeVisible();
  await expect(page.getByText('hat den Status geändert: Gelöst')).toBeVisible();
});

async function signIn(page: import('@playwright/test').Page, login: string, password: string) {
  await page.goto('/anmelden');
  await page.getByLabel('Anmeldename oder E-Mail').fill(login);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/anmelden'));
}
