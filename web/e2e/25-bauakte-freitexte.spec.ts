import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, signIn, toast } from './helpers';

// Bauakte: Freitexte (Hinweise und Zugang, Beschreibung) als Textblöcke mit Überschrift, Zeilenumbrüchen,
// erkannten Links und Telefonnummern, „Mehr anzeigen“ und mehrzeiligem Editor.
const shot = (page: Page, name: string) => process.env['SCREENSHOTS']
  ? page.waitForTimeout(700).then(() => page.screenshot({ path: `test-results/screenshots/${name}.png` }))
  : Promise.resolve();

test('Bauakte: Freitexte als Textblöcke, Links, Mehr anzeigen, mehrzeilig bearbeiten', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const id = crypto.randomUUID();
  const description = [
    'Heizestrich CT-C25-F4, 65 mm, EG und OG.',
    'Pläne: https://example.org/plaene/efh-kraus.pdf',
    'Rückfragen an die Bauleitung unter 0871 4433221 oder bauleitung@example.org.',
    '',
    'Ablauf:',
    '1. Randdämmstreifen prüfen',
    '2. Heizkreise abdrücken lassen',
    '3. Estrich einbringen, Fugenplan beachten',
    '4. Nach 3 Tagen begehbar, Belegreife mit CM-Messung prüfen',
  ].join('\n');
  const created = await apiCall(page, 'POST', '/api/v1/bauakten', { id, title: 'Freitexte EFH Kraus', city: 'Landshut', description });
  expect(created.status).toBe(201);

  await page.goto(`/bauakten/${id}`);
  const blocks = page.locator('.eoFreeText').filter({ visible: true });
  await expect(blocks.first()).toBeVisible();
  const notes = blocks.filter({ hasText: 'Hinweise und Zugang' });
  const text = blocks.filter({ hasText: 'Beschreibung' });
  await expect(notes).toContainText('Nicht erfasst');

  // Zeilenumbrüche bleiben, Links und Telefonnummern sind anklickbar, lange Texte zunächst gekürzt.
  await expect(text.getByRole('link', { name: 'https://example.org/plaene/efh-kraus.pdf' })).toHaveAttribute('href', 'https://example.org/plaene/efh-kraus.pdf');
  await expect(text.getByRole('link', { name: '0871 4433221' })).toHaveAttribute('href', 'tel:08714433221');
  await expect(text.getByRole('link', { name: 'bauleitung@example.org' })).toHaveAttribute('href', 'mailto:bauleitung@example.org');
  expect(await text.locator('br').count()).toBeGreaterThan(5);
  await expect(text).toHaveAttribute('data-clamp', 'yes');
  await text.getByText('Mehr anzeigen').click();
  await expect(text).toHaveAttribute('data-clamp', 'no');
  await expect(text.getByText('Weniger anzeigen')).toBeVisible();

  // Mehrzeiliger Editor: Text mit Zeilenumbruch speichern, Block wird hervorgehoben.
  await notes.getByRole('button', { name: 'Hinweise und Zugang bearbeiten' }).click();
  const dialog = page.getByRole('dialog', { name: 'Hinweise und Zugang bearbeiten' });
  const area = dialog.getByRole('textbox');
  await expect(area).toBeVisible();
  expect((await area.boundingBox())!.height).toBeGreaterThan(150);
  await area.fill('Zufahrt über die Hofeinfahrt.\nSchlüssel bei Familie Huber, Tel. 0871 998877.\nHund im Garten.');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toHaveText('Gespeichert');
  await expect(notes).toContainText('Zufahrt über die Hofeinfahrt.');
  await expect(notes).toHaveAttribute('data-important', 'yes');
  await expect(notes.getByRole('link', { name: '0871 998877' })).toHaveAttribute('href', 'tel:0871998877');
  expect(await notes.locator('br').count()).toBe(2);
  const saved = await apiCall<{ siteNotes: string }>(page, 'GET', `/api/v1/bauakten/${id}`);
  expect(saved.json.siteNotes).toBe('Zufahrt über die Hofeinfahrt.\nSchlüssel bei Familie Huber, Tel. 0871 998877.\nHund im Garten.');
  await shot(page, 'bauakte-freitexte');

  // Kein Text mehr in der Spaltenansicht der Basisdaten (nur einmal als Block).
  await expect(page.locator('.eoDenseForm').filter({ visible: true }).getByText('Heizestrich CT-C25-F4, 65 mm, EG und OG.')).toHaveCount(0);
  expect((await apiCall(page, 'DELETE', `/api/v1/bauakten/${id}`)).status).toBeLessThan(500);
});
