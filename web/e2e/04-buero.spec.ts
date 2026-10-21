import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, choose, nav, pageTitle, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

test('Helpdesk: Meldung bearbeiten, interne Notiz, Lösung dokumentieren', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const created = await apiCall(page, 'POST', '/api/v1/tickets', {
    id: crypto.randomUUID(), title: 'Drucker im Büro druckt nicht', category: 'officeIt', source: 'web', reporterBlocked: true,
    diagnostics: { appVersion: '1.0.0', platform: 'Web' },
  });
  expect(created.status).toBe(201);

  await nav(page, 'Helpdesk').click();
  await expect(pageTitle(page)).toHaveText('Helpdesk');
  const row = page.getByRole('row', { name: /Drucker im Büro druckt nicht/ });
  await expect(row.getByText('Kann nicht arbeiten')).toBeVisible();
  await row.getByText('Drucker im Büro druckt nicht').click();
  await expect(page).toHaveURL(/\/it-center\/helpdesk\//);

  await page.getByLabel('Antwort oder Notiz').fill('Toner prüfen, vermutlich leer');
  await page.getByText('Interne Notiz (für den Melder unsichtbar)').click();
  await page.getByRole('button', { name: /Notiz speichern|Senden|Antwort senden/ }).first().click();
  await expect(page.getByText('Toner prüfen, vermutlich leer')).toBeVisible();

  await choose(page, page.getByLabel('Status', { exact: true }), 'Gelöst');
  await page.getByLabel(/Lösung/).fill('Toner gewechselt.');
  await page.getByRole('button', { name: 'Speichern', exact: true }).click();
  await expect(page.getByText(/Status geändert: Gelöst|hat den Status/).first()).toBeVisible();
});

test('Büro: Telefon-Schnellerfassung mit Wiedervorlage, Anfrage erfassen und in Bauakte umwandeln, Tagesübersicht', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.getByRole('button', { name: 'Telefon' }).click();
  const quick = page.getByRole('dialog', { name: /Telefon-Schnellerfassung/ });
  await expect(quick).toContainText('Gespräch läuft');
  await quick.getByLabel('Wer ruft an?').fill('Herr Bauer');
  await quick.getByLabel('Notiz').fill('Fragt nach Termin für Erstbegehung');
  await quick.getByText('Wiedervorlage', { exact: true }).click();
  await quick.getByRole('button', { name: /Speichern/ }).click();
  await expect(toast(page)).toContainText('gespeichert');

  await nav(page, 'Anrufe').click();
  await expect(pageTitle(page)).toHaveText('Anrufe');
  await expect(page.getByText('Herr Bauer').first()).toBeVisible();
  await expect(page.getByText('Fragt nach Termin für Erstbegehung').first()).toBeVisible();

  await nav(page, 'Anfragen').click();
  await page.getByRole('button', { name: 'Anfrage erfassen' }).click();
  const form = page.getByRole('dialog', { name: 'Anfrage erfassen' });
  await form.getByLabel('Name', { exact: true }).fill('Petra Lang');
  await form.getByLabel('Firma').fill('Lang Bau GmbH');
  await form.getByLabel('Anliegen').fill('Heizestrich Halle Süd');
  await form.getByLabel('Ort', { exact: true }).fill('Landshut');
  await form.getByRole('button', { name: 'Speichern' }).click();
  await page.getByRole('row', { name: /Lang Bau GmbH/ }).click();
  const detail = page.getByRole('dialog', { name: /Anfrage: Petra Lang/ });
  await detail.getByRole('button', { name: 'In Bearbeitung' }).click();
  await detail.getByRole('button', { name: 'In Bauakte umwandeln' }).last().click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Umwandeln', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Heizestrich Halle Süd' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Lang Bau GmbH' })).toBeVisible();

  await nav(page, 'Tagesübersicht').click();
  await expect(pageTitle(page)).toContainText('Tagesübersicht');
});

test('Material: Lieferant und Artikel anlegen, Bestellungen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Lieferanten').click();
  await page.getByRole('button', { name: 'Lieferant anlegen' }).click();
  const supplier = page.getByRole('dialog', { name: 'Lieferant anlegen' });
  await supplier.getByLabel('Name', { exact: true }).fill('Baustoff Huber KG');
  await supplier.getByLabel('Unsere Kundennummer').fill('K-4711');
  await supplier.getByLabel('Telefon (Disposition)').fill('0871 99887');
  await supplier.getByLabel('Bestellhinweise').fill('Bestellschluss 12 Uhr für Lieferung am Folgetag');
  await supplier.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Baustoff Huber KG/ })).toContainText('K-4711');

  await nav(page, 'Materialstamm').click();
  await page.getByRole('button', { name: 'Artikel anlegen' }).click();
  const material = page.getByRole('dialog', { name: 'Artikel anlegen' });
  await material.getByLabel('Bezeichnung').fill('Randdämmstreifen 8 mm');
  await choose(page, material.getByLabel('Kategorie'), 'Randdämmstreifen');
  await choose(page, material.getByLabel('Einheit'), 'Rolle', 'Rolle');
  await material.getByLabel('Einkaufspreis je Einheit (netto)').fill('12,90');
  await choose(page, material.getByLabel('Standardlieferant'), 'Baustoff Huber KG');
  await material.getByRole('heading', { name: 'Artikel anlegen' }).click();
  await material.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Randdämmstreifen 8 mm/ })).toContainText('12,90');

  await nav(page, 'Bestellungen').click();
  await expect(pageTitle(page)).toHaveText('Bestellungen');
});

test('Kunden bearbeiten, Baustellenkarte, Firmendaten und Protokoll', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Kunden').click();
  await expect(pageTitle(page)).toHaveText('Kunden');
  await page.getByRole('link', { name: 'Familie Kraus' }).click();
  const dialog = page.getByRole('dialog', { name: 'Kunde bearbeiten' });
  await dialog.getByLabel('Telefon').fill('0871 654321');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Familie Kraus/ })).toContainText('0871 654321');

  await nav(page, 'Baustellenkarte').click();
  await expect(page.getByRole('heading', { name: 'Baustellenkarte' }).first()).toBeVisible();

  await nav(page, 'Einstellungen').click();
  await expect(pageTitle(page)).toHaveText('Einstellungen');

  await nav(page, 'Protokoll').click();
  await expect(pageTitle(page)).toHaveText('Protokoll');
  await expect(page.getByText('Benutzer angelegt').first()).toBeVisible();
});
