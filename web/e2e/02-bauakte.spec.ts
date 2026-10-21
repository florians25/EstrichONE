import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, choose, jpeg, nav, openSection, pageTitle, pdf, signIn, toast } from './helpers';

test.describe.configure({ mode: 'serial' });

const title = 'Anfrage Familie Kraus, Heizestrich';

async function openAkte(page: Page, name = title) {
  await page.goto('/bauakten');
  await page.getByRole('link', { name }).click();
  await expect(page.getByRole('heading', { name })).toBeVisible();
}

test('Anfrage mit neuem Kunden im Assistenten, Dublettenprüfung, als Auftrag übernehmen, Räume, Mängel, Notiz', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.goto('/bauakten');
  await page.getByRole('button', { name: 'Neue Anfrage' }).click();
  const dialog = page.getByRole('dialog', { name: 'Neue Anfrage' });
  // Schritt 1: Kunde ist Pflicht.
  await dialog.getByRole('button', { name: 'Weiter' }).click();
  await expect(dialog.getByText('Bitte den Kunden angeben').first()).toBeVisible();
  // Name zuletzt: Vorschläge vorhandener Kunden öffnen sich beim Tippen.
  await dialog.getByLabel('Straße (Kunde)').fill('Am Hang 3');
  await dialog.getByLabel('PLZ (Kunde)').fill('84028');
  await dialog.getByLabel('Ort (Kunde)').fill('Landshut');
  await dialog.getByLabel('Telefon (Kunde)').fill('0871 123456');
  await dialog.getByLabel('Name des Kunden').fill('Familie Kraus');
  await dialog.getByRole('button', { name: 'Weiter' }).click();
  // Schritt 2: Bauvorhaben und Baustelle.
  await dialog.getByRole('textbox', { name: 'Bauvorhaben' }).fill(title);
  await dialog.getByRole('button', { name: 'Adresse des Kunden übernehmen' }).click();
  await expect(dialog.getByLabel('Ort', { exact: true })).toHaveValue('Landshut');
  await dialog.getByRole('button', { name: 'Weiter' }).click();
  // Schritt 3: Prüfen.
  await expect(dialog).toContainText('Familie Kraus (wird neu angelegt)');
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page.getByRole('heading', { name: title })).toBeVisible();
  await expect(page.getByRole('button', { name: /Status: Anfrage/ })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Familie Kraus' })).toBeVisible();
  await page.getByRole('button', { name: 'Als Auftrag übernehmen' }).click();
  await expect(page.getByRole('button', { name: /Status: Geplant/ })).toBeVisible();
  await expect(page.locator('.sapMMsgStrip').filter({ hasText: 'Erstbegehung noch nicht ausgeführt.' }).filter({ visible: true }).first()).toBeVisible();

  // Gleicher Kunde frei eingegeben: Dublettenprüfung bietet den vorhandenen Kunden an.
  await page.getByLabel('Neue Akte').click();
  const second = page.getByRole('dialog', { name: 'Neue Bauakte' });
  await second.getByLabel('Telefon (Kunde)').fill('0871/123456');
  await second.getByLabel('Name des Kunden').fill('Kraus');
  await second.getByRole('button', { name: 'Weiter' }).click();
  await expect(second.getByText('Mögliche Dubletten')).toBeVisible();
  await expect(second).toContainText('gleiche Telefonnummer');
  await second.getByRole('button', { name: 'Diesen Kunden verwenden' }).first().click();
  await expect(second).toContainText('Vorhandener Kunde: Familie Kraus');
  await second.getByRole('button', { name: 'Weiter' }).click();
  await second.getByRole('textbox', { name: 'Bauvorhaben' }).fill('Garage Kraus');
  await second.getByRole('button', { name: 'Weiter' }).click();
  await expect(second).toContainText('Familie Kraus (vorhanden)');
  await second.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Garage Kraus' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Familie Kraus' })).toBeVisible();

  await openAkte(page);
  await openSection(page, 'Räume & Flächen');
  await page.getByRole('button', { name: 'Raum hinzufügen' }).click();
  const room = page.getByRole('dialog', { name: 'Raum hinzufügen' });
  await room.getByLabel('Raum', { exact: true }).fill('Wohnen/Essen');
  await room.getByLabel('Geschoss').fill('EG');
  await room.getByLabel('Fläche (m²)').fill('42,5');
  await room.getByLabel('Dicke (mm)').fill('65');
  await room.getByRole('switch', { name: 'Fußbodenheizung' }).click();
  await choose(page, room.getByLabel('Estrichart'), /CT|CAF|Zement|Calcium|Estrich/);
  await room.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Wohnen\/Essen/ })).toContainText('42,50 m²');
  await expect(page.locator('.eoRoomTotal')).toContainText('42,50');

  await openSection(page, 'Aufgaben & Mängel');
  await choose(page, page.getByLabel('Art', { exact: true }).first(), 'Mangel');
  await page.getByPlaceholder('Was ist mangelhaft?').fill('Randdämmstreifen fehlt im Bad');
  await page.getByRole('button', { name: 'Hinzufügen' }).click();
  const row = page.getByRole('row').filter({ has: page.locator('input[value="Randdämmstreifen fehlt im Bad"]') });
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: 'Erledigt: Randdämmstreifen fehlt im Bad' }).click();
  await page.getByRole('option', { name: 'Alle', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Wieder öffnen: Randdämmstreifen fehlt im Bad' })).toBeVisible();

  await openSection(page, 'Notizen & Verlauf');
  await page.getByLabel('Neue Notiz').fill('Kunde wünscht Ausführung ab KW 46');
  await page.getByRole('button', { name: 'Notiz speichern' }).click();
  await expect(page.locator('.notes').getByText('Kunde wünscht Ausführung ab KW 46')).toBeVisible();
  await expect(page.getByText('Anna Admin hat einen Raum angelegt.').filter({ visible: true }).first()).toBeVisible();
  await expect(page.getByText('Anna Admin hat einen Mangel als behoben markiert.').filter({ visible: true }).first()).toBeVisible();
});

test('Dokumente: Ablage, Mehrfach-Upload, Umbenennen, Unterordner, Fassungen, Vorschau, ZIP, Löschen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await openSection(page, 'Dokumente');
  await page.locator('.sapMTreeItemBase').filter({ hasText: 'Angebote (0)' }).click();
  await expect(page.getByText('Neue Dateien werden abgelegt unter: Angebote.')).toBeVisible();
  await page.locator('.eoUpload input[type=file]').setInputFiles([
    { name: 'Angebot.pdf', mimeType: 'application/pdf', buffer: pdf },
    { name: 'Leistungsverzeichnis.pdf', mimeType: 'application/pdf', buffer: Buffer.concat([pdf, Buffer.from('% LV\n')]) },
  ]);
  await expect(toast(page)).toContainText('2 Dateien hochgeladen.');
  const table = page.locator('.sapMList').filter({ hasText: 'Angebote (2)' }).last();
  await expect(table.getByRole('link', { name: 'Angebot.pdf' })).toBeVisible();
  await expect(page.locator('.sapMTreeItemBase').filter({ hasText: 'Angebote (2)' })).toBeVisible();

  // Umbenennen
  const angebot = page.getByRole('row').filter({ hasText: 'Angebot.pdf' });
  await angebot.getByRole('button', { name: 'Weitere Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Umbenennen …' }).click();
  const rename = page.getByRole('dialog', { name: 'Umbenennen' });
  await rename.getByLabel('Dateiname').fill('Angebot 2026-117');
  await rename.getByLabel('Beschreibung').fill('Heizestrich EG, Stand Oktober');
  await rename.getByRole('button', { name: 'Umbenennen' }).click();
  await expect(page.getByRole('link', { name: 'Angebot 2026-117.pdf' })).toBeVisible();
  await expect(page.getByText('Heizestrich EG, Stand Oktober')).toBeVisible();

  // Unterordner anlegen und das Leistungsverzeichnis verschieben
  await page.getByRole('button', { name: 'Neuer Unterordner' }).click();
  const folder = page.getByRole('dialog', { name: 'Neuer Unterordner' });
  await folder.getByRole('textbox').fill('Ausschreibung');
  await folder.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByText('Neue Dateien werden abgelegt unter: Angebote / Ausschreibung.')).toBeVisible();
  await page.locator('.sapMTreeItemBase').filter({ hasText: /^Alle Dokumente/ }).click();
  const lv = page.getByRole('row').filter({ hasText: 'Leistungsverzeichnis.pdf' });
  await lv.getByRole('button', { name: 'Weitere Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Verschieben …' }).click();
  const move = page.getByRole('dialog', { name: /verschieben/ });
  await move.getByLabel('Unterordner').fill('Ausschreibung');
  await move.getByRole('button', { name: 'Verschieben' }).click();
  await expect(page.locator('.sapMTreeItemBase').filter({ hasText: 'Ausschreibung (1)' })).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: 'Leistungsverzeichnis.pdf' })).toContainText('Ausschreibung');

  // Neue Fassung
  await page.getByRole('row').filter({ hasText: 'Angebot 2026-117.pdf' }).getByRole('button', { name: 'Weitere Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Neue Fassung hochladen …' }).click();
  const version = page.getByRole('dialog', { name: 'Neue Fassung hochladen' });
  await version.locator('input[type=file]').setInputFiles({ name: 'Angebot 2026-117 v2.pdf', mimeType: 'application/pdf', buffer: Buffer.concat([pdf, Buffer.from('% v2\n')]) });
  await version.getByRole('button', { name: 'Als neue Fassung hochladen' }).click();
  await expect(toast(page)).toContainText('Neue Fassung hochgeladen');
  const current = page.getByRole('row').filter({ hasText: 'Angebot 2026-117 v2.pdf' });
  await expect(current).toContainText('v2');
  await expect(page.getByRole('link', { name: 'Angebot 2026-117.pdf' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Alle Fassungen' }).click();
  await expect(page.getByRole('link', { name: 'Angebot 2026-117.pdf' })).toBeVisible();
  await page.getByRole('button', { name: 'Alle Fassungen' }).click();

  // Vorschau in der rechten Spalte mit Fassungen
  await current.getByRole('link', { name: 'Angebot 2026-117 v2.pdf' }).click();
  await expect(page).toHaveURL(/\/datei\//);
  await expect(page.locator('iframe.eoPreviewFrame')).toBeVisible();
  await expect(page.getByText('Fassung 1: Angebot 2026-117.pdf')).toBeVisible();
  await page.getByRole('button', { name: 'Schließen' }).last().click();
  await expect(page).not.toHaveURL(/\/datei\//);

  // Mehrfachauswahl: ZIP und Löschen
  await openSection(page, 'Dokumente');
  const rows = page.locator('.sapMListTbl tbody tr').filter({ hasText: '.pdf' });
  await expect(rows).toHaveCount(2);
  await rows.nth(0).locator('.sapMCb').click();
  await rows.nth(1).locator('.sapMCb').click();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Auswahl herunterladen', exact: true }).click();
  expect((await download).suggestedFilename()).toMatch(/\.zip$/);
  await page.getByRole('row').filter({ hasText: 'Leistungsverzeichnis.pdf' }).getByRole('button', { name: 'Weitere Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Löschen' }).click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'Löschen' }).click();
  await expect(page.getByRole('link', { name: 'Leistungsverzeichnis.pdf' })).toHaveCount(0);
  await openSection(page, 'Notizen & Verlauf');
  await expect(page.getByText('Anna Admin hat ein Dokument hinzugefügt.').filter({ visible: true }).first()).toBeVisible();
});

test('Zentrale Dokumentverwaltung: Ordner, Suche, Freigabe anfordern, ablehnen mit Begründung, freigeben, Dashboard', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await nav(page, 'Dokumente').click();
  await expect(pageTitle(page)).toHaveText('Dokumente');
  const table = page.locator('[id$="docTable"]').first();
  await expect(table.getByRole('link', { name: 'Angebot 2026-117 v2.pdf' })).toBeVisible();
  await expect(table).toContainText('v2');
  await page.getByRole('list', { name: 'Ordner' }).getByRole('listitem').filter({ hasText: 'Angebote' }).click();
  await expect(table.getByRole('link', { name: 'Angebot 2026-117 v2.pdf' })).toBeVisible();
  await page.getByPlaceholder('Dateiname, Beschreibung, Bauakte').fill('gibt es nicht');
  await expect(table.getByRole('link', { name: 'Angebot 2026-117 v2.pdf' })).toHaveCount(0);
  await page.getByPlaceholder('Dateiname, Beschreibung, Bauakte').fill('Kraus');
  await expect(table.getByRole('link', { name: 'Angebot 2026-117 v2.pdf' })).toBeVisible();

  await table.getByRole('row').filter({ hasText: 'Angebot 2026-117 v2.pdf' }).getByRole('button', { name: 'Freigabe anfordern' }).click();
  const request = page.getByRole('dialog', { name: 'Freigabe anfordern' });
  await choose(page, request.getByLabel('Freigabe durch'), 'Rolle Administrator', 'Rolle Adm');
  await request.getByLabel('Nachricht').fill('Bitte Preise prüfen');
  await request.getByRole('button', { name: 'Anfordern' }).click();
  await expect(toast(page)).toContainText('Freigabe angefordert');
  await expect(table.getByRole('row').filter({ hasText: 'Angebot 2026-117 v2.pdf' })).toContainText('Wartet auf Freigabe');

  // Dashboard zeigt die offene Freigabe.
  await nav(page, 'Übersicht').click();
  await expect(page.getByText('Warten auf Genehmigung').filter({ visible: true }).first()).toBeVisible();
  await page.getByRole('link', { name: 'Alle Freigaben' }).click();
  const inbox = page.locator('.sapMITBContainerContent');
  await expect(inbox.getByText('„Bitte Preise prüfen“')).toBeVisible();
  await inbox.getByRole('button', { name: 'Ablehnen' }).first().click();
  const reject = page.getByRole('dialog', { name: 'Freigabe ablehnen' });
  await reject.getByRole('button', { name: 'Ablehnen' }).click();
  await expect(reject).toBeVisible();
  await reject.getByRole('textbox', { name: /Begründung/ }).fill('Rabatt fehlt');
  await reject.getByRole('button', { name: 'Ablehnen' }).click();
  await expect(toast(page)).toContainText('Freigabe abgelehnt');
  await expect(inbox.getByRole('row').filter({ hasText: 'Angebot 2026-117 v2.pdf' }).first()).toContainText('Rabatt fehlt');

  // Erneut anfordern (in der Bauakte) und freigeben – Verlauf bleibt vollständig.
  await page.getByRole('tab', { name: /Ablage/ }).click();
  await table.getByRole('link', { name: 'Angebot 2026-117 v2.pdf' }).click();
  await expect(page).toHaveURL(/\/datei\//);
  await expect(page.getByRole('heading', { name: 'Angebot 2026-117 v2.pdf' })).toBeVisible();
  await page.locator('.sapFDynamicPageTitle').getByRole('button', { name: 'Freigabe anfordern' }).click();
  await choose(page, page.getByRole('dialog', { name: 'Freigabe anfordern' }).getByLabel('Freigabe durch'), 'Rolle Administrator', 'Rolle Adm');
  await page.getByRole('dialog', { name: 'Freigabe anfordern' }).getByRole('button', { name: 'Anfordern' }).click();
  await expect(page.getByText(/Wartet auf Freigabe durch Rolle Administrator/)).toBeVisible();
  await page.getByRole('button', { name: 'Freigeben', exact: true }).click();
  await page.getByRole('dialog', { name: 'Dokument freigeben' }).getByRole('button', { name: 'Freigeben' }).click();
  await expect(toast(page)).toContainText('Dokument freigegeben');
  await expect(page.getByText('Freigegeben', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('Abgelehnt', { exact: true }).first()).toBeVisible();
});

test('Fotos: Upload, Zeitstrahl, Großansicht mit Vor/Zurück, Beschriftung, Zuordnung zum Raum, Filter, ZIP', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await openSection(page, 'Fotos & Videos');
  await page.locator('.eoUpload input[type=file]').setInputFiles([
    { name: 'Bad-OG.jpg', mimeType: 'image/jpeg', buffer: jpeg },
    { name: 'Wohnen-EG.jpg', mimeType: 'image/jpeg', buffer: jpeg },
  ]);
  await expect(toast(page)).toContainText('2 Dateien hochgeladen.');
  await expect(page.getByText('Fotos (2)')).toBeVisible();
  await expect(page.locator('.sapMGHLI, .sapMLIBGroupHeader, [role=group]').filter({ hasText: /2 Fotos/ }).first()).toBeVisible();

  await page.locator('.eoPhotoTile').first().click();
  const lightbox = page.getByRole('dialog', { name: 'Fotos' });
  await expect(lightbox.getByText('1 von 2')).toBeVisible();
  await lightbox.getByRole('button', { name: 'Weiter' }).click();
  await expect(lightbox.getByText('2 von 2')).toBeVisible();
  await lightbox.getByLabel('Beschriftung').fill('Randdämmung Bad OG');
  await lightbox.getByRole('button', { name: 'Beschriftung speichern' }).click();
  await expect(toast(page)).toContainText('Beschriftung gespeichert');
  await lightbox.getByRole('button', { name: 'Schließen' }).click();
  await expect(page.getByText('Randdämmung Bad OG').first()).toBeVisible();

  await page.getByRole('button', { name: 'Auswählen', exact: true }).click();
  await page.locator('.eoPhotoTile').filter({ hasText: 'Randdämmung Bad OG' }).locator('.sapMCb').click();
  await page.getByRole('button', { name: 'Zuordnen …' }).click();
  const assign = page.getByRole('dialog', { name: '1 Foto zuordnen' });
  await choose(page, assign.getByLabel('Raum'), 'Wohnen/Essen');
  await assign.getByRole('button', { name: 'Zuordnen' }).click();
  await expect(toast(page)).toContainText('Zuordnung gespeichert');
  await expect(page.locator('.eoPhotoTile').filter({ hasText: 'Randdämmung Bad OG' })).toContainText('Wohnen/Essen');

  await choose(page, page.getByLabel('Raum', { exact: true }).first(), 'Ohne Raum');
  await expect(page.getByText('Fotos (1 von 2)')).toBeVisible();
  await choose(page, page.getByLabel('Raum', { exact: true }).first(), 'Alle Räume');

  await page.getByRole('option', { name: 'Raster' }).click();
  const selecting = page.getByRole('button', { name: 'Auswählen', exact: true });
  if ((await selecting.getAttribute('aria-pressed')) !== 'true') await selecting.click();
  await page.getByRole('button', { name: 'Alle auswählen' }).click();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Herunterladen (ZIP)' }).click();
  expect((await download).suggestedFilename()).toMatch(/\.zip$/);
});

test('Baustellendokumentation: Tagesbericht mit Arbeitszeit, Kolonne und Fotos, CM-Messung, Baustellenbericht', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await openSection(page, 'Dokumentation');
  await page.getByRole('button', { name: 'Tagesbericht erfassen' }).click();
  const diary = page.getByRole('dialog', { name: 'Tagesbericht erfassen' });
  await choose(page, diary.getByLabel('Wetter'), 'Regen');
  await diary.getByLabel('Temperatur').fill('9');
  await diary.getByLabel('Arbeitsende').fill('15:30');
  await diary.getByRole('combobox', { name: 'Kolonne' }).fill('Kolonne Nord');
  await diary.getByLabel('Anwesende').fill('3');
  await diary.getByLabel('Was wurde gemacht?').fill('Heizestrich im EG eingebracht, 65 mm');
  await choose(page, diary.getByLabel('Fotos des Tages'), 'Randdämmung Bad OG');
  await diary.locator('.sapMDialogTitle, .sapMTitle').first().click();
  await diary.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page)).toContainText('Tagesbericht erfasst');
  const entry = page.locator('#diaryList, [id$=diaryList]').getByRole('listitem').filter({ hasText: 'Heizestrich im EG eingebracht' });
  await expect(entry).toContainText('Regen · 9 °C · 3 anwesend');
  await expect(entry).toContainText('07:00–15:30');
  await expect(entry).toContainText('Kolonne Nord');
  await expect(entry.getByRole('button', { name: 'Randdämmung Bad OG' })).toBeVisible();

  await page.getByRole('tab', { name: /Prüfungen/ }).click();
  await page.getByRole('button', { name: 'CM-Messung erfassen' }).click();
  const cm = page.getByRole('dialog', { name: 'CM-Messung erfassen' });
  await cm.getByRole('combobox', { name: 'Messstelle (Raum)' }).fill('Wohnen/Essen EG');
  await cm.getByLabel('Messwert').fill('1,6');
  await cm.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByRole('row', { name: /Wohnen\/Essen EG/ })).toContainText(/1,6 CM-% \(Grenzwert (1,8|2,0)\)/);
  await expect(page.getByRole('row', { name: /Wohnen\/Essen EG/ }).getByText('Belegreif', { exact: true })).toBeVisible();

  // Erstbegehung als geführter Ablauf: Prüfpunkt beanstandet → Mangel wird angelegt.
  await page.getByRole('button', { name: 'Erstbegehung erfassen' }).first().click();
  const insp = page.getByRole('dialog', { name: 'Erstbegehung erfassen' });
  await insp.getByRole('button', { name: 'Weiter' }).click();
  await insp.locator('[id="inspCheck-edgeInsulation"]').getByText('Mangel', { exact: true }).click();
  await insp.locator('[id="inspNote-edgeInsulation-inner"]').fill('fehlt an der Treppe');
  await insp.getByRole('button', { name: 'Weiter' }).click();
  await expect(insp.getByRole('button', { name: 'Foto aufnehmen oder hochladen' })).toBeVisible();
  await insp.getByRole('button', { name: 'Weiter' }).click();
  await expect(insp.getByText('Randdämmstreifen vorhanden: fehlt an der Treppe')).toBeVisible();
  await insp.getByRole('button', { name: 'Weiter' }).click();
  await expect(insp).toContainText('1 Mängel werden angelegt');
  await insp.getByRole('button', { name: 'Erstbegehung speichern' }).click();
  await expect(toast(page)).toContainText('Erstbegehung gespeichert');
  await expect(page.getByText('Erstbegehungsprotokoll').first()).toBeVisible();

  // Bauakte als PDF (Server): Deckblatt, Räume, Tagebuch, Prüfungen, Mängel, Fotodokumentation.
  const pdfDownload = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Bauakte als PDF (mit Fotos)' }).click();
  const pdfFile = await pdfDownload;
  expect(pdfFile.suggestedFilename()).toMatch(/^Bauakte .*\.pdf$/);
  const pdfPath = await pdfFile.path();
  const fs = await import('node:fs');
  const bytes = fs.readFileSync(pdfPath!);
  expect(bytes.subarray(0, 5).toString()).toBe('%PDF-');
  expect(bytes.length).toBeGreaterThan(30_000);
  if (process.env['SCREENSHOTS']) fs.copyFileSync(pdfPath!, 'test-results/bauakte.pdf');

  await page.getByRole('tab', { name: 'Berichte' }).click();
  await page.getByRole('button', { name: 'Druckansicht öffnen' }).first().click();
  await expect(page).toHaveURL(/\/bericht\?art=bericht/);
  const sheet = page.locator('.eoSheet');
  await expect(sheet).toContainText('Baustellenbericht');
  await expect(sheet).toContainText('Heizestrich im EG eingebracht, 65 mm');
  await expect(sheet).toContainText('Wohnen/Essen');
  await expect(sheet).toContainText('Belegreif');
  await expect(sheet.locator('img').first()).toBeVisible();
  await page.getByRole('button', { name: 'Zur Bauakte' }).click();
  await page.goto(`${new URL(page.url()).pathname.replace(/\?.*/, '')}/bericht?art=abnahme`);
  await expect(page.locator('.eoSheet')).toContainText('Abnahmeprotokoll');
  await expect(page.locator('.eoSheet')).toContainText('Unterschrift Auftraggeber');
});

test('Prüfungen und Bautagebuch aus der App erscheinen in der Bauakte', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const bauakte = crypto.randomUUID();
  const op = (type: string, entityId: string, payload: unknown) => ({ opId: crypto.randomUUID(), type, entityId, baseVersion: null, payload });
  const result = await apiCall<{ results: { status: string }[] }>(page, 'POST', '/api/v1/sync/push', {
    operations: [
      op('bauakte.create', bauakte, { title: 'EFH Prüfung E2E', city: 'Landshut' }),
      op('diary.create', crypto.randomUUID(), { bauakteId: bauakte, day: '2026-10-20', text: 'Estrich im EG eingebracht', weather: 'rain', temperatureCelsius: 9 }),
      op('inspection.create', crypto.randomUUID(), { bauakteId: bauakte, kind: 'cmMeasurement', location: 'Bad OG', binder: 'cement', heated: true, measuredCmPercent: 1.6 }),
      op('inspection.create', crypto.randomUUID(), { bauakteId: bauakte, kind: 'initialInspection', checks: { substrate: 'ok', edgeInsulation: 'notOk' }, remarks: 'Randdämmung fehlt' }),
    ],
  });
  expect(result.json.results.map((r) => r.status)).toEqual(['applied', 'applied', 'applied', 'applied']);

  await page.goto(`/bauakten/${bauakte}?abschnitt=dokumentation`);
  await expect(page.getByRole('heading', { name: 'EFH Prüfung E2E' })).toBeVisible();
  await expect(page.getByText('Estrich im EG eingebracht')).toBeVisible();
  await expect(page.getByText('Regen · 9 °C')).toBeVisible();
  await page.getByRole('tab', { name: /Prüfungen/ }).click();
  await expect(page.getByText('Erstbegehungsprotokoll')).toBeVisible();
  await expect(page.getByText('Mit Mängeln – Klärung nötig')).toBeVisible();
  await expect(page.getByText('Randdämmstreifen vorhanden')).toBeVisible();
  await expect(page.getByText('Randdämmung fehlt')).toBeVisible();
  await expect(page.getByRole('row', { name: /Bad OG/ })).toContainText('1,6 CM-% (Grenzwert 1,8)');
  await openSection(page, 'Notizen & Verlauf');
  await expect(page.getByText('Anna Admin hat eine CM-Messung erfasst: 1,6 CM-% bei Grenzwert 1,8 CM-% – belegreif.')).toBeVisible();
});

test('Standort per Karte festlegen und in der Kopfzeile anzeigen', async ({ page }) => {
  // Kachelserver verlangen einen Referer (OSM: sonst „403 Access blocked“).
  const referers: string[] = [];
  const pixel = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');
  await page.route('https://tile.openstreetmap.org/**', (route) => {
    referers.push(route.request().headers()['referer'] ?? '');
    return route.fulfill({ status: 200, contentType: 'image/png', body: pixel });
  });
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await expect(page.getByText('Offene Aufgaben und Mängel').first()).toBeVisible();
  await expect(page.getByText('Baustellendaten')).toBeVisible();
  await page.getByRole('button', { name: 'Standort festlegen' }).click();
  await page.locator('.eoMap').last().click({ position: { x: 200, y: 100 } });
  await page.getByRole('button', { name: 'Standort speichern' }).click();
  await expect(toast(page)).toContainText('Standort gespeichert');
  await expect(page.getByRole('button', { name: 'Standort ändern' })).toBeVisible();
  await expect(page.locator('.leaflet-interactive').first()).toBeVisible();
  await expect(page.locator('img.leaflet-tile').first()).toHaveAttribute('referrerpolicy', 'strict-origin-when-cross-origin');
  expect(referers.length).toBeGreaterThan(0);
  expect(referers.every((r) => r.startsWith(new URL(page.url()).origin))).toBe(true);
});

test('Kontakte, Zuweisung, Storno mit Begründung, Löschen mit Passwort', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await openAkte(page);
  await openSection(page, 'Kontakte');
  await page.getByRole('button', { name: 'Bearbeiten' }).last().click();
  const contact = page.getByRole('dialog', { name: 'Baustellenkontakt bearbeiten' });
  await contact.getByLabel('Ansprechpartner vor Ort').fill('Herr Bauer (Bauleitung)');
  await contact.getByLabel('Telefon').fill('0171 2345678');
  await contact.getByLabel('Hinweise / Behinderungen').fill('Schlüssel beim Nachbarn Nr. 5');
  await contact.getByRole('button', { name: 'Speichern' }).click();
  await expect(page.getByText('Herr Bauer (Bauleitung)')).toBeVisible();
  await expect(page.locator('.sapMMsgStrip').filter({ hasText: 'Schlüssel beim Nachbarn Nr. 5' }).filter({ visible: true }).first()).toBeVisible();

  await page.getByRole('button', { name: /Zugewiesen:/ }).click();
  const assign = page.getByRole('dialog', { name: 'Bauakte zuweisen' });
  await expect(assign.getByRole('combobox', { name: 'Zugewiesene Mitarbeiter' })).toBeVisible();
  await assign.getByRole('button', { name: 'Zuweisen' }).click();
  await expect(toast(page)).toContainText('Zuweisung gespeichert');

  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Bauakte stornieren' }).click();
  const cancel = page.getByRole('dialog', { name: 'Bauakte stornieren' });
  await cancel.getByLabel('Stornogrund').fill('Kunde hat verschoben');
  await cancel.getByRole('button', { name: 'Stornieren' }).click();
  await expect(page.locator('.sapMMsgStrip').filter({ hasText: 'Storniert: Kunde hat verschoben' }).filter({ visible: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'Storno aufheben' }).click();
  await expect(page.getByRole('button', { name: /Status: Geplant/ })).toBeVisible();

  await openAkte(page, 'Garage Kraus');
  await page.getByRole('button', { name: 'Aktionen' }).click();
  await page.getByRole('menuitem', { name: 'In den Papierkorb' }).click();
  const remove = page.getByRole('alertdialog', { name: /Bauakte löschen/ });
  await remove.getByLabel('Passwort bestätigen').fill('falsch-falsch');
  await remove.getByRole('button', { name: 'In den Papierkorb' }).click();
  await expect(remove.getByText('Das Passwort ist nicht korrekt. Die Bauakte wurde nicht gelöscht.')).toBeVisible();
  await remove.getByLabel('Passwort bestätigen').fill(adminPassword);
  await remove.getByRole('button', { name: 'In den Papierkorb' }).click();
  await expect(pageTitle(page)).toHaveText('Bauakten');
  await expect(page.getByRole('link', { name: 'Garage Kraus' })).toHaveCount(0);
});
