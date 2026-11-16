import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { host: '127.0.0.1', proxy: { '/api': 'http://localhost:5050' } },
  test: { environment: 'jsdom', globals: true, setupFiles: './src/test/setup.ts' },
});
