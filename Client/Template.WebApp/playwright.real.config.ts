import { defineConfig, devices } from '@playwright/test';

/**
 * Phase 33 release journey against the real local Compose stack (no API mocks): WebApi, Worker, WorkflowFunctions,
 * PostgreSQL, RabbitMQ and Azurite. Run through scripts/real-journey.ps1, which supplies the demo password from the
 * git-ignored .env; nothing secret is committed. Serial and slow by design: it waits for asynchronous work.
 */
export default defineConfig({
  testDir: './e2e-real',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 10 * 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-real' }]],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'retain-on-failure',
    locale: 'pt-BR',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
