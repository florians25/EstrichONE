import { defineConfig, devices } from '@playwright/test';

/**
 * Ende-zu-Ende-Tests gegen einen echten Server (scripts/e2e.sh startet PostgreSQL, API und Web).
 * docs/reengineering/19: Web E2E mit Playwright, Hauptabläufe.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  timeout: 60_000,
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    locale: 'de-DE',
    trace: 'retain-on-failure',
    // Lokales Zertifikat von Caddy beim Test gegen das fertige Web-Image.
    ignoreHTTPSErrors: true,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
