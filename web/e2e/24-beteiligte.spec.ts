import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, pageTitle, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

const worker = { login: 'b.kaiser', name: 'Bernd Kaiser', password: 'Estrich-Bernd-2026!' };
const title = 'Wohnhaus Lindenweg, Zementestrich';
let bauakteId = '';

test('Nicht zugeteilte Bauakte: alle sehen sie, nur ansehen; eintragen schaltet Bearbeiten frei, austragen sperrt wieder', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const roles = await apiCall<{ id: string; name: string }[]>(page, 'GET', '/api/v1/access/roles');
  const site = roles.json.find((r) => r.name === 'Baustelle')!;
  const created = await apiCall<{ oneTimePassword: string }>(page, 'POST', '/api/v1/users', { displayName: worker.name, userName: worker.login, roleIds: [site.id] });
  expect(created.status).toBe(201);
  bauakteId = crypto.randomUUID();
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: bauakteId, title })).status).toBe(201);
  await page.context().clearCookies();

  await signIn(page, worker.login, created.json.oneTimePassword);
  await page.getByLabel('Bisheriges Passwort').fill(created.json.oneTimePassword);
  await page.getByLabel('Neues Passwort', { exact: true }).fill(worker.password);
  await page.getByLabel('Neues Passwort wiederholen').fill(worker.password);
  await page.getByRole('button', { name: 'Passwort ändern' }).click();
  await expect(pageTitle(page)).toContainText(worker.name);

  // Liste: auch nicht zugeteilte Bauakten; „Meine (zugeteilt)“ blendet sie aus.
  await page.goto('/bauakten');
  await expect(page.getByRole('link', { name: title })).toBeVisible();
  await page.getByRole('option', { name: 'Meine (zugeteilt)' }).click();
  await expect(page).toHaveURL(/meine=1/);
  await expect(page.getByRole('link', { name: title })).toHaveCount(0);
  await page.getByRole('option', { name: 'Alle Bauakten' }).click();
  await page.getByRole('link', { name: title }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible();

  // Nur ansehen: Hinweis-Streifen, keine Bearbeiten-Knöpfe; der Server lehnt Änderungen ab.
  const strip = page.locator('[id$="readOnlyStrip"]');
  await expect(strip).toContainText('Nur ansehen: Trag dich als Beteiligter ein, um zu bearbeiten.');
  await expect(page.getByRole('button', { name: 'Bearbeiten', exact: true })).toHaveCount(0);
  const denied = await apiCall<{ code: string }>(page, 'POST', `/api/v1/bauakten/${bauakteId}/notes`, { id: crypto.randomUUID(), text: 'Darf ich?' });
  expect(denied.status).toBe(403);
  expect(denied.json.code).toBe('BAUAKTE_NOT_ASSIGNED');

  // Eintragen: Bearbeiten frei, Streifen weg.
  await page.locator('[id$="assignMeButton"]').click();
  await expect(toast(page)).toContainText('Als Beteiligter eingetragen');
  await expect(strip).toBeHidden();
  await expect(page.getByRole('button', { name: 'Bearbeiten', exact: true })).toBeVisible();
  const note = await apiCall(page, 'POST', `/api/v1/bauakten/${bauakteId}/notes`, { id: crypto.randomUUID(), text: 'Bin eingetragen' });
  expect(note.status).toBe(201);

  // Austragen: wieder nur ansehen.
  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Als Beteiligter austragen' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Als Beteiligter austragen' }).click();
  await expect(toast(page)).toContainText('Als Beteiligter ausgetragen');
  await expect(strip).toBeVisible();
});

test('Verlauf und Benachrichtigung: die Leitung sieht, wer sich eingetragen hat', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const history = await apiCall<{ action: string; text: string }[]>(page, 'GET', `/api/v1/bauakten/${bauakteId}/history`);
  expect(history.json.some((h) => h.action === 'bauakte.member_joined' && h.text.includes(worker.name))).toBe(true);
  expect(history.json.some((h) => h.action === 'bauakte.member_left')).toBe(true);
  const inbox = await apiCall<{ items: { title: string; objectId: string }[] }>(page, 'GET', '/api/v1/notifications');
  expect(inbox.json.items.some((n) => n.objectId === bauakteId && n.title.includes('eingetragen'))).toBe(true);
});
