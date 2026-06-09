import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Registration page object
 */
export class RegisterPage extends BasePage {
  readonly path = '/register';

  constructor(page: Page) {
    super(page);
  }

  get firstNameInput(): Locator {
    return this.page.getByRole('textbox', { name: 'First name' });
  }

  get lastNameInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Last name' });
  }

  get dateOfBirthInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Date of birth' });
  }

  get phoneInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Phone number' });
  }

  get emailInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Email address' });
  }

  get passwordInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Password *', exact: true });
  }

  get confirmPasswordInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Confirm password *' });
  }

  get createAccountButton(): Locator {
    return this.page.getByRole('button', { name: 'Create Account' });
  }

  get createAccountButtonFallback(): Locator {
    return this.page.getByTestId('btn-register');
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async fillFirstName(firstName: string): Promise<void> {
    await this.firstNameInput.fill(firstName);
  }

  async fillLastName(lastName: string): Promise<void> {
    await this.lastNameInput.fill(lastName);
  }

  async fillDateOfBirth(dob: string): Promise<void> {
    await this.dateOfBirthInput.fill(dob);
  }

  async fillPhone(phone: string): Promise<void> {
    await this.phoneInput.fill(phone);
  }

  async fillEmail(email: string): Promise<void> {
    await this.emailInput.fill(email);
  }

  async fillPassword(password: string): Promise<void> {
    await this.passwordInput.fill(password);
    await this.confirmPasswordInput.fill(password);
  }

  async clickCreateAccount(): Promise<void> {
    await this.createAccountButton.click();
  }

  async register(data: {
    firstName: string;
    lastName: string;
    dob: string;
    phone: string;
    email: string;
    password: string;
  }): Promise<void> {
    await this.fillFirstName(data.firstName);
    await this.fillLastName(data.lastName);
    await this.fillDateOfBirth(data.dob);
    await this.fillPhone(data.phone);
    await this.fillEmail(data.email);
    await this.fillPassword(data.password);
    await this.clickCreateAccount();
  }
}
