import { defineConfig, devices } from '@playwright/test';

/**
 * Betrieb hinter Nginx Proxy Manager (scripts/e2e.sh mit E2E_PROXY=npm): Caddy in der Betriebsart „proxy“, davor Nginx
 * mit der Konfiguration, die NPM für einen Proxy Host erzeugt (TLS, Force SSL, HTTP/2, HSTS, Block Common Exploits,
 * Websockets). Aufgerufen wird https://app.estrichone.de – der Server ist absichtlich auf eine andere Domain eingestellt.
 */
export default defineConfig({
  testDir: './e2e-proxy',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  timeout: 90_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'https://app.estrichone.de:8443',
    locale: 'de-DE',
    timezoneId: 'Europe/Berlin',
    trace: 'retain-on-failure',
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 900 },
  },
  projects: [{
    name: 'chromium',
    use: {
      ...devices['Desktop Chrome'],
      viewport: { width: 1440, height: 900 },
      // Domain des Proxy Hosts ohne DNS auf den Testrechner lenken; ohne Proxy des Rechners (lokale Adressen).
      launchOptions: { args: ['--host-resolver-rules=MAP app.estrichone.de 127.0.0.1', '--no-proxy-server'] },
    },
  }],
});
