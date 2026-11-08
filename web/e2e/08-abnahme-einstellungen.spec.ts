import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, layoutIssues, nav, openSection, signIn, toast } from './helpers';

const title = 'Abnahme Neubau Seidl';
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');

async function openAkte(page: Page) {
  await page.goto('/bauakten');
  const list = await apiCall<{ items: { title: string }[] }>(page, 'GET', `/api/v1/bauakten?search=${encodeURIComponent(title)}`);
  if (!list.json?.items?.some((b) => b.title === title)) {
    expect((await apiCall(page, 'POST', '/api/v1/bauakten', { id: crypto.randomUUID(), title, street: 'Gartenweg 3', postalCode: '84028', city: 'Landshut' })).status).toBe(201);
    await page.reload();
  }
  await page.getByRole('link', { name: title }).click();
  await expect(page.getByRole('heading', { name: title })).toBeVisible();
}

async function settings(page: Page, tab: string) {
  await page.goto(`/system/firmendaten?bereich=${tab}`);
  await expect(page.getByRole('heading', { name: 'Systemeinstellungen' })).toBeVisible();
}

test('Einstellungen: Materialmodul ab- und wieder einschalten, Firmenlogo hochladen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await settings(page, 'module');
  await page.getByRole('switch', { name: /Material \(/ }).click();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.locator('.sapMMessageToast').last()).toContainText('Einstellungen gespeichert');
  await expect(nav(page, 'Materialstamm')).toHaveCount(0);
  await openAkte(page);
  await expect(page.locator('.sapUxAPAnchorBar').getByText('Material', { exact: true })).toHaveCount(0);

  await settings(page, 'module');
  await page.getByRole('switch', { name: /Material \(/ }).click();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(nav(page, 'Materialstamm')).toBeVisible();

  await settings(page, 'firma');
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Logo hochladen …' }).click();
  await (await chooser).setFiles({ name: 'logo.png', mimeType: 'image/png', buffer: png });
  await expect(page.locator('.sapMMessageToast').last()).toContainText('Logo gespeichert');
  // Firmenlogo nur in der Vorschau (für PDFs/Briefe), nicht in der Kopfleiste.
  await expect(page.locator('img.eoLogoPreview').first()).toBeVisible();
  await expect(page.locator('.sapFShellBar img[id$="companyLogo"]')).toHaveCount(0);
});

test('Systemeinstellungen: Bereich links wählen zeigt und markiert genau diesen Bereich, Schalter werden gespeichert', async ({ page }) => {
  // 1366 × 768 bei 125 % Skalierung (CSS 1093 × 614): Liste und Inhalt nebeneinander, kein Abschneiden.
  await page.setViewportSize({ width: 1093, height: 614 });
  await signIn(page, 'admin', adminPassword);
  await page.goto('/system/firmendaten');
  const navList = page.locator('.eoSettingsNav');
  const heading = page.locator('.eoSettingsHead .sapMTitle');
  const selected = navList.locator('li.sapMLIBSelected');
  await expect(heading).toHaveText('Firma');
  await expect(selected).toHaveCount(1);
  await expect(selected).toContainText('Firma');
  for (const [entry, key, field] of [
    ['Dokumente und Freigaben', 'dokumente', 'Vorgeschlagene Frist'], ['Module', 'module', 'Chat einschalten'],
    ['Speicher', 'speicher', 'Fotos unverändert speichern'], ['Anmeldung', 'sitzungen', 'Abmelden nach Leerlauf'],
    ['Bauakten', 'bauakte', 'Aufbau der Aktennummer'], ['Firma', 'firma', 'Firmenname'],
  ] as const) {
    await navList.getByText(entry, { exact: true }).click();
    await expect(heading).toHaveText(entry);
    await expect(selected).toHaveCount(1);
    await expect(selected).toContainText(entry);
    await expect(page.locator('.eoSettingsTabs .sapUiFormElementLbl').filter({ hasText: field }).first()).toBeVisible();
    expect(new URL(page.url()).searchParams.get('bereich')).toBe(key === 'firma' ? null : key);
    expect(await layoutIssues(page)).toEqual([]);
  }
  // Direkt aufgerufener Bereich (Link aus der Verwaltung) ist markiert – nicht der zuvor gewählte.
  await page.goto('/system/firmendaten?bereich=module');
  await expect(heading).toHaveText('Module');
  await expect(selected).toHaveCount(1);
  await expect(selected).toContainText('Module');

  // Schalter: ungespeicherte Änderung wird angezeigt, Speichern übernimmt Modul- und Chat-Schalter dauerhaft.
  const material = page.getByRole('switch', { name: /Material \(/ });
  const preview = page.getByRole('switch', { name: 'Vorschau in Pushnachrichten' });
  const before = await preview.getAttribute('aria-checked');
  await material.click();
  await preview.click();
  await expect(page.getByText('Ungespeicherte Änderungen', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Einstellungen gespeichert');
  await expect(page.getByText('Ungespeicherte Änderungen', { exact: false })).toBeHidden();
  await expect(nav(page, 'Materialstamm')).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('switch', { name: /Material \(/ })).toHaveAttribute('aria-checked', 'false');
  await expect(page.getByRole('switch', { name: 'Vorschau in Pushnachrichten' })).not.toHaveAttribute('aria-checked', before ?? '');
  // Zurücksetzen; „Verwerfen“ nimmt eine weitere Änderung zurück.
  await page.getByRole('switch', { name: /Material \(/ }).click();
  await page.getByRole('switch', { name: 'Vorschau in Pushnachrichten' }).click();
  await page.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Einstellungen gespeichert');
  // Seitennavigation ist bei dieser Breite eingeklappt (nur Symbole): Eintrag vorhanden statt sichtbar prüfen.
  await expect(nav(page, 'Materialstamm')).toHaveCount(1);
  await page.getByRole('switch', { name: /Material \(/ }).click();
  await page.getByRole('button', { name: 'Verwerfen' }).click();
  await expect(page.getByRole('switch', { name: /Material \(/ })).toHaveAttribute('aria-checked', 'true');
  await expect(page.getByText('Ungespeicherte Änderungen', { exact: false })).toBeHidden();
});

test('Abnahmeprotokoll: Freigabe per Link durch den Kunden, PDF unter „Abnahmen“', async ({ page, browser }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Abnahmeprotokoll erstellen …' }).click();
  const wizard = page.getByRole('dialog', { name: 'Abnahmeprotokoll' });
  await expect(wizard.locator('.eoAkteHeader')).toContainText(title);
  await wizard.locator('input[id="accClient-inner"]').fill('Frau Kraus');
  for (let i = 0; i < 2; i++) await wizard.getByRole('button', { name: 'Weiter' }).click();
  await wizard.getByText('Abgenommen mit Vorbehalt', { exact: true }).click();
  await wizard.getByRole('button', { name: 'Mangel hinzufügen' }).click();
  await wizard.getByPlaceholder(/Mangel oder Restarbeit/).fill('Randfuge im Bad nachschneiden');
  for (let i = 0; i < 2; i++) await wizard.getByRole('button', { name: 'Weiter' }).click();
  await wizard.getByLabel('Notizen (frei, beliebig lang)').fill('Heizprotokoll übergeben.');
  await wizard.getByRole('button', { name: 'Weiter' }).click();
  // Auftragnehmer unterschreibt auf dem Gerät, der Kunde gibt per Link frei.
  const pad = wizard.locator('[id="accContractorSignature"] canvas');
  const box = (await pad.boundingBox())!;
  await page.mouse.move(box.x + 20, box.y + 30);
  await page.mouse.down();
  await page.mouse.move(box.x + 120, box.y + 60, { steps: 8 });
  await page.mouse.move(box.x + 200, box.y + 25, { steps: 8 });
  await page.mouse.up();
  await wizard.getByText('Auftraggeber nicht vor Ort: Freigabe per Link an den Kunden').click();
  await wizard.getByRole('button', { name: 'Abnahme speichern' }).click();
  const info = page.getByRole('alertdialog', { name: /Freigabelink für den Kunden/ });
  await expect(info).toBeVisible();
  const url = (await info.textContent())!.match(/https?:\/\/\S+\/abnahme\/[A-Za-z0-9_-]+/)![0];
  await info.getByRole('button', { name: 'Schließen' }).click();

  // Kunde: ohne Anmeldung, nur dieses Protokoll.
  const customer = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage();
  await customer.goto(new URL(new URL(url).pathname, page.url()).toString());
  await expect(customer.getByText('Abgenommen mit Vorbehalt')).toBeVisible();
  await expect(customer.getByText('Randfuge im Bad nachschneiden')).toBeVisible();
  await customer.getByLabel('Ihr Name').fill('Petra Kraus');
  await customer.getByRole('button', { name: 'Freigeben' }).click();
  await expect(customer.getByText(/Das Abnahmeprotokoll ist freigegeben/)).toBeVisible();
  await customer.close();

  await openAkte(page);
  await openSection(page, 'Dokumentation');
  await page.getByRole('tab', { name: /Abnahmen/ }).click();
  await expect(page.getByText('Abgeschlossen, PDF unter „Abnahmen“').filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/Petra Kraus/).filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText(/Abnahme mit Vorbehalt. Die Akte bleibt offen/).filter({ visible: true }).first()).toBeVisible();
  await openSection(page, 'Dokumente');
  await expect(page.locator('.sapMTreeItemBase').filter({ hasText: 'Abnahmen (1)' })).toBeVisible();
});
