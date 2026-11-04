import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, jpeg, openSection, pageTitle, pdf, signIn, toast } from './helpers';

// Einstellungen für Archiv, Backup-Ziele und E-Mail (Paket infra). scripts/e2e.sh startet dafür einen echten Archivserver.
test.describe.configure({ mode: 'serial' });

const archiveAddress = process.env['E2E_ARCHIVE_ADDRESS'] ?? '';
const archiveCode = process.env['E2E_ARCHIVE_CODE'] ?? '';

test('E-Mail: Konto speichern (Passwort verschlüsselt), Testmail nennt die Ursache des Fehlers', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/e-mail');
  await expect(pageTitle(page)).toHaveText('E-Mail');
  await page.getByLabel('SMTP-Server').fill('127.0.0.1');
  await page.getByLabel('Port').fill('1');
  await page.getByLabel('Benutzername').fill('buero@e2e.de');
  await page.getByLabel('Passwort', { exact: true }).fill('geheim-123');
  await page.getByLabel('Absenderadresse').fill('buero@e2e.de');
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Gespeichert');
  await page.reload();
  await expect(page.getByLabel('Passwort', { exact: true })).toHaveAttribute('placeholder', /gespeichert/);
  await expect(page.getByLabel('Passwort', { exact: true })).toHaveValue('');

  await page.getByLabel('Empfänger').fill('chef@e2e.de');
  await page.getByRole('button', { name: 'Testmail senden' }).click();
  const dialog = page.locator('.sapMMessageBox').filter({ hasText: 'Testmail fehlgeschlagen' });
  await expect(dialog).toBeVisible({ timeout: 60_000 });
  await expect(dialog).toContainText('nicht erreichbar');
  await dialog.getByRole('button', { name: /OK|Schließen/ }).click();

  await page.getByRole('tab', { name: 'Vorlagen' }).click();
  await page.getByText('Freigabe angefordert', { exact: true }).click();
  await page.getByLabel('Betreff').fill('Bitte freigeben: {{dokument}}');
  await page.getByRole('button', { name: 'Vorlage speichern' }).click();
  await expect(toast(page)).toContainText('Vorlage gespeichert');
  await page.getByRole('tab', { name: 'Versandprotokoll' }).click();
  await expect(page.getByText('chef@e2e.de')).toBeVisible();
});

test('Archiv: falscher Code wird abgelehnt, mit richtigem Code gekoppelt, Regeln speichern', async ({ page }) => {
  test.skip(!archiveCode, 'Kein Archivserver gestartet');
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/archiv');
  await expect(pageTitle(page)).toHaveText('Archiv');
  await expect(page.getByText('Noch kein Archivserver gekoppelt.', { exact: false })).toBeVisible();

  await page.getByRole('button', { name: 'Archivserver koppeln' }).click();
  await page.getByRole('textbox', { name: 'Adresse' }).fill(archiveAddress);
  await page.getByRole('textbox', { name: 'Kopplungscode' }).fill('AAAA-BBBB-CCCC');
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  await expect(page.getByText('Der Kopplungscode stimmt nicht.', { exact: false })).toBeVisible();
  await page.getByRole('textbox', { name: 'Kopplungscode' }).fill(archiveCode);
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  const ok = page.locator('.sapMMessageBox').filter({ hasText: 'Gekoppelt mit „Archiv E2E“' });
  await expect(ok).toBeVisible({ timeout: 30_000 });
  await ok.getByRole('button', { name: /OK|Schließen/ }).click();

  await expect(page.getByText('Archiv E2E (', { exact: false })).toBeVisible();
  await expect(page.getByText('Erreichbar', { exact: false })).toBeVisible();
  // Fernaktualisierung: installierte und verfügbare Version des gekoppelten Archivservers.
  await expect(page.getByText('Aktualisierung der Zusatzserver')).toBeVisible();
  await expect(page.getByText(/Installiert \S+ · verfügbar \S+/)).toBeVisible();
  await page.getByRole('button', { name: 'Regeln speichern' }).click();
  await expect(toast(page)).toContainText('Regeln gespeichert');

  await page.getByRole('button', { name: 'Archiv prüfen' }).click();
  await expect(page.locator('.sapMMessageBox').filter({ hasText: 'In Ordnung' })).toBeVisible({ timeout: 30_000 });
});

/** Datei per API anlegen und hochladen (wie die Apps): Ankündigung, dann Inhalt. */
async function uploadFile(page: import('@playwright/test').Page, file: { id: string; bauakteId: string; kind: string; name: string; type: string; body: Buffer; capturedAt?: string }) {
  const announce = await apiCall(page, 'POST', '/api/v1/files', {
    id: file.id, bauakteId: file.bauakteId, kind: file.kind, originalName: file.name, contentType: file.type, sizeBytes: file.body.length,
    sha256: await page.evaluate(async (b64) => {
      const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
      return [...new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))].map((x) => x.toString(16).padStart(2, '0')).join('');
    }, file.body.toString('base64')),
    capturedAt: file.capturedAt ?? null,
  });
  expect(announce.status, JSON.stringify(announce.json)).toBe(201);
  const status = await page.evaluate(async ([id, b64]) => {
    const xsrf = decodeURIComponent(document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='))?.split('=')[1] ?? '');
    const response = await fetch(`/api/v1/files/${id}/content`, { method: 'PUT', headers: { 'Content-Type': 'application/octet-stream', 'X-XSRF-TOKEN': xsrf },
      body: Uint8Array.from(atob(b64), (c) => c.charCodeAt(0)) });
    return response.status;
  }, [file.id, file.body.toString('base64')] as const);
  expect(status).toBe(200);
}

test('Bauakte: „Abschließen & Archivieren“ schließt ab und archiviert sofort; Foto-Raster zeigt die Aufnahmezeit lokal', async ({ page }) => {
  test.skip(!archiveCode, 'Kein Archivserver gestartet');
  await signIn(page, 'admin', adminPassword);
  const id = '01a15700-0000-7000-8000-0000000e2ea1';
  const created = await apiCall<{ version: number }>(page, 'POST', '/api/v1/bauakten', { id, title: 'Abschluss mit Archiv E2E', status: 'planned' });
  expect(created.status).toBe(201);
  await uploadFile(page, { id: '01a15700-0000-7000-8000-0000000e2eb1', bauakteId: id, kind: 'document', name: 'Abnahme.pdf', type: 'application/pdf', body: pdf });
  // Aufnahmezeit mit Zeitzone (+01:00) – früher 500, und das Raster zeigte den rohen ISO-Text.
  await uploadFile(page, { id: '01a15700-0000-7000-8000-0000000e2ec1', bauakteId: id, kind: 'photo', name: 'estrich.jpg', type: 'image/jpeg', body: jpeg,
    capturedAt: '2026-11-03T09:15:00+01:00' });

  await page.goto(`/bauakten/${id}`);
  await openSection(page, 'Fotos & Videos');
  await page.getByRole('option', { name: 'Raster' }).click();
  const tile = page.locator('.eoPhotoTile').filter({ hasText: 'estrich.jpg' });
  await expect(tile).toContainText('03.11.2026');
  await expect(tile).not.toContainText('2026-11-03T');

  await page.getByRole('button', { name: 'Abschließen', exact: true }).filter({ visible: true }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Bauakte abschließen' });
  await expect(dialog).toBeVisible();
  const archiveButton = dialog.getByRole('button', { name: 'Abschließen & Archivieren' });
  await expect(archiveButton).toBeEnabled({ timeout: 15_000 });
  await expect(dialog.getByRole('button', { name: 'Abschließen', exact: true })).toBeVisible();
  await archiveButton.click();
  await expect(toast(page)).toContainText('Bauakte abgeschlossen');
  const progress = page.getByRole('dialog', { name: 'Archivierung: Abschluss mit Archiv E2E' });
  await expect(progress).toBeVisible();
  await expect(progress).toContainText(/Alle \d+ Dokumente archiviert\./, { timeout: 90_000 });
  await progress.getByRole('button', { name: 'Schließen' }).click();
  const detail = await apiCall<{ status: string }>(page, 'GET', `/api/v1/bauakten/${id}`);
  expect(detail.json.status).toBe('completed');
  const records = await apiCall<{ status: string }[]>(page, 'GET', `/api/v1/archive/bauakten/${id}`);
  expect(records.json.length).toBeGreaterThanOrEqual(2);
  expect(records.json.every((r) => r.status === 'archived')).toBe(true);
  const audit = await apiCall<{ items: { details: string | null }[] }>(page, 'GET', '/api/v1/audit?action=archive.bauakte_queued');
  expect(JSON.stringify(audit.json)).toContain('completion');
});

test('Backup-Ziele: Kombiserver koppeln – ein Code für Backup-Ziel und Archiv, Hinweis auf getrennten Standort', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/backup-ziele');
  await expect(pageTitle(page)).toHaveText('Backup-Ziele');
  await page.getByRole('button', { name: 'Kombiserver koppeln' }).click();
  const dialog = page.getByRole('dialog', { name: 'Kombiserver koppeln' });
  await expect(dialog).toContainText('kein zweiter Standort für das Archiv');
  await expect(dialog.getByRole('textbox', { name: 'Port Backup-Ziel' })).toHaveValue('8444');
  await expect(dialog.getByRole('textbox', { name: 'Port Archivserver' })).toHaveValue('8443');
  await dialog.getByRole('textbox', { name: 'Adresse des Kombiservers' }).fill('127.0.0.1');
  await dialog.getByRole('textbox', { name: 'Kopplungscode' }).fill('AAAA-BBBB-CCCC-DDDD');
  await dialog.getByRole('button', { name: 'Beide koppeln' }).click();
  // Archiv ist schon (mit einem anderen Server) gekoppelt → Rückfrage; ablehnen. Backup-Ziel nicht erreichbar → Fehler im Dialog.
  const replace = page.locator('.sapMMessageBox').filter({ hasText: 'Durch den Archivserver des Kombiservers ersetzen?' });
  if (archiveCode) {
    await expect(replace).toBeVisible({ timeout: 30_000 });
    await replace.getByRole('button', { name: 'Abbrechen' }).click();
  }
  await expect(dialog).toContainText('Backup-Ziel:', { timeout: 30_000 });
  await expect(dialog).toContainText('nicht erreichbar');
  await dialog.getByRole('button', { name: 'Abbrechen' }).click();
});

test('Backup-Ziele: Anleitung mit Installationsbefehl, Kopplungsdialog', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/backup-ziele');
  await expect(pageTitle(page)).toHaveText('Backup-Ziele');
  await expect(page.getByText('estrichone-installer:latest backup-ziel', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Backup-Ziel koppeln' }).click();
  await page.getByRole('textbox', { name: 'Adresse' }).fill('127.0.0.1:1');
  await page.getByRole('textbox', { name: 'Kopplungscode' }).fill('AAAA-BBBB-CCCC');
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  await expect(page.getByText('nicht erreichbar', { exact: false })).toBeVisible({ timeout: 30_000 });
});

test('Überwachung und Aktenarchiv: Systeme mit Plattenbelegung, Liste der ausgelagerten Akten', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/ueberwachung');
  await expect(pageTitle(page)).toHaveText('Überwachung');
  await expect(page.getByText('Produktivsystem (EstrichONE-Server)', { exact: true })).toBeVisible({ timeout: 30_000 });
  await page.goto('/system/aktenarchiv');
  await expect(pageTitle(page)).toHaveText('Aktenarchiv');
  await expect(page.getByRole('button', { name: 'Bauakte archivieren …' })).toBeVisible();
});
