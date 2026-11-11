import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, choose, signIn, toast } from './helpers';

// Helpdesk für die IT: Arbeitsplatz mit Kennzahlen und Ansichten, Suche nach Nummer, Sammelbearbeitung, Ticket mit Reitern
// (Melder und Geräte, Verknüpfungen, Prüfpfad), Zusammenführen, Einstellungen, Wissensdatenbank und Bewertung durch den Melder.
test.describe.configure({ mode: 'serial' });

type Detail = { summary: { id: string; number: string } };

async function ticket(page: Page, title: string, category = 'other', extra: Record<string, unknown> = {}): Promise<Detail> {
  const created = await apiCall<Detail>(page, 'POST', '/api/v1/tickets', {
    id: crypto.randomUUID(), title, category, source: 'web', reporterBlocked: false, ...extra,
  });
  expect(created.status).toBe(201);
  return created.json;
}

test('Arbeitsplatz: Kennzahlen, Ansichten, Suche nach Nummer und Sammelbearbeitung', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const a = await ticket(page, 'Monitor im Lager flackert', 'deviceDefect');
  const b = await ticket(page, 'Monitor im Büro flackert auch', 'deviceDefect');

  await page.goto('/it-center/helpdesk');
  await expect(page.locator('[id$="hdKpis"]')).toContainText('Nicht zugewiesen');
  await expect(page.locator('[id$="hdKpis"]')).toContainText('Ø erste Antwort');
  await page.locator('[id$="hdViews"]').getByText(/^Nicht zugewiesen/).click();
  const table = page.locator('[id$="hdTable"]');
  await expect(table.locator('tr').filter({ hasText: 'Monitor im Lager flackert' })).toBeVisible();

  // Volltextsuche findet die Ticketnummer.
  await page.locator('[id$="hdQueueSearch"] input').fill(b.summary.number);
  await expect(table.locator('tbody tr').filter({ hasText: 'Monitor im Büro flackert auch' })).toBeVisible();
  await expect(table.locator('tbody tr').filter({ hasText: 'Monitor im Lager flackert' })).toHaveCount(0);
  await page.locator('[id$="hdQueueSearch"] input').fill('Monitor');
  await expect(table.locator('tbody tr').filter({ hasText: 'Monitor im Lager flackert' })).toBeVisible();

  // Beide auswählen und mir zuweisen.
  for (const title of ['Monitor im Lager flackert', 'Monitor im Büro flackert auch']) {
    await table.locator('tbody tr').filter({ hasText: title }).locator('.sapMCb').first().click();
  }
  await expect(page.getByText('2 ausgewählt')).toBeVisible();
  await page.locator('[id$="bulkAssign"]').click();
  await page.locator('.sapMMenu, .sapUiMnu').getByText('Mir zuweisen').click();
  await expect(toast(page)).toContainText('2 Tickets');
  await page.locator('[id$="hdViews"]').getByText(/^Meine/).first().click();
  await expect(table.locator('tbody tr').filter({ hasText: 'Monitor im Lager flackert' })).toBeVisible();

  // Sortieren über den Spaltenkopf.
  await table.getByRole('button', { name: 'Nummer' }).click();
  await table.getByRole('button', { name: 'Nummer' }).click();
  await expect(table.locator('tbody tr').first()).toContainText(b.summary.number);
  expect(a.summary.number < b.summary.number).toBeTruthy();
});

test('Ticket: Melder und Geräte, Verknüpfen, Zeit, Prüfpfad und Zusammenführen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const target = await ticket(page, 'WLAN im Lager fällt aus');
  const duplicate = await ticket(page, 'Kein Internet im Lager', 'other', { description: 'seit acht Uhr' });

  await page.goto(`/it-center/helpdesk/${target.summary.id}`);
  await expect(page.locator('[id$="ticketTitle"]')).toHaveText('WLAN im Lager fällt aus');
  const tabs = page.locator('[id$="hdTabs"]');
  await tabs.getByText(/^Melder & Geräte/).click();
  await expect(page.locator('[id$="hdContext"]')).toContainText('Systemstatus');
  await expect(page.locator('[id$="hdDevices"]')).toBeVisible();

  await page.locator('[id$="tMore"]').click();
  await page.getByText('Arbeitszeit erfassen …').click();
  const time = page.getByRole('dialog', { name: /Arbeitszeit/ });
  await time.getByLabel('Dauer').fill('1:15');
  await time.getByLabel('Tätigkeit').fill('Access Point geprüft');
  await time.getByRole('button', { name: 'Erfassen' }).click();
  await expect(page.locator('[id$="hdTime"]')).toContainText('Access Point geprüft');
  await expect(tabs).toContainText('1:15 h');

  await tabs.getByText(/^Prüfpfad/).click();
  await expect(page.locator('[id$="hdAudit"]')).toContainText('Zeiterfassung');

  // Duplikat zusammenführen: Quelle geschlossen, Ziel mit interner Notiz und Verknüpfung.
  await page.goto(`/it-center/helpdesk/${duplicate.summary.id}`);
  await page.locator('[id$="tMore"]').click();
  await page.getByText('Als Duplikat zusammenführen …').click();
  const merge = page.getByRole('dialog', { name: /zusammenführen/ });
  await merge.getByLabel('Weiter bearbeiten in').fill(target.summary.number);
  await page.locator('.sapMComboBoxBasePicker li, .sapMSuggestionsPopover li').filter({ hasText: target.summary.number }).first().click();
  await merge.getByRole('button', { name: 'Zusammenführen' }).click();
  await expect(page).toHaveURL(new RegExp(target.summary.id));
  await expect(page.locator('.eoTimeline')).toContainText(`Ticket ${duplicate.summary.number} wurde hierher zusammengeführt`);
  await expect(page.locator('.eoTimeline')).toContainText('seit acht Uhr');
  await tabs.getByText(/^Verknüpfungen/).click();
  await expect(page.locator('[id$="hdLinks"]')).toContainText('Duplikat (zusammengeführt)');
});

test('Einstellungen und Wissensdatenbank: Gruppe, Ziele, Artikel als Vorschlag beim Melden', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/it-center/helpdesk-einstellungen');
  await expect(page.locator('[id$="hdTargets"]')).toContainText('Dringend');
  await page.locator('[id$="hdSetTabs"]').getByText(/^Gruppen/).click();
  await page.getByRole('button', { name: 'Gruppe anlegen' }).click();
  const group = page.getByRole('dialog', { name: 'Gruppe anlegen' });
  await group.getByLabel('Name').fill('IT');
  await group.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('[id$="hdGroups"]')).toContainText('IT');

  await page.goto('/wissen');
  await page.locator('[id$="kbNew"]').click();
  const article = page.getByRole('dialog', { name: 'Neuer Artikel' });
  await article.getByLabel('Titel').fill('Drucker im Büro: Papierstau beheben');
  await article.getByRole('textbox', { name: /^Anleitung/ }).fill('1. Klappe öffnen\n2. Papier vorsichtig entfernen');
  await choose(page, article.getByLabel('Kategorie'), 'Drucker/Büro-IT');
  await article.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('[id$="kbArticleTitle"]')).toHaveText('Drucker im Büro: Papierstau beheben');
  await expect(page.locator('[id$="kbBody"] ol li')).toHaveCount(2);

  const found = await apiCall<{ title: string }[]>(page, 'GET', '/api/v1/helpdesk/articles/suggest?q=Drucker%20Papierstau', undefined);
  expect(found.json.map((a) => a.title)).toContain('Drucker im Büro: Papierstau beheben');
});

test('Melder bewertet die Lösung', async ({ page }) => {
  await signIn(page, 'l.brandl', 'Estrich-Lukas-2026!');
  const own = await ticket(page, 'Kamera-App stürzt beim Foto ab', 'appError');
  await page.context().clearCookies();
  await signIn(page, 'admin', adminPassword);
  const resolved = await apiCall(page, 'PATCH', `/api/v1/tickets/${own.summary.id}`, { status: 'resolved', resolution: 'App aktualisiert.', unassign: false });
  expect(resolved.status).toBe(200);
  await page.context().clearCookies();

  await signIn(page, 'l.brandl', 'Estrich-Lukas-2026!');
  await page.goto(`/it-center/helpdesk/${own.summary.id}`);
  await expect(page.getByText('Wie zufrieden sind Sie mit der Lösung?')).toBeVisible();
  await page.locator('[id$="hdRating"] .sapMRIIcon, [id$="hdRating"] .sapUiIcon').nth(3).click();
  await page.getByRole('button', { name: 'Bewerten' }).click();
  await expect(toast(page)).toContainText('Danke');
  await expect(page.locator('.eoTimeline')).toContainText('Bewertung: 4 von 5 Sternen');
});
