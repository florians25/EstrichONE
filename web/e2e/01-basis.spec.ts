import { expect, test } from '@playwright/test';
import { adminPassword, nav, pageTitle, setupCode, signIn } from './helpers';

test.describe.configure({ mode: 'serial' });

test('Ersteinrichtung im Browser und Anmeldung als Administrator', async ({ page }) => {
  await page.goto('/anmelden');
  await expect(page.getByText('EstrichONE ist noch nicht eingerichtet.')).toBeVisible();
  await page.getByRole('link', { name: 'Jetzt einrichten' }).click();

  await page.getByLabel('Einrichtungscode').fill(setupCode);
  await page.getByLabel('Firmenname').fill('Estrichbau E2E GmbH');
  await page.getByLabel('Name', { exact: true }).fill('Anna Admin');
  await page.getByLabel('Anmeldename').fill('admin');
  await page.getByLabel('Passwort', { exact: true }).fill(adminPassword);
  await page.getByRole('button', { name: 'Einrichten und anmelden' }).click();

  await expect(page.getByRole('heading', { name: 'Guten Tag, Anna Admin' })).toBeVisible();
  await expect(page.getByLabel('Benutzermenü Anna Admin')).toBeVisible();
});

test('Falsches Passwort wird verständlich abgelehnt', async ({ page }) => {
  await page.goto('/anmelden');
  await page.getByLabel('Anmeldename oder E-Mail').fill('admin');
  await page.getByLabel('Passwort').fill('falsch-falsch');
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page.locator('.sapMMsgStrip').filter({ hasText: 'Anmeldename oder Passwort ist nicht korrekt.' })).toBeVisible();
});

test('Eigenes Logo, App-Manifest (installierbar) und keine Verletzung der CSP', async ({ page }) => {
  const violations: string[] = [];
  page.on('console', (m) => {
    if (/Content Security Policy|Refused to/.test(m.text())) violations.push(m.text());
  });
  await page.goto('/anmelden');
  await expect(page.getByAltText('EstrichONE').first()).toBeVisible();
  const href = await page.locator('link[rel=manifest]').getAttribute('href');
  expect(href).toBe('/estrichone.webmanifest');
  const manifest = await (await page.request.get(href!)).json();
  expect(manifest.name).toBe('EstrichONE');
  expect(manifest.display).toBe('standalone');
  expect(manifest.start_url).toBe('/');
  expect(manifest.icons.map((i: { src: string }) => i.src)).toEqual(expect.arrayContaining(['/icon-192.png', '/icon-512.png']));
  expect((await page.request.get('/icon-512.png')).ok()).toBe(true);
  await signIn(page, 'admin', adminPassword);
  await expect(page.locator('.sapFShellBar img').first()).toHaveAttribute('src', /logo/);
  expect(violations).toEqual([]);
});

test('Farbschema wählen: alle Themes, Live-Umschaltung, bleibt gespeichert', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  await page.getByLabel('Benutzermenü Anna Admin').click();
  await page.getByRole('menuitem', { name: 'Einstellungen' }).click();
  const dialog = page.getByRole('dialog', { name: 'Einstellungen' });
  for (const theme of ['sap_horizon', 'sap_horizon_dark', 'sap_horizon_hcb', 'sap_horizon_hcw', 'sap_fiori_3', 'sap_fiori_3_dark', 'sap_fiori_3_hcb', 'sap_fiori_3_hcw']) {
    await expect(dialog.getByText(theme, { exact: true })).toBeVisible();
  }
  await dialog.getByText('Abendhorizont (dunkel)').click();
  await expect(page.locator('link[id="sap-ui-theme-sap.ui.core"]')).toHaveAttribute('href', /sap_horizon_dark/, { timeout: 15_000 });
  await expect(page.locator('.sapFShellBar img').first()).toHaveAttribute('src', /logo/);
  await dialog.getByText('Quartz Hoher Kontrast Schwarz').click();
  await expect(page.locator('link[id="sap-ui-theme-sap.ui.core"]')).toHaveAttribute('href', /sap_fiori_3_hcb/, { timeout: 15_000 });
  await expect(page.locator('.sapFShellBar img').first()).toHaveAttribute('src', /logo-mark-white\.svg/);
  await dialog.getByRole('button', { name: 'Schließen' }).click();
  await page.reload();
  await expect(page.locator('link[id="sap-ui-theme-sap.ui.core"]')).toHaveAttribute('href', /sap_fiori_3_hcb/, { timeout: 15_000 });
  expect(await page.evaluate(() => localStorage.getItem('estrichone.theme'))).toBe('sap_fiori_3_hcb');
  // Zurück zur Systemeinstellung (Standard) für die weiteren Tests.
  await page.getByLabel('Benutzermenü Anna Admin').click();
  await page.getByRole('menuitem', { name: 'Einstellungen' }).click();
  await page.getByRole('dialog', { name: 'Einstellungen' }).getByText('Systemeinstellung folgen').click();
  await expect(page.locator('link[id="sap-ui-theme-sap.ui.core"]')).toHaveAttribute('href', /sap_horizon\//, { timeout: 15_000 });
  expect(await page.evaluate(() => localStorage.getItem('estrichone.theme'))).toBe('system');
});

test('Navigation nach Rechten, Deep-Link nach Anmeldung, Abmelden', async ({ page }) => {
  await page.goto('/bauakten');
  await expect(page).toHaveURL(/\/anmelden\?ziel=%2Fbauakten/);
  await page.getByLabel('Anmeldename oder E-Mail').fill('admin');
  await page.getByLabel('Passwort').fill(adminPassword);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/\/bauakten$/);
  await expect(pageTitle(page)).toHaveText('Bauakten');
  await nav(page, 'Übersicht').click();
  await expect(page).toHaveURL(/\/start$/);
  await page.getByLabel('Benutzermenü Anna Admin').click();
  await page.getByRole('menuitem', { name: 'Abmelden' }).click();
  await expect(page).toHaveURL(/\/anmelden/);
});
