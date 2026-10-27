import { expect, test } from '@playwright/test';
import { adminPassword, nav, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
function monday(): string {
  const d = new Date();
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7));
  return iso(d);
}

test('Disponenten-Cockpit: Raster, Verschieben per Ziehen mit Rückgängig, Schnellansicht, Tagesbesetzung, Konflikte, Freigabe, Legende', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Disponenten-Cockpit').click();
  await expect(page.locator('.eoCkPageTitle')).toHaveText('Disponenten-Cockpit');
  await page.goto(`/planung/cockpit?ansicht=week&datum=${monday()}`);
  const row = page.locator('.eoCkRow').filter({ has: page.locator('.eoCkLaneName', { hasText: 'Kolonne Admin' }) });
  const card = row.locator('.eoCkCard').first();
  await expect(card).toBeVisible();
  const title = (await card.locator('.eoCkTitle').textContent()) ?? '';
  const cells = row.locator('.eoCkCell');
  const from = await cells.evaluateAll((list, el) => list.findIndex((c) => c.contains(el as Node)), await card.elementHandle());
  const target = cells.nth(from === 0 ? 1 : 0);
  const source = cells.nth(from);
  const before = await source.locator('.eoCkCard').count();

  // Ziehen auf einen anderen Tag: optimistisch sofort dort, Strg+Z holt es zurück.
  await card.dragTo(target, { targetPosition: { x: 20, y: 60 } });
  await expect(toast(page)).toContainText('Kolonne Admin');
  await expect(target.locator('.eoCkCard', { hasText: title }).first()).toBeVisible();
  await expect(source.locator('.eoCkCard')).toHaveCount(before - 1);
  await page.locator('.eoCkTitleBlock').click();
  await page.keyboard.press('Control+z');
  await expect(source.locator('.eoCkCard')).toHaveCount(before);

  // Klick auf einen Einsatz: Schnellansicht mit Details und Aktionen.
  await source.locator('.eoCkCard', { hasText: title }).first().click();
  const quick = page.locator('.eoCkQv');
  await expect(quick).toBeVisible();
  await expect(quick.locator('.eoCkQvTitle')).toHaveText(title);
  await expect(quick).toContainText('Kontakt');
  await expect(page.locator('.eoCkQvPopover').getByRole('button', { name: 'Bearbeiten' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(quick).toBeHidden();
  const tueCell = cells.nth(1);

  // Kontextmenü der Zelle: Tagesbesetzung.
  await tueCell.click({ button: 'right', position: { x: 10, y: 10 } });
  await page.getByRole('menuitem', { name: /Tagesbesetzung bearbeiten/ }).click();
  const roster = page.getByRole('dialog', { name: /Tagesbesetzung Kolonne Admin/ });
  await expect(roster).toBeVisible();
  await roster.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Tagesbesetzung');

  // Konflikte im Zeitraum und Freigabe.
  await page.locator('.eoCkActions').getByRole('button', { name: /^\d+ Konflikte?$/ }).click();
  await expect(page.getByRole('dialog', { name: 'Konflikte im Zeitraum' })).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Schließen' }).click();
  await page.locator('.eoCkActions').getByRole('button', { name: /freigeben/ }).click();
  const release = page.getByRole('dialog', { name: 'Plan freigeben' });
  await release.getByRole('button', { name: 'Freigeben' }).click();
  await expect(toast(page)).toContainText('Plan freigegeben');

  // Legende und Bedienungshilfe (Taste ?).
  await page.locator('.eoCkToolbar [title="Legende"]').click();
  await expect(page.locator('.eoCkLegend')).toContainText('Terminarten');
  await page.keyboard.press('Escape');
  await page.locator('.eoCkTitleBlock').click();
  await page.keyboard.press('?');
  const help = page.getByRole('dialog', { name: 'Bedienung des Cockpits' });
  await expect(help).toContainText('Strg');
  await help.getByRole('button', { name: 'Schließen' }).click();

  // Kompakte Karten bleiben bedienbar.
  const compact = page.locator('.eoCkToolbar [title^="Kompakt"]');
  await compact.click();
  await expect(page.locator('.eoCkCard.eoCkCompact').first()).toBeVisible();
  await compact.click();
  await expect(page.locator('.eoCkCard.eoCkCompact')).toHaveCount(0);

  // Zeitachse des Tages.
  await page.getByRole('option', { name: 'Tag', exact: true }).click();
  await expect(page).toHaveURL(/ansicht=day/);
  await expect(page.locator('.eoCkTimeline')).toBeVisible();
});
