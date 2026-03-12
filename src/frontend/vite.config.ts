import { defineConfig } from 'vite';
import solidPlugin from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';

const apiTarget = process.env.VITE_API_TARGET ?? 'http://localhost:8090';

export default defineConfig({
  plugins: [solidPlugin(), tailwindcss()],
  server: {
    port: 3000,
    proxy: {
      '/api': apiTarget,
    },
    watch: {
      ignored: ['**/bin/**', '**/obj/**', '**/node_modules/**'],
    },
  },
  build: {
    target: 'esnext',
    outDir: 'dist',
  },
});
