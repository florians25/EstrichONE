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
  await merge.locator('input[role="combobox"]').first().fill(target.summary.number);
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
  await expect(page.locator('.eoHdTargets')).toContainText('Dringend');
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
  // Sterne per Tastatur (der Auswahlbereich liegt über den Symbolen).
  await page.locator('[id$="hdRating"]').focus();
  for (let i = 0; i < 4; i++) await page.keyboard.press('ArrowRight');
  await page.getByRole('button', { name: 'Bewerten' }).click();
  await expect(toast(page)).toContainText('Danke');
  await expect(page.locator('.eoTimeline')).toContainText('Bewertung: 4 von 5 Sternen');
});

test('Meldung aufnehmen: IT legt nach Anruf ein Ticket für den Melder an und erwähnt eine Kollegin mit @', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const it = await apiCall(page, 'POST', '/api/v1/users', {
    displayName: 'Ida Kollegin', userName: `ida${Date.now()}`, email: null, phone: null, roleIds: ['01a00000-0000-7000-8000-000000000004'],
  });
  expect(it.status).toBeLessThan(300);

  await page.goto('/it-center/helpdesk');
  await page.locator('[id$="newOnBehalf"]').click();
  const dialog = page.getByRole('dialog', { name: 'Meldung aufnehmen' });
  await choose(page, dialog.locator('input[role="combobox"]').first(), 'Lukas Brandl', 'Lukas');
  await dialog.getByLabel('Kurzbeschreibung').fill('Tablet zeigt nach Anruf keine Aufträge');
  await dialog.getByRole('button', { name: 'Ticket anlegen' }).click();
  await expect(page.locator('[id$="ticketTitle"]')).toHaveText('Tablet zeigt nach Anruf keine Aufträge');
  await expect(page.locator('.eoHdFacts')).toContainText('Telefon, aufgenommen von');
  await expect(page.locator('.eoHdFacts')).toContainText('Lukas Brandl');
  await expect(page.locator('.eoTimeline')).toContainText('aufgenommen (Telefon)');
  await expect(page.locator('[id$="tStatus"]')).toContainText('In Bearbeitung');

  // @ öffnet die Auswahl der Kolleginnen und Kollegen; Eingabe übernimmt den Namen.
  const reply = page.locator('[id$="reply"] textarea');
  await reply.click();
  await reply.pressSequentially('Bitte vor Ort prüfen @Id');
  await expect(page.locator('[id$="hdMentionPop"]')).toContainText('Ida Kollegin');
  await page.keyboard.press('Enter');
  await expect(reply).toHaveValue('Bitte vor Ort prüfen @Ida Kollegin ');
  await expect(page.locator('[id$="hdMentionPop"]')).toBeHidden();
  await page.getByText('Interne Notiz (für den Melder unsichtbar)').click();
  await page.getByRole('button', { name: 'Notiz speichern' }).click();
  await expect(page.locator('.eoTimeline')).toContainText('Benachrichtigt: Ida Kollegin');

  // Der Melder sieht das Ticket als eigenes.
  await page.context().clearCookies();
  await signIn(page, 'l.brandl', 'Estrich-Lukas-2026!');
  const mine = await apiCall<{ title: string; channel: string }[]>(page, 'GET', '/api/v1/tickets/mine', undefined);
  expect(mine.json.find((t) => t.title === 'Tablet zeigt nach Anruf keine Aufträge')?.channel).toBe('phone');
});

test('Ticket bei 1024 Pixel: Schnellaktionen ohne Überlaufmenü, Arbeitsliste seitenweise', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await signIn(page, 'admin', adminPassword);
  const t = await ticket(page, 'Bildschirm bleibt schwarz', 'deviceDefect');
  await page.goto(`/it-center/helpdesk/${t.summary.id}`);
  const quick = page.locator('[id$="hdQuick"]');
  for (const id of ['tStatus', 'tPriority', 'tCategory', 'tAssignee', 'tMore']) {
    await expect(quick.locator(`[id$="${id}"]`)).toBeVisible();
  }
  await expect(quick.locator('.sapMOTBOverflowButton, [id$="overflowButton"]')).toHaveCount(0);
  const box = await quick.boundingBox();
  const more = await quick.locator('[id$="tMore"]').boundingBox();
  expect(box && more && more.x + more.width <= box.x + box.width + 1).toBeTruthy();

  await page.goto('/it-center/helpdesk');
  await expect(page.locator('[id$="hdQueueCount"]')).toContainText(/\d+ Tickets?/);
  const pageOne = await apiCall<{ items: unknown[]; total: number }>(page, 'GET', '/api/v1/tickets/page?filter=all&limit=1&offset=0', undefined);
  expect(pageOne.json.items).toHaveLength(1);
  expect(pageOne.json.total).toBeGreaterThan(1);
});

test('Helpdesk-Einstellungen: Bundesland wählen, Feiertage werden berechnet', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/it-center/helpdesk-einstellungen');
  await choose(page, page.locator('[id$="hdState"]'), 'Bayern');
  const preview = page.locator('[id$="hdHolidayPreview"]').filter({ visible: true }).first();
  await expect(preview).toContainText('Fronleichnam');
  await expect(preview).toContainText('Karfreitag');
  await page.locator('[id$="hdSave"]').click();
  await expect(toast(page)).toContainText('gespeichert');
  const saved = await apiCall<{ state: string }>(page, 'GET', '/api/v1/helpdesk/settings', undefined);
  expect(saved.json.state).toBe('BY');
});
