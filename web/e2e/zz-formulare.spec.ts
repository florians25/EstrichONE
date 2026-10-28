import { expect, test, type Locator, type Page } from '@playwright/test';
import { writeFileSync, mkdirSync } from 'node:fs';
import { adminPassword, formLayoutIssues, openSection, signIn } from './helpers';

// Durchsicht aller Fenster: öffnet Seiten, Reiter, Dialoge und Assistenten, prüft die Formular-Überschriften und
// legt Bildschirmfotos ab (FORMS_SWEEP=1, Breite über SWEEP_WIDTH, z. B. 1366 oder 1920). Nicht Teil der regulären Tests.
test.skip(!process.env['FORMS_SWEEP'], 'nur auf Anforderung');
test.use({ actionTimeout: 6000, navigationTimeout: 20000, trace: 'off' });

const width = Number(process.env['SWEEP_WIDTH'] ?? 1366);
const height = width >= 1900 ? 1080 : 768;
const dir = `test-results/formulare-${width}`;
const report: { window: string; issues: string[] }[] = [];

const open = /^(neu|neue|neuer|neues)\b|anlegen|hinzufügen|erfassen|bearbeiten|planen|einrichten|beantragen|eintragen|nachricht|gruppe|ticket|bestellen|urlaub|abwesenheit|krank|kontakt|zuordnen|termin|einladen|ändern|verschieben|kopieren|vorlage|textbaustein|ziel|kolonne|fahrzeug|gerät|rolle|benutzer|passwort|testmail|koppeln|wiederherstellen|anfrage|aufgabe|mangel|raum|notiz|messung|bericht|freigabe anfordern|einstellungen/i;
const avoid = /löschen|entfernen|stornieren|abmelden|zurücksetzen|aktualisieren|export|drucken|herunterladen|^anmelden|jetzt sichern|ausführen|alle als gelesen|schließen|abbrechen|filter|^neue bauakte$|sortieren|gruppieren|spalten|vollbild|weitere/i;

async function settle(page: Page) {
  await page.waitForLoadState('networkidle', { timeout: 4000 }).catch(() => undefined);
  await page.waitForTimeout(700);
}

const safe = (s: string) => s.replace(/[^a-z0-9äöüß]+/gi, '-').replace(/^-|-$/g, '').slice(0, 60);

async function check(page: Page, scope: Locator, window: string, screenshot = true) {
  const issues = await formLayoutIssues(scope).catch((e) => [`Prüfung fehlgeschlagen: ${String(e).slice(0, 100)}`]);
  report.push({ window, issues });
  writeFileSync(`${dir}/report-${safe(test.info().title)}.json`, JSON.stringify(report, null, 2));
  if (screenshot) await page.waitForTimeout(500).then(() => page.screenshot({ path: `${dir}/${safe(window)}.png` }));
}

const topDialog = (page: Page) => page.locator('.sapMDialog:visible').last();

async function closeDialogs(page: Page) {
  for (let i = 0; i < 4 && (await page.locator('.sapMDialog:visible').count()) > 0; i++) {
    const d = topDialog(page);
    const cancel = d.getByRole('button', { name: /^(Abbrechen|Schließen|Verwerfen|Nein)$/ }).filter({ visible: true }).last();
    if (await cancel.count()) await cancel.click({ timeout: 2000 }).catch(() => page.keyboard.press('Escape'));
    else await page.keyboard.press('Escape');
    await page.waitForTimeout(400);
  }
}

/** Assistenten: weitere Schritte über „Weiter“, soweit ohne Eingaben möglich. */
async function steps(page: Page, d: Locator, label: string) {
  for (let step = 2; step <= 6; step++) {
    const next = d.getByRole('button', { name: 'Weiter', exact: true }).filter({ visible: true });
    if (!(await next.count()) || !(await next.first().isEnabled())) break;
    await next.first().click().catch(() => undefined);
    await page.waitForTimeout(900);
    await check(page, d, `${label} › Schritt ${step}`);
  }
}

/** Dialog über eine Schaltfläche öffnen, prüfen (mit Assistentenschritten) und schließen. */
async function dialogVia(page: Page, trigger: Locator, label: string) {
  await trigger.click({ timeout: 5000 }).catch(() => undefined);
  await page.waitForTimeout(1200);
  if (!(await page.locator('.sapMDialog:visible').count())) { report.push({ window: `${label} (nicht geöffnet)`, issues: [] }); return; }
  const d = topDialog(page);
  await check(page, d, label);
  await steps(page, d, label);
  await closeDialogs(page);
}

/** Alle passenden Schaltflächen im Inhaltsbereich der Seite anklicken und geöffnete Dialoge prüfen. */
async function crawl(page: Page, url: string, label: string, scopeSelector = '.eoAppContent, .sapFDynamicPage, .sapUxAPObjectPageLayout') {
  const names: string[] = await page.locator(scopeSelector).filter({ visible: true }).first().evaluate((root) => {
    const out = new Set<string>();
    for (const b of Array.from(root.querySelectorAll('button, [role=button]'))) {
      const r = b.getBoundingClientRect();
      if (!r.width || !r.height) continue;
      const n = (b.getAttribute('aria-label') || b.textContent || b.getAttribute('title') || '').trim();
      if (n) out.add(n);
    }
    return [...out];
  }).catch(() => []);
  const picked = names.filter((n) => open.test(n) && !avoid.test(n)).slice(0, 14);
  for (const n of picked) {
    if (page.url() !== new URL(url, page.url()).href) { await page.goto(url); await settle(page); }
    const button = page.locator(scopeSelector).filter({ visible: true }).first().getByRole('button', { name: n, exact: true }).filter({ visible: true }).first();
    if (!(await button.count())) continue;
    await button.click({ timeout: 3000 }).catch(() => undefined);
    await page.waitForTimeout(1200);
    const menu = page.locator('.sapMMenu:visible, .sapUiMnu:visible, .sapMActionSheet:visible').first();
    if (await menu.count()) { await page.keyboard.press('Escape'); continue; }
    if (await page.locator('.sapMDialog:visible').count()) {
      const d = topDialog(page);
      const title = (await d.locator('.sapMDialogTitle, .sapMTitle').first().textContent().catch(() => '')) ?? '';
      await check(page, d, `${label} – ${n} – ${title.trim()}`);
      await steps(page, d, `${label} – ${n} – ${title.trim()}`);
      await closeDialogs(page);
    } else if (page.url() !== new URL(url, page.url()).href) {
      await settle(page);
      await check(page, page.locator('body'), `${label} – ${n} (Seite)`);
    }
  }
}

async function tabs(page: Page, label: string) {
  const list = await page.getByRole('tab').filter({ visible: true }).allTextContents();
  for (const name of list.map((t) => t.trim()).filter(Boolean).slice(0, 14)) {
    await page.getByRole('tab', { name, exact: true }).filter({ visible: true }).first().click({ timeout: 2000 }).catch(() => undefined);
    await settle(page);
    const forms = await page.locator('.sapUiForm:visible').count();
    await check(page, page.locator('body'), `${label} › ${name}`, forms > 0);
  }
}

test('Formular-Überschriften in allen Fenstern', async ({ page }) => {
  test.setTimeout(1_800_000);
  mkdirSync(dir, { recursive: true });
  await page.setViewportSize({ width, height });
  await signIn(page, 'admin', adminPassword);

  const routes: [string, string][] = [
    ['/start', 'Start'], ['/tagesuebersicht', 'Tagesübersicht'], ['/bauakten', 'Bauakten'], ['/dokumente', 'Dokumente'], ['/anfragen', 'Anfragen'],
    ['/anrufe', 'Anrufe'], ['/kunden', 'Kunden'], ['/bestellungen', 'Bestellungen'], ['/material', 'Material'], ['/lieferanten', 'Lieferanten'],
    ['/planung/cockpit', 'Cockpit'], ['/planung', 'Kolonnenplanung'], ['/planung/kalender', 'Terminkalender'], ['/planung/teamplaner', 'Teamplaner'],
    ['/planung/wer-faehrt-wohin', 'Wer fährt wohin'], ['/planung/arbeitsplan', 'Arbeitsplan'], ['/planung/kolonnen', 'Kolonnen'],
    ['/planung/geraete', 'Fahrzeuge und Geräte'], ['/personal', 'Personal'], ['/personal/urlaub', 'Urlaub'], ['/chat', 'Chat'],
    ['/verwaltung/nachrichten', 'Nachrichten'], ['/verwaltung/stammdaten', 'Stammdaten'], ['/verwaltung/rollen', 'Rollen'],
    ['/verwaltung/benutzer', 'Benutzer'], ['/it-center/geraete', 'Geräte'], ['/it-center/helpdesk', 'Helpdesk'],
    ['/system/sicherung', 'Backup Center'], ['/system/backup-ziele', 'Backup-Ziele'], ['/system/archiv', 'Archiv'], ['/system/e-mail', 'E-Mail'],
    ['/system/aktenarchiv', 'Aktenarchiv'], ['/system/ueberwachung', 'Überwachung'], ['/system/hinweise', 'Hinweise'],
    ['/system/firmendaten', 'Systemeinstellungen'], ['/konto/passwort', 'Passwort'],
  ];
  for (const [url, label] of routes) {
    await closeDialogs(page);
    await page.goto(url).catch(() => undefined);
    await settle(page);
    const forms = await page.locator('.sapUiForm:visible').count();
    await check(page, page.locator('body'), label, forms > 0);
    await tabs(page, label).catch(() => undefined);
    await page.goto(url).catch(() => undefined);
    await settle(page);
    await crawl(page, url, label).catch((e) => report.push({ window: `${label} (Abbruch)`, issues: [String(e).slice(0, 120)] }));
  }

  // Detailseiten: Kunde, Mitarbeiter, Bauakte mit allen Abschnitten.
  for (const [list, label] of [['/kunden', 'Kunde'], ['/personal', 'Mitarbeiter']] as const) {
    await page.goto(list);
    await settle(page);
    await page.locator('.sapMListTbl tbody tr.sapMLIB, .sapMList li.sapMLIB').filter({ visible: true }).first().click().catch(() => undefined);
    await settle(page);
    const url = page.url();
    await check(page, page.locator('body'), `${label} (Detail)`);
    await tabs(page, label);
    await crawl(page, url, label);
  }
  await page.goto('/bauakten');
  await settle(page);
  await page.locator('.sapMListTbl tbody tr.sapMLIB').filter({ visible: true }).first().click();
  await settle(page);
  const akte = page.url();
  for (const section of ['Übersicht', 'Dokumente', 'Fotos & Videos', 'Dokumentation', 'Räume & Flächen', 'Aufgaben & Mängel', 'Material', 'Einsätze', 'Kontakte', 'Notizen & Verlauf']) {
    await page.goto(akte);
    await settle(page);
    await openSection(page, section).catch(() => undefined);
    await settle(page);
    await check(page, page.locator('body'), `Bauakte › ${section}`);
    await crawl(page, page.url(), `Bauakte › ${section}`);
  }

  // Assistent „Neue Bauakte“: alle Schritte.
  await page.goto('/bauakten');
  await settle(page);
  await page.getByRole('button', { name: 'Neue Bauakte' }).first().click();
  const wizard = page.getByRole('dialog', { name: 'Neue Bauakte' });
  await expect(wizard).toBeVisible();
  await check(page, wizard, 'Neue Bauakte › Kunde');
  await wizard.getByLabel('Name des Kunden').fill('Durchsicht Kunde');
  await wizard.getByRole('button', { name: 'Weiter' }).click();
  await page.waitForTimeout(1200);
  await check(page, wizard, 'Neue Bauakte › Bauvorhaben');
  for (let i = 0; i < 4; i++) {
    await wizard.locator('.sapUiFormTitle').nth(i + 1).scrollIntoViewIfNeeded().catch(() => undefined);
    await page.waitForTimeout(300);
    await check(page, wizard, `Neue Bauakte › Bauvorhaben (gescrollt ${i + 1})`);
  }
  await wizard.getByRole('textbox', { name: 'Bauvorhaben' }).fill('Durchsicht');
  await wizard.getByRole('button', { name: 'Weiter' }).click();
  await page.waitForTimeout(1000);
  await check(page, wizard, 'Neue Bauakte › Prüfen');
  await closeDialogs(page);

  // Einstellungen (Benutzermenü) mit allen Reitern.
  await page.getByLabel(/Benutzermenü/).click();
  await page.getByRole('menuitem', { name: 'Einstellungen' }).click();
  const settings = page.getByRole('dialog', { name: 'Einstellungen' });
  await expect(settings).toBeVisible();
  await check(page, settings, 'Einstellungen');
  for (const name of (await settings.getByRole('tab').allTextContents()).map((t) => t.trim()).filter(Boolean)) {
    await settings.getByRole('tab', { name, exact: true }).first().click().catch(() => undefined);
    await page.waitForTimeout(800);
    await check(page, settings, `Einstellungen › ${name}`);
  }
  await closeDialogs(page);

  writeFileSync(`${dir}/report.json`, JSON.stringify(report, null, 2));
  const bad = report.filter((r) => r.issues.length);
  console.log(`Fenster geprüft: ${report.length}, mit Abweichungen: ${bad.length}`);
  for (const r of bad) console.log(`${r.window}:\n  ${r.issues.join('\n  ')}`);
});

test('Weitere Fenster: Aktionen der Bauakte, Erstbegehung, Helpdesk, Chat', async ({ page }) => {
  test.setTimeout(900_000);
  report.length = 0;
  mkdirSync(dir, { recursive: true });
  await page.setViewportSize({ width, height });
  await signIn(page, 'admin', adminPassword);
  await page.goto('/bauakten');
  await settle(page);
  await page.locator('.sapMListTbl tbody tr.sapMLIB').filter({ visible: true }).first().click();
  await settle(page);
  const akte = page.url();
  const menu = async () => {
    await page.getByRole('button', { name: 'Aktionen' }).filter({ visible: true }).first().click();
    await page.waitForTimeout(500);
  };
  await menu();
  const items = (await page.getByRole('menuitem').filter({ visible: true }).allTextContents()).map((t) => t.trim())
    .filter((t) => t && !/stornieren|löschen|abschließen|archiv|pdf|export|drucken|kopieren|duplizieren|teilen|link/i.test(t));
  await page.keyboard.press('Escape');
  for (const item of items) {
    await page.goto(akte);
    await settle(page);
    await menu();
    await dialogVia(page, page.getByRole('menuitem', { name: item, exact: true }).first(), `Bauakte › Aktionen › ${item}`);
  }
  await page.goto(akte);
  await settle(page);
  await openSection(page, 'Dokumentation').catch(() => undefined);
  for (const tab of [/Prüfungen/, /Abnahmen/]) {
    await page.getByRole('tab', { name: tab }).filter({ visible: true }).first().click().catch(() => undefined);
    await settle(page);
    await check(page, page.locator('body'), `Bauakte › Dokumentation › ${tab.source}`);
    await crawl(page, page.url(), `Bauakte › Dokumentation › ${tab.source}`);
  }
  await page.goto('/it-center/helpdesk');
  await settle(page);
  await dialogVia(page, page.getByRole('button', { name: /Neues Ticket/ }).filter({ visible: true }).first(), 'Helpdesk › Neues Ticket');
  await page.goto('/chat');
  await settle(page);
  for (const name of [/Neuer Einzelchat/, /Neue Gruppe|Gruppe anlegen|Gruppenchat/]) {
    await page.goto('/chat');
    await settle(page);
    await dialogVia(page, page.getByRole('button', { name }).filter({ visible: true }).first(), `Chat › ${name.source}`);
  }
  const bad = report.filter((r) => r.issues.length);
  console.log(`Fenster geprüft: ${report.length}, mit Abweichungen: ${bad.length}`);
  for (const r of bad) console.log(`${r.window}:\n  ${r.issues.join('\n  ')}`);
});
