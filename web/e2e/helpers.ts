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

/** Eintrag der Seitennavigation (sap.tnt.SideNavigation). */
export const nav = (page: Page, name: string) => page.locator('.sapTntSideNavigation').getByText(name, { exact: true });

/** Seitentitel (DynamicPage, Objektseite). */
export const pageTitle = (page: Page) => page.locator('.sapFDynamicPageTitle .sapMTitle, .sapUxAPObjectPageHeaderTitle .sapMTitle').first();

export async function signIn(page: Page, login: string, password: string) {
  await page.goto('/anmelden');
  const user = page.locator('input[id$="loginUser-inner"]');
  const secret = page.locator('input[id$="loginPassword-inner"]');
  // Die Seite setzt beim Öffnen Fokus und Felder zurück: erst danach füllen und prüfen.
  await expect(async () => {
    await user.fill(login);
    await secret.fill(password);
    await expect(user).toHaveValue(login, { timeout: 500 });
    await expect(secret).toHaveValue(password, { timeout: 500 });
  }).toPass({ timeout: 15_000 });
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await page.waitForURL((url) => !url.pathname.startsWith('/anmelden'));
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
export const toast = (page: Page) => page.locator('.sapMMessageToast').last();

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
