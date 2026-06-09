import { Page, Locator } from '@playwright/test';

/**
 * Base page object with common functionality
 */
export abstract class BasePage {
  constructor(protected readonly page: Page) {}

  /** Get alert/notification element */
  get alert(): Locator {
    return this.page.getByRole('alert');
  }

  /** Get alert by test ID fallback */
  get alertFallback(): Locator {
    return this.page.getByTestId('app-alert');
  }

  /** Navigate to a path */
  async goto(path: string): Promise<void> {
    await this.page.goto(path);
  }

  /** Wait for navigation to complete */
  async waitForUrl(urlPattern: string | RegExp): Promise<void> {
    await this.page.waitForURL(urlPattern);
  }

  /** Get current URL */
  getCurrentUrl(): string {
    return this.page.url();
  }

  /** Get localStorage item */
  async getLocalStorageItem(key: string): Promise<string | null> {
    return this.page.evaluate((k) => localStorage.getItem(k), key);
  }

  /** Set localStorage item */
  async setLocalStorageItem(key: string, value: string): Promise<void> {
    await this.page.evaluate(([k, v]) => localStorage.setItem(k, v), [key, value]);
  }

  /** Clear localStorage */
  async clearLocalStorage(): Promise<void> {
    await this.page.evaluate(() => localStorage.clear());
  }
}
