import { defineConfig } from 'vitest/config';

// Unit-Tests der UI5-freien Logik (webapp/util): Formate, Kalendertage, IDs, Dokumentablage, Bedarf, API-Client.
export default defineConfig({ test: { environment: 'jsdom', include: ['test/**/*.test.ts'] } });
