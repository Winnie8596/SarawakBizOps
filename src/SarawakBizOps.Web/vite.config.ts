import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Tests call the API through a stubbed fetch; pin the base URL so they never depend on a local .env.
    env: { VITE_API_BASE_URL: 'http://api.test/api' }
  }
})
