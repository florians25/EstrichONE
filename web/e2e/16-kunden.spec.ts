import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, choose, openSection, pageTitle, signIn } from './helpers';

test.describe.configure({ mode: 'serial' });

test('Kundenanlage: Dublettenhinweis beim Tippen, vorhandenen Kunden verwenden, Gewerbekunde mit Ansprechpartner', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  const dialog = page.getByRole('dialog', { name: 'Neuer Kunde' });
  await expect(dialog.getByLabel('Kundennummer')).toHaveAttribute('placeholder', /wird automatisch vergeben, voraussichtlich 60\d{4}/);
  // Pflichtfeld: ohne Namen kein Anlegen.
  await dialog.getByRole('button', { name: 'Kunde anlegen' }).click();
  await expect(dialog.getByText('Bitte den Namen angeben.').first()).toBeVisible();
  // Ähnlicher Name und gleiche Telefonnummer: Hinweis erscheint schon beim Tippen.
  // Aktuelle Nummer des vorhandenen Kunden (andere Tests ändern sie), anders geschrieben eingeben.
  const kraus = (await apiCall<{ name: string; phone: string | null }[]>(page, 'GET', '/api/v1/contacts?search=Kraus')).json.find((c) => c.name === 'Familie Kraus');
  await dialog.getByLabel('Name', { exact: true }).fill('Kraus');
  await dialog.getByRole('textbox', { name: 'Telefonnummer 1' }).fill((kraus?.phone ?? '0871 123456').replace(/ /g, '/'));
  await expect(dialog.getByText(/Es gibt bereits (einen ähnlichen Kunden|\d+ ähnliche Kunden)/)).toBeVisible();
  await expect(dialog).toContainText('gleiche Telefonnummer');
  await dialog.getByRole('button', { name: 'Vorhandenen Kunden verwenden' }).first().click();
  await expect(page).toHaveURL(/\/kunden\/[0-9a-f-]{36}/);
  await expect(pageTitle(page)).toHaveText('Familie Kraus');

  // Gewerbekunde mit Ansprechpartner und automatischer Kundennummer.
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  await dialog.getByRole('option', { name: 'Gewerbekunde' }).click();
  await dialog.getByLabel('Firma', { exact: true }).fill('Wohnbau Süd GmbH');
  await dialog.getByLabel('Straße und Hausnummer').fill('Industriestraße 12');
  await dialog.getByLabel('PLZ', { exact: true }).fill('84034');
  await dialog.getByLabel('Ort', { exact: true }).fill('Landshut');
  await dialog.getByRole('textbox', { name: 'E-Mail-Adresse 1' }).fill('info@wohnbau-sued.de');
  await dialog.getByLabel('Name', { exact: true }).fill('Thomas Berger');
  await dialog.getByLabel('Funktion').fill('Bauleitung');
  await dialog.getByLabel('Notiz und Zahlungshinweise').fill('Rechnungen nur per E-Mail, Zahlungsziel 30 Tage');
  await dialog.getByRole('button', { name: 'Kunde anlegen' }).click();
  await expect(page).toHaveURL(/\/kunden\/[0-9a-f-]{36}/);
  await expect(pageTitle(page)).toHaveText('Wohnbau Süd GmbH');
  await expect(page.getByText(/Kd\.-Nr\. 60\d{4}/).first()).toBeVisible();
  await expect(page.getByText('Thomas Berger · Bauleitung').first()).toBeVisible();
  await expect(page.getByText('Rechnungen nur per E-Mail, Zahlungsziel 30 Tage').first()).toBeVisible();
});

test('Bauaktenanlage mit neuem Kunden: Kunde ausführlich erfassen, Adresse wie Kunde, Eckdaten, direkt in die Bauakte', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/bauakten');
  await page.getByRole('button', { name: 'Neue Bauakte' }).first().click();
  const wizard = page.getByRole('dialog', { name: 'Neue Bauakte' });
  await wizard.getByLabel('Name des Kunden').fill('Lena Vogt');
  await wizard.getByRole('button', { name: 'Neuen Kunden ausführlich erfassen …' }).click();
  const customer = page.getByRole('dialog', { name: 'Neuer Kunde' });
  await expect(customer.getByLabel('Name', { exact: true })).toHaveValue('Lena Vogt');
  await choose(page, customer.getByLabel('Anrede'), 'Frau', 'Frau');
  await customer.getByLabel('Straße und Hausnummer').fill('Lindenweg 7');
  await customer.getByLabel('PLZ', { exact: true }).fill('84036');
  await customer.getByLabel('Ort', { exact: true }).fill('Kumhausen');
  await customer.getByRole('textbox', { name: 'Telefonnummer 1' }).fill('0171 9876543');
  await customer.getByRole('button', { name: 'Kunde anlegen' }).click();
  await expect(customer).toBeHidden();
  await expect(wizard).toContainText('Vorhandener Kunde: Lena Vogt');
  await wizard.getByRole('button', { name: 'Weiter' }).click();

  await wizard.getByRole('textbox', { name: 'Bauvorhaben' }).fill('EFH Vogt, Heizestrich');
  await expect(wizard.getByRole('option', { name: 'Wie Kundenanschrift', selected: true })).toBeVisible();
  await expect(wizard.getByLabel('Ort', { exact: true })).toHaveValue('Kumhausen');
  await choose(page, wizard.getByLabel('Zuständig'), 'Anna Admin');
  await wizard.getByLabel('Gesamtfläche').fill('145');
  await wizard.getByRole('button', { name: 'Weiter' }).click();
  await expect(wizard).toContainText('Lindenweg 7, 84036 Kumhausen (wie Kundenanschrift)');
  await expect(wizard).toContainText('Zuständig');
  await wizard.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page).toHaveURL(/\/bauakten\/[0-9a-f-]{36}/);
  await expect(page.getByRole('heading', { name: 'EFH Vogt, Heizestrich' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Lena Vogt' })).toBeVisible();

  // Zweite Bauakte an derselben Adresse: Hinweis auf die vorhandene Akte.
  await page.locator('[id$="newBauakteButton"]').click();
  const second = page.getByRole('dialog', { name: 'Neue Bauakte' });
  await second.getByLabel('Name des Kunden').fill('Lena Vogt');
  await page.getByRole('option', { name: /Lena Vogt/ }).first().click();
  await expect(second).toContainText('Vorhandener Kunde: Lena Vogt');
  await second.getByRole('button', { name: 'Weiter' }).click();
  await expect(second.getByText('An dieser Adresse gibt es bereits eine Bauakte')).toBeVisible();
  await expect(second.getByRole('link', { name: /EFH Vogt, Heizestrich/ })).toBeVisible();
  await second.getByRole('button', { name: 'Abbrechen' }).click();
});

test('Kundenakte: Kennzahlen, Schnellaktionen, Bauakten mit Filter, Ansprechpartner pflegen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/kunden');
  await page.getByRole('row', { name: /Lena Vogt/ }).getByText('Lena Vogt', { exact: true }).click();
  await expect(pageTitle(page)).toHaveText('Lena Vogt');
  const head = page.locator('.eoCustomerHead');
  await expect(head).toContainText('Bauakten laufend');
  await expect(head).toContainText('1 von 1');
  await expect(head).toContainText('145,00 m²');
  await expect(head).toContainText('Letzte Aktivität');
  await expect(page.getByRole('button', { name: 'Anrufen' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Route' })).toBeVisible();

  await openSection(page, 'Bauakten');
  await expect(page.getByRole('row', { name: /EFH Vogt, Heizestrich/ })).toBeVisible();
  await page.getByRole('option', { name: /^Abgeschlossen/ }).click();
  await expect(page.getByText('Keine Bauakten in dieser Auswahl.')).toBeVisible();
  await page.getByRole('option', { name: /^Laufend/ }).click();
  await expect(page.getByRole('row', { name: /EFH Vogt, Heizestrich/ })).toBeVisible();

  await openSection(page, 'Kontakte');
  await expect(page.locator('.eoCustomerContactList').getByText('0171 9876543').first()).toBeVisible();
  await page.getByRole('button', { name: 'Ansprechpartner hinzufügen' }).click();
  const person = page.getByRole('dialog', { name: 'Ansprechpartner hinzufügen' });
  await person.getByLabel('Name', { exact: true }).fill('Markus Vogt');
  await person.getByLabel('Telefon').fill('0171 1112223');
  await person.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('[id$="customerPersons"]')).toContainText('Markus Vogt');
  const before = await page.getByRole('button', { name: 'Entfernen: Markus Vogt' }).count();
  await page.getByRole('button', { name: 'Entfernen: Markus Vogt' }).first().click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Entfernen' }).click();
  await expect(page.getByRole('button', { name: 'Entfernen: Markus Vogt' })).toHaveCount(before - 1);
});

test('Bauakte › Kontakte: nach Rolle gruppiert, Schnellaktionen, vorhandenen Kontakt verknüpfen statt doppelt anlegen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/bauakten');
  await page.getByRole('link', { name: 'EFH Vogt, Heizestrich' }).click();
  await openSection(page, 'Kontakte');
  const card = page.locator('.eoContactCard').filter({ hasText: 'Lena Vogt' }).first();
  await expect(card).toContainText('Kunde (Auftraggeber)');
  await expect(card.getByRole('button', { name: 'Anrufen: 0171 9876543' })).toBeVisible();
  await expect(card.getByRole('button', { name: 'Kontaktdaten kopieren: Lena Vogt' })).toBeVisible();

  // Neuer Kontakt mit Namen eines Kunden im Kundenstamm: Treffer wird angeboten und verknüpft.
  await page.getByRole('button', { name: 'Kontakt hinzufügen' }).click();
  await page.getByRole('menuitem', { name: 'Neuen Kontakt erfassen …' }).click();
  const dialog = page.getByRole('dialog', { name: 'Neuen Kontakt erfassen' });
  await choose(page, dialog.getByLabel('Rolle'), 'Bauleiter');
  await dialog.getByLabel('Firma', { exact: true }).fill('Wohnbau Süd GmbH');
  await dialog.getByLabel('Telefon', { exact: true }).click();
  await expect(dialog.getByText('Im Kundenstamm gibt es ähnliche Einträge')).toBeVisible();
  await dialog.getByRole('button', { name: 'Verknüpfen' }).first().click();
  await expect(dialog).toBeHidden();
  const linked = page.locator('.eoContactCard').filter({ hasText: 'Wohnbau Süd GmbH' }).first();
  await expect(linked).toContainText('Bauleiter');
  await expect(linked).toContainText('info@wohnbau-sued.de');
});

test('Nummernkreise in den Einstellungen: alle Datensätze, Kunden-Nummernkreis bearbeiten, Vorschau im Kundendialog', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/firmendaten?bereich=nummernkreise');
  const table = page.getByRole('grid', { name: 'Nummernkreise' });
  for (const label of ['Bauakten', 'Kunden', 'Lieferanten', 'Personal', 'Maschinen und Fahrzeuge', 'Kolonnen', 'Material', 'Bestellungen', 'Anfragen', 'Aufgaben', 'Mängel']) {
    await expect(table.getByRole('row', { name: new RegExp(`^${label} (Stammdaten|Vorgänge)`) })).toHaveCount(1);
  }
  const customers = table.getByRole('row', { name: /^Kunden Stammdaten/ });
  await expect(customers).toContainText('60{NNNN}');
  await customers.getByRole('button', { name: /bearbeiten/ }).click();
  const dialog = page.getByRole('dialog', { name: 'Nummernkreis Kunden' });
  await dialog.getByRole('textbox', { name: 'Nächste Nummer' }).fill('2000');
  await dialog.getByRole('textbox', { name: 'Nächste Nummer' }).press('Tab');
  await expect(dialog.getByText(/Nächste Nummer: 602000/)).toBeVisible();
  // Ungültiges Muster wird im Dialog abgelehnt.
  await dialog.getByRole('textbox', { name: 'Muster' }).fill('KD-');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(dialog.getByText(/genau eine laufende Nummer/).first()).toBeVisible();
  await dialog.getByRole('textbox', { name: 'Muster' }).fill('60{NNNN}');
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('.sapMMessageToast').last()).toContainText('Nummernkreis Kunden gespeichert');
  await expect(customers).toContainText('602000');
  await page.goto('/kunden');
  await page.getByRole('button', { name: 'Neuer Kunde' }).click();
  await expect(page.getByRole('dialog', { name: 'Neuer Kunde' }).getByLabel('Kundennummer')).toHaveAttribute('placeholder', /602000/);
});
