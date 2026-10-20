import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Entwicklung: /api an die lokale API (scripts/e2e.sh startet sie auf 5080). Betrieb: Caddy (deploy/Caddyfile).
export default defineConfig({
  plugins: [react()],
  server: { port: 4200, strictPort: true, proxy: { '/api': { target: 'http://localhost:5080', changeOrigin: false } } },
  build: {
    outDir: 'dist',
    chunkSizeWarningLimit: 2500,
    rollupOptions: {
      output: {
        manualChunks: { antd: ['antd', '@ant-design/icons'], pro: ['@ant-design/pro-components'], react: ['react', 'react-dom', 'react-router-dom'] },
      },
    },
  },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts', 'src/**/*.test.tsx'] },
});
