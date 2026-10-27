import { expect, test } from '@playwright/test';
import { adminPassword, signIn } from './helpers';

test('Nachricht an alle senden: Verlauf mit Zustellstatus, Glocke, Postfach, gelesen markieren', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/verwaltung/nachrichten?neu=1');
  const dialog = page.getByRole('dialog', { name: 'Neue Nachricht' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Senden' })).toBeDisabled();
  await dialog.getByText(/Alle Benutzer/).click();
  await dialog.getByLabel('Titel').fill('Morgen Treffpunkt 6:30 Uhr am Lager');
  await dialog.getByLabel('Text').fill('Bitte Messgeräte mitnehmen.');
  await dialog.getByRole('button', { name: 'Senden' }).click();
  await expect(page.locator('.sapMMessageToast')).toContainText('gesendet');
  const row = page.locator('[id$="sentTable"]').getByRole('row').filter({ hasText: 'Morgen Treffpunkt 6:30 Uhr am Lager' }).first();
  await expect(row).toContainText('Alle Benutzer');
  await row.click();
  const detail = page.getByRole('dialog', { name: 'Morgen Treffpunkt 6:30 Uhr am Lager' });
  await expect(detail).toContainText('Anna Admin');
  await detail.getByRole('button', { name: 'Schließen' }).click();

  // Die Absenderin gehört zu „alle“: Postfach und Startseite zeigen die Nachricht.
  await page.goto('/benachrichtigungen');
  const item = page.locator('.sapMNLI').filter({ hasText: 'Morgen Treffpunkt 6:30 Uhr am Lager' }).first();
  await expect(item).toBeVisible();
  await expect(item).toHaveAccessibleName(/ungelesen/);
  await item.getByRole('button', { name: 'Zusätzliche Optionen' }).click();
  await page.getByRole('menuitem', { name: 'Als gelesen markieren' }).or(page.getByRole('button', { name: 'Als gelesen markieren' })).first().click();
  await expect(page.locator('.sapMNLI').filter({ hasText: 'Morgen Treffpunkt 6:30 Uhr am Lager' }).first()).not.toHaveAccessibleName(/ungelesen/);
  await page.goto('/start');
  await expect(page.locator('.eoStartCards .sapFCard').filter({ hasText: 'Warten auf Genehmigung' })).toBeVisible();
});
