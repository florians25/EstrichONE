import { expect, test } from '@playwright/test';
import { adminPassword, formLayoutIssues, signIn } from './helpers';

// Schutz gegen verschobene Abschnittsüberschriften: jede Überschrift steht linksbündig über ihrem ersten Feld –
// in schmalen und breiten Fenstern (Bounding-Box-Vergleich in formLayoutIssues).
for (const width of [1366, 1920]) {
  test(`Formular-Überschriften stehen über ihrem Abschnitt (${width} px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: width > 1400 ? 1080 : 768 });
    await signIn(page, 'admin', adminPassword);
    await page.goto('/bauakten');
    await page.getByRole('button', { name: 'Neue Bauakte' }).first().click();
    const wizard = page.getByRole('dialog', { name: 'Neue Bauakte' });
    await wizard.getByLabel('Name des Kunden').fill('Layout Prüfung');
    await wizard.getByRole('button', { name: 'Weiter' }).click();
    await expect(wizard.locator('.sapUiFormTitle').filter({ hasText: 'Baustelle' }).first()).toBeVisible();
    expect(await formLayoutIssues(wizard)).toEqual([]);
    // Auch weiter unten (Kennwerte-Gruppen aus dem Formular-Schema).
    const titles = wizard.locator('.sapUiFormTitle');
    // Gruppen des Formular-Schemas (Bauvorhaben, Estrich, Baustelle) werden nachgeladen.
    await expect.poll(() => titles.count()).toBeGreaterThan(2);
    for (let i = 0; i < await titles.count(); i++) {
      await titles.nth(i).scrollIntoViewIfNeeded();
      await page.waitForTimeout(200);
      expect(await formLayoutIssues(wizard)).toEqual([]);
    }
    await wizard.getByRole('button', { name: 'Abbrechen' }).click();

    await page.goto('/bestellungen');
    await page.getByRole('button', { name: 'Material bestellen' }).first().click();
    const order = page.getByRole('dialog', { name: 'Material bestellen' });
    await expect(order.locator('.sapUiFormTitle').filter({ hasText: 'Lieferadresse' }).first()).toBeVisible();
    expect(await formLayoutIssues(order)).toEqual([]);
    await order.getByRole('button', { name: 'Abbrechen' }).click();
  });
}
