import { expect, test } from '@playwright/test';
import { adminPassword, pageTitle, signIn, toast } from './helpers';

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
  await page.getByLabel('Adresse').fill(archiveAddress);
  await page.getByLabel('Kopplungscode').fill('AAAA-BBBB-CCCC');
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  await expect(page.getByText('Der Kopplungscode stimmt nicht.', { exact: false })).toBeVisible();
  await page.getByLabel('Kopplungscode').fill(archiveCode);
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  const ok = page.locator('.sapMMessageBox').filter({ hasText: 'Gekoppelt mit „Archiv E2E“' });
  await expect(ok).toBeVisible({ timeout: 30_000 });
  await ok.getByRole('button', { name: /OK|Schließen/ }).click();

  await expect(page.getByText('Archiv E2E (', { exact: false })).toBeVisible();
  await expect(page.getByText('Erreichbar', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Regeln speichern' }).click();
  await expect(toast(page)).toContainText('Regeln gespeichert');

  await page.getByRole('button', { name: 'Archiv prüfen' }).click();
  await expect(page.locator('.sapMMessageBox').filter({ hasText: 'In Ordnung' })).toBeVisible({ timeout: 30_000 });
});

test('Backup-Ziele: Anleitung mit Installationsbefehl, Kopplungsdialog', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/backup-ziele');
  await expect(pageTitle(page)).toHaveText('Backup-Ziele');
  await expect(page.getByText('estrichone-installer:latest backup-ziel', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Backup-Ziel koppeln' }).click();
  await page.getByLabel('Adresse').fill('127.0.0.1:1');
  await page.getByLabel('Kopplungscode').fill('AAAA-BBBB-CCCC');
  await page.getByRole('button', { name: 'Koppeln', exact: true }).click();
  await expect(page.getByText('nicht erreichbar', { exact: false })).toBeVisible({ timeout: 30_000 });
});
