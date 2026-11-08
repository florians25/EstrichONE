import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, choose, jpeg, nav, signIn, toast } from './helpers';

// Fehler melden / Funktion wünschen: geführtes Formular (normaler Benutzer), Verwaltung durch die IT, Antwort beim Einreicher.
test.describe.configure({ mode: 'serial' });

const user = { login: 'l.brandl', password: 'Estrich-Lukas-2026!' };
const title = 'Fotos im Bautagebuch lassen sich nicht speichern';
let number = '';

const dialog = (page: Page) => page.locator('#feedbackDialog');

test('Fehler melden als normaler Benutzer: Art, Pflichtfelder, Bildschirmfoto, Zusammenfassung mit technischen Angaben, Nummer', async ({ page }) => {
  await signIn(page, user.login, user.password);
  await page.goto('/bauakten');
  // Einstieg: Schaltfläche in der Kopfleiste direkt links neben dem Profil.
  const button = page.locator('[id$="feedbackButton"]');
  await expect(button).toBeVisible();
  await expect(button).toHaveAttribute('title', 'Fehler melden / Funktion wünschen');
  const [buttonBox, avatarBox] = [await button.boundingBox(), await page.locator('[id$="profileAvatar"]').boundingBox()];
  expect(buttonBox!.x).toBeLessThan(avatarBox!.x);
  expect(avatarBox!.x - (buttonBox!.x + buttonBox!.width)).toBeLessThan(80);
  await button.click();
  await expect(dialog(page)).toBeVisible();

  // Schritt 1: Art als Kachel.
  await expect(dialog(page).getByText('Funktion wünschen', { exact: true })).toBeVisible();
  await page.locator('#feedbackKind-bug').click();

  // Schritt 2: Pflichtfelder werden geprüft; Bereich ist aus der aktuellen Seite vorbelegt.
  await expect(page.locator('#feedbackArea-inner')).toHaveValue('Bauakten');
  await page.locator('#feedbackNext').click();
  await expect(dialog(page).getByText(/Bitte noch ausfüllen: Kurze Zusammenfassung, Was ist passiert\?/)).toBeVisible();
  await page.locator('#feedbackTitle-inner').fill(title);
  await page.locator('#feedbackSteps-inner').fill('1. Bauakte öffnen\n2. Bautagebuch\n3. Foto anhängen und speichern');
  await page.locator('#feedbackExpected-inner').fill('Der Eintrag wird mit Foto gespeichert.');
  await page.locator('#feedbackActual-inner').fill('Die Ladeanzeige dreht sich endlos.');
  await dialog(page).getByRole('radio', { name: 'Immer (nachstellbar)' }).click();
  await dialog(page).getByRole('radio', { name: /Hoch: Arbeiten nicht möglich/ }).click();
  await page.locator('#feedbackNext').click();

  // Schritt 3: Bildschirmfoto anhängen.
  const chooser = page.waitForEvent('filechooser');
  await page.locator('#feedbackPick').click();
  await (await chooser).setFiles({ name: 'bildschirm.jpg', mimeType: 'image/jpeg', buffer: jpeg });
  await expect(dialog(page).getByRole('button', { name: /bildschirm\.jpg/ })).toBeVisible();
  await page.locator('#feedbackNext').click();

  // Schritt 4: Zusammenfassung inklusive automatisch mitgesendeter Angaben.
  const review = page.locator('#feedbackReview');
  await expect(review.getByText('Automatisch mitgesendet')).toBeVisible();
  await expect(review.getByText(title)).toBeVisible();
  await expect(review.getByText(/Chrome \d+/)).toBeVisible();
  await expect(review.getByText('EstrichONE-Version')).toBeVisible();
  await expect(review.getByText('/bauakten', { exact: false }).first()).toBeVisible();
  await page.locator('#feedbackSend').click();

  // Schritt 5: Bestätigung mit Nummer.
  const done = dialog(page).getByRole('heading', { name: /Ihre Meldung F[-‑]\d{4}[-‑]\d{4} ist eingegangen/ });
  await expect(done).toBeVisible();
  number = /F[-‑]\d{4}[-‑]\d{4}/.exec((await done.textContent()) ?? '')![0].replace(/‑/g, '-');
  await page.locator('#feedbackMine').click();
  await expect(page).toHaveURL(/\/meine-meldungen\/[0-9a-f-]{36}/);
  await expect(page.locator('[id$="fbItemTitle"]')).toHaveText(title);
  await expect(page.locator('[id$="fbProgress"]')).toContainText('Schritt 1 von 5: Neu');
  await expect(page.locator('[id$="fbList"] li').filter({ hasText: title })).toBeVisible();
  // Keine Verwaltungsfunktionen für Einreicher; die IT-Seite ist nicht erreichbar.
  await expect(page.locator('[id$="fbActions"]').filter({ visible: true })).toHaveCount(0);
  await expect(page.getByText('Interne Notiz (für den Einreicher unsichtbar)')).toHaveCount(0);
  expect((await apiCall(page, 'GET', '/api/v1/feedback')).status).toBe(403);
  await page.goto('/it-center/meldungen');
  await expect(page).toHaveURL(/\/start$/);
});

test('Funktionswunsch über das Benutzermenü, „Meine Meldungen“ filtert', async ({ page }) => {
  await signIn(page, user.login, user.password);
  await page.goto('/start');
  await page.locator('[id$="profileAvatar"]').click();
  await page.getByRole('menuitem', { name: 'Fehler melden / Funktion wünschen' }).click();
  await page.locator('#feedbackKind-feature').click();
  await page.locator('#feedbackTitle-inner').fill('Bauakten als Excel exportieren');
  await page.locator('#feedbackProblem-inner').fill('Für die Nachkalkulation tippe ich jede Woche alles ab.');
  await page.locator('#feedbackSolution-inner').fill('Ein Knopf „Als Excel exportieren“ über der Liste.');
  await dialog(page).getByRole('radio', { name: /Wichtig: spart regelmäßig Zeit/ }).click();
  await page.locator('#feedbackNext').click();
  await page.locator('#feedbackNext').click();
  await page.locator('#feedbackSend').click();
  await expect(dialog(page).getByRole('heading', { name: /ist eingegangen/ })).toBeVisible();
  await page.locator('#feedbackClose').click();

  await nav(page, 'Meine Meldungen').click();
  await expect(page).toHaveURL(/\/meine-meldungen$/);
  const list = page.locator('[id$="fbList"] li');
  await expect(list.filter({ hasText: 'Bauakten als Excel exportieren' })).toBeVisible();
  await page.locator('[id$="fbSearch"] input').fill('excel');
  await expect(list.filter({ hasText: title })).toHaveCount(0);
  await page.locator('[id$="fbSearch"] input').fill('');
  await expect(list.filter({ hasText: title })).toBeVisible();

  // Schmales Fenster: die Schaltfläche wandert ins Überlaufmenü der Kopfleiste und bleibt erreichbar.
  await page.setViewportSize({ width: 800, height: 700 });
  await page.waitForTimeout(800);
  await page.locator('.sapFShellBar').getByRole('button', { name: 'Zusätzliche Optionen' }).click();
  await page.getByRole('button', { name: 'Fehler melden / Funktion wünschen' }).filter({ visible: true }).first().click();
  await expect(dialog(page)).toBeVisible();
  await page.locator('#feedbackCancel').click();
});

test('IT verwaltet: Zähler, Filter, Status, Priorität, Zuständigkeit, interne Notiz, Antwort, Duplikat', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Fehler & Wünsche').click();
  await expect(page).toHaveURL(/\/it-center\/meldungen$/);
  await expect(page.locator('[id$="fbTitle"]')).toHaveText('Fehler & Wünsche');
  await expect(page.locator('.eoFbCounters').getByText('Neu')).toBeVisible();
  await choose(page, page.locator('[id$="fbKind"]'), 'Fehler');
  const row = page.locator('[id$="fbList"] li').filter({ hasText: title });
  await expect(row).toBeVisible();
  await expect(page.locator('[id$="fbList"] li').filter({ hasText: 'Bauakten als Excel exportieren' })).toHaveCount(0);
  await row.click();
  await expect(page).toHaveURL(/\/it-center\/meldungen\/[0-9a-f-]{36}/);

  // Alle Angaben, Anhang und technische Infos.
  await expect(page.locator('[id$="fbDetails"]').getByText('Die Ladeanzeige dreht sich endlos.')).toBeVisible();
  await expect(page.getByAltText('bildschirm.jpg').first()).toBeVisible();
  // Anhang als kleine Kachel, Klick öffnet das Detailfenster mit dem Bild (statt eines neuen Tabs).
  await page.getByRole('button', { name: 'Foto öffnen: bildschirm.jpg' }).first().click();
  const viewer = page.getByRole('dialog', { name: 'Foto' });
  await expect(viewer.locator('img.eoMediaStageMedia')).toBeAttached();
  await expect(viewer.getByRole('button', { name: 'Herunterladen' })).toBeVisible();
  await viewer.getByRole('button', { name: 'Schließen' }).click();
  await expect(viewer).toHaveCount(0);
  await expect(page.locator('[id$="fbContext"]').getByText(/Chrome \d+/)).toBeVisible();
  await expect(page.locator('[id$="fbContext"]').getByText('User-Agent')).toBeVisible();

  await choose(page, page.locator('[id$="fbItemStatus"]'), 'In Prüfung');
  await expect(toast(page)).toContainText('Status: In Prüfung');
  await choose(page, page.locator('[id$="fbItemPriority"]'), 'Hoch');
  await expect(toast(page)).toContainText('Priorität geändert');
  await page.getByRole('button', { name: 'Mir zuweisen' }).click();
  await expect(page.locator('.eoHdFacts').getByText('Anna Admin')).toBeVisible();

  await page.locator('[id$="fbReply"] textarea').fill('Vermutlich Zeitüberschreitung beim Hochladen – Logs prüfen.');
  await page.getByText('Interne Notiz (für den Einreicher unsichtbar)').click();
  await page.locator('[id$="fbSend"]').click();
  await expect(page.locator('.eoTimeline').getByText('Vermutlich Zeitüberschreitung beim Hochladen – Logs prüfen.')).toBeVisible();
  await page.locator('[id$="fbReply"] textarea').fill('Danke für die genaue Beschreibung! Wir beheben das mit dem nächsten Update.');
  await page.locator('[id$="fbSend"]').click();
  await expect(page.locator('.eoTimeline').getByText('Danke für die genaue Beschreibung!', { exact: false })).toBeVisible();
  await choose(page, page.locator('[id$="fbItemStatus"]'), 'Geplant');
  await expect(toast(page)).toContainText('Status: Geplant');

  // Duplikat mit Verweis auf die erste Meldung.
  const second = await apiCall<{ summary: { id: string } }>(page, 'POST', '/api/v1/feedback', {
    id: crypto.randomUUID(), kind: 'bug', title: 'Bautagebuch: Foto speichern hängt', area: 'Bauakten', actualResult: 'hängt', frequency: 'once', impact: 'medium',
  });
  expect(second.status).toBe(201);
  await page.goto(`/it-center/meldungen/${second.json.summary.id}`);
  await choose(page, page.locator('[id$="fbItemStatus"]'), 'Duplikat');
  const duplicateDialog = page.getByRole('dialog', { name: 'Als Duplikat markieren' });
  await duplicateDialog.getByLabel(/Duplikat von/).fill(number.toLowerCase());
  await duplicateDialog.getByRole('button', { name: 'Markieren' }).click();
  await expect(toast(page)).toContainText(`Als Duplikat von ${number} markiert`);
  await expect(page.locator('.eoHdFacts').getByText(number)).toBeVisible();
});

test('Einreicher sieht Status und Antwort (ohne interne Notiz) und wird benachrichtigt', async ({ page }) => {
  await signIn(page, user.login, user.password);
  const inbox = await apiCall<{ items: { title: string; body: string | null; link: string }[] }>(page, 'GET', '/api/v1/notifications');
  expect(inbox.json.items.some((n) => n.title === `Antwort zu Meldung ${number}`)).toBe(true);
  expect(inbox.json.items.some((n) => n.title === `Status geändert: Meldung ${number}` && (n.body ?? '').endsWith('· Geplant'))).toBe(true);
  const link = inbox.json.items.find((n) => n.title.includes(number))!.link;
  expect(link).toMatch(/^\/meine-meldungen\//);

  await page.goto(link);
  await expect(page.locator('[id$="fbItemTitle"]')).toHaveText(title);
  await expect(page.locator('[id$="fbProgress"]')).toContainText('Schritt 3 von 5: Geplant');
  await expect(page.locator('.eoTimeline').getByText('Danke für die genaue Beschreibung!', { exact: false })).toBeVisible();
  await expect(page.locator('.eoTimeline').getByText('Vermutlich Zeitüberschreitung', { exact: false })).toHaveCount(0);
  await page.locator('[id$="fbReply"] textarea').fill('Danke, ich warte auf das Update.');
  await page.locator('[id$="fbSend"]').click();
  await expect(page.locator('.eoTimeline').getByText('Danke, ich warte auf das Update.')).toBeVisible();
});
