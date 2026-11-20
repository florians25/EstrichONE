import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, choose, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

// Bildschirmfotos der Abwesenheitsverwaltung nur auf Anforderung (SCREENSHOTS=1, Ordner SCREENSHOT_DIR).
const shot = async (page: Page, name: string) => {
  if (!process.env['SCREENSHOTS']) return;
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${process.env['SCREENSHOT_DIR'] ?? 'test-results/screenshots'}/${name}.png` });
};

async function dialog(page: Page, name: string | RegExp) {
  const d = page.getByRole('dialog', { name });
  await expect(d).toBeVisible();
  return d;
}

const de = (d: Date) => d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });

/** Montag in etwa drei Monaten (genug Abstand zu Daten anderer Tests). */
function futureMonday(weeks: number): Date {
  const d = new Date();
  d.setDate(d.getDate() + weeks * 7);
  while (d.getDay() !== 1) d.setDate(d.getDate() + 1);
  return d;
}

test('Abwesenheiten: Betriebsferien, halber Tag mit Vorschau, Genehmigung, Änderungsantrag, Urlaubskonto, Teamkalender Jahr', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page, 'admin', adminPassword);

  // Arten und Betriebsferien (Personalverwaltung): Brückentag für alle eintragen.
  await page.goto('/personal/urlaub?reiter=verwaltung');
  await expect(page.getByRole('row', { name: /Kind krank/ })).toBeVisible();
  await expect(page.getByRole('row', { name: /Überstundenabbau/ })).toBeVisible();
  const bridge = futureMonday(14);
  bridge.setDate(bridge.getDate() + 4);
  await page.getByRole('button', { name: 'Betriebsferien eintragen' }).click();
  const closure = await dialog(page, 'Betriebsferien oder Brückentag eintragen');
  await closure.getByLabel('Bezeichnung').fill('Brückentag');
  await closure.getByLabel('Von').fill(de(bridge));
  await closure.getByLabel('Bis einschließlich').fill(de(bridge));
  await closure.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Betriebsferien eingetragen');
  await shot(page, 'web-arten-betriebsferien');

  // Halber Tag mit Vorschau (Arbeitstage, Rest, Genehmigung).
  const start = futureMonday(10);
  await page.goto('/personal/urlaub?reiter=mein');
  await page.getByRole('button', { name: 'Urlaub beantragen' }).click();
  const form = await dialog(page, 'Urlaub beantragen');
  await form.getByLabel('Von').fill(de(start));
  const end = new Date(start);
  end.setDate(end.getDate() + 2);
  await form.getByLabel('Bis einschließlich').fill(de(end));
  await form.getByLabel('Bemerkung').click();
  await form.getByRole('switch', { name: 'Erster Tag nur halb' }).click();
  await form.getByLabel('Bemerkung').fill('Familienfeier');
  await expect(form.getByText(/2,5 Arbeitstage/)).toBeVisible();
  await expect(form.getByText(/zur Genehmigung weitergeleitet/)).toBeVisible();
  await shot(page, 'web-antrag-vorschau');
  await form.getByRole('button', { name: 'Beantragen' }).click();
  const ok = page.getByRole('button', { name: 'OK' });
  await page.waitForTimeout(800);
  if (await ok.isVisible().catch(() => false)) await ok.click();

  // Genehmigung (offene Anträge).
  await page.getByRole('tab', { name: /Urlaubsanträge/ }).click();
  const row = page.getByRole('row', { name: /Familienfeier/ });
  await expect(row).toBeVisible();
  await expect(row).toContainText('2,5');
  await shot(page, 'web-genehmigung');
  await row.getByRole('button', { name: 'Genehmigen' }).click();
  const decide = page.getByRole('dialog', { name: /genehmigen$/ });
  await expect(toast(page).or(decide).first()).toBeVisible();
  if (await decide.isVisible()) await decide.getByRole('button', { name: 'Genehmigen' }).click();
  await expect(toast(page)).toContainText('Genehmigt');

  // Änderungsantrag: einen Tag länger.
  await page.getByRole('tab', { name: 'Mein Urlaub' }).click();
  const mine = page.getByRole('row', { name: /Familienfeier/ }).filter({ hasText: 'Genehmigt' });
  await mine.getByRole('button', { name: 'Ändern' }).click();
  const change = await dialog(page, 'Änderung beantragen');
  const longer = new Date(end);
  longer.setDate(longer.getDate() + 1);
  await change.getByLabel('Bis einschließlich').fill(de(longer));
  await change.getByLabel('Bemerkung').click();
  await expect(change.getByText(/3,5 Arbeitstage/)).toBeVisible();
  await change.getByRole('button', { name: 'Beantragen' }).click();
  await page.waitForTimeout(800);
  if (await ok.isVisible().catch(() => false)) await ok.click();
  await page.getByRole('tab', { name: /Urlaubsanträge/ }).click();
  const changeRow = page.getByRole('row', { name: /Änderungsantrag/ });
  await expect(changeRow).toBeVisible();
  await changeRow.getByRole('button', { name: 'Genehmigen' }).click();
  const confirm = await dialog(page, /genehmigen$/);
  await confirm.getByRole('button', { name: 'Genehmigen' }).click();
  await expect(toast(page)).toContainText('Genehmigt');
  await page.getByRole('tab', { name: 'Mein Urlaub' }).click();
  await expect(page.getByRole('row', { name: /Ersetzt/ })).toBeVisible();
  await expect(page.getByText('Urlaubskonto', { exact: false }).first()).toBeVisible();
  await shot(page, 'web-meine-uebersicht');

  // Urlaubskonten: Korrektur, CSV.
  await page.getByRole('tab', { name: 'Urlaubskonten' }).click();
  const account = page.getByRole('row', { name: /Anna Admin/ });
  await expect(account).toBeVisible();
  await shot(page, 'web-urlaubskonten');
  await account.click();
  const edit = await dialog(page, /Urlaubskonto \d{4}: Anna Admin/);
  await edit.getByLabel('Korrektur (+/−)').fill('1');
  await edit.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Urlaubskonto gespeichert');
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'CSV' }).click();
  expect((await download).suggestedFilename()).toMatch(/^Urlaubskonten \d{4}\.csv$/);

  // Teamkalender: Monat mit Farben je Art, halber Tag, Brückentag; Jahresansicht.
  await page.goto(`/personal/urlaub?reiter=plan&monat=${start.getFullYear()}-${String(start.getMonth() + 1).padStart(2, '0')}`);
  const grid = page.locator('.eoVacTable');
  await expect(grid).toBeVisible();
  await expect(grid.locator('tr').filter({ hasText: 'Anna Admin' }).locator('td.eoVacHalf').first()).toBeVisible();
  await expect(page.locator('.eoVacSwatch').filter({ hasText: 'Überstundenabbau' })).toBeVisible();
  await shot(page, 'web-teamkalender-monat');
  await page.getByRole('option', { name: 'Jahr', exact: true }).click();
  await expect(page.locator('.eoVacTable.eoVacCompact')).toBeVisible();
  await shot(page, 'web-teamkalender-jahr');

  // Krankmeldung „Kind krank“ (vertraulich).
  await page.getByRole('button', { name: 'Krankmeldung erfassen' }).click();
  const sick = await dialog(page, 'Krankmeldung erfassen');
  await choose(page, sick.getByLabel('Mitarbeiter'), 'Anna Admin');
  await choose(page, sick.getByLabel('Art'), 'Kind krank');
  const day = futureMonday(20);
  await sick.getByLabel('Von').fill(de(day));
  await sick.getByLabel('Bis einschließlich').fill(de(day));
  await sick.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Krankmeldung erfasst');
});

test('Warnungen bei Krankmeldungen: betroffene Einsätze, AU fehlt, eAU abgerufen, Einstellungen, Startseite und Planung', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page, 'admin', adminPassword);
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const today = new Date();
  const tomorrow = new Date(today);
  tomorrow.setDate(tomorrow.getDate() + 1);

  // Kolonne mit Mindestbesetzung und Einsatz morgen.
  const me = await apiCall<{ id: string }>(page, 'GET', '/api/v1/auth/me');
  const crewId = crypto.randomUUID();
  expect((await apiCall(page, 'PUT', `/api/v1/planning/crews/${crewId}`, { name: 'E2E Warnkolonne', memberUserIds: [me.json.id], minimumStaff: 1, isActive: true })).status).toBe(200);
  const bauakte = await apiCall<{ id: string }>(page, 'POST', '/api/v1/bauakten', { id: crypto.randomUUID(), title: 'E2E Halle Warnung', city: 'Landshut' });
  expect((await apiCall(page, 'PUT', `/api/v1/planning/assignments/${crypto.randomUUID()}`, {
    bauakteId: bauakte.json.id, day: iso(tomorrow), activity: 'Estrich einbringen', crewId,
  })).status).toBe(200);

  // Krankmeldung seit drei Tagen (heute 4. Kalendertag), ohne AU.
  const from = new Date(today);
  from.setDate(from.getDate() - 3);
  const to = new Date(today);
  to.setDate(to.getDate() + 3);
  await page.goto('/personal/urlaub?reiter=krank');
  await page.getByRole('button', { name: 'Krankmeldung erfassen' }).click();
  const sick = await dialog(page, 'Krankmeldung erfassen');
  await choose(page, sick.getByLabel('Mitarbeiter'), 'Anna Admin');
  await choose(page, sick.getByLabel('Art'), 'Krankheit');
  await sick.getByLabel('Von').fill(de(from));
  await sick.getByLabel('Bis einschließlich').fill(de(to));
  await sick.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Krankmeldung erfasst');

  // Warnungsliste mit MessageStrip und ObjectStatus.
  const table = page.locator('[id$="warningsTable"]');
  await expect(table).toBeVisible();
  await expect(table.getByRole('row', { name: /Anna Admin: AU-Bescheinigung fehlt/ })).toBeVisible();
  const impact = table.getByRole('row', { name: /Anna Admin abwesend/ });
  await expect(impact).toBeVisible();
  await expect(impact).toContainText('E2E Halle Warnung');
  await expect(impact).toContainText('niemand mehr eingeplant');
  await expect(page.locator('[id$="warningStrip"]')).toContainText('dringend');
  await shot(page, 'web-krankmeldungen-warnungen');

  // eAU abgerufen → AU-Warnung entfällt, Spalte zeigt „eAU abgerufen“.
  await table.getByRole('row', { name: /AU-Bescheinigung fehlt/ }).getByRole('button', { name: 'eAU abgerufen' }).click();
  await expect(toast(page)).toContainText('als abgerufen markiert');
  await expect(table.getByRole('row', { name: /AU-Bescheinigung fehlt/ })).toHaveCount(0);
  await expect(page.getByText('eAU abgerufen').first()).toBeVisible();

  // Einstellungen: Häufungen standardmäßig aus, AU ab 4. Kalendertag.
  await page.getByRole('button', { name: 'Warnungen einstellen' }).first().click();
  const settings = await dialog(page, 'Warnungen bei Krankmeldungen und Abwesenheiten');
  await expect(settings.getByLabel('Nachweis erwartet ab Kalendertag')).toHaveValue('4');
  await expect(settings.getByLabel('Hinweis ab Anzahl kurzer Krankmeldungen in 12 Monaten')).toHaveValue('0');
  await expect(settings.getByRole('switch', { name: 'Krankmeldung direkt vor/nach Urlaub oder Wochenende' })).not.toBeChecked();
  await shot(page, 'web-warnungen-einstellungen');
  await settings.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Einstellungen der Warnungen gespeichert');

  // Startseite: Kennzahl; Planung: MessageStrip mit betroffenen Einsätzen (ohne Grund).
  await page.goto('/start');
  await expect(page.getByRole('listitem').filter({ hasText: 'Warnungen Abwesenheit' })).toBeVisible();
  await shot(page, 'web-start-warnungen');
  await page.goto(`/planung?ansicht=tag&datum=${iso(tomorrow)}`);
  const strip = page.locator('[id$="absenceStrip"]');
  await expect(strip).toContainText('betrifft geplante Einsätze');
  await expect(strip).not.toContainText('krank');
  await strip.getByText('Betroffene Einsätze anzeigen').click();
  const list = await dialog(page, 'Abwesenheit betrifft Einsätze');
  await expect(list).toContainText('E2E Halle Warnung');
  await shot(page, 'web-planung-abwesenheit');
  await list.getByRole('button', { name: 'Alle zur Kenntnis genommen' }).click();
  await expect(strip).toBeHidden();

  // Aufräumen: Kolonne deaktivieren (andere Tests zählen aktive Kolonnen).
  expect((await apiCall(page, 'PUT', `/api/v1/planning/crews/${crewId}`, { name: 'E2E Warnkolonne', memberUserIds: [me.json.id], isActive: false })).status).toBe(200);
});
