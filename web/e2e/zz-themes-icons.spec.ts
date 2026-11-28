import { expect, test, type Page } from '@playwright/test';
import { adminPassword, signIn } from './helpers';

// Icons auf der Verwaltungsübersicht in allen angebotenen Themes: Jedes Icon muss ein Zeichen der Icon-Schrift haben
// und sich deutlich von seinem Hintergrund abheben (Kontrast mindestens 3 zu 1). Früher waren die Icons in Quartz und
// im hohen Kontrast unsichtbar, weil Vordergrund und Hintergrund dieselbe Theme-Farbe hatten.
const themes = ['sap_horizon', 'sap_horizon_dark', 'sap_horizon_hcb', 'sap_horizon_hcw', 'sap_fiori_3', 'sap_fiori_3_dark', 'sap_fiori_3_hcb', 'sap_fiori_3_hcw'];

async function iconIssues(page: Page, selector: string): Promise<string[]> {
  return page.evaluate((sel) => {
    const rgb = (value: string): number[] | null => {
      const m = value.match(/rgba?\(([^)]+)\)/);
      if (!m) return null;
      const parts = m[1].split(/[ ,/]+/).filter(Boolean).map(Number);
      return parts.length >= 4 && parts[3] === 0 ? null : parts.slice(0, 3);
    };
    const lum = (c: number[]) => {
      const [r, g, b] = c.map((v) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; });
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    /** Erster nicht durchsichtiger Hintergrund vom Element nach außen. */
    const background = (el: Element | null): number[] => {
      for (let e = el; e; e = e.parentElement) {
        const c = rgb(getComputedStyle(e).backgroundColor);
        if (c) return c;
      }
      return [255, 255, 255];
    };
    const issues: string[] = [];
    const icons = [...document.querySelectorAll(sel)] as HTMLElement[];
    if (icons.length === 0) issues.push('keine Icons gefunden');
    for (const icon of icons) {
      const name = icon.getAttribute('data-sap-ui-icon-content') ?? '';
      const before = getComputedStyle(icon, '::before').content;
      if (!name && (!before || before === 'none' || before === '""')) issues.push(`${icon.id}: kein Zeichen`);
      const fg = rgb(getComputedStyle(icon).color) ?? [0, 0, 0];
      const bg = background(icon);
      const [a, b] = [lum(fg), lum(bg)].sort((x, y) => y - x);
      const ratio = (a + 0.05) / (b + 0.05);
      if (ratio < 3) issues.push(`${icon.closest('li')?.textContent?.trim().slice(0, 30) ?? icon.id}: Kontrast ${ratio.toFixed(2)}`);
    }
    if (!document.fonts.check('1rem SAP-icons')) issues.push('Icon-Schrift SAP-icons nicht geladen');
    return issues;
  }, selector);
}

for (const theme of themes) {
  test(`Verwaltung: Icons sichtbar im Theme ${theme}`, async ({ page }) => {
    await page.addInitScript((id) => window.localStorage.setItem('estrichone.theme', id), theme);
    await signIn(page, 'admin', adminPassword);
    await page.goto('/verwaltung');
    await expect(page.locator('.eoAdminTileIcon').first()).toBeVisible();
    await page.waitForFunction((id) => document.documentElement.className.includes(id) || document.body.className.includes(id)
      || (window as unknown as { sap?: { ui?: { getCore?: () => { getConfiguration?: () => { getTheme?: () => string } } } } }).sap?.ui?.getCore?.()?.getConfiguration?.()?.getTheme?.() === id,
    theme, { timeout: 15000 }).catch(() => undefined);
    await page.waitForTimeout(800);
    expect(await iconIssues(page, '.eoAdminTileIcon')).toEqual([]);
  });
}
