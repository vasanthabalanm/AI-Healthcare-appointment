import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Login page object
 */
export class LoginPage extends BasePage {
  readonly path = '/login';

  constructor(page: Page) {
    super(page);
  }

  get emailInput(): Locator {
    return this.page.getByLabel('Email');
  }

  get emailInputFallback(): Locator {
    return this.page.getByTestId('input-email');
  }

  get passwordInput(): Locator {
    return this.page.getByLabel('Password');
  }

  get passwordInputFallback(): Locator {
    return this.page.getByTestId('input-password');
  }

  get signInButton(): Locator {
    return this.page.getByRole('button', { name: 'Sign In' });
  }

  get signInButtonFallback(): Locator {
    return this.page.getByTestId('btn-signin');
  }

  get forgotPasswordLink(): Locator {
    return this.page.getByRole('link', { name: /forgot/i });
  }

  get registerLink(): Locator {
    return this.page.getByRole('link', { name: /register|create account/i });
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async fillEmail(email: string): Promise<void> {
    await this.emailInput.fill(email);
  }

  async fillPassword(password: string): Promise<void> {
    await this.passwordInput.fill(password);
  }

  async clickSignIn(): Promise<void> {
    await this.signInButton.click();
  }

  async login(email: string, password: string): Promise<void> {
    await this.fillEmail(email);
    await this.fillPassword(password);
    await this.clickSignIn();
  }
}
