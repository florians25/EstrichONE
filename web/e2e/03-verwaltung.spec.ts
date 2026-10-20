import { expect, test } from '@playwright/test';
import { adminPassword, choose, nav, pageTitle, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

test('Benutzer anlegen, Einmalpasswort, Pflichtwechsel bei der ersten Anmeldung', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Benutzer').click();
  await expect(pageTitle(page)).toHaveText('Benutzer');
  await page.getByRole('button', { name: 'Benutzer anlegen' }).click();

  const dialog = page.getByRole('dialog', { name: 'Benutzer anlegen' });
  await dialog.getByLabel('Name', { exact: true }).fill('Lukas Brandl');
  await dialog.getByLabel('Anmeldename').fill('l.brandl');
  await choose(page, dialog.getByLabel('Rollen'), 'Baustelle');
  await dialog.getByRole('heading', { name: 'Benutzer anlegen' }).click();
  await dialog.getByRole('button', { name: 'Anlegen' }).click();

  const oneTime = page.getByTestId('one-time-password');
  await expect(oneTime).toContainText('Lukas Brandl');
  const password = (await page.getByTestId('one-time-code').textContent())!.trim();
  expect(password.length).toBeGreaterThan(8);
  await expect(page.getByRole('row', { name: /Lukas Brandl/ })).toContainText('Baustelle');

  await page.getByLabel('Benutzermenü Anna Admin').click();
  await page.getByRole('menuitem', { name: 'Abmelden' }).click();
  await expect(page).toHaveURL(/\/anmelden/);
  await expect(page.getByLabel('Anmeldename oder E-Mail')).toBeVisible();
  await signIn(page, 'l.brandl', password);
  await expect(page.getByText('Sie haben ein Einmalpasswort erhalten')).toBeVisible();
  await page.getByLabel('Bisheriges Passwort').fill(password);
  await page.getByLabel('Neues Passwort', { exact: true }).fill('Estrich-Lukas-2026!');
  await page.getByLabel('Neues Passwort wiederholen').fill('Estrich-Lukas-2026!');
  await page.getByRole('button', { name: 'Passwort ändern' }).click();
  await expect(pageTitle(page)).toContainText('Lukas Brandl');
  // Rolle „Baustelle“: keine Verwaltung in der Navigation.
  await expect(nav(page, 'Benutzer')).toHaveCount(0);
});

test('Gerät einrichten (QR-Code) und Sicherung zeigt ihren Zustand', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Geräte').click();
  await page.getByRole('button', { name: 'Gerät einrichten' }).click();
  await page.getByText('Poolgerät (mehrere Personen)').click();
  await page.getByLabel('Gerätename (optional)').fill('Fahrzeug 3');
  await page.getByRole('button', { name: 'Code erzeugen' }).click();
  await expect(page.getByTestId('enrollment-code')).toHaveText(/^[A-Z2-9]{4}-[A-Z2-9]{4}$/);
  await page.getByRole('button', { name: 'Fertig' }).click();

  await nav(page, 'Sicherung').click();
  await expect(pageTitle(page)).toHaveText('Sicherung');
  await expect(page.getByText('Letzte Sicherung', { exact: true })).toBeVisible();
  await expect(page.getByText('Wiederherstellungstest', { exact: true })).toBeVisible();
});

test('Planung: Kolonne und Pumpe anlegen, Einsatz planen, Doppelbelegung wird angezeigt, Verschieben per Ziehen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Kolonnen und Geräte').click();
  await page.getByRole('button', { name: 'Kolonne anlegen' }).click();
  const crew = page.getByRole('dialog', { name: 'Kolonne anlegen' });
  await crew.getByLabel('Name der Kolonne').fill('Kolonne Admin');
  await choose(page, crew.getByLabel('Kolonnenführer'), 'Anna Admin');
  await crew.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Kolonne Admin/ })).toContainText('Anna Admin');

  await page.getByRole('tab', { name: /Fahrzeuge und Geräte/ }).click();
  await page.getByRole('button', { name: 'Fahrzeug oder Gerät hinzufügen' }).click();
  const resource = page.getByRole('dialog', { name: 'Fahrzeug oder Gerät hinzufügen' });
  await resource.getByLabel('Bezeichnung').fill('Mixokret 2');
  await resource.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Mixokret 2/ })).toContainText('Verfügbar');

  await nav(page, 'Kolonnenplanung').click();
  await expect(pageTitle(page)).toContainText('Kolonnenplanung');
  await page.getByRole('button', { name: /^Einsatz planen: Kolonne Admin, Montag/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Einsatz planen' });
  await choose(page, dialog.getByLabel('Bauakte', { exact: true }), /Kraus/, 'Kraus');
  await dialog.getByLabel('Tätigkeit').fill('Estrich einbringen EG');
  await choose(page, dialog.getByLabel('Fahrzeuge und Geräte'), 'Mixokret 2');
  await dialog.getByRole('heading', { name: 'Einsatz planen' }).click();
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('button', { name: /Estrich einbringen EG, .*Kraus/ })).toBeVisible();

  await page.getByRole('button', { name: /^Einsatz planen: Termine ohne Kolonne, Montag/ }).click();
  const second = page.getByRole('dialog', { name: 'Einsatz planen' });
  await choose(page, second.getByLabel('Bauakte', { exact: true }), /Kraus/, 'Kraus');
  await second.getByLabel('Tätigkeit').fill('Randdämmung stellen');
  await choose(page, second.getByLabel('Weitere Mitarbeiter'), 'Anna Admin');
  await second.getByRole('heading', { name: 'Einsatz planen' }).click();
  await choose(page, second.getByLabel('Fahrzeuge und Geräte'), 'Mixokret 2');
  await second.getByRole('heading', { name: 'Einsatz planen' }).click();
  await second.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('dialog')).toContainText('mehrfach eingeplant');
  await page.getByRole('dialog').getByRole('button', { name: 'Schließen' }).click();
  await expect(page.getByRole('button', { name: /Randdämmung stellen, .*Kraus.*2 Hinweise/ })).toBeVisible();

  // Ziehen auf Mittwoch derselben Kolonne.
  const item = page.getByRole('button', { name: /Estrich einbringen EG/ });
  const target = page.getByRole('button', { name: /^Einsatz planen: Kolonne Admin, Mittwoch/ }).locator('xpath=..');
  await item.dragTo(target);
  await expect(toast(page)).toContainText(/verschoben/i);
  await expect(target.getByRole('button', { name: /Estrich einbringen EG/ })).toBeVisible();

  // Wochenplan als PDF vom Server.
  const weekPdf = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Wochen-PDF' }).click();
  expect((await weekPdf).suggestedFilename()).toMatch(/^Kolonnenplan \d{4}-\d{2}-\d{2}\.pdf$/);

  // Dienstplan je Mitarbeiter.
  await page.getByRole('option', { name: 'Mitarbeiter', exact: true }).click();
  await expect(page.getByRole('rowheader', { name: /Anna Admin/ })).toBeVisible();
});

test('Disposition: Vorrat einplanen, Terminart, Tag-, Monatsansicht und Arbeitsplan', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Kolonnenplanung').click();
  await page.getByRole('button', { name: /Baustellen-Vorrat/ }).click();
  const backlog = page.locator('.eoBacklog');
  await backlog.getByRole('option', { name: / alle$/ }).click();
  await backlog.getByRole('listitem').filter({ hasText: 'Kraus' }).first().getByRole('button', { name: 'Einplanen' }).click();
  const dialog = page.getByRole('dialog', { name: 'Einsatz planen' });
  await expect(dialog).toBeVisible();
  await choose(page, dialog.getByLabel('Terminart'), 'Erstbegehung');
  await expect(dialog.getByLabel('Tätigkeit')).toHaveValue('Erstbegehung');
  await choose(page, dialog.getByLabel('Kolonne', { exact: true }), 'Kolonne Admin');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  // Bei Hinweisen (Doppelbelegung) bleibt der Dialog offen – gespeichert ist trotzdem.
  await page.waitForTimeout(1500);
  const close = page.getByRole('dialog', { name: 'Einsatz planen' }).getByRole('button', { name: 'Schließen' });
  if (await close.isVisible().catch(() => false)) await close.click();
  await expect(page.getByRole('button', { name: /Erstbegehung, .*Kraus/ }).first()).toBeVisible();

  await page.getByRole('option', { name: 'Tag', exact: true }).click();
  await expect(pageTitle(page)).toContainText('Tageseinteilung');
  await page.getByRole('option', { name: 'Monat', exact: true }).click();
  await expect(pageTitle(page)).toContainText('Kalender');

  await nav(page, 'Arbeitsplan').click();
  await expect(pageTitle(page)).toContainText('Arbeitsplan');
  await page.getByRole('option', { name: '14 Tage', exact: true }).click();
  await expect(page.getByText('Erstbegehung', { exact: true }).first()).toBeVisible();
});

test('Rollen und Rechte: eigene Rolle anlegen, Rechtematrix bearbeiten', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Rollen und Rechte').click();
  await expect(pageTitle(page)).toHaveText('Rollen und Rechte');
  await page.getByRole('button', { name: 'Rolle anlegen' }).click();
  const dialog = page.getByRole('dialog', { name: 'Rolle anlegen' });
  await dialog.getByLabel('Name', { exact: true }).fill('Disposition');
  await choose(page, dialog.getByLabel('Rechte übernehmen von'), 'Baustelle');
  await dialog.getByRole('button', { name: 'Anlegen' }).click();
  await expect(toast(page)).toContainText('Rolle angelegt');
  const box = page.locator('.sapMCb[title="Disposition: Einsätze planen"]');
  await expect(box).toBeVisible();
  await box.click();
  await page.getByRole('button', { name: 'Änderungen speichern' }).click();
  await expect(toast(page)).toContainText('1 Rolle gespeichert');
  await expect(page.locator('.sapMCb[title="Disposition: Einsätze planen"] input')).toBeChecked();
});

test('Stammdaten: Textbaustein, Ordnervorlage und Bauakten-Vorlage', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Stammdaten').click();
  await expect(pageTitle(page)).toHaveText('Stammdaten');
  await page.getByRole('button', { name: 'Textbaustein anlegen' }).click();
  let dialog = page.getByRole('dialog', { name: 'Textbaustein anlegen' });
  await dialog.getByLabel('Name', { exact: true }).fill('Untergrund');
  await dialog.getByLabel('Text', { exact: true }).fill('Untergrund besenrein übergeben, Randdämmstreifen vorhanden.');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Untergrund besenrein/ })).toBeVisible();

  await page.getByRole('tab', { name: /Bauakten-Vorlagen/ }).click();
  await page.getByRole('button', { name: 'Vorlage anlegen' }).click();
  dialog = page.getByRole('dialog', { name: 'Vorlage anlegen' });
  await dialog.getByLabel('Name', { exact: true }).fill('EFH Heizestrich');
  await dialog.getByLabel('Beschreibung').fill('Heizestrich CAF 65 mm auf Dämmung');
  await dialog.getByLabel(/^Räume/).fill('Wohnen/Essen; EG; 42,5; 65; FBH\nBad; OG; 8; 65; FBH');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /EFH Heizestrich/ })).toContainText('Wohnen/Essen EG, Bad OG');
});
