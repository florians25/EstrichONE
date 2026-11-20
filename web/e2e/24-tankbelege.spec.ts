import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, choose, nav, pageTitle, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

const day = (offset: number) => {
  const d = new Date();
  d.setDate(d.getDate() + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

test('Tankbelege: Hinweise zur Plausibilität, prüfen, freigeben, erstatten, Auswertung je Fahrzeug, Export', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const vehicleId = crypto.randomUUID();
  expect((await apiCall(page, 'PUT', `/api/v1/planning/resources/${vehicleId}`, {
    kind: 'vehicle', name: 'Sprinter 7', identifier: 'LA-EB 707', tankCapacityLiters: 75,
  })).status).toBe(200);
  const save = (body: Record<string, unknown>) => apiCall<{ number: string; id: string }>(page, 'PUT', `/api/v1/fuel-receipts/${crypto.randomUUID()}`,
    { vehicleId, fuelType: 'diesel', paymentMethod: 'fuelCard', ...body });
  expect((await save({ date: day(-6), odometerKm: 81_200, liters: 58.4, amountGross: 99.22 })).status).toBe(200);
  const refund = await save({ date: day(-3), odometerKm: 81_710, liters: 55.1, amountGross: 93.6, paymentMethod: 'private', station: 'Aral Landshut Nord' });
  expect(refund.status).toBe(200);
  expect(refund.json.number).toMatch(/^TB-\d{4}-\d{4}$/);
  const tooMuch = await save({ date: day(-1), odometerKm: 82_300, liters: 90, amountGross: 152.1 });

  await nav(page, 'Tankbelege').click();
  await expect(pageTitle(page)).toContainText('Tankbelege');
  const table = page.locator('[id$="receipts"]');
  const flagged = table.getByRole('row').filter({ hasText: tooMuch.json.number });
  await expect(flagged).toContainText('1 Hinweis');
  await expect(flagged).toContainText('Neu');

  // Beleg öffnen: Hinweis zur Tankgröße, freigeben.
  await flagged.getByText(tooMuch.json.number).click();
  const detail = page.getByRole('dialog', { name: `Tankbeleg ${tooMuch.json.number}` });
  await expect(detail.getByText(/mehr, als in den Tank passt/)).toBeVisible();
  await expect(detail.getByText('Kein Belegfoto vorhanden.')).toBeVisible();
  await detail.getByRole('button', { name: 'Freigeben' }).click();
  await expect(toast(page)).toContainText('freigegeben');
  await expect(flagged).toContainText('Geprüft');

  // Private Auslage: freigeben und als erstattet markieren (Mehrfachauswahl).
  const privateRow = table.getByRole('row').filter({ hasText: refund.json.number });
  await expect(privateRow).toContainText('Privat ausgelegt');
  await privateRow.getByRole('checkbox').click();
  await page.getByRole('button', { name: 'Freigeben', exact: true }).click();
  await expect(privateRow).toContainText('Geprüft');
  await privateRow.getByRole('checkbox').click();
  await page.getByRole('button', { name: 'Erstattet/Gebucht' }).click();
  await expect(privateRow).toContainText('Erstattet');

  // Filter: nur private Auslagen.
  await choose(page, page.getByLabel('Zahlart'), 'Privat ausgelegt');
  await expect(table.getByRole('row').filter({ hasText: tooMuch.json.number })).toHaveCount(0);
  await expect(privateRow).toBeVisible();
  await choose(page, page.getByLabel('Zahlart'), 'Alle Zahlarten');

  // Neuen Beleg im Büro erfassen: Nummer wird automatisch vergeben.
  await page.getByRole('button', { name: 'Beleg erfassen' }).click();
  const form = page.getByRole('dialog', { name: 'Tankbeleg erfassen' });
  await expect(form.getByLabel('Belegnummer')).toHaveAttribute('placeholder', 'wird automatisch vergeben');
  await choose(page, form.getByLabel('Fahrzeug'), 'Sprinter 7');
  await form.getByLabel('Menge').fill('12,5');
  await form.getByLabel('Betrag brutto').fill('21,30');
  await form.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText(/Beleg TB-\d{4}-\d{4} erfasst/);

  // Auswertung je Fahrzeug.
  await page.getByRole('tab', { name: /Auswertung je Fahrzeug/ }).click();
  const stats = page.locator('[id$="statistics"]').getByRole('row').filter({ hasText: 'Sprinter 7' });
  await expect(stats).toContainText('l/100 km');

  // CSV-Export für die Buchhaltung.
  const csv = await page.evaluate(async () => {
    const response = await fetch('/api/v1/fuel-receipts/export.csv');
    return { status: response.status, type: response.headers.get('content-type'), text: await response.text() };
  });
  expect(csv.status).toBe(200);
  expect(csv.type).toContain('text/csv');
  expect(csv.text).toContain('Umsatz (brutto);Soll/Haben-Kennzeichen');
  expect(csv.text).toContain(refund.json.number);

  // Geräte & Fahrzeuge: Tankbelege und Verbrauch des Fahrzeugs.
  await nav(page, 'Geräte & Fahrzeuge').click();
  await page.getByRole('row', { name: /Sprinter 7/ }).getByRole('button', { name: 'Tankbelege und Verbrauch' }).click();
  const vehicle = page.getByRole('dialog', { name: 'Tankbelege: Sprinter 7 (LA-EB 707)' });
  await expect(vehicle.getByText('Letzte Belege')).toBeVisible();
  await expect(vehicle.getByText(refund.json.number, { exact: false })).toBeVisible();
  await vehicle.getByRole('button', { name: 'Schließen' }).click();
});
