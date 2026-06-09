import { Page, Locator } from '@playwright/test';
import { BasePage } from './base.page';

/**
 * Admin User Management page object
 */
export class AdminUsersPage extends BasePage {
  readonly path = '/admin/users';

  constructor(page: Page) {
    super(page);
  }

  get createUserButton(): Locator {
    return this.page.getByRole('button', { name: '+ Create user' });
  }

  get userRows(): Locator {
    return this.page.getByRole('row');
  }

  get emailInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Email address' });
  }

  get firstNameInput(): Locator {
    return this.page.getByRole('textbox', { name: 'First name' });
  }

  get lastNameInput(): Locator {
    return this.page.getByRole('textbox', { name: 'Last name' });
  }

  get roleSelect(): Locator {
    return this.page.getByRole('combobox', { name: 'Role' });
  }

  get roleSelectFallback(): Locator {
    return this.page.getByTestId('select-role');
  }

  get createButton(): Locator {
    return this.page.getByRole('button', { name: 'Create user', exact: true });
  }

  get deactivateButton(): Locator {
    return this.page.getByRole('button', { name: /deactivate/i });
  }

  get confirmButton(): Locator {
    // In the deactivate modal, the confirm button is labeled "Deactivate"
    return this.page.getByRole('heading', { name: 'Deactivate user?' })
      .locator('..').locator('..').getByRole('button', { name: 'Deactivate' });
  }

  async navigate(): Promise<void> {
    await this.goto(this.path);
  }

  async openCreateUserForm(): Promise<void> {
    await this.createUserButton.click();
  }

  async fillUserForm(data: {
    email: string;
    firstName: string;
    lastName: string;
    role: string;
  }): Promise<void> {
    await this.emailInput.fill(data.email);
    await this.firstNameInput.fill(data.firstName);
    await this.lastNameInput.fill(data.lastName);
    await this.roleSelect.selectOption(data.role);
  }

  async createUser(data: {
    email: string;
    firstName: string;
    lastName: string;
    role: string;
  }): Promise<void> {
    await this.openCreateUserForm();
    await this.fillUserForm(data);
    await this.createButton.click();
  }

  getUserRow(nameOrEmail: string): Locator {
    // The page uses generic elements, not table rows
    // Find the container that has the user's email
    return this.page.locator(`text=${nameOrEmail}`).locator('..');
  }

  async deactivateUser(nameOrEmail: string): Promise<void> {
    // Find the text containing the email, then find the nearest Deactivate button
    const userText = this.page.getByText(nameOrEmail);
    // Click the Deactivate button in the same row/container
    await userText.locator('xpath=following::button[contains(text(), "Deactivate")]').first().click();
    await this.confirmButton.click();
  }
}
