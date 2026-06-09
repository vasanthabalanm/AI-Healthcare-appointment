import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for Clinical Healthcare Platform
 * @see https://playwright.dev/docs/test-configuration
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI
    ? [['junit', { outputFile: 'results.xml' }], ['html', { open: 'never' }]]
    : [['html', { open: 'on-failure' }]],

  timeout: 30000,

  expect: {
    timeout: 10000,
  },

  use: {
    baseURL: process.env.BASE_URL || 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 10000,
    navigationTimeout: 15000,
    // Slow down actions for better visibility in headed mode
    launchOptions: {
      slowMo: process.env.SLOW_MO ? parseInt(process.env.SLOW_MO) : 0,
    },
  },

  projects: [
    // Setup project for authentication
    {
      name: 'setup',
      testMatch: /.*\.setup\.ts/,
    },
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
      },
      dependencies: ['setup'],
    },
    {
      name: 'firefox',
      use: {
        ...devices['Desktop Firefox'],
      },
      dependencies: ['setup'],
    },
    {
      name: 'webkit',
      use: {
        ...devices['Desktop Safari'],
      },
      dependencies: ['setup'],
    },
    // Demo project - no auth setup, no webServer needed
    {
      name: 'demo',
      testMatch: /demo\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
      },
      // No dependencies - runs standalone
    },
  ],

  // WebServer disabled - start Angular manually: cd ../clinical-hub && npm start
  // webServer: {
  //   command: 'cd ../clinical-hub && npm start',
  //   url: 'http://localhost:4200',
  //   reuseExistingServer: !process.env.CI,
  //   timeout: 120000,
  // },
});
