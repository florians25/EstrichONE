import { expect, test, type Page } from '@playwright/test';

const PASSWORD = 'e2e-sehr-geheimes-passwort';
const ui = (page: Page, id: string) => page.locator(`[id$="--${id}"]`).first();
const input = (page: Page, id: string) => page.locator(`[id$="--${id}-inner"]`).first();

/** Jede CSP-Verletzung oder Skriptfehler lässt den Test scheitern (strikte CSP ohne unsafe-eval/inline-Skripte). */
function watchConsole(page: Page): string[] {
  const problems: string[] = [];
  page.on('console', (message) => {
    const text = message.text();
    if (/Content Security Policy|Refused to (load|execute|apply)|EvalError/i.test(text)) {
      problems.push(text);
    }
  });
  page.on('pageerror', (error) => problems.push(`Seitenfehler: ${error.message}`));
  return problems;
}

async function login(page: Page) {
  await page.goto('/');
  await expect(ui(page, 'loginButton')).toBeVisible();
  await input(page, 'userName').fill('admin');
  await input(page, 'password').fill(PASSWORD);
  await ui(page, 'loginButton').click();
  await expect(page.getByText('Übersicht', { exact: true }).first()).toBeVisible();
}

test.describe.serial('Hosting-Konsole', () => {
  test('Ersteinrichtung mit Einmal-Code aus dem Protokoll', async ({ page }) => {
    const problems = watchConsole(page);
    const code = process.env['E2E_SETUP_CODE'];
    expect(code, 'E2E_SETUP_CODE fehlt').toBeTruthy();
    await page.goto('/');
    await expect(page.getByText('Ersteinrichtung der Hosting-Konsole')).toBeVisible();
    // Nur das eigene Logo (Wortmarke) – kein SAP-Branding.
    await expect(page.locator('img[src*="brand/logo-horizontal"]')).toBeVisible();
    await input(page, 'setupCode').fill('FALS-CHER-CODE');
    await input(page, 'userName').fill('admin');
    await input(page, 'password').fill(PASSWORD);
    await input(page, 'password2').fill(PASSWORD);
    await ui(page, 'loginButton').click();
    await expect(ui(page, 'loginError')).toContainText('Einrichtungscode ist ungültig');
    // Nach einem Fehler wird das Passwort aus Sicherheitsgründen geleert.
    await expect(input(page, 'password')).toHaveValue('');
    await input(page, 'setupCode').fill(code!);
    await input(page, 'password').fill(PASSWORD);
    await ui(page, 'loginButton').click();
    await expect(page.getByText('Übersicht', { exact: true }).first()).toBeVisible();
    await expect(ui(page, 'tileTotal')).toBeVisible();
    await page.screenshot({ path: 'test-results/01-uebersicht.png', fullPage: true });
    expect(problems).toEqual([]);
  });

  test('Mandant anlegen, Liste, Detail und Sperren', async ({ page }) => {
    const problems = watchConsole(page);
    await login(page);
    await page.goto('/#/mandanten');
    await ui(page, 'createTenantButton').click();
    await input(page, 'fCompany').fill('Estrichbau Müller GmbH');
    await expect(input(page, 'fSlug')).toHaveValue('estrichbau-mueller');
    await input(page, 'fCustomer').fill('K-1001');
    await input(page, 'fContact').fill('Max Müller');
    await input(page, 'fEmail').fill('info@mueller.example');
    await page.screenshot({ path: 'test-results/02-mandant-anlegen.png' });
    await ui(page, 'tenantSave').click();

    // Detailseite: Bereitstellung läuft (simuliert), danach aktiv mit Einrichtungscode.
    await expect(ui(page, 'tenantTitle')).toHaveText('Estrichbau Müller GmbH');
    await expect(ui(page, 'detailState')).toContainText('Aktiv', { timeout: 60_000 });
    await expect(ui(page, 'setupCodeText')).toHaveText(/^[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$/, { timeout: 60_000 });
    await expect(ui(page, 'containerTable')).toContainText('postgres');
    await page.screenshot({ path: 'test-results/03-mandant-detail.png', fullPage: true });

    // Liste mit Suche
    await page.goto('/#/mandanten');
    await expect(ui(page, 'tenantTable')).toContainText('Estrichbau Müller GmbH');
    await page.locator('[id$="--tenantSearch-I"]').fill('gibt-es-nicht');
    await expect(ui(page, 'tenantTable')).not.toContainText('Estrichbau Müller GmbH');
    await page.locator('[id$="--tenantSearch-I"]').fill('mueller');
    await expect(ui(page, 'tenantTable')).toContainText('Estrichbau Müller GmbH');
    await page.screenshot({ path: 'test-results/04-mandanten.png', fullPage: true });

    // Sperren (Zahlungsverzug) → Wartungsseite
    await ui(page, 'tenantTable').getByText('Estrichbau Müller GmbH').click();
    await ui(page, 'actLock').click();
    await input(page, 'lockReason').fill('Zahlungsverzug');
    await ui(page, 'lockConfirm').click();
    // Vorgangsprotokoll öffnet sich live
    const log = ui(page, 'operationDialog');
    await expect(log).toBeVisible();
    await expect(ui(page, 'operationLogText').locator('textarea')).toHaveValue(/Abgeschlossen/, { timeout: 60_000 });
    await page.screenshot({ path: 'test-results/05-vorgang-sperren.png' });
    await log.getByRole('button', { name: 'Schließen' }).click();
    await expect(ui(page, 'detailState')).toContainText('Gesperrt', { timeout: 60_000 });
    await page.screenshot({ path: 'test-results/06-gesperrt.png', fullPage: true });
    expect(problems).toEqual([]);
  });

  test('Ankündigung an alle Instanzen verteilen', async ({ page }) => {
    const problems = watchConsole(page);
    await login(page);
    await page.goto('/#/hinweise');
    await ui(page, 'createAnnouncement').click();
    await input(page, 'nTitle').fill('Neue Funktion: Bautagebuch');
    await input(page, 'nText').fill('Ab sofort in allen Instanzen verfügbar.');
    await ui(page, 'noticeSave').click();
    await expect(ui(page, 'noticeTable')).toContainText('Neue Funktion: Bautagebuch');
    await expect(ui(page, 'noticeTable')).toContainText('1 von 1');
    await page.screenshot({ path: 'test-results/06b-hinweise.png', fullPage: true });
    expect(problems).toEqual([]);
  });

  test('Darstellung: Theme live umschalten und speichern, Logo je Theme', async ({ page }) => {
    const problems = watchConsole(page);
    await login(page);
    await page.getByRole('button', { name: 'Darstellung' }).click();
    await page.getByText('Evening Horizon (dunkel)').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'sap_horizon_dark');
    await ui(page, 'themeSave').click();
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'sap_horizon_dark');
    await page.screenshot({ path: 'test-results/07-dunkel.png', fullPage: true });

    await page.getByRole('button', { name: 'Darstellung' }).click();
    await page.getByText('Horizon Hoher Kontrast Schwarz').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'sap_horizon_hcb');
    await expect(page.locator('img[src*="brand/logo-mark-white.svg"]').first()).toBeAttached();
    await ui(page, 'themeSave').click();
    await page.screenshot({ path: 'test-results/08-kontrast.png', fullPage: true });

    // Alle Themes sind self-hosted ladbar.
    for (const theme of ['sap_fiori_3', 'sap_fiori_3_dark', 'sap_belize', 'sap_belize_plus', 'sap_horizon_hcw']) {
      const response = await page.request.get(`${await page.evaluate(() => (document.getElementById('sap-ui-bootstrap') as HTMLScriptElement).src.replace(/sap-ui-core\.js$/, ''))}sap/m/themes/${theme}/library.css`);
      expect(response.status(), theme).toBe(200);
    }

    // Zurück zur Systemeinstellung
    await page.getByRole('button', { name: 'Darstellung' }).click();
    await ui(page, 'themeAuto').click();
    await ui(page, 'themeSave').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', /sap_horizon/);
    expect(problems).toEqual([]);
  });

  test('Ereignisprotokoll und Abmelden', async ({ page }) => {
    await login(page);
    await page.goto('/#/ereignisse');
    await expect(ui(page, 'auditTable')).toContainText('Mandant angelegt');
    await expect(ui(page, 'auditTable')).toContainText('Ersteinrichtung abgeschlossen');
    await page.screenshot({ path: 'test-results/09-ereignisse.png', fullPage: true });
    await page.locator('[id$="--shellBar"] .sapFAvatar, [id$="--shellBar"] [role="button"][aria-haspopup]').last().click();
    await page.getByText('Abmelden').click();
    await expect(ui(page, 'loginButton')).toBeVisible();
  });
});
