import { expect, test, type Browser, type Page } from '@playwright/test';
import { mkdirSync, writeFileSync } from 'node:fs';
import { adminPassword, layoutIssues, openSection, signIn } from './helpers';

// Layout auf typischen Büro-Bildschirmen: Windows-Laptops mit 1920 px und 125 %/150 % Skalierung haben effektiv nur
// 1536/1280 px, 1280er-Fenster bei 125 %/150 % nur 1024/853 px. Keine Seite darf dort waagerecht überlaufen, Inhalte
// abschneiden oder Texte überlagern (Prüfung je Seite mit layoutIssues).

/** Hauptseiten (Pfade); Detailseiten mit ID werden zur Laufzeit ergänzt. */
const pages = [
  '/start', '/tagesuebersicht', '/bauakten', '/bauakten/karte', '/dokumente', '/dokumente?reiter=freigaben', '/benachrichtigungen', '/chat',
  '/anrufe', '/anfragen', '/kunden', '/bestellungen', '/material', '/lieferanten',
  '/planung/cockpit', '/planung', '/planung/kalender', '/planung/teamplaner', '/planung/arbeitsplan', '/planung/kolonnen', '/planung/geraete',
  '/personal', '/personal/urlaub', '/verwaltung', '/verwaltung/stammdaten', '/verwaltung/rollen', '/verwaltung/benutzer', '/verwaltung/nachrichten',
  '/it-center/geraete', '/it-center/helpdesk', '/system/sicherung', '/system/backup-ziele', '/system/archiv', '/system/auslagerung', '/system/e-mail',
  '/system/aktenarchiv', '/system/ueberwachung', '/system/hinweise', '/system/protokoll', '/konto/sicherheit',
  ...['firma', 'darstellung', 'pdf', 'bauakte', 'dokumente', 'module', 'sitzungen', 'benachrichtigungen', 'speicher', 'system']
    .map((b) => `/system/firmendaten${b === 'firma' ? '' : `?bereich=${b}`}`),
];

async function settle(page: Page) {
  await page.waitForLoadState('networkidle', { timeout: 5000 }).catch(() => undefined);
  await page.locator('.sapUiLocalBusyIndicator:visible, .sapMBusyIndicator:visible').first().waitFor({ state: 'hidden', timeout: 5000 }).catch(() => undefined);
  await page.waitForTimeout(600);
}

/** Detailseiten mit echter ID (Bauakte, Kunde, Mitarbeiter), soweit Daten vorhanden. */
async function detailPages(page: Page): Promise<string[]> {
  return page.evaluate(async () => {
    const get = async (p: string) => (await fetch(`/api/v1/${p}`)).json().catch(() => null);
    const out: string[] = [];
    const akten = await get('bauakten?limit=1');
    if (akten?.items?.[0]?.id) out.push(`/bauakten/${akten.items[0].id}`);
    const kunden = await get('contacts?limit=1');
    if (kunden?.[0]?.id) out.push(`/kunden/${kunden[0].id}`);
    const staff = await get('staff/employees');
    if (staff?.[0]?.id) out.push(`/personal/${staff[0].id}`);
    return out;
  });
}

/** Abschnitte der Bauakte (Reiter der Objektseite), jeweils mit Prüfung; liefert Befunde je Abschnitt. */
const sections = ['Dokumente', 'Fotos & Videos', 'Dokumentation', 'Räume & Flächen', 'Aufgaben & Mängel', 'Material', 'Einsätze', 'Kontakte', 'Notizen & Verlauf'];
async function eachSection(page: Page, details: string[], visit: (name: string) => Promise<void>) {
  const akte = details.find((p) => p.startsWith('/bauakten/'));
  if (!akte) return;
  await page.goto(akte);
  await settle(page);
  for (const section of sections) {
    if (!(await openSection(page, section).then(() => true, () => false))) continue;
    await settle(page);
    await visit(section);
  }
}

async function session(browser: Browser, width: number, height: number, scale: number) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: scale, locale: 'de-DE', timezoneId: 'Europe/Berlin' });
  const page = await context.newPage();
  await signIn(page, 'admin', adminPassword);
  return { context, page };
}

// Regulärer Test: 1280 × 720 bei 125 % und 150 % (CSS-Fenster 1024 × 576 bzw. 853 × 480).
for (const scale of [1.25, 1.5]) {
  test(`Hauptseiten ohne Überlauf und Überlagerung bei 1280 × 720 mit ${scale * 100} %`, async ({ browser }) => {
    test.setTimeout(300_000);
    const { context, page } = await session(browser, Math.round(1280 / scale), Math.round(720 / scale), scale);
    const found: string[] = [];
    const details = await detailPages(page);
    for (const path of [...pages, ...details]) {
      await page.goto(path);
      await settle(page);
      for (const issue of await layoutIssues(page)) found.push(`${path}: ${issue}`);
    }
    await eachSection(page, details, async (name) => { for (const issue of await layoutIssues(page)) found.push(`Bauakte › ${name}: ${issue}`); });
    await context.close();
    expect(found).toEqual([]);
  });
}

// Durchsicht mit Bildschirmfotos über alle Breiten und Zoomstufen (LAYOUT_SWEEP=1), nicht Teil der regulären Tests.
const sweep: [string, number, number, number][] = [];
for (const [w, h] of [[1280, 720], [1366, 768], [1440, 900], [1536, 864], [1920, 1080]]) {
  for (const z of [1, 1.25, 1.5]) sweep.push([`${w}-${Math.round(z * 100)}`, Math.round(w / z), Math.round(h / z), z]);
}
sweep.push(['ipad-1024', 1024, 768, 2], ['ipad-820', 820, 1180, 2]);

test.describe('Durchsicht', () => {
  test.skip(!process.env['LAYOUT_SWEEP'], 'nur auf Anforderung');
  const only = (process.env['LAYOUT_SWEEP_ONLY'] ?? '').split(',').filter(Boolean);
  for (const [name, width, height, scale] of sweep) {
    if (only.length && !only.includes(name)) continue;
    test(`Layout ${name}`, async ({ browser }) => {
      test.setTimeout(600_000);
      const dir = `${process.env['LAYOUT_DIR'] ?? 'test-results/layout'}/${name}`;
      mkdirSync(dir, { recursive: true });
      // Bildschirmfotos in 1:1 (kleinere Dateien); die Skalierung ändert das CSS-Layout nicht.
      const { context, page } = await session(browser, width, height, process.env['LAYOUT_HIDPI'] ? scale : 1);
      const report: Record<string, string[]> = {};
      const details = await detailPages(page);
      for (const path of [...pages, ...details]) {
        await page.goto(path);
        await settle(page);
        report[path] = await layoutIssues(page);
        const file = path.replace(/^\//, '').replace(/[^a-z0-9]+/gi, '-') || 'root';
        await page.screenshot({ path: `${dir}/${file}.png` });
      }
      await eachSection(page, details, async (name) => {
        report[`Bauakte › ${name}`] = await layoutIssues(page);
        await page.screenshot({ path: `${dir}/bauakte-${name.replace(/[^a-z0-9äöü]+/gi, '-')}.png` });
      });
      writeFileSync(`${dir}/report.json`, JSON.stringify(report, null, 2));
      await context.close();
    });
  }
});
