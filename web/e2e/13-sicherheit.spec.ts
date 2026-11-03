import { createHmac } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, pageTitle, signIn } from './helpers';

test.describe.configure({ mode: 'serial' });

/** RFC 6238 (HMAC-SHA1, 6 Ziffern, 30 s) – wie eine Authenticator-App. */
function totp(secret: string, offsetSteps = 0): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of secret.replace(/\s/g, '')) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
  const key = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 1000 / 30) + offsetSteps));
  const hash = createHmac('sha1', key).update(counter).digest();
  const offset = hash[hash.length - 1] & 0x0f;
  return ((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
}

const user = { login: 's.weber', name: 'Sabine Weber', password: 'Estrich-Sabine-2026!' };
let secret = '';
let recovery: string[] = [];

async function signOut(page: Page) {
  await page.getByLabel(`Benutzermenü ${user.name}`).click();
  await page.getByRole('menuitem', { name: 'Abmelden' }).click();
  await expect(page).toHaveURL(/\/anmelden/);
}

test('Zwei-Faktor-Anmeldung einrichten: QR-Code, Wiederherstellungscodes, Anmeldung mit Code', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const roles = await apiCall<{ id: string; name: string }[]>(page, 'GET', '/api/v1/access/roles');
  const worker = roles.json.find((r) => r.name === 'Baustelle')!;
  const created = await apiCall<{ oneTimePassword: string }>(page, 'POST', '/api/v1/users', { displayName: user.name, userName: user.login, roleIds: [worker.id] });
  expect(created.status).toBe(201);
  await page.context().clearCookies();

  await signIn(page, user.login, created.json.oneTimePassword);
  await page.getByLabel('Bisheriges Passwort').fill(created.json.oneTimePassword);
  await page.getByLabel('Neues Passwort', { exact: true }).fill(user.password);
  await page.getByLabel('Neues Passwort wiederholen').fill(user.password);
  await page.getByRole('button', { name: 'Passwort ändern' }).click();
  await expect(pageTitle(page)).toContainText(user.name);

  await page.goto('/konto/sicherheit');
  await expect(pageTitle(page)).toHaveText('Anmeldung und Sicherheit');
  await expect(page.getByText('Nicht eingerichtet.')).toBeVisible();
  await expect(page.getByText('Diese Sitzung')).toBeVisible();
  await page.getByRole('button', { name: 'Einrichten' }).click();
  await page.getByRole('dialog').getByLabel('Aktuelles Passwort').fill(user.password);
  await page.getByRole('button', { name: 'Weiter' }).click();
  const setup = page.getByRole('dialog', { name: 'Zwei-Faktor-Anmeldung einrichten' });
  await expect(setup.getByRole('img', { name: 'QR-Code für die Authenticator-App' })).toBeVisible();
  secret = (await page.getByTestId('totp-secret').textContent())!.replace(/\s/g, '');
  expect(secret).toMatch(/^[A-Z2-7]{32}$/);
  await setup.getByRole('textbox').fill('000000');
  await setup.getByRole('button', { name: 'Einschalten' }).click();
  await expect(setup.getByText('Code nicht korrekt').or(setup.getByText(/Code passt nicht/))).toBeVisible();
  await setup.getByRole('textbox').fill(totp(secret));
  await setup.getByRole('button', { name: 'Einschalten' }).click();
  const codes = page.getByTestId('recovery-codes');
  await expect(codes).toBeVisible();
  recovery = (await codes.textContent())!.trim().split(/\s+/);
  expect(recovery).toHaveLength(10);
  await page.getByRole('button', { name: 'Fertig' }).click();
  await expect(page.getByText(/Eingeschaltet seit .*Noch 10 Wiederherstellungscodes/)).toBeVisible();

  await signOut(page);
  await page.locator('input[id$="loginUser-inner"]').fill(user.login);
  await page.locator('input[id$="loginPassword-inner"]').fill(user.password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  const code = page.locator('input[id$="loginCode-inner"]');
  await expect(code).toBeVisible();
  await code.fill('123456');
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page.locator('.sapMMsgStrip').filter({ hasText: 'Der Bestätigungscode ist falsch' })).toBeVisible();
  // Der Einrichtungscode ist verbraucht – der nächste gilt (Uhrabweichung ±30 s).
  await code.fill(totp(secret, 1));
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/anmelden'));
  await expect(pageTitle(page)).toContainText(user.name);
});

test('Sitzungen: zweite Anmeldung sehen und überall sonst abmelden', async ({ page, browser }) => {
  // Zwei Browser, je mit einem Wiederherstellungscode (jeder gilt einmal).
  const base = test.info().project.use.baseURL!;
  const other = await browser.newContext({ baseURL: base, ignoreHTTPSErrors: true, locale: 'de-DE' });
  const second = await other.newPage();
  for (const [p, code] of [[page, recovery[0]], [second, recovery[1]]] as const) {
    await p.goto('/anmelden');
    await p.locator('input[id$="loginUser-inner"]').fill(user.login);
    await p.locator('input[id$="loginPassword-inner"]').fill(user.password);
    await p.getByRole('button', { name: 'Anmelden' }).click();
    await p.locator('input[id$="loginCode-inner"]').fill(code);
    await p.getByRole('button', { name: 'Anmelden' }).click();
    await p.waitForURL((url) => !url.pathname.startsWith('/anmelden'));
  }

  await page.goto('/konto/sicherheit');
  await expect(page.getByText(/Noch 8 Wiederherstellungscodes/)).toBeVisible();
  const rows = page.locator('[id$="--sessions"] tbody tr.sapMListTblRow');
  await expect(rows).toHaveCount(3); // erster Test, dieser Browser, zweiter Browser
  await page.getByRole('button', { name: 'Überall sonst abmelden' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Überall sonst abmelden' }).click();
  await expect(rows).toHaveCount(1);
  // Der zweite Browser ist abgemeldet: die nächste Seite führt zur Anmeldung.
  await second.reload();
  await expect(second).toHaveURL(/\/anmelden/, { timeout: 20_000 });
  await other.close();
});
