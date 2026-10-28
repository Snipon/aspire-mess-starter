// @ts-check
import { defineConfig } from 'astro/config';
import node from '@astrojs/node';
import tailwindcss from '@tailwindcss/vite';

// Aspire injects PORT for `npm run dev`; the built standalone server reads PORT itself.
const port = process.env.PORT ? Number(process.env.PORT) : 4321;

export default defineConfig({
  output: 'server',
  adapter: node({ mode: 'standalone' }),
  server: { port, host: true },
  vite: {
    plugins: [tailwindcss()],
  },
});
