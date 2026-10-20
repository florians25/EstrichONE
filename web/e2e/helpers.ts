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
  await page.getByLabel('Anmeldename oder E-Mail').fill(login);
  await page.getByLabel('Passwort', { exact: true }).fill(password);
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
