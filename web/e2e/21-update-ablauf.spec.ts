import fs from 'node:fs';
import path from 'node:path';
import { expect, test, type Page } from '@playwright/test';
import { adminPassword, signIn } from './helpers';

/**
 * Update-Ablauf (deploy/README.md): Stand wie von update.sh bzw. der Hosting-Konsole in die Datei schreiben, die die API
 * liest (EstrichOne__Notices__UpdateStatePath = E2E_UPDATE_STATE in scripts/e2e.sh). Ankündigung mit Countdown, Wartungs-
 * bildschirm, API weg (Caddy liefert /update-status.json), Freigabe → Neuladen → Anmeldung mit Hinweis.
 */
test.describe.configure({ mode: 'serial' });

const stateFile = process.env['E2E_UPDATE_STATE'] ?? '';
const shots = process.env['E2E_SHOTS'] ?? 'test-results';
/** Je Lauf eindeutig – die API beendet die Sitzungen einmal je Update-Kennung. */
const runId = `e2e-update-${Date.now()}`;
test.skip(!stateFile, 'E2E_UPDATE_STATE fehlt (Pfad der Update-Statusdatei der API).');

function state(phase: string, seconds: number | null) {
  const now = Date.now();
  return {
    id: runId, phase, fromVersion: '1.0.15', targetVersion: '9.9.9', startedAt: new Date(now - 5_000).toISOString(),
    phaseStartedAt: new Date(now).toISOString(), phaseEndsAt: seconds === null ? null : new Date(now + seconds * 1000).toISOString(),
    expiresAt: new Date(now + 3_600_000).toISOString(), source: 'e2e',
  };
}

function write(phase: string, seconds: number | null) {
  fs.mkdirSync(path.dirname(stateFile), { recursive: true });
  fs.writeFileSync(`${stateFile}.tmp`, JSON.stringify(state(phase, seconds)));
  fs.renameSync(`${stateFile}.tmp`, stateFile);
}

async function shot(page: Page, name: string) {
  for (const width of [1920, 1280]) {
    await page.setViewportSize({ width, height: width === 1920 ? 1080 : 800 });
    await page.waitForTimeout(300);
    await page.screenshot({ path: `${shots}/${name}-${width}.png` });
  }
  await page.setViewportSize({ width: 1440, height: 900 });
}

test.afterAll(() => {
  if (stateFile) fs.rmSync(stateFile, { force: true });
});

test('Update: Ankündigung mit Countdown, Wartungsbildschirm auch ohne API, Freigabe mit neuer Anmeldung', async ({ page }) => {
  test.setTimeout(150_000);
  fs.rmSync(stateFile, { force: true });
  await signIn(page, 'admin', adminPassword);
  await page.goto('/start');
  await expect(page.getByText('Guten Tag', { exact: false }).first()).toBeVisible();

  // 1. Ankündigung: Die nächste API-Antwort trägt X-EstrichONE-Update – die Leiste erscheint sofort mit Countdown.
  write('announce', 60);
  await page.locator('.sapTntNLI').filter({ hasText: /^Bauakten$/ }).first().click();
  const banner = page.locator('[id$="--updateAnnounce"]');
  await expect(banner).toBeVisible({ timeout: 35_000 });
  await expect(banner).toContainText(/Update auf Version 9\.9\.9 beginnt in 0:[0-5]\d – bitte Arbeit speichern/);
  // Hinweisleisten bündig mit dem Seiteninhalt, nicht am Rand bzw. an der Kopfzeile.
  const box = (await banner.boundingBox())!;
  const main = (await page.locator('.eoMain').first().boundingBox())!;
  expect(box.x - main.x).toBeGreaterThanOrEqual(16);
  expect(main.x + main.width - (box.x + box.width)).toBeGreaterThanOrEqual(16);
  expect(box.y - main.y).toBeGreaterThanOrEqual(8);
  await shot(page, 'update-1-ankuendigung');
  // Neue Anmeldungen sind gesperrt.
  const login = await page.evaluate(async () => (await fetch('/api/v1/auth/web/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'x', password: 'y' }),
  })).status);
  expect(login).toBe(503);

  // 2. Update: Wartungsbildschirm mit Phase, Zielversion und Fortschritt.
  write('update', 60);
  const overlay = page.locator('[id$="--updateOverlay"]');
  await expect(overlay).toBeVisible({ timeout: 15_000 });
  await expect(overlay).toContainText('Update wird eingespielt');
  await expect(overlay.locator('[id$="--updateVersion"]')).toHaveText('Zielversion 9.9.9');
  await expect(overlay.locator('[id$="--updatePhase"]')).toContainText('Schritt 2 von 3: Update');
  await shot(page, 'update-2-update');

  // API weg (Container werden getauscht): Stand kommt von Caddy, keine Störungsseite.
  await page.route('**/api/v1/**', (route) => route.fulfill({ status: 502, contentType: 'application/problem+json', body: '{"code":"SERVER_UNAVAILABLE"}' }));
  await page.route('**/update-status.json', (route) => route.fulfill({ json: state('finish', 60) }));
  await expect(overlay).toContainText('Update wird abgeschlossen', { timeout: 15_000 });
  await expect(overlay.locator('[id$="--updateCountdown"]')).toHaveText(/^[01]:\d\d$/);
  await expect(page.locator('[id$="--outageOverlay"]')).toBeHidden();
  await expect(overlay).toContainText('Server wird gerade getauscht');
  await page.unroute('**/api/v1/**');
  await page.unroute('**/update-status.json');

  // Störungsseite (OpenUI5, Caddy bei nicht erreichbarer API) zeigt denselben Wartungsbildschirm.
  const fallback = await page.context().newPage();
  await fallback.route('**/update-status.json', (route) => route.fulfill({ json: state('update', 40) }));
  await fallback.route('**/api/v1/**', (route) => route.fulfill({ status: 502, contentType: 'application/problem+json', body: '{"code":"SERVER_UNAVAILABLE"}' }));
  await fallback.goto('/stoerung.html');
  await expect(fallback.locator('.sapMIllustratedMessage').filter({ hasText: 'Update wird eingespielt' })).toBeVisible({ timeout: 20_000 });
  await expect(fallback.locator('[id$="updatePhase"]')).toContainText('Schritt 2 von 3: Update');
  await expect(fallback.getByText('Server wird gerade getauscht')).toBeVisible();
  await shot(fallback, 'update-stoerungsseite');
  await fallback.close();

  // 3. Abschluss mit neuer Version, dann Freigabe: Seite lädt neu, Anmeldung mit Hinweis.
  write('finish', 60);
  await expect(overlay).not.toContainText('Server wird gerade getauscht', { timeout: 15_000 });
  await shot(page, 'update-3-abschluss');
  fs.rmSync(stateFile, { force: true });
  await expect(page.locator('[id$="--updateDoneStrip"]')).toContainText('EstrichONE wurde auf Version 9.9.9 aktualisiert', { timeout: 20_000 });
  await expect(page.locator('input[id$="loginUser-inner"]')).toBeVisible();
  await shot(page, 'update-4-freigegeben');
  // Die alte Sitzung ist beendet – neue Anmeldung klappt.
  await signIn(page, 'admin', adminPassword);
});
