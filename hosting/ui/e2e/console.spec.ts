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

    // Einrichtungsassistent: Passwort steht (selbst gewählt), Zwei-Faktor überspringen, Version, Sicherung, Benachrichtigungen.
    await expect(ui(page, 'setupPage')).toBeVisible();
    await expect(ui(page, 'spPassword')).toContainText('Passwort ist festgelegt');
    await ui(page, 'setupNext').click();
    await expect(ui(page, 'sTotpBegin')).toBeVisible();
    await ui(page, 'setupSkip').click();
    await expect(ui(page, 'sVersion')).toBeVisible();
    await ui(page, 'setupNext').click();
    await input(page, 'sRepository').fill('kein repository');
    await ui(page, 'setupNext').click();
    await input(page, 'nWebhook').fill('https://hooks.example.org/estrichone');
    await page.screenshot({ path: 'test-results/01a-einrichtung.png' });
    await ui(page, 'setupNext').click();
    // Ungültiges Sicherungsziel wird vom Server abgewiesen.
    await expect(page.getByText('restic-Repository angeben').first()).toBeVisible();
    await page.getByRole('button', { name: /Schließen|OK/ }).last().click();
    await ui(page, 'setupBack').click();
    await input(page, 'sRepository').fill('sftp:backup@sicherung.example.org:/estrichone');
    await ui(page, 'setupNext').click();
    await ui(page, 'setupNext').click();
    await expect(ui(page, 'sCreateTenant')).toBeVisible();
    await page.screenshot({ path: 'test-results/01b-einrichtung-fertig.png' });
    await page.goto('/#/');
    await expect(ui(page, 'tileTotal')).toBeVisible();
    await expect(ui(page, 'instanceTable')).toBeVisible();
    await page.screenshot({ path: 'test-results/01-uebersicht.png', fullPage: true });
    expect(problems).toEqual([]);
  });

  test('Mandant anlegen, Liste, Detail und Sperren', async ({ page }) => {
    const problems = watchConsole(page);
    await login(page);
    await page.goto('/#/mandanten');
    await ui(page, 'createTenantButton').click();
    // Assistent: Firma → Adresse → Ressourcen → erste Admin-Person → Prüfen
    await input(page, 'wCompany').fill('Estrichbau Müller GmbH');
    await input(page, 'wCustomer').fill('K-1001');
    await input(page, 'wContact').fill('Max Müller');
    await input(page, 'wEmail').fill('info@mueller.example');
    await ui(page, 'wizardNext').click();
    await expect(input(page, 'wSlug')).toHaveValue('estrichbau-mueller');
    await ui(page, 'wizardNext').click();
    await expect(ui(page, 'wPlans')).toContainText('Starter');
    await ui(page, 'wizardNext').click();
    await expect(input(page, 'wAdminName')).toHaveValue('Max Müller');
    await expect(input(page, 'wAdminUser')).toHaveValue('max.mueller');
    await page.screenshot({ path: 'test-results/02-mandant-anlegen.png' });
    await ui(page, 'wizardNext').click();
    await expect(ui(page, 'wSummaryDomain')).toContainText('estrichbau-mueller.');
    await ui(page, 'wizardCreate').click();

    // Detailseite: Bereitstellung und Ersteinrichtung laufen (simuliert), danach aktiv mit Zugangsbrief.
    await expect(ui(page, 'tenantTitle')).toHaveText('Estrichbau Müller GmbH');
    await expect(ui(page, 'detailState')).toContainText('Aktiv', { timeout: 60_000 });
    await expect(ui(page, 'letterPanel')).toBeVisible({ timeout: 60_000 });
    await expect(ui(page, 'containerTable')).toContainText('postgres');
    await page.screenshot({ path: 'test-results/03-mandant-detail.png', fullPage: true });
    await ui(page, 'openLetter').click();
    await input(page, 'passwordPrompt').fill(PASSWORD);
    await ui(page, 'passwordPromptConfirm').click();
    await expect(ui(page, 'letterUser')).toHaveText('max.mueller');
    await expect(ui(page, 'letterPassword')).toHaveText(/^[A-Za-z0-9]{4}(-[A-Za-z0-9]{4}){3}$/);
    await page.screenshot({ path: 'test-results/03b-zugangsbrief.png' });
    await ui(page, 'accessLetterDialog').getByRole('button', { name: 'Schließen' }).click();

    // Sicherung → Wiederherstellungspunkt in der Liste
    await ui(page, 'actBackup').click();
    await expect(ui(page, 'operationLogText').locator('textarea')).toHaveValue(/Abgeschlossen/, { timeout: 60_000 });
    await ui(page, 'operationDialog').getByRole('button', { name: 'Schließen' }).click();
    await expect(ui(page, 'snapshotTable')).toContainText('/repository', { timeout: 30_000 });

    // Liste mit Suche
    await page.goto('/#/mandanten');
    await expect(ui(page, 'tenantTable')).toContainText('Estrichbau Müller GmbH');
    await page.locator('[id$="--tenantSearch-I"]').fill('gibt-es-nicht');
    await expect(ui(page, 'tenantTable')).not.toContainText('Estrichbau Müller GmbH');
    await page.locator('[id$="--tenantSearch-I"]').fill('mueller');
    await expect(ui(page, 'tenantTable')).toContainText('Estrichbau Müller GmbH');
    await page.screenshot({ path: 'test-results/04-mandanten.png', fullPage: true });

    // Massenaktion: Auswahl → Neu starten
    await ui(page, 'tenantTable').locator('.sapMCb').nth(1).click();
    await expect(ui(page, 'bulkBar')).toContainText('1 ausgewählt');
    await ui(page, 'bulkRestart').click();
    await page.getByRole('button', { name: 'OK' }).click();
    await expect(page.getByText('1 eingereiht').first()).toBeVisible();

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

  test('Benachrichtigungen, Archiv und Einstellungen', async ({ page }) => {
    const problems = watchConsole(page);
    await login(page);
    await page.getByRole('button', { name: 'Benachrichtigungen' }).first().click();
    await expect(ui(page, 'notificationList')).toContainText('Keine offenen Probleme');
    await page.keyboard.press('Escape');
    await page.goto('/#/archiv');
    await expect(ui(page, 'archiveTable')).toContainText('Keine Endsicherungen');
    await page.goto('/#/einstellungen');
    await page.getByText('Benachrichtigungen', { exact: true }).last().click();
    await expect(input(page, 'nWebhook')).toHaveValue('https://hooks.example.org/estrichone');
    await page.screenshot({ path: 'test-results/06c-benachrichtigungen.png', fullPage: true });
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

  test('Konto „nur lesen“: sieht Instanzen, Apps und Module, ändert nichts', async ({ page, browser }) => {
    const problems = watchConsole(page);
    await login(page);
    await page.goto('/#/einstellungen');
    await page.getByText('Konten und Rollen', { exact: true }).first().click();
    await ui(page, 'addAdmin').click();
    await input(page, 'aUser').fill('leitstand');
    await input(page, 'aName').fill('Leitstand Bereitschaft');
    await ui(page, 'aRole').getByText('Nur lesen', { exact: true }).click();
    await input(page, 'aPassword').fill('Start-Lesezugang-2026');
    await ui(page, 'adminSave').click();
    await expect(ui(page, 'adminTable')).toContainText('Nur lesen · Startpasswort');
    await page.screenshot({ path: 'test-results/10-konten-rollen.png', fullPage: true });

    const viewer = await (await browser.newContext()).newPage();
    const viewerProblems = watchConsole(viewer);
    await viewer.goto('/');
    await input(viewer, 'userName').fill('leitstand');
    await input(viewer, 'password').fill('Start-Lesezugang-2026');
    await ui(viewer, 'loginButton').click();
    // Erst ein eigenes Passwort, Zwei-Faktor optional, danach direkt zur Übersicht (kein Einrichtungsassistent).
    await input(viewer, 'sCurrent').fill('Start-Lesezugang-2026');
    await input(viewer, 'sNew').fill('Leitstand-eigenes-2026');
    await input(viewer, 'sRepeat').fill('Leitstand-eigenes-2026');
    await ui(viewer, 'setupNext').click();
    await expect(ui(viewer, 'sTotpBegin')).toBeVisible();
    await ui(viewer, 'setupNext').click();
    await expect(ui(viewer, 'instanceTable')).toBeVisible();
    await expect(viewer.getByText('Hosting-Konsole · nur lesen')).toBeVisible();
    await expect(ui(viewer, 'dashboardCreate')).toBeHidden();
    await expect(ui(viewer, 'appTable')).toContainText('Estrichbau Müller GmbH');
    await viewer.goto('/#/mandanten');
    await expect(ui(viewer, 'tenantTable')).toContainText('Estrichbau Müller GmbH');
    await expect(ui(viewer, 'createTenantButton')).toBeHidden();
    await ui(viewer, 'tenantTable').getByText('Estrichbau Müller GmbH').click();
    await expect(ui(viewer, 'tenantTitle')).toHaveText('Estrichbau Müller GmbH');
    await expect(ui(viewer, 'actDelete')).toBeHidden();
    await expect(ui(viewer, 'actUpdate')).toBeHidden();
    await expect(ui(viewer, 'moduleTable')).toContainText('Bautagebuch', { timeout: 30_000 });
    await viewer.screenshot({ path: 'test-results/11-nur-lesen-instanz.png', fullPage: true });
    // Der Server lehnt Änderungen auch ohne Oberfläche ab.
    const state = await (await viewer.request.get('/api/auth/state')).json();
    const denied = await viewer.request.post('/api/tenants', { data: { companyName: 'X' }, headers: { 'X-CSRF-Token': state.csrfToken } });
    expect(denied.status()).toBe(403);
    expect((await denied.json()).code).toBe('READ_ONLY');
    expect(viewerProblems).toEqual([]);
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
