import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, pageTitle, signIn, toast } from './helpers';

// System → Speicher-Auslagerung (Paket offload): scripts/e2e.sh startet dafür einen echten Storage (Archivdienst, Rolle offload).
test.describe.configure({ mode: 'serial' });

const offloadAddress = process.env['E2E_OFFLOAD_ADDRESS'] ?? '';
const offloadCode = process.env['E2E_OFFLOAD_CODE'] ?? '';

test('Speicher-Auslagerung: koppeln, Regeln speichern, Dokument auslagern, transparent öffnen, wieder lokal speichern', async ({ page }) => {
  test.skip(!offloadCode, 'Kein Storage gestartet');
  test.setTimeout(180_000);
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/auslagerung');
  await expect(pageTitle(page)).toHaveText('Speicher-Auslagerung');
  await expect(page.getByText('So koppeln Sie einen Storage', { exact: false })).toBeVisible();

  await page.getByRole('button', { name: 'Storage koppeln' }).click();
  await page.getByRole('textbox', { name: 'Adresse' }).fill(offloadAddress);
  await page.getByRole('textbox', { name: 'Kopplungscode' }).fill(offloadCode);
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  const ok = page.locator('.sapMMessageBox').filter({ hasText: 'Gekoppelt mit „Storage E2E“' });
  await expect(ok).toBeVisible({ timeout: 30_000 });
  await ok.getByRole('button', { name: /OK|Schließen/ }).click();
  await expect(page.getByText('Erreichbar', { exact: false }).first()).toBeVisible();

  // Regeln: jedes Alter; im E2E-Stack läuft kein Backup-Dienst → ausdrücklich ohne Sicherungskopie (mit Warnung bestätigen).
  const age = page.locator('input[id$="olAge-input-inner"]');
  await age.fill('0');
  await age.press('Tab');
  await page.locator('[id$="--olBackup"]').click();
  await page.locator('button[id$="--offloadSave"]').click();
  const warn = page.locator('.sapMMessageBox').filter({ hasText: 'Ohne Sicherungskopie' });
  await expect(warn).toBeVisible();
  await warn.getByRole('button', { name: 'Trotzdem speichern' }).click();
  await expect(toast(page)).toContainText('Gespeichert');

  const bauakte = '01a15700-0000-7000-8000-0000000e2f01';
  expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: bauakte, title: 'Auslagerung E2E', status: 'planned' })).status).toBe(201);
  const file = '01a15700-0000-7000-8000-0000000e2f11';
  const body = Buffer.from(`%PDF-1.4\n% Auslagerung ${Date.now()}\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n`);
  const status = await page.evaluate(async ([id, owner, b64]) => {
    const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
    const sha = [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map((x) => x.toString(16).padStart(2, '0')).join('');
    const xsrf = decodeURIComponent(document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='))?.split('=')[1] ?? '');
    const headers = { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': xsrf };
    await fetch('/api/v1/files', { method: 'POST', headers, body: JSON.stringify({ id, bauakteId: owner, kind: 'document', originalName: 'Altplan.pdf',
      contentType: 'application/pdf', sizeBytes: bytes.length, sha256: sha, category: 'plan' }) });
    const r = await fetch(`/api/v1/files/${id}/content`, { method: 'PUT', headers: { 'Content-Type': 'application/octet-stream', 'X-XSRF-TOKEN': xsrf }, body: bytes });
    return r.status;
  }, [file, bauakte, body.toString('base64')] as const);
  expect(status).toBe(200);

  await page.reload();
  await page.getByRole('button', { name: 'Jetzt auslagern' }).click();
  await expect(toast(page)).toContainText('Auslagerung gestartet');
  const files = page.locator('[id$="offloadFiles"]');
  await expect(files.getByText('Altplan.pdf')).toBeVisible({ timeout: 90_000 });
  await expect(files.locator('.sapMObjStatus').filter({ hasText: 'Auf dem Storage' }).first()).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole('link', { name: 'Auslagerung E2E' }).first()).toBeVisible();

  // Öffnen: Original kommt vom Storage (Kopfzeile), Inhalt unverändert.
  const download = await page.evaluate(async (id) => {
    const r = await fetch(`/api/v1/files/${id}/content`, { cache: 'no-store' });
    return { status: r.status, storage: r.headers.get('X-EstrichOne-Storage'), text: await r.text() };
  }, file);
  expect(download.status).toBe(200);
  expect(download.storage).toBe('offload');
  expect(download.text).toContain('% Auslagerung');
  const where = await apiCall<{ fileId: string }[]>(page, 'GET', `/api/v1/files/offload/locations?ids=${file}`);
  expect(where.json.map((w) => w.fileId)).toContain(file);

  // Wieder lokal speichern (Bauakte).
  await page.locator('.sapMListTblRow').filter({ hasText: 'Auslagerung E2E' }).getByRole('button', { name: 'Wieder lokal speichern' }).first().click();
  const confirm = page.locator('.sapMMessageBox').filter({ hasText: 'Auslagerung E2E' });
  await confirm.getByRole('button', { name: 'Wieder lokal speichern' }).click();
  await expect(toast(page)).toContainText('Rückholen gestartet');
  await expect.poll(async () => (await apiCall<{ fileId: string }[]>(page, 'GET', `/api/v1/files/offload/locations?ids=${file}`)).json.length,
    { timeout: 60_000 }).toBe(0);
  const local = await page.evaluate(async (id) => (await fetch(`/api/v1/files/${id}/content`, { cache: 'no-store' })).headers.get('X-EstrichOne-Storage'), file);
  expect(local).toBeNull();

  // Aufräumen für die folgenden Tests: alles zurückholen, Standardregeln (nichts Frisches mehr auslagern).
  expect((await apiCall(page, 'POST', '/api/v1/files/offload/recall', { all: true })).status).toBe(202);
  await expect.poll(async () => (await apiCall<{ counts: { offloadedBlobs: number; pendingBlobs: number } }>(page, 'GET', '/api/v1/files/offload')).json.counts.offloadedBlobs,
    { timeout: 90_000 }).toBe(0);
  expect((await apiCall(page, 'PUT', '/api/v1/files/offload/settings', { autoWhenLow: false, autoByRules: false, thresholdPercent: 15, thresholdFreeGb: null,
    minAgeMonths: 6, onlyCompletedBauakten: false, includePhotos: true, includeVideos: true, includeDocuments: true, requireBackup: true, cacheLimitMb: 2048 })).status).toBe(200);
});
