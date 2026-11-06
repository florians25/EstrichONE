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

const cards = (page: Page) => page.locator('.eoMain .eoNoticeStack .eoNoticeCard');

test('Hinweiskarten: Zeitraum, Zusammenfassen, Details, Ausblenden je Benutzer (Störung nicht ausblendbar)', async ({ page }) => {
  await page.goto('/anmelden');
  await setNotices(page, {
    announcements: [
      { id: 'e2e-info', title: 'Neue Funktion', text: 'Bauakten lassen sich jetzt als PDF ausdrucken.', severity: 'info' },
      { id: 'e2e-crit', title: 'Störung beim E-Mail-Versand', text: 'Ausgehende E-Mails werden verzögert zugestellt.', severity: 'critical' },
    ],
    maintenance: { message: 'Update auf die neue Version', startsAt: inMinutes(60 * 24 * 2), endsAt: inMinutes(60 * 24 * 2 + 90), adminsMayWork: true },
  });
  await signIn(page, 'admin', adminPassword);
  await page.goto('/start');

  // Eingeklappt: nur die Störung (wichtigster Hinweis), der Rest zusammengefasst.
  const critical = cards(page).filter({ hasText: 'Störung beim E-Mail-Versand' });
  await expect(critical).toBeVisible();
  await expect(critical).toHaveClass(/eoNoticeCard--Error/);
  await expect(critical.getByRole('button', { name: /^Ausblenden/ })).toHaveCount(0);
  await expect(cards(page)).toHaveCount(1);
  await page.getByRole('button', { name: '2 weitere Hinweise' }).click();
  await expect(cards(page)).toHaveCount(3);

  const maintenance = cards(page).filter({ hasText: 'Geplante Wartung' });
  await expect(maintenance).toHaveClass(/eoNoticeCard--Information/);
  await expect(maintenance.locator('.eoNoticeChip')).toHaveText('in 2 Tagen');
  await expect(maintenance.locator('.eoNoticePeriod')).toHaveText(/^[A-Z][a-z]\., \d\d\.\d\d\., \d\d:\d\d(–\d\d:\d\d)? Uhr/);
  await expect(maintenance).toContainText('Update auf die neue Version');

  // Details: vollständiger Text im Dialog.
  await maintenance.getByRole('button', { name: /^Details/ }).click();
  const dialog = page.getByRole('dialog').filter({ hasText: 'Update auf die neue Version' });
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: 'Schließen' }).click();
  await expect(dialog).toHaveCount(0);

  // Ausblenden bleibt über das Neuladen hinaus (je Benutzer) erhalten.
  const info = cards(page).filter({ hasText: 'Neue Funktion' });
  await info.getByRole('button', { name: /^Ausblenden/ }).click();
  await expect(info).toHaveCount(0);
  await page.reload();
  await expect(critical).toBeVisible();
  await expect(cards(page).filter({ hasText: 'Neue Funktion' })).toHaveCount(0);
  // Auf der Startseite bleiben alle Hinweise nachlesbar.
  await expect(page.locator('.eoStartNotices .eoNoticeCard').filter({ hasText: 'Neue Funktion' })).toBeVisible();

  // Kein Überlagern: der Seiteninhalt beginnt unter dem Hinweisstapel.
  const stack = await page.locator('.eoMain .eoNoticeStack').boundingBox();
  const content = await page.locator('.eoMain .eoContent').boundingBox();
  expect(stack && content && content.y >= stack.y + stack.height - 1).toBeTruthy();

  // Vorschau in der Verwaltung zeigt dieselben Karten.
  await page.goto('/system/hinweise');
  await expect(page.locator('[id$="--noticePreview"] .eoNoticeCard')).toHaveCount(3);
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
  await expect(page.locator('.eoMain .eoNoticeStack .eoNoticeKind-maintenanceRunning')).toContainText('Wartungsmodus aktiv');
  await expect(page.locator('.eoMain .eoNoticeStack .eoNoticeKind-maintenanceRunning')).toContainText('läuft seit');
  await expect(page.locator('.eoMaintenance')).toBeHidden();

  // Wartung beendet: „Erneut prüfen“ bzw. die automatische Prüfung gibt die Oberfläche frei.
  await setNotices(page, { announcements: [], maintenance: null });
  await page.reload();
  await expect(page.locator('.eoMain .eoNoticeStack .eoNoticeKind-maintenanceRunning')).toHaveCount(0);
});
