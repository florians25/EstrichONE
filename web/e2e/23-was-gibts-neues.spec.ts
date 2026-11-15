import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, signIn } from './helpers';

/**
 * „Was gibt’s Neues“ nach der Anmeldung: Der Gesehen-Stand liegt auf dem Server. Neue Benutzer (hier: admin aus
 * 01-basis) sehen nichts; nach einem „Server-Update“ (Stand auf eine ältere Version zurückgesetzt) erscheint das Fenster
 * genau einmal, ältere Versionen eingeklappt. Danach über das Benutzermenü erreichbar.
 * Versionshinweise: EstrichOne__AppUpdates__ReleaseNotesPath = docs/releases (scripts/e2e.sh).
 */
const dialog = (page: Page) => page.getByRole('dialog', { name: /Was gibt’s Neues/ });

const shot = async (page: Page, name: string) => {
  if (!process.env['SCREENSHOTS']) return;
  for (const width of [1280, 1920]) {
    await page.setViewportSize({ width, height: width === 1920 ? 1080 : 800 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: `test-results/screenshots/was-gibts-neues-${name}-${width}.png` });
  }
  await page.setViewportSize({ width: 1440, height: 900 });
};

test('Was gibt’s Neues: nach Versionswechsel einmal, nicht erneut; über das Benutzermenü', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  // Bereits angemeldeter Benutzer ohne Versionswechsel: kein Fenster.
  await page.waitForTimeout(1500);
  await expect(dialog(page)).toHaveCount(0);

  // Wie nach einem Server-Update: zuletzt gesehen war 0.1.0.
  const state = await apiCall<{ serverVersion: string }>(page, 'GET', '/api/v1/release-notes/whats-new');
  expect(state.status).toBe(200);
  expect((await apiCall(page, 'PUT', '/api/v1/release-notes/seen', { audience: 'web', version: '0.1.0' })).status).toBe(204);

  await page.reload();
  const whatsNew = dialog(page);
  await expect(whatsNew).toBeVisible({ timeout: 20_000 });
  await expect(whatsNew).toContainText(`Was gibt’s Neues in Version ${state.json.serverVersion}`);
  // Gruppiert nach Version, neueste oben; Server-Anleitungen fehlen.
  await expect(whatsNew.getByRole('heading', { name: `Version ${state.json.serverVersion}`, exact: true })).toBeVisible();
  await expect(whatsNew).not.toContainText('sudo /opt/estrichone');
  // Viele Versionen: ältere eingeklappt.
  const older = whatsNew.getByText(/Ältere Versionen? \(/);
  await expect(older).toBeVisible();
  await shot(page, 'dialog');
  await whatsNew.getByRole('button', { name: 'Verstanden' }).click();
  await expect(whatsNew).toHaveCount(0);

  // Nicht erneut – auch nicht nach Neuladen oder neuer Anmeldung.
  await page.reload();
  await page.waitForTimeout(2000);
  await expect(dialog(page)).toHaveCount(0);
  const after = await apiCall<{ notes: unknown[] }>(page, 'GET', '/api/v1/release-notes/whats-new');
  expect(after.json.notes).toHaveLength(0);

  // Benutzermenü › „Was gibt’s Neues“.
  await page.getByLabel(/^Benutzermenü/).first().click();
  await page.getByRole('menuitem', { name: 'Was gibt’s Neues' }).click();
  await expect(dialog(page)).toBeVisible();
  await shot(page, 'menue');
  await dialog(page).getByRole('button', { name: 'Verstanden' }).click();
  await expect(dialog(page)).toHaveCount(0);
});
