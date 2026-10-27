import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, choose, jpeg, nav, pageTitle, pdf, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
/** Montag dieser Woche – dort plant 03-verwaltung die Einsätze auf „Kraus“. */
function monday(): string {
  const d = new Date();
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7));
  return iso(d);
}

async function dialog(page: Page, name: string | RegExp) {
  const d = page.getByRole('dialog', { name });
  await expect(d).toBeVisible();
  return d;
}

test('Personal: aus Benutzerkonten übernehmen, Stammdaten, Qualifikation mit Ablauf, Abwesenheit, Personaldokument', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Mitarbeiter').click();
  await expect(pageTitle(page)).toHaveText('Personal');
  await page.getByRole('button', { name: 'Aus Benutzerkonten übernehmen' }).click();
  await page.getByRole('button', { name: 'Übernehmen', exact: true }).click();
  await expect(toast(page)).toContainText(/Mitarbeiter angelegt|bereits/);
  const row = page.getByRole('row', { name: /Anna Admin/ });
  await expect(row).toContainText('Konto: admin');
  await row.getByText('Anna Admin').click();
  await expect(page).toHaveURL(/\/personal\//);

  // Erst nach dem Laden klicken; ein Klick während des Neuaufbaus der Kopfzeile kann verloren gehen (schnelle CI).
  await expect(pageTitle(page)).toContainText('Anna Admin');
  const form = page.getByRole('dialog', { name: /bearbeiten$/ });
  await expect(async () => {
    if (!(await form.isVisible())) await page.getByRole('button', { name: 'Bearbeiten' }).click();
    await expect(form).toBeVisible({ timeout: 3000 });
  }).toPass({ timeout: 20_000 });
  await form.getByLabel('Personalnummer').fill('P-001');
  await form.getByLabel('Handy').fill('0171 2345678');
  await form.getByRole('textbox', { name: 'Ort' }).fill('Landshut');
  await form.getByLabel('Stundensatz').fill('24,50');
  await form.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Gespeichert');
  await expect(page.getByText('0171 2345678').first()).toBeVisible();

  await page.getByRole('tab', { name: 'Qualifikationen' }).click();
  await page.getByRole('button', { name: 'Hinzufügen' }).click();
  const qual = await dialog(page, 'Qualifikation hinzufügen');
  await qual.getByLabel('Bezeichnung').fill('C1E');
  const soon = new Date();
  soon.setDate(soon.getDate() + 20);
  await qual.getByLabel('Gültig bis').fill(soon.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' }));
  await qual.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /C1E/ })).toBeVisible();

  await page.getByRole('tab', { name: 'Urlaub und Abwesenheiten' }).click();
  await page.getByRole('button', { name: 'Abwesenheit eintragen' }).click();
  const abs = await dialog(page, 'Abwesenheit eintragen');
  const later = new Date();
  later.setDate(later.getDate() + 40);
  const de = (d: Date) => d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });
  await abs.getByLabel('Von').fill(de(later));
  later.setDate(later.getDate() + 4);
  await abs.getByLabel('Bis einschließlich').fill(de(later));
  await abs.getByLabel('Notiz').fill('Sommerurlaub');
  await abs.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Sommerurlaub/ })).toBeVisible();

  await page.getByRole('tab', { name: 'Dokumente' }).click();
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Hochladen' }).click();
  await (await chooser).setFiles({ name: 'Arbeitsvertrag.pdf', mimeType: 'application/pdf', buffer: pdf });
  const doc = await dialog(page, 'Personaldokument ablegen');
  await doc.getByRole('button', { name: 'Hochladen' }).click();
  await expect(page.getByRole('row', { name: /Arbeitsvertrag/ })).toBeVisible();

  await nav(page, 'Mitarbeiter').click();
  await expect(page.getByRole('row', { name: /Anna Admin/ })).toContainText('Nachweis(e) laufen ab');
  await expect(page.getByText(/Qualifikation(en)? oder Führerschein(e)? (läuft|laufen)/)).toBeVisible();
});

test('Urlaub: beantragen, genehmigen, Urlaubsplan mit Feiertagen und PDF, Krankmeldung vertraulich, Fristen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Mein Urlaub / Urlaubsantrag').click();
  await expect(pageTitle(page)).toHaveText('Urlaub und Abwesenheiten');
  const start = new Date();
  start.setMonth(start.getMonth() + 2, 1);
  while (start.getDay() !== 1) start.setDate(start.getDate() + 1);
  const end = new Date(start);
  end.setDate(end.getDate() + 4);
  const de = (d: Date) => d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' });

  await page.getByRole('button', { name: 'Urlaub beantragen' }).click();
  const form = await dialog(page, 'Urlaub beantragen');
  await form.getByLabel('Von').fill(de(start));
  await form.getByLabel('Bis einschließlich').fill(de(end));
  await form.getByLabel('Bemerkung').fill('Sommerferien');
  await form.getByRole('button', { name: 'Beantragen' }).click();
  const ok = page.getByRole('button', { name: 'OK' });
  await page.waitForTimeout(800);
  if (await ok.isVisible().catch(() => false)) await ok.click();

  await page.getByRole('tab', { name: /Anträge/ }).click();
  const row = page.getByRole('row', { name: /Sommerferien/ });
  await expect(row).toBeVisible();
  // Ein Klick genügt; nur bei Hinweisen (z. B. Kolonne dünn besetzt) folgt eine Bestätigung.
  await row.getByRole('button', { name: 'Genehmigen' }).click();
  const decide = page.getByRole('dialog', { name: /genehmigen$/ });
  await expect(toast(page).or(decide).first()).toBeVisible();
  if (await decide.isVisible()) {
    await decide.getByLabel('Kommentar').fill('Gute Erholung!');
    await decide.getByRole('button', { name: 'Genehmigen' }).click();
  }
  await expect(toast(page)).toContainText('Genehmigt');

  await page.getByRole('tab', { name: 'Mein Urlaub' }).click();
  await expect(page.getByRole('row', { name: /Genehmigt/ }).first()).toBeVisible();

  await page.goto(`/personal/urlaub?reiter=plan&monat=${start.getFullYear()}-${String(start.getMonth() + 1).padStart(2, '0')}`);
  const grid = page.locator('.eoVacTable');
  await expect(grid).toBeVisible();
  await expect(grid.locator('tr').filter({ hasText: 'Anna Admin' }).locator('td.eoVac-vacation').first()).toBeVisible();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'PDF Monat' }).click();
  expect((await download).suggestedFilename()).toMatch(/^Urlaubsplan \d{4}-\d{2}\.pdf$/);

  await page.getByRole('button', { name: 'Krankmeldung erfassen' }).click();
  const sick = await dialog(page, 'Krankmeldung erfassen');
  await choose(page, sick.getByLabel('Mitarbeiter'), 'Anna Admin');
  const today = new Date();
  await sick.getByLabel('Von').fill(de(today));
  await sick.getByLabel('Bis einschließlich').fill(de(today));
  await sick.getByLabel('Notiz').fill('Erkältung');
  await sick.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Krankmeldung erfasst');
  await page.getByRole('tab', { name: /Krankmeldungen/ }).click();
  await expect(page.getByRole('row', { name: /Erkältung/ })).toBeVisible();

  await page.getByRole('tab', { name: /Fristen/ }).click();
  await expect(page.getByText(/Führerschein C1E läuft ab/)).toBeVisible();
});

test('Terminkalender: wiederkehrender Termin mit Teilnehmer, Agenda, Monat, Kalender ausblenden, ein Vorkommen löschen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Terminkalender').click();
  await expect(page.locator('.eoCalendarSide')).toBeVisible();
  await expect(page.locator('.eoCalendarList').getByRole('checkbox', { name: 'Firmenkalender' })).toBeVisible();
  await page.getByRole('button', { name: 'Neuer Termin' }).click();
  const form = await dialog(page, 'Neuer Termin');
  await form.getByLabel('Titel').fill('Baubesprechung Büro');
  await choose(page, form.getByLabel('Wiederholung'), 'Wöchentlich');
  await choose(page, form.getByLabel('Teilnehmer'), 'Anna Admin');
  await form.getByRole('heading', { name: 'Neuer Termin' }).click();
  await form.getByLabel('Ort').fill('Besprechungsraum');
  await form.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Termin gespeichert');
  await expect(page.locator('.sapUiCalendarApp, .sapMSinglePCAppointment, [role=listitem]').filter({ hasText: 'Baubesprechung Büro' }).first()).toBeVisible();

  // Agenda: die nächsten 30 Tage, Serie erscheint mehrfach.
  await page.getByRole('option', { name: 'Agenda', exact: true }).first().click();
  await expect(page).toHaveURL(/ansicht=agenda/);
  const items = page.locator('.eoAgenda').getByText('Baubesprechung Büro', { exact: true });
  await expect(items.first()).toBeVisible();
  await expect(async () => expect(await items.count()).toBeGreaterThanOrEqual(4)).toPass({ timeout: 15_000 });

  // Ein Vorkommen löschen („Nur dieser Termin“).
  const before = await items.count();
  await items.nth(1).click();
  const edit = await dialog(page, 'Termin bearbeiten');
  await edit.getByRole('button', { name: 'Löschen' }).click();
  await page.getByRole('button', { name: 'Nur dieser Termin' }).click();
  await expect(toast(page)).toContainText('Termin gelöscht');
  await expect(items).toHaveCount(before - 1);

  // Monat und Kalender ausblenden.
  await page.locator('.eoAgenda').getByRole('option', { name: 'Monat', exact: true }).click();
  await expect(page).toHaveURL(/ansicht=monat/);
  await page.locator('.eoCalendarList').getByRole('checkbox', { name: 'Firmenkalender' }).click();
  await expect(page.locator('.sapMSinglePCAppointment, .sapUiCalendarApp').filter({ hasText: 'Baubesprechung Büro' })).toHaveCount(0);
  await page.locator('.eoCalendarList').getByRole('checkbox', { name: 'Firmenkalender' }).click();
  // Zurück zur Woche und erneut Monat/Woche: früher „duplicate id …vWeek--item“.
  for (const name of ['Woche', 'Monat', 'Woche']) {
    await page.getByRole('option', { name, exact: true }).filter({ visible: true }).first().click();
    await expect(page).toHaveURL(new RegExp(`ansicht=${name.toLowerCase()}`));
  }
  await expect(page.getByText(/unerwarteter Fehler|duplicate id/)).toHaveCount(0);
});

test('Kolonnenplanung: ohne Bauvorhaben wird ein Termin angelegt (C1) und erscheint im Plan', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Kolonnenplanung').click();
  await page.getByRole('button', { name: /^Einsatz planen: Kolonne Admin, Dienstag/ }).click();
  const plan = await dialog(page, 'Einsatz planen');
  await plan.getByRole('link', { name: /Stattdessen Termin anlegen/ }).click();
  const form = await dialog(page, 'Neuer Termin');
  await form.getByLabel('Titel').fill('Werkstatt: Pumpe warten');
  await choose(page, form.getByLabel('Terminart'), 'Sonstiges');
  await form.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Termin gespeichert');
  await expect(page.getByRole('button', { name: /Termin Werkstatt: Pumpe warten/ })).toBeVisible();
  // Einsatz ohne Bauakte ist nicht möglich: Pflichtfeld.
  await page.getByRole('button', { name: /^Einsatz planen: Kolonne Admin, Donnerstag/ }).click();
  const second = await dialog(page, 'Einsatz planen');
  await second.getByLabel('Tätigkeit').fill('Phantasie-Eintrag');
  await second.getByRole('button', { name: 'Speichern' }).click();
  await expect(second).toContainText('Bitte „Bauakte“ angeben.');
  await second.getByRole('button', { name: 'Abbrechen' }).click();
});

test('Teamplaner: Zeilen je Kolonne und Mitarbeiter, Einsatz mit Uhrzeit, Verschieben auf andere Uhrzeit, Tages-PDF', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto(`/planung/teamplaner?datum=${monday()}`);
  await expect(pageTitle(page)).toHaveText('Teamplaner');
  const pc = page.locator('.eoPlanningCalendar');
  await expect(pc.getByText('Kolonne Admin').first()).toBeVisible();
  const appointment = pc.locator('.sapUiCalendarApp').filter({ hasText: 'Kraus' }).first();
  await expect(appointment).toBeVisible();

  // Öffnen und Uhrzeit ändern (Konflikte bleiben sichtbar).
  await appointment.click();
  const form = await dialog(page, 'Einsatz bearbeiten');
  await form.getByLabel('Beginn').fill('09:00');
  await form.getByLabel('Ende (optional)').fill('11:30');
  await form.getByRole('heading', { name: 'Einsatz bearbeiten' }).click();
  await form.getByRole('button', { name: 'Speichern' }).click();
  const close = page.getByRole('dialog', { name: 'Einsatz bearbeiten' }).getByRole('button', { name: 'Schließen' });
  await page.waitForTimeout(1000);
  if (await close.isVisible().catch(() => false)) await close.click();

  // Ziehen auf eine spätere Stunde (HTML5-Ziehen des PlanningCalendar) → Server verschiebt, Meldung mit neuer Uhrzeit.
  const moved = pc.locator('.sapUiCalendarApp').filter({ hasText: 'Kraus' }).first();
  const lane = moved.locator('xpath=ancestor::*[contains(@class,"sapUiCalendarRowApps")][1]');
  const ab = (await moved.boundingBox())!;
  const lb = (await lane.boundingBox())!;
  const right = ab.x - lb.x + 170;
  const targetX = right < lb.width - 30 ? right : Math.max(10, ab.x - lb.x - 120);
  await moved.dragTo(lane, { sourcePosition: { x: 20, y: 20 }, targetPosition: { x: targetX, y: ab.y - lb.y + 20 } });
  await expect(toast(page)).toContainText(/geplant|Konflikt/);

  await page.getByRole('option', { name: 'Mitarbeiter', exact: true }).click();
  await expect(page).toHaveURL(/zeilen=mitarbeiter/);
  await expect(pc.getByText('Anna Admin').first()).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Tages-PDF' }).click();
  expect((await download).suggestedFilename()).toMatch(/^Tageseinteilung \d{4}-\d{2}-\d{2}\.pdf$/);
});

test('Wer fährt wohin: nicht mehr im Web (Navigation, alte Links ins Cockpit), Planungs-API und PDF für die App bleiben', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await expect(page.getByText('Wer fährt wohin', { exact: true })).toHaveCount(0);

  // Alter Link aus einer Pushnachricht: landet im Cockpit (Tagesansicht des Tages).
  await page.goto(`/planung/wer-faehrt-wohin?tag=${monday()}`);
  await expect(page).toHaveURL(new RegExp(`/planung/cockpit\\?ansicht=day&datum=${monday()}`));

  // Derselbe Endpunkt wie für die iOS-App bleibt erhalten.
  const board = await apiCall<{ sites: unknown[]; crews: unknown[]; mySiteIds: string[] }>(page, 'GET', `/api/v1/planning/dispatch/${monday()}`);
  expect(board.status).toBe(200);
  expect(Array.isArray(board.json.crews)).toBe(true);
  const pdf = await page.evaluate(async (d) => (await fetch(`/api/v1/planning/dispatch/${d}/print.pdf`)).status, monday());
  expect(pdf).toBe(200);
});

test('Helpdesk: Verlauf als Zeitleiste mit Anhang in der Antwort', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const created = await apiCall<{ summary: { id: string } }>(page, 'POST', '/api/v1/tickets', {
    id: crypto.randomUUID(), title: 'Tablet lädt keine Fotos hoch', description: 'Seit heute Morgen hängt der Upload.', category: 'sync', source: 'web',
    reporterBlocked: false,
  });
  expect(created.status).toBe(201);
  await page.goto(`/it-center/helpdesk/${created.json.summary.id}`);
  await expect(page.locator('.eoTimeline')).toContainText('Seit heute Morgen hängt der Upload.');
  await page.getByLabel('Antwort oder Notiz').fill('Bitte App neu starten – siehe Bild.');
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Datei anhängen' }).click();
  await (await chooser).setFiles({ name: 'anleitung.jpg', mimeType: 'image/jpeg', buffer: jpeg });
  await expect(page.locator('.eoReplyBar').getByRole('button', { name: 'anleitung.jpg' })).toBeVisible();
  await page.getByRole('button', { name: 'Antwort senden' }).click();
  const timeline = page.locator('.eoTimeline');
  await expect(timeline).toContainText('Bitte App neu starten – siehe Bild.');
  await expect(timeline.getByRole('link', { name: 'anleitung.jpg' })).toBeVisible();

  // Schnellaktion: Priorität wirkt sofort (ohne Speichern).
  await choose(page, page.getByLabel('Priorität', { exact: true }), 'Hoch');
  await expect(timeline).toContainText('Priorität: Normal → Hoch');
  await page.getByRole('option', { name: 'Nur Nachrichten' }).click();
  await expect(timeline).not.toContainText('Priorität: Normal → Hoch');
});
