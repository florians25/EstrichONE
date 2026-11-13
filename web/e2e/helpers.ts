import { expect, type Locator, type Page } from '@playwright/test';

export const adminPassword = 'Estrich-Chefin-2026!';
export const setupCode = process.env['E2E_SETUP_CODE'] ?? '';

/** Kleines gültiges JPEG (1×1, grau) für Foto-Uploads. */
export const jpeg = Buffer.from(
  '/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////' +
    '2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFAAB' +
    'AAAAAAAAAAAAAAAAAAAAA//EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAUAQEAAAAAAAAAAAAAAAAAAAAA/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMR' +
    'AD8AN//Z',
  'base64',
);
export const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');
/** Kleinste „MP4“-Datei (nur ftyp-Box, Marke isom): genügt der Typerkennung des Servers für Video-Kacheln. */
export const mp4 = Buffer.concat([Buffer.from([0, 0, 0, 24]), Buffer.from('ftypisom'), Buffer.from([0, 0, 2, 0]), Buffer.from('isommp41')]);

/** Eintrag der Seitennavigation (sap.tnt.SideNavigation). */
export const nav = (page: Page, name: string) => page.locator('.sapTntSideNavigation').getByText(name, { exact: true });

/** Seitentitel (DynamicPage, Objektseite). */
export const pageTitle = (page: Page) => page.locator('.sapFDynamicPageTitle .sapMTitle, .sapUxAPObjectPageHeaderTitle .sapMTitle').first();

export async function signIn(page: Page, login: string, password: string) {
  await page.goto('/anmelden');
  const user = page.locator('input[id$="loginUser-inner"]');
  const secret = page.locator('input[id$="loginPassword-inner"]');
  // Viele Anmeldungen kurz hintereinander (mehrere Testdateien) können die Begrenzung je Adresse erreichen (429):
  // dann die Meldung ausgeben, kurz warten und erneut anmelden statt bis zum Testende zu hängen.
  for (let attempt = 1; ; attempt++) {
    // Die Seite setzt beim Öffnen Fokus und Felder zurück: erst danach füllen und prüfen.
    await expect(async () => {
      await user.fill(login);
      await secret.fill(password);
      await expect(user).toHaveValue(login, { timeout: 500 });
      await expect(secret).toHaveValue(password, { timeout: 500 });
    }).toPass({ timeout: 15_000 });
    await page.getByRole('button', { name: 'Anmelden' }).click();
    const done = await page.waitForURL((url) => !url.pathname.startsWith('/anmelden'), { timeout: 20_000 }).then(() => true, () => false);
    if (done) return;
    const hint = await page.locator('.sapMMsgStrip:visible, .eoToast').allTextContents().catch(() => []);
    if (attempt >= 4) throw new Error(`Anmeldung als ${login} nicht möglich: ${hint.join(' | ') || 'keine Meldung'}`);
    console.log(`Anmeldung als ${login} (Versuch ${attempt}) blieb auf der Anmeldeseite: ${hint.join(' | ') || 'keine Meldung'} – neuer Versuch`);
    await page.waitForTimeout(25_000);
    await page.goto('/anmelden');
  }
}

/** UI5-Auswahl (Select, ComboBox, MultiComboBox) öffnen und eine Option wählen. */
export async function choose(page: Page, field: Locator, option: string | RegExp, type?: string) {
  const item = page.locator('.sapMSelectList li, .sapMPopover li[role=option], .sapMDialog li[role=option], .sapMComboBoxBasePicker li, .sapMSuggestionsPopover li')
    .filter({ hasText: option }).filter({ visible: true }).first();
  await expect(async () => {
    if (!(await item.isVisible())) {
      // sap.m.Select: das Label zeigt auf ein verstecktes Element – dann das sichtbare Feld (Elternelement) anklicken.
      const first = field.first();
      const hidden = /HiddenSelect/.test((await first.getAttribute('class')) ?? '');
      const target = hidden ? first.locator('..') : first;
      await target.click({ force: true });
      if (type) {
        await target.fill(type);
      }
      // MultiComboBox öffnet die Liste nicht per Klick ins Eingabefeld – dann per F4.
      const multi = await target.evaluate((e) => !!e.closest('.sapMMultiComboBox')).catch(() => false);
      if (multi) {
        await page.waitForTimeout(300);
        if (!(await item.isVisible())) {
          await target.press('F4').catch(() => undefined);
        }
      }
    }
    await item.click({ timeout: 2000 });
  }).toPass({ timeout: 20_000 });
  // Auswahlliste erst schließen lassen – sonst trifft der nächste Klick noch die ausblendende Liste.
  await item.waitFor({ state: 'hidden', timeout: 3000 }).catch(() => undefined);
}

/** Abschnitt der Bauakte öffnen (Reiter, ggf. aus dem Überlauf „+n“). */
export async function openSection(page: Page, name: string) {
  const tab = page.getByRole('tab', { name, exact: true }).filter({ visible: true }).first();
  await expect(async () => {
    if (await tab.isVisible()) {
      await tab.click({ timeout: 2000 });
    } else {
      await page.getByRole('button', { name: /^\+\d+$/ }).filter({ visible: true }).first().click({ timeout: 2000 });
      await page.locator('.sapMITBSelectList li, .sapMPopover li, [role=menuitem], [role=option]').filter({ hasText: name }).filter({ visible: true })
        .first().click({ timeout: 2000 });
    }
    await expect(page.getByRole('tab', { name, exact: true }).filter({ visible: true }).first())
      .toHaveAttribute('aria-selected', 'true', { timeout: 1500 });
  }).toPass({ timeout: 20_000 });
}

/** Meldung (MessageToast) prüfen. */
export const toast = (page: Page) => page.locator('.eoToast .eoToastText').last();

/** API-Aufruf aus der Browser-Sitzung (mit CSRF), z. B. für App-Daten über den Sync. */
export async function apiCall<T>(page: Page, method: string, path: string, body?: unknown): Promise<{ status: number; json: T }> {
  return page.evaluate(async ([m, p, b]) => {
    const xsrf = decodeURIComponent(document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='))?.split('=')[1] ?? '');
    const response = await fetch(p as string, {
      method: m as string, headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': xsrf }, body: b === undefined ? undefined : JSON.stringify(b),
    });
    const text = await response.text();
    return { status: response.status, json: text ? JSON.parse(text) : null };
  }, [method, path, body] as const) as Promise<{ status: number; json: T }>;
}

/** Hochladen über den Dialog („Hochladen“ in der Werkzeugleiste → Dateien wählen → „Hochladen“). */
export async function uploadViaDialog(page: Page, files: Parameters<Locator['setInputFiles']>[0], prepare?: (dialog: Locator) => Promise<void>) {
  await page.getByRole('button', { name: 'Hochladen', exact: true }).filter({ visible: true }).first().click();
  const dialog = page.getByRole('dialog', { name: /hochladen/ });
  await expect(dialog).toBeVisible();
  if (prepare) await prepare(dialog);
  await dialog.locator('input.eoFileInput').setInputFiles(files);
  await dialog.getByRole('button', { name: 'Hochladen', exact: true }).click();
  return dialog;
}

/** Menü „Weitere Aktionen“ einer Zeile öffnen und Eintrag wählen (robust gegen Neuaufbau der Liste nach dem Laden). */
export async function rowMenu(page: Page, row: Locator, item: string) {
  const entry = page.getByRole('menuitem', { name: item });
  await expect(async () => {
    if (!(await entry.isVisible())) {
      await row.getByRole('button', { name: 'Weitere Aktionen' }).click();
      await page.waitForTimeout(400);
    }
    await entry.click({ timeout: 2500 });
  }).toPass({ timeout: 30_000 });
}

/**
 * Layout-Prüfung aller sichtbaren Formulare (SimpleForm) unterhalb von `scope`: Abschnittsüberschriften stehen
 * linksbündig über ihrem ersten Feld (Bounding-Box-Vergleich), nicht als Feld im Raster, in bearbeitbaren
 * Formularen nicht nebeneinander und die erste Überschrift nicht abgeschnitten. Liefert die gefundenen Abweichungen.
 */
export async function formLayoutIssues(scope: Locator): Promise<string[]> {
  return scope.evaluate((root) => {
    const issues: string[] = [];
    const box = (e: Element) => e.getBoundingClientRect();
    const shown = (e: Element) => { const r = box(e); return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden'; };
    const name = (e: Element) => `„${(e.textContent ?? '').trim().slice(0, 40)}“`;
    for (const form of Array.from(root.querySelectorAll('.sapUiForm'))) {
      if (!shown(form)) continue;
      const own = (e: Element) => e.closest('.sapUiForm') === form;
      for (const t of Array.from(form.querySelectorAll('.sapMTitle:not(.sapUiFormTitle)'))) {
        const inner = t.closest('.sapMTB, .sapMIBar, .sapMPanelHdr, .sapUiFormTitle, .sapMList, .sapMFlexBox');
        if (!own(t) || !shown(t) || (inner && form.contains(inner))) continue;
        issues.push(`Überschrift ${name(t)} steht als Feld im Formular statt als Abschnittstitel`);
      }
      const titles = Array.from(form.querySelectorAll('.sapUiFormTitle')).filter((t) => own(t) && shown(t));
      for (const t of titles) {
        const container = t.parentElement!;
        const fields = Array.from(container.querySelectorAll('.sapUiFormElementLbl, .sapMLabel, .sapMInputBase, .sapMSlt, .sapMCb, .sapMSwt, .sapMSegB, .sapMText'))
          .filter((e) => !t.contains(e) && !e.contains(t) && shown(e) && e.closest('.sapUiForm') === form);
        if (!fields.length) continue;
        const tr = box(t);
        const top = Math.min(...fields.map((f) => box(f).top));
        const left = Math.min(...fields.map((f) => box(f).left));
        if (tr.bottom > top + 1) issues.push(`Überschrift ${name(t)} steht nicht über ihrem Abschnitt (unten ${Math.round(tr.bottom)} > erstes Feld ${Math.round(top)})`);
        if (tr.left > left + 24) issues.push(`Überschrift ${name(t)} nicht linksbündig (links ${Math.round(tr.left)}, Felder ab ${Math.round(left)})`);
        const scroller = t.closest('.sapMDialogScroll, .sapMWizardStepContainer, .sapFDynamicPageContentWrapper, .sapMPageEnableScrolling');
        if (scroller && scroller.scrollTop === 0 && tr.top < box(scroller).top - 1) issues.push(`Überschrift ${name(t)} oben abgeschnitten`);
      }
      if (form.classList.contains('sapUiFormEdit')) {
        for (let i = 0; i < titles.length; i++) {
          for (let j = i + 1; j < titles.length; j++) {
            const a = box(titles[i]); const b = box(titles[j]);
            if (Math.abs(a.top - b.top) < 4) issues.push(`Überschriften ${name(titles[i])} und ${name(titles[j])} stehen nebeneinander`);
          }
        }
      }
    }
    return issues;
  });
}

/**
 * Layout-Prüfung der ganzen Seite (oder eines Bereichs): findet
 * - horizontalen Überlauf des Dokuments und von Bildlaufbereichen (außer ausdrücklich scrollbaren Tafeln/Tabellen),
 * - Inhalte, die rechts/links/unten aus ihrem Container (overflow hidden/auto) oder aus dem Fenster laufen
 *   und dabei nicht absichtlich mit „…“ gekürzt sind,
 * - sich überlagernde Texte zweier verschiedener Elemente.
 * Liefert lesbare Befunde (leer = in Ordnung).
 */
export async function layoutIssues(page: Page, scopeSelector = 'body'): Promise<string[]> {
  return page.evaluate((sel) => {
    const issues = new Set<string>();
    const root = document.querySelector(sel) ?? document.body;
    const vw = document.documentElement.clientWidth;
    // Bereiche, die absichtlich waagerecht scrollen (Plantafeln, Kalender, breite Tabellen mit eigenem Bildlauf, Reiterleisten).
    const scrollOk = '.eoScrollX, .eoPlanBoard, .eoCkScroll, .sapMITBScrollContainer, .sapMITH, .sapMITBHead, .sapUiCalendarRowApps, .sapMSinglePCRow, .sapMPlanCal, .sapUiCalendarRowVisualContainer, '
      + '.sapMSinglePlanningCalendar, .sapMSPCMonthDays, .sapUiTableCnt, .sapUiTableCCnt, .sapMListTblCnt.eoWide, .leaflet-container, .sapMTokenizer, .sapMTokenizerScrollContainer, .sapMSegB, '
      + '.sapMCrsl, .sapMHdrCntrCntr, .sapMSlt, .sapMInputBaseContentWrapper, .sapMPopover, .sapMDialog, .sapMIBar, .sapMTB, .sapUiInvisibleText';
    const label = (e: Element) => {
      const id = (e.id || '').replace(/^__[a-z]+\d+-?/i, '').replace(/^.*--/, '');
      const text = (e.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 50);
      return `${e.tagName.toLowerCase()}${id ? '#' + id : ''}${e.classList[0] ? '.' + e.classList[0] : ''} „${text}“`;
    };
    const visible = (e: Element) => {
      if (!(e instanceof HTMLElement || e instanceof SVGElement)) return false;
      const r = e.getBoundingClientRect();
      if (r.width < 1 || r.height < 1) return false;
      for (let p: Element | null = e; p; p = p.parentElement) {
        const s = getComputedStyle(p);
        if (s.visibility === 'hidden' || s.display === 'none' || s.opacity === '0') return false;
        if (p.classList.contains('sapUiInvisibleText') || p.classList.contains('sapUiPseudoInvisibleText') || p.classList.contains('sapUiHidden')) return false;
        if (p.getAttribute('aria-hidden') === 'true' && p.classList.contains('sapUiHiddenPlaceholder')) return false;
      }
      return true;
    };
    const se = document.scrollingElement as HTMLElement;
    if (se.scrollWidth > se.clientWidth + 1) issues.add(`Seite läuft waagerecht über (${se.scrollWidth} px Inhalt bei ${se.clientWidth} px Fenster)`);
    const candidates = Array.from(root.querySelectorAll('*')).filter((e) => {
      if (e.closest('.sapMPopover, .sapMDialog, .sapUiBLy, .eoToasts, .sapMSelectList, .sapUiInvisibleText') && !root.closest('.sapMDialog')) return false;
      return true;
    });
    const clipOf = (e: Element): Element | null => {
      for (let p = e.parentElement; p && p !== document.documentElement; p = p.parentElement) {
        const s = getComputedStyle(p);
        if (s.overflowX !== 'visible' || s.overflowY !== 'visible') return p;
      }
      return null;
    };
    const ellipsized = (e: Element, stop: Element | null) => {
      for (let p: Element | null = e; p && p !== stop; p = p.parentElement) {
        const s = getComputedStyle(p);
        if (s.textOverflow === 'ellipsis' || s.webkitLineClamp !== 'none' && s.webkitLineClamp !== '') return true;
      }
      return false;
    };
    // 1) Bildlaufbereiche mit waagerechtem Überlauf
    for (const e of candidates) {
      if (!(e instanceof HTMLElement)) continue;
      const s = getComputedStyle(e);
      if ((s.overflowX === 'auto' || s.overflowX === 'scroll') && e.scrollWidth > e.clientWidth + 2 && e.clientWidth > 0 && visible(e) && !e.closest(scrollOk)) {
        issues.add(`Waagerechter Bildlauf in ${label(e)} (${e.scrollWidth} > ${e.clientWidth} px)`);
      }
    }
    // 2) Text, Felder und Schaltflächen außerhalb ihres Containers oder des Fensters
    const leaves = candidates.filter((e) => {
      if (e.matches('input, textarea, .sapMBtn, .sapMSwt, .sapMSlt, .sapMInputBase, .sapMStepInput, .sapMCb, .sapMSegB, img')) return true;
      return Array.from(e.childNodes).some((n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim().length > 1);
    }).filter(visible);
    const textRect = (e: Element) => {
      const nodes = Array.from(e.childNodes).filter((n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim());
      if (!nodes.length) return e.getBoundingClientRect();
      const range = document.createRange();
      range.setStartBefore(nodes[0]);
      range.setEndAfter(nodes[nodes.length - 1]);
      return range.getBoundingClientRect();
    };
    for (const e of leaves) {
      const r = textRect(e);
      if (r.width < 1) continue;
      if (e.closest(scrollOk)) continue;
      if (r.right > vw + 2 && !ellipsized(e, null)) { issues.add(`Rechts aus dem Fenster: ${label(e)} (bis ${Math.round(r.right)} px bei ${vw} px)`); continue; }
      const clip = clipOf(e);
      if (!clip || clip === document.body) continue;
      const c = clip.getBoundingClientRect();
      if (ellipsized(e, clip)) continue;
      const cs = getComputedStyle(clip);
      const scrollsX = cs.overflowX === 'auto' || cs.overflowX === 'scroll';
      const scrollsY = cs.overflowY === 'auto' || cs.overflowY === 'scroll';
      if (!scrollsX && (r.right > c.right + 2 || r.left < c.left - 2) && r.left < c.right && r.right > c.left) issues.add(`Abgeschnitten (waagerecht): ${label(e)} in ${label(clip).slice(0, 60)}`);
      if (!scrollsY && r.bottom > c.bottom + 2 && r.top < c.bottom - 2 && r.height < 200) issues.add(`Abgeschnitten (unten): ${label(e)} in ${label(clip).slice(0, 60)}`);
    }
    // 2b) Wörter mitten im Wort umbrochen (zu schmale Spalten, z. B. „Nam|e“): mehr Zeilen als Wortteile.
    for (const e of leaves) {
      const nodes = Array.from(e.childNodes).filter((n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim().length > 3);
      for (const n of nodes) {
        const text = (n.textContent ?? '').trim();
        const parts = text.split(/[\s\-_./·,:;()]+/).filter(Boolean).length;
        const range = document.createRange();
        range.selectNodeContents(n);
        const lines = new Set(Array.from(range.getClientRects()).filter((r) => r.width > 0).map((r) => Math.round(r.top))).size;
        // Lange Einzelwörter ohne Leerzeichen (Kennungen, Dateinamen, Adressen) dürfen in schmalen Zellen umbrechen.
        const longToken = text.split(/\s+/).some((w) => w.length > 14);
        if (lines > parts && !longToken && !e.closest(scrollOk) && getComputedStyle(e).hyphens !== 'auto') issues.add(`Mitten im Wort umbrochen (${lines} Zeilen): ${label(e)}`);
      }
    }
    // 3) Überlagerte Texte (zwei verschiedene Elemente, Textflächen schneiden sich deutlich)
    // Sichtbarer Teil: Textfläche geschnitten mit allen Bildlauf-/Clip-Containern (weggescrollte Inhalte zählen nicht).
    const clipped = (e: Element, r: DOMRect) => {
      let [l, t, rr, b] = [r.left, r.top, r.right, r.bottom];
      for (let p: Element | null = e; p && p !== document.documentElement; p = p.parentElement) {
        const s = getComputedStyle(p);
        if (s.overflowX === 'visible' && s.overflowY === 'visible') continue;
        const c = p.getBoundingClientRect();
        l = Math.max(l, c.left); t = Math.max(t, c.top); rr = Math.min(rr, c.right); b = Math.min(b, c.bottom);
      }
      return new DOMRect(l, t, Math.max(0, rr - l), Math.max(0, b - t));
    };
    // Je Zeile ein Rechteck (mehrzeiliger Fließtext mit Link dahinter überlagert sich nicht, nur weil sich die
    // umschließenden Rechtecke schneiden).
    const lineRects = (e: Element) => {
      const nodes = Array.from(e.childNodes).filter((n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim());
      const rects: DOMRect[] = [];
      for (const n of nodes) { const range = document.createRange(); range.selectNodeContents(n); rects.push(...Array.from(range.getClientRects())); }
      return rects.map((r) => clipped(e, r)).filter((r) => r.width > 2 && r.height > 2);
    };
    const pieces = leaves.filter((e) => Array.from(e.childNodes).some((n) => n.nodeType === Node.TEXT_NODE && (n.textContent ?? '').trim().length > 1))
      .flatMap((e) => lineRects(e).map((r) => ({ e, r })));
    const texts = pieces;
    for (let i = 0; i < texts.length; i++) {
      for (let j = i + 1; j < texts.length; j++) {
        const a = texts[i]; const b = texts[j];
        if (a.e === b.e || a.e.contains(b.e) || b.e.contains(a.e)) continue;
        const w = Math.min(a.r.right, b.r.right) - Math.max(a.r.left, b.r.left);
        const h = Math.min(a.r.bottom, b.r.bottom) - Math.max(a.r.top, b.r.top);
        if (w > 3 && h > Math.min(a.r.height, b.r.height) * 0.4) {
          // Nur wenn beide an dieser Stelle wirklich sichtbar sind (nicht verdeckt durch Kopfzeilen o. Ä.).
          const x = Math.max(a.r.left, b.r.left) + w / 2; const y = Math.max(a.r.top, b.r.top) + h / 2;
          const hit = document.elementFromPoint(x, y);
          if (!hit || (!a.e.contains(hit) && !b.e.contains(hit) && !hit.contains(a.e) && !hit.contains(b.e))) continue;
          // Verdeckt statt überlagert: Das obere Element (z. B. fester Seitentitel) hat einen deckenden Hintergrund
          // zwischen sich und dem gemeinsamen Vorfahren – der untere Text ist dann nicht sichtbar (gewolltes Darunterscrollen).
          const under = a.e.contains(hit) || hit.contains(a.e) ? b.e : a.e;
          let covered = false;
          for (let p: Element | null = hit; p && !p.contains(under); p = p.parentElement) {
            const bg = getComputedStyle(p).backgroundColor;
            if (bg && bg !== 'transparent' && !/rgba\(.*,\s*0\)$/.test(bg)) { covered = true; break; }
          }
          if (covered) continue;
          issues.add(`Texte überlagern sich: ${label(a.e)} und ${label(b.e)}`);
        }
      }
    }
    return Array.from(issues);
  }, scopeSelector);
}

/**
 * Am Wochenende zeigen Kalender und Disposition standardmäßig die Arbeitswoche – „heute“ angelegte Termine wären
 * nicht sichtbar. Für datumsabhängige Tests den Browser auf den nächsten Werktag, 10:00 Uhr, stellen.
 */
export async function onWeekday(page: Page) {
  const now = new Date();
  const day = now.getDay();
  if (day !== 0 && day !== 6) return;
  const target = new Date(now);
  target.setDate(now.getDate() + (day === 6 ? 2 : 1));
  target.setHours(10, 0, 0, 0);
  await page.clock.setFixedTime(target);
}
