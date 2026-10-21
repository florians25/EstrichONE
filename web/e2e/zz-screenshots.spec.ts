import { test, type Page } from '@playwright/test';
import { adminPassword, openSection, signIn } from './helpers';

// Bildschirmfotos für die visuelle Durchsicht (SCREENSHOTS=1), nicht Teil der regulären Tests.
test.skip(!process.env['SCREENSHOTS'], 'nur auf Anforderung');

const shot = (page: Page, name: string, fullPage = false) =>
  page.waitForTimeout(800).then(() => page.screenshot({ path: `test-results/screenshots/${name}.png`, fullPage }));

test('Bildschirmfotos', async ({ page }) => {
  test.setTimeout(240_000);
  await page.goto('/anmelden');
  await shot(page, 'login');
  await signIn(page, 'admin', adminPassword);
  await shot(page, 'start', true);
  const pages: [string, string][] = [
    ['/bauakten', 'bauakten'], ['/tagesuebersicht', 'today'], ['/bauakten/karte', 'sitemap'], ['/planung', 'planning'],
    ['/planung/tageseinteilung', 'planning-day'], ['/planung/kalender', 'planning-month'], ['/planung/arbeitsplan', 'workplan'],
    ['/planung/kolonnen', 'crews'], ['/planung/geraete', 'vehicles'], ['/anfragen', 'inquiries'], ['/anrufe', 'calls'], ['/kunden', 'customers'],
    ['/bestellungen', 'orders'], ['/material', 'materials'], ['/lieferanten', 'suppliers'], ['/verwaltung/benutzer', 'users'],
    ['/it-center/geraete', 'devices'], ['/it-center/helpdesk', 'helpdesk'], ['/system/sicherung', 'backup'],
    ['/system/firmendaten', 'company'], ['/system/protokoll', 'audit'], ['/dokumente', 'dms'], ['/dokumente?reiter=freigaben', 'approvals'],
    ['/verwaltung/rollen', 'roles'], ['/verwaltung/stammdaten', 'masterdata'],
  ];
  for (const [path, name] of pages) {
    await page.goto(path);
    await shot(page, name);
  }
  await page.goto('/bauakten');
  await page.getByRole('link', { name: /Anfrage Familie Kraus/ }).click();
  await shot(page, 'bauakte-overview');
  for (const [section, file] of [['Dokumente', 'documents'], ['Fotos & Videos', 'photos'], ['Dokumentation', 'documentation'],
    ['Räume & Flächen', 'rooms'], ['Aufgaben & Mängel', 'work'], ['Material', 'material'], ['Einsätze', 'assignments'],
    ['Kontakte', 'contacts'], ['Notizen & Verlauf', 'notes']]) {
    await openSection(page, section).catch(() => undefined);
    await shot(page, `bauakte-${file}`);
  }
  for (const theme of ['Abendhorizont (dunkel)', 'Quartz hell', 'Quartz dunkel', 'Horizon Hoher Kontrast Schwarz']) {
    await page.getByLabel(/Benutzermenü/).click();
    await page.getByRole('menuitem', { name: 'Einstellungen' }).click();
    await page.getByRole('dialog', { name: 'Einstellungen' }).getByText(theme, { exact: true }).click();
    await page.waitForTimeout(1500);
    await shot(page, `theme-${theme.replace(/[^a-z]/gi, '').toLowerCase()}`);
    await page.getByRole('dialog', { name: 'Einstellungen' }).getByRole('button', { name: 'Schließen' }).click();
  }
  await page.getByLabel(/Benutzermenü/).click();
  await page.getByRole('menuitem', { name: 'Einstellungen' }).click();
  await page.getByRole('dialog', { name: 'Einstellungen' }).getByText('Systemeinstellung folgen').click();
});
