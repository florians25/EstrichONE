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
  '/it-center/geraete', '/it-center/helpdesk', '/it-center/helpdesk-einstellungen', '/wissen', '/it-center/meldungen', '/meine-meldungen', '/system/sicherung', '/system/backup-ziele', '/system/archiv', '/system/auslagerung', '/system/e-mail',
  '/system/aktenarchiv', '/system/ueberwachung', '/system/hinweise', '/system/protokoll', '/konto/sicherheit',
  ...['firma', 'darstellung', 'pdf', 'bauakte', 'nummernkreise', 'dokumente', 'module', 'sitzungen', 'benachrichtigungen', 'speicher', 'system']
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

/**
 * Seitenkopf: linke und rechte Kante wie der Inhalt darunter, keine eigene Hintergrundfläche (gleicher Hintergrund wie
 * die Seite), Aktionen mit Luft zum rechten Rand, nichts rechts abgeschnitten; Hinweisleisten oben mit demselben Einzug.
 */
async function headerIssues(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const visible = (e: Element | null): e is HTMLElement => !!e && (e as HTMLElement).getBoundingClientRect().width > 0;
    const out: string[] = [];
    const op = [...document.querySelectorAll('.sapUxAPObjectPageLayout')].find(visible);
    const dp = [...document.querySelectorAll('.sapFDynamicPage')].find(visible);
    let title: HTMLElement | undefined;
    let left = 0;
    let right = 0;
    if (op) {
      title = [...op.querySelectorAll<HTMLElement>(':scope > .sapUxAPObjectPageHeaderTitle > .sapFDynamicPageTitle')].find(visible);
      const block = [...op.querySelectorAll<HTMLElement>('.sapUxAPBlockContainer')].find(visible);
      if (!block) return out;
      left = block.getBoundingClientRect().left;
      right = block.getBoundingClientRect().right;
    } else if (dp) {
      title = [...dp.querySelectorAll<HTMLElement>(':scope > .sapFDynamicPageTitleWrapper > .sapFDynamicPageTitle')].find(visible);
      const content = dp.querySelector<HTMLElement>(':scope > .sapFDynamicPageContentWrapper .sapFDynamicPageContent');
      if (!content) return out;
      const cs = getComputedStyle(content);
      const r = content.getBoundingClientRect();
      left = r.left + parseFloat(cs.paddingLeft);
      right = r.right - parseFloat(cs.paddingRight);
    }
    if (!title) return out;
    const t = title.getBoundingClientRect();
    const cs = getComputedStyle(title);
    if (Math.abs(t.left - left) > 2) out.push(`Kopf links bei ${Math.round(t.left)} px, Inhalt bei ${Math.round(left)} px`);
    // Klassische Bildlaufleiste: UI5 rückt den Titel um ihre Breite ein, der Inhalt ebenso.
    if (Math.abs(t.right - right) > 2 && Math.abs(t.right - right) > 18) out.push(`Kopf rechts bei ${Math.round(t.right)} px, Inhalt bei ${Math.round(right)} px`);
    if (t.right > window.innerWidth) out.push('Kopf rechts abgeschnitten');
    const page = [...document.querySelectorAll('.sapFDynamicPage, .sapUxAPObjectPageLayout')].find(visible)!;
    const pageBg = getComputedStyle(document.body).backgroundColor;
    const surfaces = [title, ...page.querySelectorAll<HTMLElement>('.sapFDynamicPageHeader, .sapUxAPObjectPageNavigation, .sapUxAPObjectPageNavigation .sapMITH')]
      .filter((e) => visible(e) && e.getBoundingClientRect().height > 0);
    for (const e of surfaces) {
      const bg = getComputedStyle(e).backgroundColor;
      if (bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent' && bg !== pageBg) out.push(`Kopf mit eigener Fläche (${e.className.split(' ')[0]}: ${bg})`);
    }
    const strip = [...document.querySelectorAll<HTMLElement>('.eoMain .eoTopNotices > .sapMMsgStrip')].find(visible);
    if (strip && dp && Math.abs(strip.getBoundingClientRect().left - t.left) > 2) {
      out.push(`Hinweisleiste links bei ${Math.round(strip.getBoundingClientRect().left)} px, Kopf bei ${Math.round(t.left)} px`);
    }
    if (t.top - (title.parentElement?.getBoundingClientRect().top ?? 0) < 4) out.push('Kopf klebt oben an der Kante');
    for (const a of title.querySelectorAll<HTMLElement>('.sapFDynamicPageTitleActionsBar, .sapFDynamicPageTitleTopRight')) {
      const r = a.getBoundingClientRect();
      if (r.width > 0 && r.right > t.right - 8) out.push(`Aktionen ohne Abstand zum rechten Rand (${Math.round(t.right - r.right)} px)`);
    }
    return out;
  });
}

const headerPages = ['/system/backup-ziele', '/system/archiv', '/kunden', '/it-center/helpdesk', '/system/firmendaten', '/bauakten'];
for (const [width, height, scale] of [[1920, 1080, 1], [1920, 1080, 1.25], [1280, 720, 1], [1280, 720, 1.5]] as const) {
  test(`Seitenkopf bündig und ohne eigene Fläche bei ${width} × ${height} mit ${scale * 100} %`, async ({ browser }) => {
    test.setTimeout(180_000);
    const { context, page } = await session(browser, Math.round(width / scale), Math.round(height / scale), scale);
    const found: string[] = [];
    const details = await detailPages(page);
    for (const path of [...headerPages, ...details]) {
      await page.goto(path);
      await settle(page);
      for (const issue of await headerIssues(page)) found.push(`${path}: ${issue}`);
    }
    await context.close();
    expect(found).toEqual([]);
  });
}

/**
 * Gescrollter Zustand: Der Inhalt läuft unter dem stehenden Seitenkopf hindurch, darf aber nirgends zwischen Kopfzeile
 * und Unterkante des Kopfs (samt Abstandsstreifen) durchscheinen – oben, an den Seiten, an den Ecken und darunter.
 * Geprüft mit elementFromPoint: Jeder Punkt muss zum Kopfbereich gehören (oder zur Bildlaufleiste des Inhalts).
 */
async function scrolledHeaderIssues(page: Page, px: number): Promise<string[] | null> {
  const ready = await page.evaluate((px) => {
    const visible = (e: Element | null): e is HTMLElement => !!e && (e as HTMLElement).getBoundingClientRect().width > 0;
    const scroller = [...document.querySelectorAll<HTMLElement>('.sapUxAPObjectPageWrapper, .sapFDynamicPage > .sapFDynamicPageContentWrapper')].find(visible);
    if (!scroller || scroller.scrollHeight - scroller.clientHeight < 40) return false;
    scroller.scrollTop = Math.min(px, scroller.scrollHeight - scroller.clientHeight);
    scroller.dispatchEvent(new Event('scroll'));
    return true;
  }, px);
  if (!ready) return null;
  await page.waitForTimeout(700);
  return page.evaluate(() => {
    const visible = (e: Element | null): e is HTMLElement => !!e && (e as HTMLElement).getBoundingClientRect().width > 0;
    const out: string[] = [];
    const scroller = [...document.querySelectorAll<HTMLElement>('.sapUxAPObjectPageWrapper, .sapFDynamicPage > .sapFDynamicPageContentWrapper')].find(visible)!;
    const head = [...document.querySelectorAll<HTMLElement>('.sapUxAPObjectPageLayout > .sapUxAPObjectPageHeaderTitle, .sapFDynamicPage > .sapFDynamicPageTitleWrapper')]
      .find(visible);
    const title = head?.querySelector<HTMLElement>(':scope > .sapFDynamicPageTitle');
    if (!head || !title) return out;
    // Nur wenn der Inhalt tatsächlich unter dem Kopf liegt (bei aufgeklapptem Kopfbereich im Inhalt setzt dieser den Kopf fort).
    const content = scroller.querySelector<HTMLElement>('.sapUxAPObjectPageContainer, .sapFDynamicPageContent');
    const h = head.getBoundingClientRect();
    if (!content || content.getBoundingClientRect().top >= h.bottom) return out;
    const opaque = (c: string) => c.startsWith('rgb(') || (c.startsWith('rgba(') && parseFloat(c.split(',')[3]) >= 0.99);
    const after = getComputedStyle(head, '::after');
    const gap = after.content !== 'none' && after.position === 'absolute' ? parseFloat(after.height) || 0 : 0;
    const t = title.getBoundingClientRect();
    const xs = [h.left + 2, (h.left + t.left) / 2, t.left + 2, t.left + 6, (t.left + t.right) / 2, t.right - 6, t.right - 2, (t.right + h.right) / 2];
    const ys = [h.top + 1, (h.top + t.top) / 2, t.top + 2, t.top + 6, t.bottom - 6, t.bottom - 2, h.bottom - 2];
    if (gap >= 2) ys.push(h.bottom + gap / 2, h.bottom + gap - 1);
    for (const y of ys) {
      for (const x of xs) {
        if (x >= h.right - 1) continue;
        const el = document.elementFromPoint(x, y);
        if (!el || el === scroller) continue;
        if (head.contains(el)) {
          // Der Kopf muss an dieser Stelle deckend gemalt sein (eigene Fläche oder die eines Elternteils im Kopf).
          if (y >= h.bottom) { if (opaque(after.backgroundColor)) continue; } else {
            let e: Element | null = el;
            while (e && e !== head.parentElement && !opaque(getComputedStyle(e).backgroundColor)) e = e.parentElement;
            if (e && e !== head.parentElement) continue;
          }
          out.push(`Inhalt scheint bei ${Math.round(x)}/${Math.round(y)} durch den durchsichtigen Kopf`);
          continue;
        }
        out.push(`Inhalt scheint bei ${Math.round(x)}/${Math.round(y)} durch (${(el.className || el.tagName).toString().split(' ')[0]})`);
      }
    }
    return [...new Set(out)].slice(0, 6);
  });
}

for (const [width, height, scale, scheme] of [[1920, 1080, 1, 'dark'], [1280, 720, 1, 'light'], [1280, 720, 1.25, 'dark'], [1280, 720, 1.5, 'light']] as const) {
  test(`Gescrollter Seitenkopf deckt den Inhalt ab bei ${width} × ${height} mit ${scale * 100} % (${scheme === 'dark' ? 'dunkel' : 'hell'})`, async ({ browser }) => {
    test.setTimeout(180_000);
    const { context, page } = await session(browser, Math.round(width / scale), Math.round(height / scale), scale);
    await page.emulateMedia({ colorScheme: scheme });
    const found: string[] = [];
    let checked = 0;
    const details = await detailPages(page);
    for (const path of ['/start', ...headerPages, ...details]) {
      for (const px of [60, 600]) {
        await page.goto(path);
        await settle(page);
        const issues = await scrolledHeaderIssues(page, px);
        if (issues === null) break;
        checked++;
        for (const issue of issues) found.push(`${path} (${px} px): ${issue}`);
      }
    }
    await eachSection(page, details, async (name) => {
      const issues = await scrolledHeaderIssues(page, 600);
      if (issues) checked++;
      for (const issue of issues ?? []) found.push(`Bauakte › ${name}: ${issue}`);
    });
    await context.close();
    expect(found).toEqual([]);
    expect(checked).toBeGreaterThan(0);
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
