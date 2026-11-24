import { defineConfig, devices } from '@playwright/test'

const previewUrl = 'http://127.0.0.1:4173'
const externalUrl = process.env.PLAYWRIGHT_BASE_URL

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL: externalUrl ?? (process.env.CI ? previewUrl : 'http://localhost:5173'),
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: externalUrl ? undefined : process.env.CI
    ? { command: 'npm run preview -- --host 127.0.0.1 --port 4173 --strictPort', url: previewUrl }
    : {
        command: 'npm run dev',
        url: 'http://localhost:5173',
        reuseExistingServer: true,
        timeout: 120_000,
      },
})
