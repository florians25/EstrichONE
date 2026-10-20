import { defineConfig, devices } from '@playwright/test';

/**
 * Ende-zu-Ende-Tests gegen einen echten Server (scripts/e2e.sh startet PostgreSQL, API und Web).
 * Die Dateien laufen nacheinander und bauen aufeinander auf (Einrichtung → Bauakten → Planung …).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  timeout: 90_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    locale: 'de-DE',
    timezoneId: 'Europe/Berlin',
    trace: 'retain-on-failure',
    acceptDownloads: true,
    // Lokales Zertifikat von Caddy beim Test gegen das fertige Web-Image.
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 900 },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
});
