/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // The API runs on HTTPS (Secure, SameSite=Strict cookies); the proxy keeps the SPA and the API on one origin.
    proxy: {
      '/api': { target: 'https://localhost:7080', secure: false },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts'],
  },
})
