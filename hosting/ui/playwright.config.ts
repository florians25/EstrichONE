import { defineConfig, devices } from '@playwright/test';

/**
 * Ende-zu-Ende-Tests der Hosting-Konsole gegen eine laufende Konsole mit simuliertem Docker
 * (scripts/e2e.sh startet das Konsolen-Image mit Hosting__Docker=fake).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  timeout: 120_000,
  expect: { timeout: 20_000 },
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:5090',
    locale: 'de-DE',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
});
