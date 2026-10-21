import { expect, test, type Page } from '@playwright/test';
import { adminPassword, openSection, signIn, toast } from './helpers';

/** Erste Bauakte der Liste öffnen. */
async function openFirstAkte(page: Page) {
  await page.goto('/bauakten');
  await page.locator('.sapMListTbl a.sapMLnk').first().click();
  await expect(page.locator('.sapUxAPObjectPageLayout')).toBeVisible();
}

test('Große Datei hinter einem Proxy mit 10-MB-Grenze: in Teilen, nach Verbindungsabbruch fortgesetzt', async ({ page }) => {
  const limit = 10 * 1024 * 1024;
  const sizes: number[] = [];
  let aborted = false;
  // Wie Nginx Proxy Manager mit client_max_body_size 10m – plus ein einmaliger Verbindungsabbruch.
  await page.route('**/api/v1/files/*/content', async (route) => {
    const request = route.request();
    if (request.method() !== 'PUT') return route.fallback();
    const size = request.postDataBuffer()?.length ?? 0;
    sizes.push(size);
    if (size > limit) {
      return route.fulfill({ status: 413, contentType: 'text/html', body: '<html><body>413 Request Entity Too Large</body></html>' });
    }
    if (!aborted && sizes.length === 2) {
      aborted = true;
      return route.abort('connectionreset');
    }
    return route.fallback();
  });

  await signIn(page, 'admin', adminPassword);
  await openFirstAkte(page);
  await openSection(page, 'Dokumente');
  const big = Buffer.concat([Buffer.from('%PDF-1.4\n'), Buffer.alloc(12 * 1024 * 1024, 0x20), Buffer.from('\n%%EOF\n')]);
  await page.locator('.eoUpload input[type=file]').first().setInputFiles({ name: 'Großer Plan.pdf', mimeType: 'application/pdf', buffer: big });
  await expect(toast(page)).toContainText('hochgeladen', { timeout: 60_000 });
  await expect(page.getByRole('link', { name: 'Großer Plan.pdf' }).first()).toBeVisible();
  expect(aborted).toBe(true);
  expect(Math.max(...sizes)).toBeLessThanOrEqual(4 * 1024 * 1024);
  expect(sizes.length).toBeGreaterThanOrEqual(5);
});

test('Abgelaufenes Zugriffs-Cookie wird still erneuert; abgelaufene Sitzung: Anmeldedialog ohne Datenverlust', async ({ page, context }) => {
  await signIn(page, 'admin', adminPassword);
  await openFirstAkte(page);

  // Browser hat das Zugriffs-Cookie verworfen (früher nach 10 Minuten): weiterarbeiten ohne Abmeldung.
  await context.clearCookies({ name: 'eo_at' });
  await openSection(page, 'Notizen & Verlauf');
  await page.reload();
  await expect(page.locator('.sapUxAPObjectPageLayout')).toBeVisible();
  await expect(page).not.toHaveURL(/anmelden/);

  // Sitzung ganz weg: Eingabe bleibt, Anmeldedialog, danach wird gespeichert.
  await openSection(page, 'Notizen & Verlauf');
  await context.clearCookies({ name: 'eo_at' });
  await context.clearCookies({ name: 'eo_rt' });
  const note = page.getByPlaceholder('Notiz schreiben …');
  await note.fill('Notiz nach Sitzungsablauf');
  await page.getByRole('button', { name: /Notiz speichern|Speichern/ }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Erneut anmelden' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Passwort', { exact: true }).fill(adminPassword);
  await dialog.getByRole('button', { name: 'Anmelden' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText('Notiz nach Sitzungsablauf').first()).toBeVisible();
});
