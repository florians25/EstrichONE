import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, signIn, toast } from './helpers';

// Artikel und Leistungen als List Report mit Objektseite: Anlegen mit Vorbelegung (m², 19 %) und Brutto-Netto-Umrechnung,
// Filter und Sortierung, Objektseite mit Duplizieren und Inaktiv setzen statt Löschen, Preise mehrerer Einträge ändern.
test.describe.configure({ mode: 'serial' });

test('Artikel: anlegen, filtern, Objektseite, duplizieren, inaktiv setzen, Preise ändern', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: true, lexwareArticles: true } })).status).toBe(200);
  await page.goto('/artikel');

  // Anlegen: Vorbelegung m² und 19 %, Eingabe brutto rechnet netto.
  await page.locator('[id$="articleNew"]').click();
  const form = page.getByRole('dialog', { name: 'Eintrag anlegen' });
  await form.getByLabel('Bezeichnung').fill('Heizestrich CT-C30-F5 im Wohnbau');
  await form.getByLabel('Artikelnummer').fill('H-300');
  await form.getByLabel('Preis je Einheit brutto').fill('35,70');
  await form.getByLabel('Preis je Einheit brutto').press('Tab');
  await expect(form.getByLabel('Preis je Einheit netto')).toHaveValue(/^30(,00)?$/);
  await form.getByRole('button', { name: /^(Speichern|Anlegen)$/ }).click();
  await expect(toast(page)).toContainText('Eintrag gespeichert');
  const table = page.locator('[id$="articleTable"]');
  const row = table.getByRole('row').filter({ hasText: 'Heizestrich CT-C30-F5' });
  await expect(row).toContainText('m²');
  await expect(row).toContainText('35,70');

  // Filter: Art Material blendet die Leistung aus.
  await page.locator('[id$="articleKind"]').click();
  await page.getByRole('option', { name: 'Material' }).click();
  await expect(table).not.toContainText('Heizestrich CT-C30-F5');
  await page.locator('[id$="articleKind"]').click();
  await page.getByRole('option', { name: 'Alle' }).click();
  await page.screenshot({ path: 'test-results/artikel-liste.png' });

  // Objektseite: duplizieren und inaktiv setzen.
  await row.click();
  await expect(page).toHaveURL(/\/artikel\//);
  await expect(page.locator('[id$="articleGross"]')).toContainText('35,70');
  await page.screenshot({ path: 'test-results/artikel-objektseite.png' });
  await page.locator('[id$="articleDuplicate"]').click();
  const copy = page.getByRole('dialog', { name: 'Eintrag duplizieren' });
  await expect(copy.getByLabel('Bezeichnung')).toHaveValue('Heizestrich CT-C30-F5 im Wohnbau (Kopie)');
  await copy.getByRole('button', { name: /^(Speichern|Anlegen)$/ }).click();
  await expect(toast(page)).toContainText('Kopie angelegt');
  await page.locator('[id$="articleActiveToggle"]').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Inaktiv setzen' }).click();
  await expect(toast(page)).toContainText('Inaktiv gesetzt');

  // Liste: die Kopie ist ausgeblendet (nur aktive), Preise des Originals um 10 Prozent erhöhen.
  await page.goto('/artikel');
  await expect(table).not.toContainText('(Kopie)');
  await table.getByRole('row').filter({ hasText: 'Heizestrich CT-C30-F5' }).getByRole('checkbox').click();
  await page.locator('[id$="articlePrices"]').click();
  const prices = page.getByRole('dialog', { name: /Preise ändern/ });
  await prices.getByRole('spinbutton').fill('10');
  await prices.getByRole('spinbutton').press('Tab');
  await expect(prices).toContainText('33,00');
  await prices.getByRole('button', { name: 'Preise übernehmen' }).click();
  await expect(toast(page)).toContainText('1 Preis geändert');
  await expect(table.getByRole('row').filter({ hasText: 'Heizestrich CT-C30-F5' })).toContainText('33,00');

  expect((await apiCall(page, 'PUT', '/api/v1/settings/modules', { modules: { lexware: false, lexwareArticles: false } })).status).toBe(200);
});
